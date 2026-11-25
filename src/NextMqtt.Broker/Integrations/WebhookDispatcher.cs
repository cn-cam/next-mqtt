using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NextMqtt.Core.Protocol;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker.Integrations;

/// <summary>
/// Delivers matching publishes to HTTP webhooks on a bounded background queue.
/// </summary>
/// <remarks>
/// 每个 webhook 一条独立队列和后台任务：发布路径只做入队，网络慢或失败不会阻塞 MQTT
/// 投递。队列满时丢弃并计数，符合"监控优先于审计完整性"的取舍。
/// </remarks>
public sealed class WebhookDispatcher : IAsyncDisposable
{
    /// <summary>Attempts per event before it counts as failed.</summary>
    /// <remarks>共 3 次，含首次；间隔按 200 毫秒线性增长。</remarks>
    private const int MaxAttempts = 3;

    /// <summary>One entry per valid webhook rule.</summary>
    private readonly List<Target> _targets;
    /// <summary>Client supplied by the host; this class does not own it.</summary>
    private readonly HttpClient _httpClient;
    /// <summary>Logger for skipped rules and exhausted retries.</summary>
    private readonly ILogger<WebhookDispatcher> _logger;
    /// <summary>Cancelled on dispose to stop every worker.</summary>
    private readonly CancellationTokenSource _lifetime = new();
    /// <summary>One delivery task per target.</summary>
    private readonly List<Task> _workers = [];
    /// <summary>Events their endpoint accepted.</summary>
    private long _delivered;
    /// <summary>Events that used up all their attempts.</summary>
    private long _failed;
    /// <summary>Events rejected because a queue was full.</summary>
    private long _dropped;

    /// <summary>Builds one target per valid webhook rule and starts its worker.</summary>
    /// <remarks>URL 或过滤器非法的规则被跳过，其余照常启动。</remarks>
    /// <param name="options">Webhook rules from configuration.</param>
    /// <param name="httpClient">Client used for POSTs; timeouts are applied per rule.</param>
    /// <param name="logger">Logger for delivery failures.</param>
    public WebhookDispatcher(
        IntegrationsOptions options,
        HttpClient httpClient,
        ILogger<WebhookDispatcher> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _targets = [];

        foreach (var rule in options.Webhooks)
        {
            if (string.IsNullOrWhiteSpace(rule.Url)
                || !Uri.TryCreate(rule.Url, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || !MqttTopic.IsValidFilter(rule.Filter, out _))
            {
                logger.LogWarning("Ignoring webhook {Name}: invalid URL or filter", rule.Name);
                continue;
            }

            var target = new Target(
                rule,
                TopicFilter.Parse(rule.Filter),
                Channel.CreateBounded<WebhookEvent>(new BoundedChannelOptions(Math.Max(1, rule.MaxQueue))
                {
                    FullMode = BoundedChannelFullMode.DropWrite,
                    SingleReader = true,
                }));

            _targets.Add(target);
            _workers.Add(Task.Run(() => RunAsync(target, _lifetime.Token), CancellationToken.None));
        }
    }

    /// <summary>Number of active webhook rules.</summary>
    /// <remarks>被跳过的规则不计入。</remarks>
    public int TargetCount => _targets.Count;

    /// <summary>Events accepted by the endpoint since start.</summary>
    /// <remarks>以 2xx 为准；3xx 与 4xx 都按失败处理。</remarks>
    public long Delivered => Interlocked.Read(ref _delivered);

    /// <summary>Events that exhausted their retries.</summary>
    /// <remarks>每条事件只计一次失败。</remarks>
    public long Failed => Interlocked.Read(ref _failed);

    /// <summary>Events dropped because a queue was full.</summary>
    /// <remarks>丢弃发生在入队时，而不是投递时。</remarks>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Queues the event for every matching webhook without blocking.</summary>
    /// <remarks>同时命中多条规则就会发出多份，这是有意为之。</remarks>
    /// <param name="publish">Message that was routed locally.</param>
    public void Publish(PublishPacket publish)
    {
        foreach (var target in _targets)
        {
            if (!target.Filter.Matches(publish.Topic))
            {
                continue;
            }

            var message = new WebhookEvent(
                target.Options.Name,
                publish.Topic,
                publish.QoS,
                publish.Retain,
                DateTimeOffset.UtcNow,
                target.Options.IncludePayload ? Convert.ToBase64String(publish.Payload) : null);

            if (!target.Queue.Writer.TryWrite(message))
            {
                Interlocked.Increment(ref _dropped);
            }
        }
    }

    /// <summary>Stops the workers and waits for in-flight posts.</summary>
    /// <remarks>先取消再排空，正在发送的那一次不会被打断成半条。</remarks>
    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();

        foreach (var target in _targets)
        {
            target.Queue.Writer.TryComplete();
        }

        try
        {
            await Task.WhenAll(_workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }

        _lifetime.Dispose();
    }

    /// <summary>Drains one target's queue and posts every event.</summary>
    /// <remarks>单消费者串行投递，保证同一 webhook 的事件顺序。</remarks>
    /// <param name="target">Webhook rule and its queue.</param>
    /// <param name="cancellationToken">Stops the worker.</param>
    private async Task RunAsync(Target target, CancellationToken cancellationToken)
    {
        await foreach (var message in target.Queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await TrySendAsync(target, message, cancellationToken).ConfigureAwait(false))
            {
                Interlocked.Increment(ref _delivered);
            }
            else
            {
                Interlocked.Increment(ref _failed);
            }
        }
    }

    /// <summary>Posts one event with bounded retries and exponential backoff.</summary>
    /// <remarks>取消时直接失败，不再重试。</remarks>
    /// <param name="target">Webhook rule.</param>
    /// <param name="message">Event to deliver.</param>
    /// <param name="cancellationToken">Stops retrying.</param>
    /// <returns><c>true</c> when the endpoint accepted the event.</returns>
    private async Task<bool> TrySendAsync(Target target, WebhookEvent message, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(500, target.Options.TimeoutMs)));

                using var request = new HttpRequestMessage(HttpMethod.Post, target.Options.Url)
                {
                    Content = JsonContent.Create(message),
                };

                foreach (var header in target.Options.Headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                using var response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                _logger.LogDebug(
                    "Webhook {Name} returned {Status} (attempt {Attempt})",
                    target.Options.Name,
                    (int)response.StatusCode,
                    attempt);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Webhook {Name} failed (attempt {Attempt})",
                    target.Options.Name,
                    attempt);
            }

            if (attempt < MaxAttempts)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
        }

        _logger.LogWarning("Webhook {Name} dropped an event after {Attempts} attempts", target.Options.Name, MaxAttempts);
        return false;
    }

    /// <summary>Event body sent to a webhook endpoint.</summary>
    /// <remarks>字段名是外部契约，改动会影响已接入的接收方。</remarks>
    /// <param name="Name">Webhook name.</param>
    /// <param name="Topic">Published topic.</param>
    /// <param name="QoS">QoS of the publish.</param>
    /// <param name="Retain">Retain flag of the publish.</param>
    /// <param name="ReceivedAt">When the broker accepted the message.</param>
    /// <param name="Payload">Base64 payload, or null when payloads are disabled.</param>
    public sealed record WebhookEvent(
        string Name,
        string Topic,
        MqttQoS QoS,
        bool Retain,
        DateTimeOffset ReceivedAt,
        string? Payload);

    /// <summary>One rule plus its queue.</summary>
    /// <remarks>队列容量来自该规则的 MaxQueue，至少为 1。</remarks>
    /// <param name="Options">Configured rule.</param>
    /// <param name="Filter">Parsed filter.</param>
    /// <param name="Queue">Bounded event queue.</param>
    private sealed record Target(
        WebhookOptions Options,
        TopicFilter Filter,
        Channel<WebhookEvent> Queue);
}
