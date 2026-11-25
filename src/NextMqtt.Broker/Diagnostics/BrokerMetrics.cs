using System.Collections.Concurrent;

namespace NextMqtt.Broker.Diagnostics;

/// <summary>Monotonic broker counters. Gauges are sampled from live state.</summary>
/// <remarks>所有计数使用 <see cref="Interlocked"/> 累加，读取时保证可见性。</remarks>
public sealed class BrokerMetrics
{
    private long _connectionsOpened;
    private long _connectionsClosed;
    private long _connectionsRejected;
    private long _authFailures;
    private long _publishesReceived;
    private long _publishesSent;
    private long _bytesReceived;
    private long _bytesSent;
    private long _droppedMessages;
    private long _retransmissions;
    private long _subscriptionsCreated;
    private long _sessionsExpired;
    private long _connectionsRateLimited;
    private long _publishesRateLimited;

    /// <summary>Connections that completed CONNECT since start.</summary>
    /// <remarks>统计成功建连，被拒绝的不算。</remarks>
    public long ConnectionsOpened => Interlocked.Read(ref _connectionsOpened);

    /// <summary>Connections that ended for any reason since start.</summary>
    /// <remarks>与建连计数相减才是当前连接数。</remarks>
    public long ConnectionsClosed => Interlocked.Read(ref _connectionsClosed);

    /// <summary>CONNECT attempts rejected by authentication, limits or the rate limiter.</summary>
    /// <remarks>包含鉴权失败与限额，细分看下面两个计数。</remarks>
    public long ConnectionsRejected => Interlocked.Read(ref _connectionsRejected);

    /// <summary>Failed authentication attempts.</summary>
    /// <remarks>只统计凭据被拒，不包含报文格式错误。</remarks>
    public long AuthFailures => Interlocked.Read(ref _authFailures);

    /// <summary>PUBLISH packets accepted from clients.</summary>
    /// <remarks>只算通过限流与规则之后仍被接收的报文。</remarks>
    public long PublishesReceived => Interlocked.Read(ref _publishesReceived);

    /// <summary>PUBLISH packets delivered to client connections.</summary>
    /// <remarks>一个报文投给三个订阅者就计三次。</remarks>
    public long PublishesSent => Interlocked.Read(ref _publishesSent);

    /// <summary>Bytes read from client sockets, including fixed headers.</summary>
    /// <remarks>反映真实链路流量，不是载荷大小。</remarks>
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    /// <summary>Bytes written to client sockets, including fixed headers.</summary>
    /// <remarks>压缩与主题别名会直接体现在这个数字上。</remarks>
    public long BytesSent => Interlocked.Read(ref _bytesSent);

    /// <summary>Messages discarded by queue limits, expiry or quotas.</summary>
    /// <remarks>所有没送到的消息的统一出口，排障先看它。</remarks>
    public long DroppedMessages => Interlocked.Read(ref _droppedMessages);

    /// <summary>QoS retransmissions performed by the inflight window.</summary>
    /// <remarks>持续增长说明有客户端迟迟不回 PUBACK。</remarks>
    public long Retransmissions => Interlocked.Read(ref _retransmissions);

    /// <summary>Successful subscriptions created since start.</summary>
    /// <remarks>重复订阅同一过滤器也算一次成功。</remarks>
    public long SubscriptionsCreated => Interlocked.Read(ref _subscriptionsCreated);

    /// <summary>Sessions removed after their expiry interval elapsed.</summary>
    /// <remarks>管理员手动清理不计入。</remarks>
    public long SessionsExpired => Interlocked.Read(ref _sessionsExpired);

    /// <summary>CONNECTs rejected by the per-address connection rate limit.</summary>
    /// <remarks>单独计数，便于区分攻击与重连风暴。</remarks>
    public long ConnectionsRateLimited => Interlocked.Read(ref _connectionsRateLimited);

    /// <summary>PUBLISH packets dropped by a per-client or per-topic rate limit.</summary>
    /// <remarks>客户端配额与主题配额都记在这里。</remarks>
    public long PublishesRateLimited => Interlocked.Read(ref _publishesRateLimited);

    /// <summary>Records an accepted connection.</summary>
    /// <remarks>在 CONNACK 发出之后调用，与关闭计数配对。</remarks>
    public void ConnectionOpened() => Interlocked.Increment(ref _connectionsOpened);

    /// <summary>Records a closed connection.</summary>
    /// <remarks>每条连接只调用一次，由清理路径保证。</remarks>
    public void ConnectionClosed() => Interlocked.Increment(ref _connectionsClosed);

    /// <summary>Records a rejected CONNECT.</summary>
    /// <remarks>鉴权失败、限额与限流共用一个入口。</remarks>
    public void ConnectionRejected() => Interlocked.Increment(ref _connectionsRejected);

    /// <summary>Records a failed authentication attempt.</summary>
    /// <remarks>后台按 IP 排序找爆破来源时用。</remarks>
    public void AuthenticationFailed() => Interlocked.Increment(ref _authFailures);

    /// <summary>Records one inbound PUBLISH.</summary>
    /// <remarks>接收侧计数，在规则丢弃之前就加。</remarks>
    public void PublishReceived() => Interlocked.Increment(ref _publishesReceived);

    /// <summary>Records one outbound PUBLISH.</summary>
    /// <remarks>按订阅者计，不做去重。</remarks>
    public void PublishSent() => Interlocked.Increment(ref _publishesSent);

    /// <summary>Adds bytes read from a client.</summary>
    /// <remarks>读取循环每读一块就累加，不采样。</remarks>
    /// <param name="count">Byte count for one read.</param>
    public void AddBytesReceived(long count) => Interlocked.Add(ref _bytesReceived, count);

    /// <summary>Adds bytes written to a client.</summary>
    /// <remarks>写循环里累加，重传的报文也算。</remarks>
    /// <param name="count">Byte count for one write.</param>
    public void AddBytesSent(long count) => Interlocked.Add(ref _bytesSent, count);

    /// <summary>Records a message dropped by policy or back-pressure.</summary>
    /// <remarks>这里指规则、过期与队列溢出，限流丢弃另计。</remarks>
    public void MessageDropped() => Interlocked.Increment(ref _droppedMessages);

    /// <summary>Records one QoS retransmission.</summary>
    /// <remarks>定时重发与重连补发都会调用。</remarks>
    public void Retransmitted() => Interlocked.Increment(ref _retransmissions);

    /// <summary>Records one successful subscription.</summary>
    /// <remarks>共享订阅同样算一次。</remarks>
    public void SubscriptionCreated() => Interlocked.Increment(ref _subscriptionsCreated);

    /// <summary>Records one expired session.</summary>
    /// <remarks>只统计超过过期时间被清掉的会话。</remarks>
    public void SessionExpired() => Interlocked.Increment(ref _sessionsExpired);

    /// <summary>Records a CONNECT rejected by the connection rate limit.</summary>
    /// <remarks>与鉴权失败分开，避免误判成密码错误。</remarks>
    public void ConnectionRateLimited() => Interlocked.Increment(ref _connectionsRateLimited);

    /// <summary>Records a PUBLISH dropped by a rate limit or topic quota.</summary>
    /// <remarks>丢弃前必须先记接收，否则监控会读到只有丢弃的中间状态。</remarks>
    public void PublishRateLimited() => Interlocked.Increment(ref _publishesRateLimited);

    /// <summary>Builds an immutable statistics snapshot for the API and metrics.</summary>
    /// <remarks>一次性取全部计数，避免接口内多次读取前后不一致。</remarks>
    /// <param name="connectedClients">Connections currently online.</param>
    /// <param name="sessions">Sessions in the registry, online and offline.</param>
    /// <param name="subscriptions">Subscription entries.</param>
    /// <param name="retainedMessages">Retained messages stored.</param>
    /// <param name="messagesPerSecond">Publish rate for the last 60 seconds.</param>
    /// <returns>Snapshot that no longer changes.</returns>
    public BrokerStats Snapshot(
        int connectedClients,
        int sessions,
        int subscriptions,
        int retainedMessages,
        IReadOnlyList<long> messagesPerSecond)
    {
        return new BrokerStats(
            ConnectionsOpened,
            ConnectionsClosed,
            ConnectionsRejected,
            AuthFailures,
            PublishesReceived,
            PublishesSent,
            BytesReceived,
            BytesSent,
            DroppedMessages,
            Retransmissions,
            SubscriptionsCreated,
            SessionsExpired,
            ConnectionsRateLimited,
            PublishesRateLimited,
            connectedClients,
            sessions,
            subscriptions,
            retainedMessages,
            messagesPerSecond.ToArray(),
            DateTimeOffset.UtcNow);
    }
}

/// <summary>Counters and gauges reported by the statistics endpoint.</summary>
/// <remarks>JSON 契约，字段名改动会影响面板与外部采集。</remarks>
/// <param name="ConnectionsOpened">Connections that completed CONNECT.</param>
/// <param name="ConnectionsClosed">Connections that ended.</param>
/// <param name="ConnectionsRejected">CONNECT attempts rejected.</param>
/// <param name="AuthFailures">Failed authentication attempts.</param>
/// <param name="PublishesReceived">PUBLISH packets accepted.</param>
/// <param name="PublishesSent">PUBLISH packets delivered.</param>
/// <param name="BytesReceived">Bytes read from clients.</param>
/// <param name="BytesSent">Bytes written to clients.</param>
/// <param name="DroppedMessages">Messages dropped by policy or back-pressure.</param>
/// <param name="Retransmissions">QoS retransmissions.</param>
/// <param name="SubscriptionsCreated">Successful subscriptions.</param>
/// <param name="SessionsExpired">Sessions removed after expiry.</param>
/// <param name="ConnectionsRateLimited">CONNECTs rejected by the rate limiter.</param>
/// <param name="PublishesRateLimited">PUBLISH packets dropped by a quota.</param>
/// <param name="ConnectedClients">Connections currently online.</param>
/// <param name="Sessions">Sessions in the registry.</param>
/// <param name="Subscriptions">Subscription entries.</param>
/// <param name="RetainedMessages">Retained messages stored.</param>
/// <param name="MessagesPerSecond">Publish rate for the last 60 seconds.</param>
/// <param name="CollectedAt">UTC timestamp of the snapshot.</param>
public sealed record BrokerStats(
    long ConnectionsOpened,
    long ConnectionsClosed,
    long ConnectionsRejected,
    long AuthFailures,
    long PublishesReceived,
    long PublishesSent,
    long BytesReceived,
    long BytesSent,
    long DroppedMessages,
    long Retransmissions,
    long SubscriptionsCreated,
    long SessionsExpired,
    long ConnectionsRateLimited,
    long PublishesRateLimited,
    int ConnectedClients,
    int Sessions,
    int Subscriptions,
    int RetainedMessages,
    long[] MessagesPerSecond,
    DateTimeOffset CollectedAt);

/// <summary>One row of the clients table.</summary>
/// <remarks>在线与离线会话共用这一行结构，靠 connected 区分。</remarks>
/// <param name="ClientId">Client identifier.</param>
/// <param name="Username">Authenticated username, when credentials were used.</param>
/// <param name="ProtocolVersion">MQTT 3.1.1 or 5.0.</param>
/// <param name="Connected">True while a connection is attached.</param>
/// <param name="RemoteAddress">Peer address of the current connection.</param>
/// <param name="ConnectedAt">When the current connection was established.</param>
/// <param name="LastSeen">Timestamp of the last packet received.</param>
/// <param name="SessionExpiryInterval">Session expiry in seconds.</param>
/// <param name="Subscriptions">Number of subscriptions for this client.</param>
/// <param name="InflightMessages">Unacknowledged outbound messages.</param>
/// <param name="QueuedMessages">Messages waiting in the offline queue.</param>
/// <param name="IsSuperuser">True when ACL checks are bypassed.</param>
public sealed record ClientSnapshot(
    string ClientId,
    string? Username,
    string ProtocolVersion,
    bool Connected,
    string? RemoteAddress,
    DateTimeOffset ConnectedAt,
    DateTimeOffset LastSeen,
    uint SessionExpiryInterval,
    int Subscriptions,
    int InflightMessages,
    int QueuedMessages,
    bool IsSuperuser);

/// <summary>One row of the subscriptions table.</summary>
/// <remarks>共享订阅带组名，普通订阅该字段为空。</remarks>
/// <param name="ClientId">Subscriber client identifier.</param>
/// <param name="Filter">Filter as originally supplied.</param>
/// <param name="SharedGroup">Shared subscription group, or null.</param>
/// <param name="QoS">Maximum QoS granted to the subscription.</param>
/// <param name="NoLocal">True when the client's own messages are suppressed.</param>
/// <param name="RetainAsPublished">True when the retain flag is preserved.</param>
/// <param name="RetainHandling">MQTT 5 retain handling option.</param>
/// <param name="Identifier">Subscription identifier, or null.</param>
public sealed record SubscriptionSnapshot(
    string ClientId,
    string Filter,
    string? SharedGroup,
    string QoS,
    bool NoLocal,
    bool RetainAsPublished,
    string RetainHandling,
    int? Identifier);

/// <summary>One row of the retained messages table.</summary>
/// <remarks>只列未过期的，过期但未清扫的不显示。</remarks>
/// <param name="Topic">Topic the message is stored under.</param>
/// <param name="PayloadBytes">Payload size in bytes.</param>
/// <param name="QoS">QoS stored with the message.</param>
/// <param name="StoredAt">When the message was retained.</param>
/// <param name="ExpiresAt">Expiry timestamp, or null when it does not expire.</param>
public sealed record RetainedSnapshot(
    string Topic,
    int PayloadBytes,
    string QoS,
    DateTimeOffset StoredAt,
    DateTimeOffset? ExpiresAt);

/// <summary>One row of the cluster table.</summary>
/// <remarks>成员信息来自心跳，最多比真实状态晚一个周期。</remarks>
/// <param name="NodeId">Identifier advertised by the node.</param>
/// <param name="Address">Base URL used for heartbeats and forwarding.</param>
/// <param name="Status">alive, suspect or dead.</param>
/// <param name="LastSeen">Last successful heartbeat.</param>
/// <param name="IsSelf">True for the local node.</param>
public sealed record ClusterNodeSnapshot(
    string NodeId,
    string Address,
    string Status,
    DateTimeOffset LastSeen,
    bool IsSelf);

/// <summary>Cluster membership as seen by one node.</summary>
/// <remarks>各节点视角可能略有差异，属正常现象。</remarks>
/// <param name="NodeId">Local node identifier.</param>
/// <param name="Status">standalone, clustered or singleton.</param>
/// <param name="Nodes">Membership table.</param>
public sealed record ClusterSnapshot(
    string NodeId,
    string Status,
    IReadOnlyList<ClusterNodeSnapshot> Nodes);

/// <summary>Fixed-window rate tracker used by the dashboard sparkline.</summary>
/// <remarks>一秒一格，够用且开销极低。</remarks>
public sealed class RateTracker
{
    private readonly int _capacity;
    private readonly ConcurrentQueue<(long Second, long Count)> _samples = new();
    private readonly object _gate = new();
    private long _currentSecond;
    private long _currentCount;

    /// <summary>Creates a tracker that keeps the last N one-second samples.</summary>
    /// <remarks>窗口越长内存越大，默认值够画一分钟曲线。</remarks>
    /// <param name="capacitySeconds">Window length in seconds; 60 by default.</param>
    public RateTracker(int capacitySeconds = 60)
    {
        _capacity = capacitySeconds;
    }

    /// <summary>Counts one event in the current second.</summary>
    /// <remarks>跨秒时先清掉旧样本再累加。</remarks>
    public void Increment()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        lock (_gate)
        {
            if (now != _currentSecond)
            {
                if (_currentSecond != 0)
                {
                    _samples.Enqueue((_currentSecond, _currentCount));
                }

                _currentSecond = now;
                _currentCount = 0;
            }

            _currentCount++;
            Trim(now);
        }
    }

    /// <summary>Returns the samples, oldest first, padded with zeros.</summary>
    /// <remarks>补零是为了让前端按固定长度直接画图。</remarks>
    /// <returns>Array of length <c>capacitySeconds</c>.</returns>
    public long[] Snapshot()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var values = new long[_capacity];

        lock (_gate)
        {
            Trim(now);

            foreach (var (second, count) in _samples)
            {
                var index = _capacity - 1 - (int)(now - second);
                if (index >= 0 && index < _capacity)
                {
                    values[index] = count;
                }
            }

            if (_currentSecond == now)
            {
                values[^1] = _currentCount;
            }
        }

        return values;
    }

    /// <summary>Drops samples that fell out of the window; caller holds the lock.</summary>
    /// <remarks>只在 Add 里调用，不需要额外的定时器。</remarks>
    /// <param name="now">Current Unix second.</param>
    private void Trim(long now)
    {
        while (_samples.TryPeek(out var sample) && now - sample.Second >= _capacity)
        {
            _samples.TryDequeue(out _);
        }
    }
}
