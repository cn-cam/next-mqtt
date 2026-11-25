using System.Text;
using System.Threading.Channels;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using NextMqtt.Core.Protocol;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker.Integrations;

/// <summary>Kafka bridge settings (ADR 0007).</summary>
public sealed class KafkaOptions
{
    /// <summary>Forward publishes to Kafka.</summary>
    public bool Enabled { get; set; }

    /// <summary>Comma-separated bootstrap servers.</summary>
    public string BootstrapServers { get; set; } = "127.0.0.1:9092";

    /// <summary>Prefix added to every Kafka topic.</summary>
    public string TopicPrefix { get; set; } = "mqtt.";

    /// <summary>Topic filter that selects messages to forward.</summary>
    public string Filter { get; set; } = "#";

    /// <summary>Buffered messages before new ones are dropped.</summary>
    public int MaxQueue { get; set; } = 5000;

    /// <summary>Producer client id shown in Kafka tooling.</summary>
    public string ClientId { get; set; } = "nextmqtt";

    /// <summary>Batching delay in milliseconds.</summary>
    public double LingerMs { get; set; } = 5;

    /// <summary>Compression: none, gzip, snappy, lz4 or zstd.</summary>
    public string Compression { get; set; } = "none";
}

/// <summary>
/// Forwards matching publishes to Kafka through one background producer.
/// </summary>
/// <remarks>
/// 与 NATS 桥保持同样的取舍：发布路径只入队，队列满就丢弃并计数；断线由客户端库
/// 自身重试，不阻塞 MQTT 投递。集群中只有首次接收发布的节点转发，避免重复。
/// </remarks>
public sealed class KafkaBridge : IAsyncDisposable
{
    private readonly KafkaOptions _options;
    private readonly TopicFilter _filter;
    private readonly IProducer<string, byte[]>? _producer;
    private readonly Channel<(string Topic, byte[] Payload)> _queue;
    private readonly ILogger<KafkaBridge> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task? _worker;
    private long _forwarded;
    private long _dropped;
    private long _failed;

    /// <summary>Builds the producer and starts the delivery worker.</summary>
    /// <param name="options">Bridge settings.</param>
    /// <param name="logger">Logger for delivery failures.</param>
    public KafkaBridge(KafkaOptions options, ILogger<KafkaBridge> logger)
    {
        _options = options;
        _logger = logger;
        _filter = TopicFilter.Parse(options.Filter);
        _queue = Channel.CreateBounded<(string, byte[])>(new BoundedChannelOptions(Math.Max(1, options.MaxQueue))
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

        var config = new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = options.ClientId,
            LingerMs = options.LingerMs,
            // Surface an unreachable broker quickly instead of buffering for minutes.
            MessageTimeoutMs = 5000,
            CompressionType = ParseCompression(options.Compression),
        };

        try
        {
            _producer = new ProducerBuilder<string, byte[]>(config).Build();
            _worker = Task.Run(() => RunAsync(_lifetime.Token), CancellationToken.None);
        }
        catch (Exception ex)
        {
            // A bad broker address must not stop the MQTT broker from starting.
            _logger.LogWarning(ex, "Kafka producer could not be created; bridge disabled");
            _producer = null;
        }
    }

    /// <summary>Messages acknowledged by Kafka.</summary>
    public long Forwarded => Interlocked.Read(ref _forwarded);

    /// <summary>Messages dropped because the queue was full.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Messages that failed after the client library gave up.</summary>
    public long Failed => Interlocked.Read(ref _failed);

    /// <summary>Maps an MQTT topic to a Kafka topic name.</summary>
    /// <remarks>`/` 变 `.`；空格、`*`、`>` 换成下划线，避免非法主题名。</remarks>
    /// <param name="prefix">Configured prefix.</param>
    /// <param name="topic">MQTT topic.</param>
    public static string MapTopic(string prefix, string topic)
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
        if (_producer is null || !_filter.Matches(publish.Topic))
        {
            return;
        }

        if (!_queue.Writer.TryWrite((MapTopic(_options.TopicPrefix, publish.Topic), publish.Payload)))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    /// <summary>Flushes the producer and stops the worker.</summary>
    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _queue.Writer.TryComplete();

        if (_worker is not null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown.
            }
        }

        if (_producer is not null)
        {
            _producer.Flush(TimeSpan.FromSeconds(2));
            _producer.Dispose();
        }

        _lifetime.Dispose();
    }

    /// <summary>Produces queued messages until the queue closes.</summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var (topic, payload) in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await _producer!
                    .ProduceAsync(topic, new Message<string, byte[]> { Value = payload }, cancellationToken)
                    .ConfigureAwait(false);

                Interlocked.Increment(ref _forwarded);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is ProduceException<string, byte[]> or KafkaException)
            {
                Interlocked.Increment(ref _failed);
                _logger.LogWarning(ex, "Kafka produce to {Topic} failed", topic);
            }
        }
    }

    /// <summary>Parses the configured compression name.</summary>
    /// <param name="value">none, gzip, snappy, lz4 or zstd.</param>
    private static CompressionType ParseCompression(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "gzip" => CompressionType.Gzip,
            "snappy" => CompressionType.Snappy,
            "lz4" => CompressionType.Lz4,
            "zstd" => CompressionType.Zstd,
            _ => CompressionType.None,
        };
}
