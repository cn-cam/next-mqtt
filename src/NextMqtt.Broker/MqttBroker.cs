using System.Collections.Concurrent;
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
/// <remarks>带上旧连接，调用方先关掉它再开始读写，避免两个连接同时往同一会话写。</remarks>
public sealed record SessionAttachResult(
    BrokerSession Session,
    bool SessionPresent,
    IClientConnection? PreviousConnection);

/// <summary>
/// The broker facade: authentication, session lifecycle, routing, retained
/// messages and administration.
/// </summary>
/// <remarks>
/// 这一层不碰 socket：连接层把解析好的报文交进来，状态机之外的所有决定都在这里，
/// 因此可以脱网做单元测试。并发上只有会话接管与遗嘱调度用了锁，其余路径依赖
/// 各自组件内部同步。
/// </remarks>
public sealed class MqttBroker : IAsyncDisposable
{
    /// <summary>Broker-wide logger for lifecycle events and delivery warnings.</summary>
    private readonly ILogger<MqttBroker> _logger;
    /// <summary>Serialises session attach so two CONNECTs with the same identifier cannot race.</summary>
    private readonly SemaphoreSlim _attachGate = new(1, 1);
    /// <summary>Delayed wills waiting to fire, keyed by client identifier.</summary>
    private readonly Dictionary<string, CancellationTokenSource> _pendingWills = new(StringComparer.Ordinal);
    /// <summary>Guards the pending-will map; never held across an await.</summary>
    private readonly object _willGate = new();
    /// <summary>Cancelled on stop and dispose; ends the maintenance loop.</summary>
    private readonly CancellationTokenSource _lifetime = new();
    /// <summary>Durable snapshot store, or null when sessions stay in memory.</summary>
    private readonly ISessionSnapshotStore? _sessionStore;
    /// <summary>Minimum time between two snapshots, from storage options.</summary>
    private readonly TimeSpan _sessionSnapshotInterval;
    /// <summary>Earliest time the next snapshot may be written.</summary>
    private DateTimeOffset _nextSessionSnapshot = DateTimeOffset.MinValue;
    /// <summary>Cheap change detector: session, queued-message and subscription counts.</summary>
    private (int Sessions, int Queued, int Subscriptions) _lastSessionFingerprint = (-1, -1, -1);
    /// <summary>Background sweep task, created on the first start.</summary>
    private Task? _maintenanceTask;
    /// <summary>Set once in dispose so the call is idempotent.</summary>
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
        RabbitBridge? rabbit = null,
        ISessionReplicaStore? replicaStore = null)
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
        _replicaStore = replicaStore;
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

        if (replicaStore is not null)
        {
            RestoreReplicas(replicaStore.Load());
        }
    }

    /// <summary>Limits and timings this broker was created with.</summary>
    /// <remarks>创建时取值，运行期改配置不会影响已经存在的实例。</remarks>
    public BrokerOptions Options { get; }

    /// <summary>Credential validator used by CONNECT.</summary>
    /// <remarks>可能是本地账号、JWT 或自定义实现，Broker 不关心凭据来源。</remarks>
    public IClientAuthenticator Authenticator { get; }

    /// <summary>Topic ACL used for publish and subscribe.</summary>
    /// <remarks>发布与订阅各检查一次，结果不缓存。</remarks>
    public ITopicAuthorizer Authorizer { get; }

    /// <summary>Cross-node router; a null implementation when clustering is off.</summary>
    /// <remarks>关闭集群时它是空实现，调用点不需要写分支。</remarks>
    public IClusterRouter Cluster { get; }

    /// <summary>Per-address CONNECT limiter, disabled unless configured.</summary>
    /// <remarks>未配置时取令牌恒成功，不产生哈希查找。</remarks>
    public ConnectionRateLimiter ConnectionLimits { get; }

    /// <summary>Shared per-topic publish quotas, disabled unless configured.</summary>
    /// <remarks>按主题过滤器匹配，一个主题命中多条配额时每条都要通过。</remarks>
    public TopicQuotaLimiter TopicQuotas { get; }

    /// <summary>Optional NATS forwarding for locally accepted messages.</summary>
    /// <remarks>只在首次接收的节点调用，避免集群里重复投递。</remarks>
    public NatsBridge? Nats { get; }

    /// <summary>Optional Kafka forwarding for locally accepted messages.</summary>
    /// <remarks>异步投递，失败只影响桥本身，不阻断本地路由。</remarks>
    public KafkaBridge? Kafka { get; }

    /// <summary>Optional RabbitMQ forwarding for locally accepted messages.</summary>
    /// <remarks>publisher confirm 成功后才计入转发数。</remarks>
    public RabbitBridge? Rabbit { get; }

    /// <summary>Optional routing rules that can discard messages before storage.</summary>
    /// <remarks>在保留、集成与投递之前执行：被丢弃的消息不会进 webhook 或消息桥，改写的主题对下游可见。</remarks>
    public RuleEngine? Rules { get; }

    /// <summary>Optional webhook fan-out for locally accepted messages.</summary>
    /// <remarks>每条规则一条独立队列，慢端点不会拖住发布路径。</remarks>
    public WebhookDispatcher? Webhooks { get; }

    /// <summary>Enhanced authentication providers by method name.</summary>
    /// <remarks>方法名大小写敏感，未注册的方法按协议错误拒绝。</remarks>
    public IReadOnlyDictionary<string, IEnhancedAuthenticator> EnhancedAuthenticators { get; }
        = new Dictionary<string, IEnhancedAuthenticator>(StringComparer.Ordinal);

    /// <summary>
    /// Number of live cluster nodes used to split rate-limit budgets; 1 when the
    /// broker runs standalone.
    /// </summary>
    /// <remarks>限流按节点均分：节点越多，单节点放行额度越小。</remarks>
    public int ClusterSize { get; private set; } = 1;

    /// <summary>All sessions, online and offline.</summary>
    /// <remarks>键是客户端 ID；持久会话在断开后仍留在这里直到过期。</remarks>
    public SessionRegistry Sessions { get; } = new();

    /// <summary>Subscription index used for routing.</summary>
    /// <remarks>与 Sessions 分开维护，清理会话时两处都要删。</remarks>
    public SubscriptionIndex Subscriptions { get; } = new();

    /// <summary>Retained messages, optionally journaled to disk.</summary>
    /// <remarks>是否落盘在启动时决定，运行期不切换。</remarks>
    public RetainedMessageStore Retained { get; }

    /// <summary>Monotonic counters exposed through the API and metrics endpoint.</summary>
    /// <remarks>进程内累加，重启归零，不承诺跨重启连续。</remarks>
    public BrokerMetrics Metrics { get; } = new();

    /// <summary>Inbound publish rate for the last 60 seconds.</summary>
    /// <remarks>一秒一格，只用于面板曲线。</remarks>
    public RateTracker PublishRate { get; } = new();

    /// <summary>Outbound delivery rate for the last 60 seconds.</summary>
    /// <remarks>按订阅者计数，跨节点转发进来的也算。</remarks>
    public RateTracker DeliveryRate { get; } = new();

    /// <summary>Newest replica of sessions owned by peers.</summary>
    /// <remarks>只在原节点不可用时启用；条目由对端周期性推送刷新。</remarks>
    private readonly ConcurrentDictionary<string, ClusterSessionReplica> _sessionReplicas = new(StringComparer.Ordinal);

    /// <summary>Version counter for replicas produced by this node.</summary>
    private long _replicaVersion;

    /// <summary>Round-robin cursor over the session snapshot.</summary>
    private int _replicaCursor;

    /// <summary>Round-robin cursor over sessions with queued or inflight messages.</summary>
    private int _replicaCriticalCursor;

    /// <summary>Background loop that pushes session replicas.</summary>
    private Task? _replicaTask;

    /// <summary>Durable replica store, or null when replicas stay in memory.</summary>
    private readonly ISessionReplicaStore? _replicaStore;

    /// <summary>Set when replicas changed and the snapshot is stale.</summary>
    private int _replicasDirty;

    /// <summary>Earliest time the next replica snapshot may be written.</summary>
    private DateTimeOffset _nextReplicaPersist = DateTimeOffset.MinValue;

    /// <summary>Interval between replica snapshots, matching the storage options.</summary>
    private static readonly TimeSpan ReplicaPersistInterval = TimeSpan.FromSeconds(5);

    /// <summary>Returns the provider registered for an authentication method, if any.</summary>
    /// <remarks>未注册返回 null，由连接层回 0x8C。</remarks>
    /// <param name="method">Authentication Method from CONNECT.</param>
    /// <returns>Provider or null when the method is not supported.</returns>
    public IEnhancedAuthenticator? GetEnhancedAuthenticator(string method)
        => EnhancedAuthenticators.TryGetValue(method, out var authenticator) ? authenticator : null;

    /// <summary>Validates credentials through the configured authenticator.</summary>
    /// <remarks>直接转给认证器，Broker 不做二次判断。</remarks>
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
        if (Cluster.Enabled
            && !connect.CleanStart
            && Sessions.Get(connect.ClientId) is null)
        {
            await ClaimRemoteSessionAsync(connect.ClientId, cancellationToken).ConfigureAwait(false);
        }

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

    /// <summary>Asks the cluster for a session before creating a fresh one.</summary>
    /// <remarks>传输失败只告警：宁可新建会话，也不让客户端连不上。</remarks>
    /// <param name="clientId">Session to claim.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    private async Task ClaimRemoteSessionAsync(string clientId, CancellationToken cancellationToken)
    {
        try
        {
            var transfer = await Cluster.ClaimSessionAsync(clientId, cancellationToken).ConfigureAwait(false);

            if (transfer is not null && InstallSession(transfer))
            {
                _logger.LogInformation(
                    "Client {ClientId} resumed a session transferred from another node",
                    clientId);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Session claim for {ClientId} failed; starting a fresh session", clientId);
        }
    }

    /// <summary>Installs a session transferred from another node.</summary>
    /// <remarks>走与启动恢复相同的路径，避免两套状态重建逻辑。</remarks>
    /// <param name="record">Snapshot built by the previous owner.</param>
    /// <returns><c>true</c> when the session is present afterwards.</returns>
    public bool InstallSession(PersistedSession record)
    {
        RestoreSessions([record]);
        return Sessions.Get(record.ClientId) is not null;
    }

    /// <summary>Stores a replica pushed by another node.</summary>
    /// <remarks>只保留版本更新的副本；过期或更旧的推送直接丢弃。</remarks>
    /// <param name="replica">Snapshot received from a peer.</param>
    public void StoreSessionReplica(ClusterSessionReplica replica)
    {
        var existing = _sessionReplicas.GetValueOrDefault(replica.Session.ClientId);

        if (existing is not null && existing.Version >= replica.Version)
        {
            return;
        }

        _sessionReplicas[replica.Session.ClientId] = replica;
        TrimReplicas(DateTimeOffset.UtcNow);
        Interlocked.Exchange(ref _replicasDirty, 1);
    }

    /// <summary>Loads persisted replicas at startup.</summary>
    /// <remarks>过期条目在读取时丢弃；数量超过上限时按更新时间裁剪。</remarks>
    /// <param name="replicas">Replicas read from the store.</param>
    private void RestoreReplicas(IReadOnlyList<ClusterSessionReplica> replicas)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var replica in replicas)
        {
            if (string.IsNullOrWhiteSpace(replica.Session.ClientId))
            {
                continue;
            }

            var expiry = replica.Session.ExpiresAt;

            if (expiry is { } expiresAt && expiresAt <= now)
            {
                continue;
            }

            _sessionReplicas[replica.Session.ClientId] = replica;
        }

        TrimReplicas(now);
    }

    /// <summary>Writes the replica snapshot when it changed and the interval elapsed.</summary>
    /// <param name="now">Current time.</param>
    private void PersistReplicasIfDue(DateTimeOffset now)
    {
        if (_replicaStore is null
            || Volatile.Read(ref _replicasDirty) == 0
            || now < _nextReplicaPersist)
        {
            return;
        }

        _nextReplicaPersist = now + ReplicaPersistInterval;
        PersistReplicas();
    }

    /// <summary>Writes every held replica to the store.</summary>
    /// <remarks>整表覆盖写，因此只适合在内存副本变化后调用；失败只记日志。</remarks>
    private void PersistReplicas()
    {
        if (_replicaStore is null)
        {
            return;
        }

        try
        {
            _replicaStore.Save(_sessionReplicas.Values.ToList());
            Interlocked.Exchange(ref _replicasDirty, 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Session replica snapshot could not be written");
        }
    }

    /// <summary>Returns the newest replica this node holds for a session.</summary>
    /// <remarks>调用方必须确认副本记录的属主已经不可用，否则会复活旧状态。</remarks>
    /// <param name="clientId">Session to look up.</param>
    public ClusterSessionReplica? GetSessionReplica(string clientId)
        => _sessionReplicas.TryGetValue(clientId, out var replica) ? replica : null;

    /// <summary>Number of replicas held for sessions owned by peers.</summary>
    /// <remarks>用于诊断与测试。</remarks>
    public int SessionReplicaCount => _sessionReplicas.Count;

    /// <summary>Removes a session locally and returns it for another node.</summary>
    /// <remarks>
    /// 与 attach 共用同一把锁，避免交还会话时又被本机连接接管；
    /// 旧连接以 0x8E 断开，遗嘱不发布，因为迁移不是异常断开。
    /// </remarks>
    /// <param name="clientId">Session to hand over.</param>
    /// <param name="cancellationToken">Cancels the disconnect.</param>
    /// <returns>The snapshot, or null when this node does not hold the session.</returns>
    public async Task<PersistedSession?> DetachSessionAsync(string clientId, CancellationToken cancellationToken = default)
    {
        BrokerSession? session;
        IClientConnection? connection;
        PersistedSession record;

        await _attachGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            session = Sessions.Get(clientId);

            if (session is null)
            {
                return null;
            }

            record = CreateTransferRecord(session);
            connection = session.Connection;

            session.PrepareForTransfer();
            Sessions.Remove(clientId);
            Subscriptions.RemoveAll(clientId);
            session.Dispose();
        }
        finally
        {
            _attachGate.Release();
        }

        if (connection is not null)
        {
            await connection.DisconnectAsync(
                MqttReasonCodes.SessionTakenOver,
                "Session moved to another node.",
                cancellationToken).ConfigureAwait(false);
        }

        PersistSessions(force: false);

        _logger.LogInformation(
            "Session {ClientId} handed over with {Count} queued message(s)",
            clientId,
            record.Queue.Count);

        return record;
    }

    /// <summary>Builds the transfer snapshot for one session.</summary>
    /// <param name="session">Session that is leaving this node.</param>
    private PersistedSession CreateTransferRecord(BrokerSession session)
    {
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

        record.Subscriptions = Subscriptions.GetByClient(session.ClientId)
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

        record.Queue = session.SnapshotForTransfer(DateTimeOffset.UtcNow)
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

        record.InboundQos2 = session.SnapshotInboundQos2()
            .Select(entry => new PersistedInboundPublish
            {
                PacketId = entry.PacketId,
                Topic = entry.Publish.Topic,
                Payload = Convert.ToBase64String(entry.Publish.Payload),
                QoS = (int)entry.Publish.QoS,
                Retain = entry.Publish.Retain,
                Properties = PropertyRecord.FromProperties(entry.Publish.Properties),
            })
            .ToList();

        return record;
    }

    /// <summary>Applies disconnect rules: will publication, expiry and metrics.</summary>
    /// <remarks>遗嘱只在非正常断开或显式 DisconnectWithWill 时发布；会话是否保留看过期时间。</remarks>
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
    /// <remarks>顺序是校验主题、落保留、计数、集成、规则、本地投递、跨节点转发。</remarks>
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

        // Integrations run after the rules: a dropped message never reaches a
        // webhook or a bridge, and a rewritten message is forwarded under its new
        // topic. Only the origin node forwards, so a cluster does not duplicate.
        if (forwardToCluster)
        {
            Webhooks?.Publish(publish);
            Nats?.Publish(publish);
            Kafka?.Publish(publish);
            Rabbit?.Publish(publish);
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
    /// <remarks>不再二次转发，否则消息会在节点之间来回放大。</remarks>
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
    /// <remarks>同一客户端的多个命中合并成一次投递，QoS 取最大，订阅标识合并去重。</remarks>
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
    /// <remarks>走完整路由，因此会触发保留、规则与集成；主题非法直接抛异常而不是静默丢弃。</remarks>
    /// <param name="topic">Topic name.</param>
    /// <param name="payload">Payload bytes.</param>
    /// <param name="qos">Delivery guarantee to use.</param>
    /// <param name="retain">Stores the message as the retained value for the topic.</param>
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
    /// <remarks>幂等：重复调用不会起第二个清扫线程。</remarks>
    /// <param name="cancellationToken">Cancels startup.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _maintenanceTask ??= Task.Run(() => MaintenanceLoopAsync(_lifetime.Token), CancellationToken.None);
        await Cluster.StartAsync(cancellationToken).ConfigureAwait(false);

        if (Cluster.Enabled)
        {
            // A session restored from local disk may have been taken over by a
            // peer while this node was down; reconcile once membership is known.
            _ = Task.Run(() => ReconcileWithPeersAsync(_lifetime.Token), CancellationToken.None);
        }

        if (Cluster.ReplicatesSessions)
        {
            _replicaTask ??= Task.Run(() => ReplicaLoopAsync(_lifetime.Token), CancellationToken.None);
        }
    }

    /// <summary>Pushes a batch of replicas every configured interval.</summary>
    /// <remarks>独立于清扫循环，否则 ReplicaInterval 会被 10 秒一次的清扫吞掉。</remarks>
    /// <param name="cancellationToken">Stops the loop.</param>
    private async Task ReplicaLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                ReplicateSessionBatch(DateTimeOffset.UtcNow);
                PersistReplicasIfDue(DateTimeOffset.UtcNow);
                await Task.Delay(Cluster.ReplicaInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Session replication round failed; retrying on the next tick");
            }
        }
    }

    /// <summary>Waits for peers, then drops restored sessions they already own.</summary>
    /// <remarks>启动时成员表还是空的，直接查副本会漏掉对端；等心跳收敛后再比对，并复查一次。</remarks>
    /// <param name="cancellationToken">Stops the reconciliation.</param>
    private async Task ReconcileWithPeersAsync(CancellationToken cancellationToken)
    {
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);

            while (DateTimeOffset.UtcNow < deadline && Cluster.GetSnapshot().Nodes.Count <= 1)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            var dropped = await DropStaleRestoredSessionsAsync(cancellationToken).ConfigureAwait(false);

            // The new owner may not have replicated yet; check once more later.
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            dropped += await DropStaleRestoredSessionsAsync(cancellationToken).ConfigureAwait(false);

            if (dropped > 0)
            {
                _logger.LogInformation("Dropped {Count} stale restored session(s)", dropped);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    /// <summary>Drops restored sessions that a live peer now owns.</summary>
    /// <remarks>
    /// 节点停机期间会话可能已被对端接管；本机文件里的旧副本会导致重复投递。
    /// 启动后向存活对端查询会话位置：对端副本更新（或时间相同但节点名更大）时删除本地副本。
    /// 只读探测，不会踢掉对端在线的客户端。
    /// </remarks>
    /// <param name="cancellationToken">Stops the reconciliation.</param>
    /// <returns>Number of sessions dropped.</returns>
    public async Task<int> DropStaleRestoredSessionsAsync(CancellationToken cancellationToken = default)
    {
        if (!Cluster.Enabled)
        {
            return 0;
        }

        var dropped = 0;
        var self = Cluster.GetSnapshot().NodeId;

        foreach (var session in Sessions.Snapshot())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (!session.Persistent || session.IsOnline)
            {
                continue;
            }

            var owner = await Cluster.FindSessionOwnerAsync(session.ClientId, cancellationToken).ConfigureAwait(false);

            if (owner is null)
            {
                continue;
            }

            var localTimestamp = session.LastSeen.ToUnixTimeSeconds();

            if (owner.LastSeenUnix < localTimestamp
                || (owner.LastSeenUnix == localTimestamp
                    && string.CompareOrdinal(owner.NodeId, self) <= 0))
            {
                continue;
            }

            _logger.LogInformation(
                "Dropping restored session {ClientId}: the live copy is on {Owner}",
                session.ClientId,
                owner.NodeId);

            RemoveSession(session);
            dropped++;
        }

        if (dropped > 0)
        {
            PersistSessions(force: true);
        }

        return dropped;
    }

    /// <summary>Stops background loops without disposing session state.</summary>
    /// <remarks>停心跳与清扫，但保留会话内存，宿主可以先落盘再释放。</remarks>
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

        if (_replicaTask is not null)
        {
            try
            {
                await _replicaTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        PersistReplicas();
    }

    /// <summary>Samples counters and gauges for the statistics endpoint.</summary>
    /// <remarks>一次取齐，避免面板上各数字来自不同时刻。</remarks>
    public BrokerStats GetStats()
        => Metrics.Snapshot(
            Sessions.ConnectedCount(),
            Sessions.Count,
            Subscriptions.Count,
            Retained.Count,
            PublishRate.Snapshot());

    /// <summary>Lists sessions with subscription counts for the clients table.</summary>
    /// <remarks>每个会话单独查订阅数，会话量大时这个接口是 O(n·m)。</remarks>
    public IReadOnlyList<ClientSnapshot> GetClients()
        => Sessions.Snapshot()
            .Select(session => session.Snapshot(Subscriptions.GetByClient(session.ClientId).Count))
            .OrderBy(client => client.ClientId, StringComparer.Ordinal)
            .ToList();

    /// <summary>Lists every subscription for the subscriptions table.</summary>
    /// <remarks>按客户端与过滤器排序，保证分页稳定。</remarks>
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
    /// <remarks>按主题排序；过期但尚未清扫的也会出现。</remarks>
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
    /// <remarks>在线连接发 DISCONNECT，离线会话直接删除，两种情况都返回成功。</remarks>
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
    /// <remarks>返回 false 表示该主题本来就没有保留值。</remarks>
    /// <param name="topic">Exact topic name.</param>
    public bool DeleteRetained(string topic) => Retained.Remove(topic);

    /// <summary>Removes every retained message and returns how many were deleted.</summary>
    /// <remarks>返回的是删除前的数量，调用方可以据此写审计日志；写删除记录后才清空，恢复时不会复活。</remarks>
    /// <returns>Number of retained messages that were removed.</returns>
    public int ClearRetained()
    {
        var removed = Retained.Count;
        Retained.Clear();
        return removed;
    }

    /// <summary>Membership view for the cluster page and API.</summary>
    /// <remarks>关闭集群时返回单节点视图。</remarks>
    public ClusterSnapshot GetCluster() => Cluster.GetSnapshot();

    /// <summary>Flushes session state and releases resources.</summary>
    /// <remarks>先停后台循环再落盘，最后逐会话释放；重复调用安全。</remarks>
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

    /// <summary>Rebuilds offline sessions and subscriptions from snapshots, skipping invalid filters.</summary>
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
                record.Queue.Select(TryToOutbound).OfType<OutboundMessage>(),
                record.InboundQos2.Select(TryToInbound).OfType<BrokerSession.InboundQos2Entry>());

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
    /// <remarks>纯 JSON 快照，不含连接与在途报文；导入时还会再过滤掉过期或非法的条目。</remarks>
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
    /// <remarks>
    /// 保留消息是整表替换而不是合并，先清空再写入，避免备份里没有的旧主题留下来；
    /// 会话则相反，只补缺失的，正在运行的连接不会被一份旧快照顶掉。
    /// </remarks>
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
    /// <remarks>
    /// 100 毫秒轮询一次，等到超时就返回；返回值非零说明这次停机丢了未确认的消息，
    /// 调用方可以据此决定是否让编排系统延后终止。
    /// </remarks>
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

    /// <summary>Recomputes limits after the cluster gained or lost a node.</summary>
    /// <remarks>成员变化时立即调用，不必等下一次清扫，避免新节点在分摊前拿到旧配额。</remarks>
    public void OnClusterMembershipChanged() => ApplyClusterRateScale();

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

    /// <summary>Writes a snapshot when the interval elapsed; a no-op without a store.</summary>
    private void PersistSessionsIfDue(DateTimeOffset now)    {
        if (_sessionStore is null || now < _nextSessionSnapshot)
        {
            return;
        }

        _nextSessionSnapshot = now + _sessionSnapshotInterval;
        PersistSessions(force: false);
    }

    /// <summary>Writes the snapshot unless nothing changed and the write is not forced.</summary>
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

    /// <summary>Projects persistent sessions and their subscriptions into the on-disk shape.</summary>
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

            snapshot.Add(CreateSessionSnapshot(session, subscriptions, DateTimeOffset.UtcNow, includeInflight: true));
        }

        return snapshot;
    }

    /// <summary>Projects one session into the transferable shape.</summary>
    /// <param name="session">Session to project.</param>
    /// <param name="subscriptions">Subscriptions grouped by client id.</param>
    /// <param name="now">Used to resolve message expiry for inflight entries.</param>
    /// <param name="includeInflight">True for replicas, which should not lose unacknowledged messages.</param>
    private static PersistedSession CreateSessionSnapshot(
        BrokerSession session,
        IReadOnlyDictionary<string, List<Subscription>> subscriptions,
        DateTimeOffset now,
        bool includeInflight = false)
    {
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

        record.Queue = (includeInflight ? session.SnapshotForTransfer(now) : session.SnapshotQueue())
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

        record.InboundQos2 = session.SnapshotInboundQos2()
            .Select(entry => new PersistedInboundPublish
            {
                PacketId = entry.PacketId,
                Topic = entry.Publish.Topic,
                Payload = Convert.ToBase64String(entry.Publish.Payload),
                QoS = (int)entry.Publish.QoS,
                Retain = entry.Publish.Retain,
                Properties = PropertyRecord.FromProperties(entry.Publish.Properties),
            })
            .ToList();

        return record;
    }

    /// <summary>Converts a stored message for delivery, or null when its payload is not valid base64.</summary>
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

    /// <summary>Converts a stored inbound QoS 2 publish, or null when its payload is not valid base64.</summary>
    /// <remarks>包 ID 超出范围或主题为空时同样丢弃，避免恢复出无法完成的握手。</remarks>
    private static BrokerSession.InboundQos2Entry? TryToInbound(PersistedInboundPublish message)
    {
        if (message.PacketId is < 1 or > ushort.MaxValue || string.IsNullOrEmpty(message.Topic))
        {
            return null;
        }

        try
        {
            return new BrokerSession.InboundQos2Entry(
                (ushort)message.PacketId,
                new PublishPacket
                {
                    Topic = message.Topic,
                    Payload = Convert.FromBase64String(message.Payload),
                    QoS = MqttQoS.ExactlyOnce,
                    Retain = message.Retain,
                    PacketId = (ushort)message.PacketId,
                    Properties = PropertyRecord.ToProperties(message.Properties),
                });
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Runs the sweep on the shorter of the sweep and retransmit intervals, at least once a second.</summary>
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

    /// <summary>Prunes retained messages, persists sessions, rescales limits, retransmits and expires sessions.</summary>
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

    /// <summary>Pushes one batch of session replicas to peers.</summary>
    /// <remarks>按快照顺序轮转，每轮最多 200 个，避免大量会话时把心跳和转发挤掉。</remarks>
    /// <param name="now">Current time.</param>
    private void ReplicateSessionBatch(DateTimeOffset now)
    {
        var sessions = Sessions.Snapshot()
            .Where(session => session.Persistent)
            .ToList();

        if (sessions.Count == 0)
        {
            return;
        }

        var subscriptions = Subscriptions.GetAll()
            .GroupBy(subscription => subscription.ClientId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var nodeId = Cluster.GetSnapshot().NodeId;
        var budget = Math.Min(Cluster.ReplicaBatchSize, sessions.Count);
        var count = sessions.Count;

        // Sessions with queued or unacknowledged messages are the ones a crash
        // would actually lose data for, so they refresh first. A slice of the
        // batch stays available for the plain sweep, otherwise idle sessions
        // would never reach a peer while busy ones keep filling the budget.
        var critical = sessions
            .Where(session => session.QueuedCount + session.InflightCount > 0)
            .OrderByDescending(session => session.QueuedCount + session.InflightCount)
            .ThenByDescending(session => session.LastSeen)
            .ToList();

        var sweepReserve = budget >= 10 && critical.Count < count
            ? Math.Max(1, budget / 10)
            : 0;

        var criticalBudget = critical.Count >= budget
            ? Math.Max(0, budget - sweepReserve)
            : critical.Count;

        var toPush = new List<BrokerSession>(budget);
        var pushed = new HashSet<string>(StringComparer.Ordinal);

        if (criticalBudget > 0)
        {
            var criticalStart = _replicaCriticalCursor % critical.Count;
            var taken = Math.Min(criticalBudget, critical.Count);

            for (var index = 0; index < taken; index++)
            {
                var session = critical[(criticalStart + index) % critical.Count];

                if (pushed.Add(session.ClientId))
                {
                    toPush.Add(session);
                }
            }

            _replicaCriticalCursor = (criticalStart + taken) % critical.Count;
        }

        var start = _replicaCursor % count;
        var scanned = 0;

        for (var offset = 0; offset < count && toPush.Count < budget; offset++)
        {
            var session = sessions[(start + offset) % count];
            scanned = offset + 1;

            if (pushed.Add(session.ClientId))
            {
                toPush.Add(session);
            }
        }

        _replicaCursor = scanned > 0 ? (start + scanned) % count : start;

        foreach (var session in toPush)
        {
            var replica = new ClusterSessionReplica(
                CreateSessionSnapshot(session, subscriptions, now, includeInflight: true),
                nodeId,
                Interlocked.Increment(ref _replicaVersion),
                now.ToUnixTimeSeconds());

            _ = PushReplicaAsync(replica);
        }
    }

    /// <summary>Sends one replica and swallows transport failures.</summary>
    /// <remarks>下一轮会重发同一个会话，所以这里只需要记 Debug 日志。</remarks>
    /// <param name="replica">Replica to send.</param>
    private async Task PushReplicaAsync(ClusterSessionReplica replica)
    {
        try
        {
            await Cluster.ReplicateSessionAsync(replica, _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Session replica for {ClientId} could not be pushed", replica.Session.ClientId);
        }
    }

    /// <summary>Drops expired replicas and keeps the table bounded.</summary>
    /// <param name="now">Current time.</param>
    private void TrimReplicas(DateTimeOffset now)
    {
        var capacity = Cluster.ReplicaCapacity;

        if (_sessionReplicas.Count <= capacity)
        {
            return;
        }

        var cutoff = now.AddMinutes(-30).ToUnixTimeSeconds();

        foreach (var entry in _sessionReplicas)
        {
            if (entry.Value.UpdatedAtUnix < cutoff)
            {
                _sessionReplicas.TryRemove(entry.Key, out _);
            }
        }

        // Still too large: evict the oldest replicas first.
        var excess = _sessionReplicas.Count - capacity;

        if (excess <= 0)
        {
            return;
        }

        foreach (var entry in _sessionReplicas.OrderBy(item => item.Value.UpdatedAtUnix).Take(excess))
        {
            _sessionReplicas.TryRemove(entry.Key, out _);
        }
    }

    /// <summary>Removes a session and its subscriptions, then disposes it.</summary>
    private void RemoveSession(BrokerSession session)
    {
        Sessions.Remove(session.ClientId);
        Subscriptions.RemoveAll(session.ClientId);
        session.Dispose();
    }

    /// <summary>Publishes the will after its delay, clamped to the session expiry, replacing any earlier one.</summary>
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

    /// <summary>Cancels a delayed will, for example when the client reconnects first.</summary>
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

    /// <summary>Keeps only the user-visible will properties and routes the message like any publish.</summary>
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

    /// <summary>Builds one delivery per client: highest QoS, merged identifiers, absolute expiry.</summary>
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

    /// <summary>Applies the 3.1.1 rule: clean start is non-persistent, otherwise expiry is unlimited.</summary>
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

    /// <summary>Merges several matching subscriptions of one client into a single delivery.</summary>
    private sealed class DeliveryAggregate
    {
        /// <summary>Highest QoS among the matched subscriptions.</summary>
        public MqttQoS QoS { get; set; } = MqttQoS.AtMostOnce;

        /// <summary>True when any matched subscription keeps the original retain flag.</summary>
        public bool RetainAsPublished { get; set; }

        /// <summary>Identifiers to echo on the delivery, de-duplicated.</summary>
        public HashSet<int> SubscriptionIdentifiers { get; } = [];
    }
}
