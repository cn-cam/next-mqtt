using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NextMqtt.Core.Protocol;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker.Integrations;

/// <summary>NATS bridge settings.</summary>
public sealed class NatsOptions
{
    /// <summary>Forward publishes to NATS.</summary>
    public bool Enabled { get; set; }

    /// <summary>Server address, for example <c>nats://127.0.0.1:4222</c>.</summary>
    public string Url { get; set; } = "nats://127.0.0.1:4222";

    /// <summary>Prefix added to every subject; the topic is appended with dots.</summary>
    public string SubjectPrefix { get; set; } = "mqtt.";

    /// <summary>Topic filter that selects messages to forward.</summary>
    public string Filter { get; set; } = "#";

    /// <summary>Buffered messages before new ones are dropped.</summary>
    public int MaxQueue { get; set; } = 5000;

    /// <summary>Optional user for CONNECT.</summary>
    public string? Username { get; set; }

    /// <summary>Optional password for CONNECT.</summary>
    public string? Password { get; set; }
}

/// <summary>
/// Forwards matching publishes to NATS over its text protocol, with no client
/// library dependency.
/// </summary>
/// <remarks>
/// 发布路径只负责入队；后台任务维护一条 TCP 连接，断线后自动重连。主题按 MQTT 规则
/// 转成 NATS 主题：<c>/</c> 变 <c>.</c>，空格与通配符替换为下划线。队列满时丢弃并计数。
/// </remarks>
public sealed class NatsBridge : IAsyncDisposable
{
    private readonly NatsOptions _options;
    private readonly TopicFilter _filter;
    private readonly Channel<(string Subject, byte[] Payload)> _queue;
    private readonly ILogger<NatsBridge> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _worker;
    private long _forwarded;
    private long _dropped;

    /// <summary>Parses the URL, validates the filter and starts the worker.</summary>
    /// <param name="options">Bridge settings.</param>
    /// <param name="logger">Logger for connection failures.</param>
    public NatsBridge(NatsOptions options, ILogger<NatsBridge> logger)
    {
        _options = options;
        _logger = logger;
        _filter = TopicFilter.Parse(options.Filter);
        _queue = Channel.CreateBounded<(string, byte[])>(new BoundedChannelOptions(Math.Max(1, options.MaxQueue))
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

        _worker = Task.Run(() => RunAsync(_lifetime.Token), CancellationToken.None);
    }

    /// <summary>Messages accepted by the NATS server.</summary>
    public long Forwarded => Interlocked.Read(ref _forwarded);

    /// <summary>Messages dropped because the queue was full or the link was down.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Queues a message when it matches the configured filter.</summary>
    /// <param name="publish">Message accepted by the broker.</param>
    public void Publish(PublishPacket publish)
    {
        if (!_filter.Matches(publish.Topic))
        {
            return;
        }

        if (!_queue.Writer.TryWrite((Subject(publish.Topic), publish.Payload)))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    /// <summary>Stops the worker and closes the connection.</summary>
    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _queue.Writer.TryComplete();

        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }

        _lifetime.Dispose();
    }

    /// <summary>Maps an MQTT topic to a safe NATS subject.</summary>
    /// <param name="topic">MQTT topic name.</param>
    private string Subject(string topic)
    {
        var builder = new StringBuilder(_options.SubjectPrefix.Length + topic.Length);

        builder.Append(_options.SubjectPrefix);

        foreach (var character in topic)
        {
            builder.Append(character switch
            {
                '/' => '.',
                ' ' or '*' or '>' => '_',
                _ => character,
            });
        }

        return builder.ToString();
    }

    /// <summary>Keeps one connection alive and publishes queued messages.</summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var (host, port) = ParseAddress(_options.Url);

        while (!cancellationToken.IsCancellationRequested)
        {
            using var client = new TcpClient { NoDelay = true };

            try
            {
                await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);

                var stream = client.GetStream();
                await ReadInfoAsync(stream, cancellationToken).ConfigureAwait(false);
                await SendConnectAsync(stream, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("NATS bridge connected to {Host}:{Port}", host, port);

                await PumpAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
            {
                _logger.LogWarning(ex, "NATS bridge connection failed; retrying");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Writes queued messages to an established connection.</summary>
    /// <param name="stream">Connected NATS stream.</param>
    /// <param name="cancellationToken">Stops the pump.</param>
    private async Task PumpAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        await foreach (var (subject, payload) in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var header = Encoding.ASCII.GetBytes($"PUB {subject} {payload.Length}\r\n");
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync("\r\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _forwarded);
        }
    }

    /// <summary>Reads the server INFO line that starts every NATS connection.</summary>
    /// <param name="stream">Connected stream.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    private static async Task ReadInfoAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[512];
        var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

        if (read == 0)
        {
            throw new IOException("NATS server closed the connection before INFO.");
        }
    }

    /// <summary>Sends CONNECT so the server accepts PUB frames.</summary>
    /// <param name="stream">Connected stream.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    private async Task SendConnectAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var credentials = new StringBuilder();

        if (!string.IsNullOrEmpty(_options.Username))
        {
            credentials.Append($",\"user\":\"{_options.Username}\"");

            if (!string.IsNullOrEmpty(_options.Password))
            {
                credentials.Append($",\"pass\":\"{_options.Password}\"");
            }
        }

        var json = $"{{\"verbose\":false,\"pedantic\":false,\"name\":\"nextmqtt\",\"lang\":\"csharp\",\"version\":\"1.0\"{credentials}}}\r\n";
        var connect = Encoding.ASCII.GetBytes("CONNECT " + json);

        await stream.WriteAsync(connect, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Splits a <c>nats://host:port</c> address.</summary>
    /// <param name="url">Configured URL.</param>
    /// <returns>Host and port, defaulting to 4222.</returns>
    private static (string Host, int Port) ParseAddress(string url)
    {
        var text = url.StartsWith("nats://", StringComparison.OrdinalIgnoreCase) ? url[7..] : url;
        var separator = text.LastIndexOf(':');

        if (separator <= 0 || !int.TryParse(text[(separator + 1)..], out var port))
        {
            return (text, 4222);
        }

        return (text[..separator], port);
    }
}
