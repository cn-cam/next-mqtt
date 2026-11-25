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
    private const int MaxAttempts = 3;

    private readonly List<Target> _targets;
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookDispatcher> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Task> _workers = [];
    private long _delivered;
    private long _failed;
    private long _dropped;

    /// <summary>Builds one target per valid webhook rule and starts its worker.</summary>
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
    public int TargetCount => _targets.Count;

    /// <summary>Events accepted by the endpoint since start.</summary>
    public long Delivered => Interlocked.Read(ref _delivered);

    /// <summary>Events that exhausted their retries.</summary>
    public long Failed => Interlocked.Read(ref _failed);

    /// <summary>Events dropped because a queue was full.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Queues the event for every matching webhook without blocking.</summary>
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
    /// <param name="Options">Configured rule.</param>
    /// <param name="Filter">Parsed filter.</param>
    /// <param name="Queue">Bounded event queue.</param>
    private sealed record Target(
        WebhookOptions Options,
        TopicFilter Filter,
        Channel<WebhookEvent> Queue);
}
