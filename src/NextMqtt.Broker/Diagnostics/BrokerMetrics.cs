using System.Collections.Concurrent;

namespace NextMqtt.Broker.Diagnostics;

/// <summary>Monotonic broker counters. Gauges are sampled from live state.</summary>
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

    public long ConnectionsOpened => Interlocked.Read(ref _connectionsOpened);
    public long ConnectionsClosed => Interlocked.Read(ref _connectionsClosed);
    public long ConnectionsRejected => Interlocked.Read(ref _connectionsRejected);
    public long AuthFailures => Interlocked.Read(ref _authFailures);
    public long PublishesReceived => Interlocked.Read(ref _publishesReceived);
    public long PublishesSent => Interlocked.Read(ref _publishesSent);
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);
    public long BytesSent => Interlocked.Read(ref _bytesSent);
    public long DroppedMessages => Interlocked.Read(ref _droppedMessages);
    public long Retransmissions => Interlocked.Read(ref _retransmissions);
    public long SubscriptionsCreated => Interlocked.Read(ref _subscriptionsCreated);
    public long SessionsExpired => Interlocked.Read(ref _sessionsExpired);
    public long ConnectionsRateLimited => Interlocked.Read(ref _connectionsRateLimited);
    public long PublishesRateLimited => Interlocked.Read(ref _publishesRateLimited);

    public void ConnectionOpened() => Interlocked.Increment(ref _connectionsOpened);
    public void ConnectionClosed() => Interlocked.Increment(ref _connectionsClosed);
    public void ConnectionRejected() => Interlocked.Increment(ref _connectionsRejected);
    public void AuthenticationFailed() => Interlocked.Increment(ref _authFailures);
    public void PublishReceived() => Interlocked.Increment(ref _publishesReceived);
    public void PublishSent() => Interlocked.Increment(ref _publishesSent);
    public void AddBytesReceived(long count) => Interlocked.Add(ref _bytesReceived, count);
    public void AddBytesSent(long count) => Interlocked.Add(ref _bytesSent, count);
    public void MessageDropped() => Interlocked.Increment(ref _droppedMessages);
    public void Retransmitted() => Interlocked.Increment(ref _retransmissions);
    public void SubscriptionCreated() => Interlocked.Increment(ref _subscriptionsCreated);
    public void SessionExpired() => Interlocked.Increment(ref _sessionsExpired);
    public void ConnectionRateLimited() => Interlocked.Increment(ref _connectionsRateLimited);
    public void PublishRateLimited() => Interlocked.Increment(ref _publishesRateLimited);

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

public sealed record SubscriptionSnapshot(
    string ClientId,
    string Filter,
    string? SharedGroup,
    string QoS,
    bool NoLocal,
    bool RetainAsPublished,
    string RetainHandling,
    int? Identifier);

public sealed record RetainedSnapshot(
    string Topic,
    int PayloadBytes,
    string QoS,
    DateTimeOffset StoredAt,
    DateTimeOffset? ExpiresAt);

public sealed record ClusterNodeSnapshot(
    string NodeId,
    string Address,
    string Status,
    DateTimeOffset LastSeen,
    bool IsSelf);

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

    public RateTracker(int capacitySeconds = 60)
    {
        _capacity = capacitySeconds;
    }

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

    private void Trim(long now)
    {
        while (_samples.TryPeek(out var sample) && now - sample.Second >= _capacity)
        {
            _samples.TryDequeue(out _);
        }
    }
}
