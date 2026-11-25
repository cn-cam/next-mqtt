using Microsoft.Extensions.Logging;
using NextMqtt.Broker.Clustering;
using NextMqtt.Broker.Diagnostics;
using NextMqtt.Broker.Integrations;
using NextMqtt.Broker.Messaging;
using NextMqtt.Broker.Options;
using NextMqtt.Broker.RateLimiting;
using NextMqtt.Broker.Security;
using NextMqtt.Broker.Sessions;
using NextMqtt.Broker.Storage;
using NextMqtt.Broker.Subscriptions;
using NextMqtt.Core.Protocol;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker;

/// <summary>Result of binding a connection to a session.</summary>
public sealed record SessionAttachResult(
    BrokerSession Session,
    bool SessionPresent,
    IClientConnection? PreviousConnection);

/// <summary>
/// The broker facade: authentication, session lifecycle, routing, retained
/// messages and administration.
/// </summary>
public sealed class MqttBroker : IAsyncDisposable
{
    private readonly ILogger<MqttBroker> _logger;
    private readonly SemaphoreSlim _attachGate = new(1, 1);
    private readonly Dictionary<string, CancellationTokenSource> _pendingWills = new(StringComparer.Ordinal);
    private readonly object _willGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ISessionSnapshotStore? _sessionStore;
    private readonly TimeSpan _sessionSnapshotInterval;
    private DateTimeOffset _nextSessionSnapshot = DateTimeOffset.MinValue;
    private (int Sessions, int Queued, int Subscriptions) _lastSessionFingerprint = (-1, -1, -1);
    private Task? _maintenanceTask;
    private bool _disposed;

    /// <summary>Creates the broker facade and restores persisted state.</summary>
    /// <remarks>构造时重放保留消息日志与会话快照，因此调用方应先准备好存储实现。</remarks>
    public MqttBroker(
        BrokerOptions options,
        IClientAuthenticator authenticator,
        ITopicAuthorizer authorizer,
        IClusterRouter cluster,
        ILogger<MqttBroker> logger,
        IRetainedJournal? retainedJournal = null,
        ISessionSnapshotStore? sessionStore = null,
        int sessionSnapshotSeconds = 30,
        IEnumerable<IEnhancedAuthenticator>? enhancedAuthenticators = null,
        WebhookDispatcher? webhooks = null,
        RuleEngine? rules = null,
        NatsBridge? nats = null,
        KafkaBridge? kafka = null,
        RabbitBridge? rabbit = null)
    {
        Options = options;
        Authenticator = authenticator;
        Authorizer = authorizer;
        Cluster = cluster;
        ConnectionLimits = new ConnectionRateLimiter(options.RateLimits);
        TopicQuotas = new TopicQuotaLimiter(options.RateLimits);
        Retained = new RetainedMessageStore(retainedJournal);
        _logger = logger;
        _sessionStore = sessionStore;
        Webhooks = webhooks;
        Rules = rules;
        Nats = nats;
        Kafka = kafka;
        Rabbit = rabbit;

        foreach (var provider in enhancedAuthenticators ?? [])
        {
            if (EnhancedAuthenticators is Dictionary<string, IEnhancedAuthenticator> registry)
            {
                registry[provider.Method] = provider;
            }
        }
        _sessionSnapshotInterval = TimeSpan.FromSeconds(Math.Max(1, sessionSnapshotSeconds));

        if (sessionStore is not null)
        {
            RestoreSessions(sessionStore.Load());
        }
    }

    /// <summary>Limits and timings this broker was created with.</summary>
    public BrokerOptions Options { get; }

    /// <summary>Credential validator used by CONNECT.</summary>
    public IClientAuthenticator Authenticator { get; }

    /// <summary>Topic ACL used for publish and subscribe.</summary>
    public ITopicAuthorizer Authorizer { get; }

    /// <summary>Cross-node router; a null implementation when clustering is off.</summary>
    public IClusterRouter Cluster { get; }

    /// <summary>Per-address CONNECT limiter, disabled unless configured.</summary>
    public ConnectionRateLimiter ConnectionLimits { get; }

    /// <summary>Shared per-topic publish quotas, disabled unless configured.</summary>
    public TopicQuotaLimiter TopicQuotas { get; }

    /// <summary>Optional NATS forwarding for locally accepted messages.</summary>
    public NatsBridge? Nats { get; }

    /// <summary>Optional Kafka forwarding for locally accepted messages.</summary>
    public KafkaBridge? Kafka { get; }

    /// <summary>Optional RabbitMQ forwarding for locally accepted messages.</summary>
    public RabbitBridge? Rabbit { get; }

    /// <summary>Optional routing rules that can discard messages before storage.</summary>
    public RuleEngine? Rules { get; }

    /// <summary>Optional webhook fan-out for locally accepted messages.</summary>
    public WebhookDispatcher? Webhooks { get; }

    /// <summary>Enhanced authentication providers by method name.</summary>
    public IReadOnlyDictionary<string, IEnhancedAuthenticator> EnhancedAuthenticators { get; }
        = new Dictionary<string, IEnhancedAuthenticator>(StringComparer.Ordinal);

    /// <summary>
    /// Number of live cluster nodes used to split rate-limit budgets; 1 when the
    /// broker runs standalone.
    /// </summary>
    /// <remarks>限流按节点均分：节点越多，单节点放行额度越小。</remarks>
    public int ClusterSize { get; private set; } = 1;

    /// <summary>All sessions, online and offline.</summary>
    public SessionRegistry Sessions { get; } = new();

    /// <summary>Subscription index used for routing.</summary>
    public SubscriptionIndex Subscriptions { get; } = new();

    /// <summary>Retained messages, optionally journaled to disk.</summary>
    public RetainedMessageStore Retained { get; }

    /// <summary>Monotonic counters exposed through the API and metrics endpoint.</summary>
    public BrokerMetrics Metrics { get; } = new();

    /// <summary>Inbound publish rate for the last 60 seconds.</summary>
    public RateTracker PublishRate { get; } = new();

    /// <summary>Outbound delivery rate for the last 60 seconds.</summary>
    public RateTracker DeliveryRate { get; } = new();

    /// <summary>Returns the provider registered for an authentication method, if any.</summary>
    /// <param name="method">Authentication Method from CONNECT.</param>
    /// <returns>Provider or null when the method is not supported.</returns>
    public IEnhancedAuthenticator? GetEnhancedAuthenticator(string method)
        => EnhancedAuthenticators.TryGetValue(method, out var authenticator) ? authenticator : null;

    /// <summary>Validates credentials through the configured authenticator.</summary>
    /// <param name="context">Client identity and credentials from CONNECT.</param>
    /// <param name="cancellationToken">Cancels the async authentication step.</param>
    public ValueTask<AuthenticationResult> AuthenticateAsync(
        ClientAuthenticationContext context,
        CancellationToken cancellationToken = default)
        => Authenticator.AuthenticateAsync(context, cancellationToken);

    /// <summary>Binds a connection to a session, taking over an existing one.</summary>
    /// <remarks>cleanStart 时清除旧状态；否则复用会话并报告 session present。</remarks>
    /// <param name="connect">CONNECT packet from the client.</param>
    /// <param name="principal">Authenticated identity.</param>
    /// <param name="connection">Connection that will own the session.</param>
    /// <param name="cancellationToken">Cancels the attach.</param>
    public async Task<SessionAttachResult> AttachSessionAsync(
        ConnectPacket connect,
        ClientPrincipal principal,
        IClientConnection connection,
        CancellationToken cancellationToken = default)
    {
        await _attachGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        BrokerSession session;
        IClientConnection? previous;
        bool sessionPresent;
        bool removeDiscarded = false;

        try
        {
            var existing = Sessions.Get(connect.ClientId);
            var persistent = ResolvePersistence(connect, out var expiry);

            if (existing is not null
                && (connect.CleanStart || existing.ProtocolVersion != connect.ProtocolVersion))
            {
                previous = existing.Connection;
                Sessions.Remove(existing.ClientId);
                Subscriptions.RemoveAll(existing.ClientId);
                removeDiscarded = true;
                existing.Dispose();
                existing = null;
            }
            else
            {
                previous = existing?.Connection;
            }

            if (existing is null)
            {
                session = new BrokerSession(connect.ClientId, Options, Metrics);
                if (!Sessions.TryAdd(session))
                {
                    session.Dispose();
                    throw new InvalidOperationException($"Session '{connect.ClientId}' already exists.");
                }

                sessionPresent = false;
            }
            else
            {
                session = existing;
                sessionPresent = true;
            }

            session.Bind(connection, connect, principal, persistent, expiry);
            CancelPendingWill(connect.ClientId);
        }
        finally
        {
            _attachGate.Release();
        }

        if (removeDiscarded)
        {
            _logger.LogDebug("Discarded previous session state for client {ClientId}", connect.ClientId);
        }

        if (previous is not null && !ReferenceEquals(previous, connection))
        {
            await previous.DisconnectAsync(
                MqttReasonCodes.SessionTakenOver,
                "Session taken over by a new connection.",
                cancellationToken).ConfigureAwait(false);
        }

        if (sessionPresent)
        {
            session.Resume();
        }

        return new SessionAttachResult(session, sessionPresent, previous);
    }

    /// <summary>Applies disconnect rules: will publication, expiry and metrics.</summary>
    /// <param name="session">Session whose connection ended.</param>
    /// <param name="connection">Connection that closed.</param>
    /// <param name="disconnect">DISCONNECT packet, when the client sent one.</param>
    /// <param name="graceful">True when the socket closed without a protocol error.</param>
    /// <param name="cancellationToken">Cancels will publication.</param>
    public async Task OnConnectionClosedAsync(
        BrokerSession session,
        IClientConnection connection,
        DisconnectPacket? disconnect,
        bool graceful,
        CancellationToken cancellationToken = default)
    {
        if (!session.Detach(connection))
        {
            return;
        }

        Metrics.ConnectionClosed();

        if (disconnect is not null
            && session.ProtocolVersion == MqttProtocolVersion.V500
            && disconnect.Properties.GetUInt32(MqttPropertyId.SessionExpiryInterval) is { } requestedExpiry)
        {
            session.UpdateSessionExpiry(requestedExpiry);
        }

        var will = session.Will;
        var publishWill = !graceful
            || (disconnect is not null && disconnect.ReasonCode == MqttReasonCodes.DisconnectWithWill);

        var remove = !session.Persistent || session.SessionExpiryInterval == 0;

        if (remove)
        {
            RemoveSession(session);
        }
        else
        {
            session.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(session.SessionExpiryInterval);
        }

        if (publishWill && will is not null)
        {
            ScheduleWill(session.ClientId, will, session.SessionExpiryInterval);
        }
        else
        {
            session.ClearWill();
        }

        await Task.CompletedTask;
    }

    /// <summary>Routes a publish to local subscribers and, optionally, to peers.</summary>
    public async ValueTask RouteAsync(
        PublishPacket publish,
        string? sourceClientId,
        CancellationToken cancellationToken = default,
        bool forwardToCluster = true,
        bool applyRetained = true)
    {
        if (!MqttTopic.IsValidTopicName(publish.Topic, out _))
        {
            _logger.LogWarning("Dropping publish with invalid topic");
            Metrics.MessageDropped();
            return;
        }

        var messageExpiry = publish.Properties.GetUInt32(MqttPropertyId.MessageExpiryInterval);

        if (messageExpiry == 0)
        {
            if (publish.Retain && publish.Payload.Length == 0)
            {
                Retained.Remove(publish.Topic);
            }

            Metrics.MessageDropped();
            return;
        }

        if (publish.Retain && applyRetained)
        {
            Retained.Publish(publish, DateTimeOffset.UtcNow);
        }

        Metrics.PublishReceived();
        PublishRate.Increment();

        // Only the node that first accepted the publish feeds integrations, so a
        // cluster does not post the same message once per node.
        if (forwardToCluster)
        {
            Webhooks?.Publish(publish);
            Nats?.Publish(publish);
            Kafka?.Publish(publish);
            Rabbit?.Publish(publish);
        }

        if (Rules?.ShouldDrop(publish.Topic) == true)
        {
            Metrics.MessageDropped();
            return;
        }

        if (Rules is not null
            && Rules.TryRewrite(publish.Topic, out var rewritten)
            && !string.Equals(rewritten, publish.Topic, StringComparison.Ordinal))
        {
            if (!MqttTopic.IsValidTopicName(rewritten, out var rewriteError))
            {
                _logger.LogWarning(
                    "Dropping message: rewrite produced an invalid topic {Topic}: {Error}",
                    rewritten,
                    rewriteError);
                Metrics.MessageDropped();
                return;
            }

            publish = new PublishPacket
            {
                Topic = rewritten,
                Payload = publish.Payload,
                QoS = publish.QoS,
                Retain = publish.Retain,
                Duplicate = publish.Duplicate,
                PacketId = publish.PacketId,
                Properties = publish.Properties,
            };
        }

        var messageId = Guid.NewGuid().ToString("N");
        var matches = Subscriptions.Match(publish.Topic);
        if (matches.Count > 0)
        {
            DeliverMatches(publish, matches, sourceClientId, messageId);
        }

        if (forwardToCluster && Cluster.Enabled)
        {
            await Cluster.RouteAsync(publish, messageId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Entry point used by the cluster transport for remote publishes.</summary>
    public ValueTask RouteFromClusterAsync(PublishPacket publish, CancellationToken cancellationToken = default)
        => RouteAsync(publish, sourceClientId: null, cancellationToken, forwardToCluster: false);

    /// <summary>Applies a retained write forwarded by a peer using the origin timestamp.</summary>
    /// <remarks>用发起节点的时间做 last-write-wins，避免各节点本地时钟导致的顺序不一致。</remarks>
    /// <param name="publish">Retained publish forwarded by a peer.</param>
    /// <param name="retainedAt">When the origin node accepted the message.</param>
    /// <param name="cancellationToken">Cancels routing.</param>
    public ValueTask RouteFromClusterRetainedAsync(
        PublishPacket publish,
        DateTimeOffset retainedAt,
        CancellationToken cancellationToken = default)
    {
        Retained.Publish(publish, retainedAt);
        return RouteAsync(
            publish,
            sourceClientId: null,
            cancellationToken,
            forwardToCluster: false,
            applyRetained: false);
    }

    /// <summary>Delivers a message forwarded specifically for one shared group.</summary>
    /// <remarks>只有被选中的节点会收到这条消息，因此这里不再做所有权判断，也不再转发。</remarks>
    /// <param name="publish">Message forwarded by a peer.</param>
    /// <param name="sharedGroup">Group the origin selected for this node.</param>
    /// <param name="cancellationToken">Cancels routing.</param>
    public ValueTask RouteSharedFromClusterAsync(
        PublishPacket publish,
        string sharedGroup,
        CancellationToken cancellationToken = default)
    {
        var matches = Subscriptions.Match(publish.Topic)
            .Where(match => string.Equals(match.SharedGroup, sharedGroup, StringComparison.Ordinal))
            .ToList();

        if (matches.Count > 0)
        {
            DeliverMatches(
                publish,
                matches,
                sourceClientId: null,
                messageId: Guid.NewGuid().ToString("N"),
                checkSharedOwnership: false);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Delivers one message to the local sessions matched by the index.</summary>
    /// <param name="publish">Message being routed.</param>
    /// <param name="matches">Local subscription matches.</param>
    /// <param name="sourceClientId">Publishing client, used by noLocal.</param>
    /// <param name="messageId">Stable id used for shared-group ownership.</param>
    /// <param name="checkSharedOwnership">False when the message is already shared-scoped.</param>
    private void DeliverMatches(
        PublishPacket publish,
        List<SubscriptionMatch> matches,
        string? sourceClientId,
        string messageId,
        bool checkSharedOwnership = true)
    {
        var deliveries = new Dictionary<string, DeliveryAggregate>(StringComparer.Ordinal);

        foreach (var match in matches)
        {
            var subscription = match.Subscription;

            if (checkSharedOwnership
                && match.SharedGroup is { } sharedGroup
                && Cluster.Enabled
                && !Cluster.OwnsSharedGroup(publish.Topic, sharedGroup, messageId))
            {
                continue;
            }

            if (subscription.Options.NoLocal
                && sourceClientId is not null
                && string.Equals(subscription.ClientId, sourceClientId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!deliveries.TryGetValue(subscription.ClientId, out var aggregate))
            {
                aggregate = new DeliveryAggregate();
                deliveries[subscription.ClientId] = aggregate;
            }

            var qos = (MqttQoS)Math.Min((int)publish.QoS, (int)subscription.Options.QoS);
            if (qos > aggregate.QoS)
            {
                aggregate.QoS = qos;
            }

            aggregate.RetainAsPublished |= subscription.Options.RetainAsPublished;

            if (subscription.Identifier is { } identifier)
            {
                aggregate.SubscriptionIdentifiers.Add(identifier);
            }
        }

        foreach (var (clientId, aggregate) in deliveries)
        {
            var session = Sessions.Get(clientId);
            if (session is null)
            {
                continue;
            }

            var message = BuildOutboundMessage(publish, aggregate);
            if (session.Deliver(message))
            {
                DeliveryRate.Increment();
            }
        }
    }

    /// <summary>Publishes internally, for example for a will or the dashboard.</summary>
    /// <param name="topic">Topic name.</param>
    /// <param name="payload">Payload bytes.</param>
    /// <param name="qos">Delivery guarantee to use.</param>
    /// <param name="cancellationToken">Cancels routing.</param>
    public async ValueTask PublishAsync(
        string topic,
        byte[] payload,
        MqttQoS qos = MqttQoS.AtMostOnce,
        bool retain = false,
        CancellationToken cancellationToken = default)
    {
        if (!MqttTopic.IsValidTopicName(topic, out var error))
        {
            throw new ArgumentException(error, nameof(topic));
        }

        var publish = new PublishPacket
        {
            Topic = topic,
            Payload = payload,
            QoS = qos,
            Retain = retain,
        };

        await RouteAsync(publish, sourceClientId: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Starts the maintenance loop and the cluster router.</summary>
    /// <param name="cancellationToken">Cancels startup.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _maintenanceTask ??= Task.Run(() => MaintenanceLoopAsync(_lifetime.Token), CancellationToken.None);
        return Cluster.StartAsync(cancellationToken).AsTask();
    }

    /// <summary>Stops background loops without disposing session state.</summary>
    public async Task StopAsync()
    {
        _lifetime.Cancel();
        await Cluster.StopAsync().ConfigureAwait(false);

        if (_maintenanceTask is not null)
        {
            try
            {
                await _maintenanceTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    /// <summary>Samples counters and gauges for the statistics endpoint.</summary>
    public BrokerStats GetStats()
        => Metrics.Snapshot(
            Sessions.ConnectedCount(),
            Sessions.Count,
            Subscriptions.Count,
            Retained.Count,
            PublishRate.Snapshot());

    /// <summary>Lists sessions with subscription counts for the clients table.</summary>
    public IReadOnlyList<ClientSnapshot> GetClients()
        => Sessions.Snapshot()
            .Select(session => session.Snapshot(Subscriptions.GetByClient(session.ClientId).Count))
            .OrderBy(client => client.ClientId, StringComparer.Ordinal)
            .ToList();

    /// <summary>Lists every subscription for the subscriptions table.</summary>
    public IReadOnlyList<SubscriptionSnapshot> GetSubscriptions()
        => Subscriptions.GetAll()
            .Select(subscription => new SubscriptionSnapshot(
                subscription.ClientId,
                subscription.Filter.Original,
                subscription.Filter.SharedGroup,
                subscription.Options.QoS.ToString(),
                subscription.Options.NoLocal,
                subscription.Options.RetainAsPublished,
                subscription.Options.RetainHandling.ToString(),
                subscription.Identifier))
            .OrderBy(subscription => subscription.ClientId, StringComparer.Ordinal)
            .ThenBy(subscription => subscription.Filter, StringComparer.Ordinal)
            .ToList();

    /// <summary>Lists retained messages for the retained table.</summary>
    public IReadOnlyList<RetainedSnapshot> GetRetained()
        => Retained.Snapshot()
            .Select(message => new RetainedSnapshot(
                message.Topic,
                message.Payload.Length,
                message.QoS.ToString(),
                message.StoredAt,
                message.ExpiresAt))
            .OrderBy(message => message.Topic, StringComparer.Ordinal)
            .ToList();

    /// <summary>Administratively disconnects a client with 0x98.</summary>
    /// <param name="clientId">Session to disconnect.</param>
    /// <param name="cancellationToken">Cancels the disconnect.</param>
    /// <returns><c>true</c> when a connected client was found and kicked.</returns>
    public async Task<bool> KickClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var session = Sessions.Get(clientId);
        if (session is null)
        {
            return false;
        }

        var connection = session.Connection;

        if (connection is not null)
        {
            await connection.DisconnectAsync(
                MqttReasonCodes.AdministrativeAction,
                "Disconnected by an administrator.",
                cancellationToken).ConfigureAwait(false);

            return true;
        }

        RemoveSession(session);
        return true;
    }

    /// <summary>Deletes one retained message.</summary>
    /// <param name="topic">Exact topic name.</param>
    public bool DeleteRetained(string topic) => Retained.Remove(topic);

    /// <summary>Number of retained messages after an administrative clear request.</summary>
    /// <returns>Remaining count, which is zero after the call.</returns>
    public int ClearRetained() => Retained.Count;

    /// <summary>Membership view for the cluster page and API.</summary>
    public ClusterSnapshot GetCluster() => Cluster.GetSnapshot();

    /// <summary>Flushes session state and releases resources.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        PersistSessions(force: true);

        lock (_willGate)
        {
            foreach (var cts in _pendingWills.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }

            _pendingWills.Clear();
        }

        foreach (var session in Sessions.Snapshot())
        {
            Subscriptions.RemoveAll(session.ClientId);
            session.Dispose();
        }

        Sessions.Clear();
        _attachGate.Dispose();
        _lifetime.Dispose();
    }

    private void RestoreSessions(IReadOnlyList<PersistedSession> persisted)
    {
        if (persisted.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var restored = 0;

        foreach (var record in persisted)
        {
            if (string.IsNullOrWhiteSpace(record.ClientId)
                || record.ClientId.Length > Options.MaxClientIdLength
                || (record.ExpiresAt is { } expiry && expiry <= now)
                || Sessions.Get(record.ClientId) is not null)
            {
                continue;
            }

            var claims = new Dictionary<string, string>(StringComparer.Ordinal);

            if (record.Claims is not null)
            {
                foreach (var pair in record.Claims)
                {
                    claims[pair.Key] = pair.Value;
                }
            }

            var principal = new ClientPrincipal(
                record.Name ?? record.ClientId,
                record.Username,
                record.ClientId,
                record.IsSuperuser,
                claims);

            var session = new BrokerSession(record.ClientId, Options, Metrics);

            session.Restore(
                principal,
                (MqttProtocolVersion)record.ProtocolVersion,
                record.SessionExpiryInterval,
                record.ExpiresAt,
                record.LastSeen,
                record.Queue.Select(TryToOutbound).OfType<OutboundMessage>());

            if (!Sessions.TryAdd(session))
            {
                session.Dispose();
                continue;
            }

            foreach (var subscription in record.Subscriptions)
            {
                if (!MqttTopic.IsValidFilter(subscription.Filter, out _))
                {
                    continue;
                }

                Subscriptions.AddOrReplace(new Subscription
                {
                    ClientId = record.ClientId,
                    Filter = TopicFilter.Parse(subscription.Filter),
                    Options = new MqttSubscriptionOptions(
                        (MqttQoS)subscription.QoS,
                        subscription.NoLocal,
                        subscription.RetainAsPublished,
                        (MqttRetainHandling)subscription.RetainHandling),
                    Identifier = subscription.Identifier,
                });
            }

            restored++;
        }

        if (restored > 0)
        {
            _logger.LogInformation("Restored {Count} persistent session(s) from disk", restored);
        }
    }

    /// <summary>Exports retained messages and persistent sessions.</summary>
    public BrokerBackup CreateBackup() => new()
    {
        CreatedAt = DateTimeOffset.UtcNow,
        Retained = Retained.Snapshot()
            .Select(message => new PersistedRetained
            {
                Topic = message.Topic,
                Payload = Convert.ToBase64String(message.Payload),
                QoS = (int)message.QoS,
                StoredAt = message.StoredAt,
                ExpiresAt = message.ExpiresAt,
                Properties = PropertyRecord.FromProperties(message.Properties),
            })
            .ToList(),
        Sessions = SnapshotSessions(),
    };

    /// <summary>
    /// Imports a backup. Retained messages replace the current set; sessions are
    /// only added when no live session with the same client id exists.
    /// </summary>
    public RestoreResult RestoreBackup(BrokerBackup backup)
    {
        var now = DateTimeOffset.UtcNow;
        var retained = 0;

        Retained.Clear();

        foreach (var item in backup.Retained)
        {
            try
            {
                var message = new RetainedMessage
                {
                    Topic = item.Topic,
                    Payload = Convert.FromBase64String(item.Payload),
                    QoS = (MqttQoS)item.QoS,
                    StoredAt = item.StoredAt,
                    ExpiresAt = item.ExpiresAt,
                    Properties = PropertyRecord.ToProperties(item.Properties),
                };

                if (message.IsExpired(now) || !MqttTopic.IsValidTopicName(message.Topic, out _))
                {
                    continue;
                }

                Retained.Set(message);
                retained++;
            }
            catch (FormatException)
            {
                // Skip an unreadable payload rather than failing the whole import.
            }
        }

        var before = Sessions.Count;

        RestoreSessions(
            backup.Sessions
                .Where(session => Sessions.Get(session.ClientId) is null)
                .ToList());

        var sessions = Sessions.Count - before;

        PersistSessions(force: true);
        return new RestoreResult(retained, sessions);
    }

    /// <summary>
    /// Waits for in-flight QoS traffic to be acknowledged before shutdown.
    /// Returns the number of sessions that still had inflight messages.
    /// </summary>
    public async Task<int> DrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (true)
        {
            var pending = Sessions.Snapshot().Count(session => session.IsOnline && session.InflightCount > 0);

            if (pending == 0 || DateTimeOffset.UtcNow >= deadline)
            {
                return pending;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return pending;
            }
        }
    }

    /// <summary>
    /// Recomputes the per-node share of every rate limit from live membership.
    /// </summary>
    /// <remarks>配置的是集群总量：单节点不缩放，多节点按存活节点均分。</remarks>
    private void ApplyClusterRateScale()
    {
        var alive = Cluster.GetSnapshot().Nodes.Count(node => node.Status == "alive");
        var size = Math.Max(1, alive);

        if (size == ClusterSize)
        {
            return;
        }

        ClusterSize = size;
        ConnectionLimits.SetClusterSize(size);
        TopicQuotas.SetClusterSize(size);

        foreach (var session in Sessions.Snapshot())
        {
            if (session.Connection is IRateScalable scalable)
            {
                scalable.SetClusterSize(size);
            }
        }

        _logger.LogInformation("Rate-limit budgets rescaled for {Nodes} live node(s)", size);
    }

    private void PersistSessionsIfDue(DateTimeOffset now)    {
        if (_sessionStore is null || now < _nextSessionSnapshot)
        {
            return;
        }

        _nextSessionSnapshot = now + _sessionSnapshotInterval;
        PersistSessions(force: false);
    }

    private void PersistSessions(bool force)
    {
        if (_sessionStore is null)
        {
            return;
        }

        var snapshot = SnapshotSessions();
        var fingerprint = (
            snapshot.Count,
            snapshot.Sum(session => session.Queue.Count),
            snapshot.Sum(session => session.Subscriptions.Count));

        if (!force && fingerprint == _lastSessionFingerprint)
        {
            return;
        }

        _sessionStore.Save(snapshot);
        _lastSessionFingerprint = fingerprint;
    }

    private List<PersistedSession> SnapshotSessions()
    {
        var subscriptions = Subscriptions.GetAll()
            .GroupBy(subscription => subscription.ClientId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var snapshot = new List<PersistedSession>();

        foreach (var session in Sessions.Snapshot())
        {
            if (!session.Persistent)
            {
                continue;
            }

            var principal = session.Principal;

            var record = new PersistedSession
            {
                ClientId = session.ClientId,
                Name = principal.Name,
                Username = principal.Username,
                IsSuperuser = principal.IsSuperuser,
                Claims = principal.Claims.Count == 0
                    ? null
                    : principal.Claims.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                ProtocolVersion = (int)session.ProtocolVersion,
                SessionExpiryInterval = session.SessionExpiryInterval,
                ExpiresAt = session.ExpiresAt,
                LastSeen = session.LastSeen,
            };

            if (subscriptions.TryGetValue(session.ClientId, out var clientSubscriptions))
            {
                record.Subscriptions = clientSubscriptions
                    .Select(subscription => new PersistedSubscription
                    {
                        Filter = subscription.Filter.Original,
                        QoS = (int)subscription.Options.QoS,
                        NoLocal = subscription.Options.NoLocal,
                        RetainAsPublished = subscription.Options.RetainAsPublished,
                        RetainHandling = (int)subscription.Options.RetainHandling,
                        Identifier = subscription.Identifier,
                    })
                    .ToList();
            }

            record.Queue = session.SnapshotQueue()
                .Select(message => new PersistedMessage
                {
                    Topic = message.Topic,
                    Payload = Convert.ToBase64String(message.Payload),
                    QoS = (int)message.QoS,
                    Retain = message.Retain,
                    ExpiresAt = message.ExpiresAt,
                    Properties = PropertyRecord.FromProperties(message.Properties),
                })
                .ToList();

            snapshot.Add(record);
        }

        return snapshot;
    }

    private static OutboundMessage? TryToOutbound(PersistedMessage message)
    {
        try
        {
            return new OutboundMessage
            {
                Topic = message.Topic,
                Payload = Convert.FromBase64String(message.Payload),
                QoS = (MqttQoS)message.QoS,
                Retain = message.Retain,
                ExpiresAt = message.ExpiresAt,
                Properties = PropertyRecord.ToProperties(message.Properties),
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private async Task MaintenanceLoopAsync(CancellationToken cancellationToken)    {
        var interval = Options.SweepInterval < Options.RetransmitInterval
            ? Options.SweepInterval
            : Options.RetransmitInterval;

        if (interval < TimeSpan.FromSeconds(1))
        {
            interval = TimeSpan.FromSeconds(1);
        }

        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                Sweep(DateTimeOffset.UtcNow);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private void Sweep(DateTimeOffset now)
    {
        Retained.PruneExpired(now);
        PersistSessionsIfDue(now);
        ApplyClusterRateScale();

        foreach (var session in Sessions.Snapshot())
        {
            if (session.IsOnline)
            {
                session.RetransmitDue(now);
                continue;
            }

            if (session.ExpiresAt is { } expiry && expiry <= now)
            {
                Metrics.SessionExpired();
                RemoveSession(session);
            }
        }
    }

    private void RemoveSession(BrokerSession session)
    {
        Sessions.Remove(session.ClientId);
        Subscriptions.RemoveAll(session.ClientId);
        session.Dispose();
    }

    private void ScheduleWill(string clientId, MqttWill will, uint sessionExpiryInterval)
    {
        var delaySeconds = will.Properties.GetUInt32(MqttPropertyId.WillDelayInterval) ?? 0;

        if (sessionExpiryInterval > 0 && sessionExpiryInterval != uint.MaxValue)
        {
            delaySeconds = Math.Min(delaySeconds, sessionExpiryInterval);
        }

        var cts = new CancellationTokenSource();

        lock (_willGate)
        {
            if (_pendingWills.TryGetValue(clientId, out var existing))
            {
                existing.Cancel();
                existing.Dispose();
            }

            _pendingWills[clientId] = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (delaySeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cts.Token).ConfigureAwait(false);
                }

                await PublishWillAsync(will, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Session reconnected or the broker is shutting down.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish will message for client {ClientId}", clientId);
            }
            finally
            {
                lock (_willGate)
                {
                    if (_pendingWills.TryGetValue(clientId, out var current) && ReferenceEquals(current, cts))
                    {
                        _pendingWills.Remove(clientId);
                    }
                }

                cts.Dispose();
            }
        }, CancellationToken.None);
    }

    private void CancelPendingWill(string clientId)
    {
        lock (_willGate)
        {
            if (_pendingWills.Remove(clientId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }
    }

    private async Task PublishWillAsync(MqttWill will, CancellationToken cancellationToken)
    {
        var properties = new MqttProperties();

        foreach (var property in will.Properties.Items)
        {
            switch (property.Id)
            {
                case MqttPropertyId.PayloadFormatIndicator:
                case MqttPropertyId.MessageExpiryInterval:
                case MqttPropertyId.ContentType:
                case MqttPropertyId.ResponseTopic:
                case MqttPropertyId.CorrelationData:
                case MqttPropertyId.UserProperty:
                    properties.Add(property.Id, property.Value);
                    break;
            }
        }

        var publish = new PublishPacket
        {
            Topic = will.Topic,
            Payload = will.Payload,
            QoS = will.QoS,
            Retain = will.Retain,
            Properties = properties,
        };

        await RouteAsync(publish, sourceClientId: null, cancellationToken).ConfigureAwait(false);
    }

    private OutboundMessage BuildOutboundMessage(PublishPacket publish, DeliveryAggregate aggregate)
    {
        var properties = new MqttProperties();

        foreach (var property in publish.Properties.Items)
        {
            if (property.Id == MqttPropertyId.TopicAlias)
            {
                continue;
            }

            properties.Add(property.Id, property.Value);
        }

        foreach (var identifier in aggregate.SubscriptionIdentifiers)
        {
            properties.AddVariableByteInteger(MqttPropertyId.SubscriptionIdentifier, identifier);
        }

        DateTimeOffset? expiresAt = null;

        if (publish.Properties.GetUInt32(MqttPropertyId.MessageExpiryInterval) is { } expirySeconds and > 0)
        {
            expiresAt = DateTimeOffset.UtcNow.AddSeconds(expirySeconds);
        }

        return new OutboundMessage
        {
            Topic = publish.Topic,
            Payload = publish.Payload,
            QoS = aggregate.QoS,
            Retain = aggregate.RetainAsPublished && publish.Retain,
            Properties = properties,
            ExpiresAt = expiresAt,
        };
    }

    private static bool ResolvePersistence(ConnectPacket connect, out uint expiry)
    {
        if (connect.ProtocolVersion == MqttProtocolVersion.V500)
        {
            expiry = connect.Properties.GetUInt32(MqttPropertyId.SessionExpiryInterval) ?? 0;
            return expiry > 0;
        }

        if (connect.CleanStart)
        {
            expiry = 0;
            return false;
        }

        expiry = uint.MaxValue;
        return true;
    }

    private sealed class DeliveryAggregate
    {
        public MqttQoS QoS { get; set; } = MqttQoS.AtMostOnce;

        public bool RetainAsPublished { get; set; }

        public HashSet<int> SubscriptionIdentifiers { get; } = [];
    }
}
