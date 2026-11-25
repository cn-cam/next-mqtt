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
    public long ConnectionsOpened => Interlocked.Read(ref _connectionsOpened);

    /// <summary>Connections that ended for any reason since start.</summary>
    public long ConnectionsClosed => Interlocked.Read(ref _connectionsClosed);

    /// <summary>CONNECT attempts rejected by authentication, limits or the rate limiter.</summary>
    public long ConnectionsRejected => Interlocked.Read(ref _connectionsRejected);

    /// <summary>Failed authentication attempts.</summary>
    public long AuthFailures => Interlocked.Read(ref _authFailures);

    /// <summary>PUBLISH packets accepted from clients.</summary>
    public long PublishesReceived => Interlocked.Read(ref _publishesReceived);

    /// <summary>PUBLISH packets delivered to client connections.</summary>
    public long PublishesSent => Interlocked.Read(ref _publishesSent);

    /// <summary>Bytes read from client sockets, including fixed headers.</summary>
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    /// <summary>Bytes written to client sockets, including fixed headers.</summary>
    public long BytesSent => Interlocked.Read(ref _bytesSent);

    /// <summary>Messages discarded by queue limits, expiry or quotas.</summary>
    public long DroppedMessages => Interlocked.Read(ref _droppedMessages);

    /// <summary>QoS retransmissions performed by the inflight window.</summary>
    public long Retransmissions => Interlocked.Read(ref _retransmissions);

    /// <summary>Successful subscriptions created since start.</summary>
    public long SubscriptionsCreated => Interlocked.Read(ref _subscriptionsCreated);

    /// <summary>Sessions removed after their expiry interval elapsed.</summary>
    public long SessionsExpired => Interlocked.Read(ref _sessionsExpired);

    /// <summary>CONNECTs rejected by the per-address connection rate limit.</summary>
    public long ConnectionsRateLimited => Interlocked.Read(ref _connectionsRateLimited);

    /// <summary>PUBLISH packets dropped by a per-client or per-topic rate limit.</summary>
    public long PublishesRateLimited => Interlocked.Read(ref _publishesRateLimited);

    /// <summary>Records an accepted connection.</summary>
    public void ConnectionOpened() => Interlocked.Increment(ref _connectionsOpened);

    /// <summary>Records a closed connection.</summary>
    public void ConnectionClosed() => Interlocked.Increment(ref _connectionsClosed);

    /// <summary>Records a rejected CONNECT.</summary>
    public void ConnectionRejected() => Interlocked.Increment(ref _connectionsRejected);

    /// <summary>Records a failed authentication attempt.</summary>
    public void AuthenticationFailed() => Interlocked.Increment(ref _authFailures);

    /// <summary>Records one inbound PUBLISH.</summary>
    public void PublishReceived() => Interlocked.Increment(ref _publishesReceived);

    /// <summary>Records one outbound PUBLISH.</summary>
    public void PublishSent() => Interlocked.Increment(ref _publishesSent);

    /// <summary>Adds bytes read from a client.</summary>
    /// <param name="count">Byte count for one read.</param>
    public void AddBytesReceived(long count) => Interlocked.Add(ref _bytesReceived, count);

    /// <summary>Adds bytes written to a client.</summary>
    /// <param name="count">Byte count for one write.</param>
    public void AddBytesSent(long count) => Interlocked.Add(ref _bytesSent, count);

    /// <summary>Records a message dropped by policy or back-pressure.</summary>
    public void MessageDropped() => Interlocked.Increment(ref _droppedMessages);

    /// <summary>Records one QoS retransmission.</summary>
    public void Retransmitted() => Interlocked.Increment(ref _retransmissions);

    /// <summary>Records one successful subscription.</summary>
    public void SubscriptionCreated() => Interlocked.Increment(ref _subscriptionsCreated);

    /// <summary>Records one expired session.</summary>
    public void SessionExpired() => Interlocked.Increment(ref _sessionsExpired);

    /// <summary>Records a CONNECT rejected by the connection rate limit.</summary>
    public void ConnectionRateLimited() => Interlocked.Increment(ref _connectionsRateLimited);

    /// <summary>Records a PUBLISH dropped by a rate limit or topic quota.</summary>
    public void PublishRateLimited() => Interlocked.Increment(ref _publishesRateLimited);

    /// <summary>Builds an immutable statistics snapshot for the API and metrics.</summary>
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
/// <param name="NodeId">Local node identifier.</param>
/// <param name="Status">standalone, clustered or singleton.</param>
/// <param name="Nodes">Membership table.</param>
public sealed record ClusterSnapshot(
    string NodeId,
    string Status,
    IReadOnlyList<ClusterNodeSnapshot> Nodes);

/// <summary>Fixed-window rate tracker used by the dashboard sparkline.</summary>
public sealed class RateTracker
{
    private readonly int _capacity;
    private readonly ConcurrentQueue<(long Second, long Count)> _samples = new();
    private readonly object _gate = new();
    private long _currentSecond;
    private long _currentCount;

    /// <summary>Creates a tracker that keeps the last N one-second samples.</summary>
    /// <param name="capacitySeconds">Window length in seconds; 60 by default.</param>
    public RateTracker(int capacitySeconds = 60)
    {
        _capacity = capacitySeconds;
    }

    /// <summary>Counts one event in the current second.</summary>
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
    /// <param name="now">Current Unix second.</param>
    private void Trim(long now)
    {
        while (_samples.TryPeek(out var sample) && now - sample.Second >= _capacity)
        {
            _samples.TryDequeue(out _);
        }
    }
}
