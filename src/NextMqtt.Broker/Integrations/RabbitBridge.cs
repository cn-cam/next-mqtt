using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NextMqtt.Core.Protocol;
using NextMqtt.Core.Topics;
using RabbitMQ.Client;

namespace NextMqtt.Broker.Integrations;

/// <summary>RabbitMQ bridge settings (ADR 0008).</summary>
public sealed class RabbitOptions
{
    /// <summary>Forward publishes to RabbitMQ.</summary>
    public bool Enabled { get; set; }

    /// <summary>Broker host name or IP.</summary>
    public string HostName { get; set; } = "127.0.0.1";

    /// <summary>AMQP port.</summary>
    public int Port { get; set; } = 5672;

    /// <summary>Login user.</summary>
    public string UserName { get; set; } = "guest";

    /// <summary>Login password.</summary>
    public string Password { get; set; } = "guest";

    /// <summary>Virtual host.</summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>Durable topic exchange that receives the messages.</summary>
    public string Exchange { get; set; } = "nextmqtt";

    /// <summary>Prefix added to every routing key.</summary>
    public string RoutingKeyPrefix { get; set; } = "mqtt.";

    /// <summary>Topic filter that selects messages to forward.</summary>
    public string Filter { get; set; } = "#";

    /// <summary>Buffered messages before new ones are dropped.</summary>
    public int MaxQueue { get; set; } = 5000;
}

/// <summary>
/// Forwards matching publishes to a RabbitMQ topic exchange with publisher
/// confirms, so a message only counts as forwarded after the broker accepts it.
/// </summary>
/// <remarks>
/// 与 NATS/Kafka 桥同样的取舍：发布路径只入队；连接断开会自动重连并重新声明交换机。
/// 消息标记为持久化，配合 durable exchange 才能在 RabbitMQ 重启后保留。
/// </remarks>
public sealed class RabbitBridge : IAsyncDisposable
{
    private readonly RabbitOptions _options;
    private readonly TopicFilter _filter;
    private readonly Channel<(string RoutingKey, byte[] Payload)> _queue;
    private readonly ILogger<RabbitBridge> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _worker;
    private long _forwarded;
    private long _dropped;
    private long _failed;

    /// <summary>Validates the filter and starts the delivery worker.</summary>
    /// <param name="options">Bridge settings.</param>
    /// <param name="logger">Logger for connection and publish failures.</param>
    public RabbitBridge(RabbitOptions options, ILogger<RabbitBridge> logger)
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

    /// <summary>Messages confirmed by RabbitMQ.</summary>
    public long Forwarded => Interlocked.Read(ref _forwarded);

    /// <summary>Messages dropped because the queue was full.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Messages that could not be published.</summary>
    public long Failed => Interlocked.Read(ref _failed);

    /// <summary>Maps an MQTT topic to a dotted routing key.</summary>
    /// <remarks>`/` 变 `.`；空格与 `*`/`>` 换成下划线，避免非法 routing key。</remarks>
    /// <param name="prefix">Configured prefix.</param>
    /// <param name="topic">MQTT topic.</param>
    public static string MapRoutingKey(string prefix, string topic)
    {
        var builder = new StringBuilder(prefix.Length + topic.Length);
        builder.Append(prefix);

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

    /// <summary>Queues a message when it matches the filter.</summary>
    /// <param name="publish">Message accepted by the broker.</param>
    public void Publish(PublishPacket publish)
    {
        if (!_filter.Matches(publish.Topic))
        {
            return;
        }

        if (!_queue.Writer.TryWrite((MapRoutingKey(_options.RoutingKeyPrefix, publish.Topic), publish.Payload)))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    /// <summary>Stops the worker and closes the AMQP connection.</summary>
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

    /// <summary>Keeps one confirmed channel alive and publishes queued messages.</summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            IConnection? connection = null;
            IChannel? channel = null;

            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = _options.HostName,
                    Port = _options.Port,
                    UserName = _options.UserName,
                    Password = _options.Password,
                    VirtualHost = _options.VirtualHost,
                };

                connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                    cancellationToken).ConfigureAwait(false);

                await channel.ExchangeDeclareAsync(
                    _options.Exchange,
                    ExchangeType.Topic,
                    durable: true,
                    autoDelete: false,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "RabbitMQ bridge connected to {Host}:{Port}, exchange {Exchange}",
                    _options.HostName,
                    _options.Port,
                    _options.Exchange);

                await PumpAsync(channel, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RabbitMQ bridge connection failed; retrying");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            finally
            {
                if (channel is not null)
                {
                    await channel.DisposeAsync().ConfigureAwait(false);
                }

                if (connection is not null)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>Publishes queued messages on an established channel.</summary>
    /// <param name="channel">Confirmed channel.</param>
    /// <param name="cancellationToken">Stops the pump.</param>
    private async Task PumpAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await foreach (var (routingKey, payload) in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await channel.BasicPublishAsync(
                    _options.Exchange,
                    routingKey,
                    mandatory: false,
                    basicProperties: new BasicProperties { Persistent = true },
                    body: payload,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                Interlocked.Increment(ref _forwarded);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Interlocked.Increment(ref _failed);
                _logger.LogWarning(ex, "RabbitMQ publish to {RoutingKey} failed", routingKey);
                throw;
            }
        }
    }
}
