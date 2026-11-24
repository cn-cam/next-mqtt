using NextMqtt.Broker.Diagnostics;
using NextMqtt.Broker.Options;
using NextMqtt.Broker.Security;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Sessions;

/// <summary>
/// Persistent client state: identity, subscriptions (indexed elsewhere),
/// outbound inflight window, offline queue and inbound QoS 2 handshake.
/// </summary>
public sealed class BrokerSession : IDisposable
{
    private readonly object _gate = new();
    private readonly BrokerOptions _options;
    private readonly BrokerMetrics _metrics;
    private readonly Queue<QueuedMessage> _queue = new();
    private readonly Dictionary<ushort, OutboundInflight> _inflight = new();
    private readonly Dictionary<ushort, PublishPacket> _inboundQos2 = new();
    private IClientConnection? _connection;
    private ushort _nextPacketId;
    private bool _disposed;

    public BrokerSession(string clientId, BrokerOptions options, BrokerMetrics metrics)
    {
        ClientId = clientId;
        _options = options;
        _metrics = metrics;
    }

    public string ClientId { get; }

    public MqttProtocolVersion ProtocolVersion { get; private set; } = MqttProtocolVersion.V311;

    public ClientPrincipal Principal { get; private set; } = ClientPrincipal.Anonymous(string.Empty);

    public string? Username => Principal.Username;

    public bool Persistent { get; private set; }

    public uint SessionExpiryInterval { get; private set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset ConnectedAt { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastSeen { get; private set; } = DateTimeOffset.UtcNow;

    public MqttWill? Will { get; private set; }

    public IClientConnection? Connection
    {
        get
        {
            lock (_gate)
            {
                return _connection;
            }
        }
    }

    public bool IsOnline => Connection is not null;

    public int InflightCount
    {
        get
        {
            lock (_gate)
            {
                return _inflight.Count;
            }
        }
    }

    public int QueuedCount
    {
        get
        {
            lock (_gate)
            {
                return _queue.Count;
            }
        }
    }

    public int PendingQos2Count
    {
        get
        {
            lock (_gate)
            {
                return _inboundQos2.Count;
            }
        }
    }

    public string? RemoteAddress => Connection?.RemoteAddress;

    public void Bind(
        IClientConnection connection,
        ConnectPacket connect,
        ClientPrincipal principal,
        bool persistent,
        uint sessionExpiryInterval)
    {
        lock (_gate)
        {
            _connection = connection;
            ProtocolVersion = connect.ProtocolVersion;
            Principal = principal;
            Persistent = persistent;
            SessionExpiryInterval = sessionExpiryInterval;
            ExpiresAt = null;
            Will = connect.Will;
            ConnectedAt = DateTimeOffset.UtcNow;
            LastSeen = ConnectedAt;
        }
    }

    /// <summary>Detaches the connection if it is still the active one.</summary>
    public bool Detach(IClientConnection connection)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_connection, connection))
            {
                return false;
            }

            _connection = null;
            LastSeen = DateTimeOffset.UtcNow;
            return true;
        }
    }

    public void UpdateSessionExpiry(uint seconds)
    {
        lock (_gate)
        {
            SessionExpiryInterval = seconds;
            Persistent = seconds > 0;
        }
    }

    public void ClearWill()
    {
        lock (_gate)
        {
            Will = null;
        }
    }

    public void ClearQueues()
    {
        lock (_gate)
        {
            _queue.Clear();
            _inflight.Clear();
            _inboundQos2.Clear();
        }
    }

    /// <summary>
    /// Queues or sends a message. Returns false if the message was dropped
    /// (fresh non-persistent session, expired message or full queue).
    /// </summary>
    public bool Deliver(OutboundMessage message)
    {
        if (message.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
        {
            _metrics.MessageDropped();
            return false;
        }

        IClientConnection? connection;
        MqttPacket? packet = null;

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            connection = _connection;

            if (connection is null)
            {
                if (!Persistent)
                {
                    _metrics.MessageDropped();
                    return false;
                }

                EnqueueLocked(message);
                return true;
            }

            if (message.QoS == MqttQoS.AtMostOnce)
            {
                packet = BuildPublish(message, packetId: 0, duplicate: false);
            }
            else if (_inflight.Count >= _options.MaxInflightMessages)
            {
                EnqueueLocked(message);
                return true;
            }
            else
            {
                var packetId = AllocatePacketIdLocked();
                var publish = BuildPublish(message, packetId, duplicate: false);
                _inflight[packetId] = new OutboundInflight(publish, message.QoS);
                packet = publish;
            }
        }

        if (packet is not null && connection is not null)
        {
            if (!connection.TrySend(packet))
            {
                _metrics.MessageDropped();
                return false;
            }

            _metrics.PublishSent();
        }

        return true;
    }

    /// <summary>Resends inflight messages and drains the offline queue.</summary>
    public void Resume()
    {
        IClientConnection? connection;
        var packets = new List<MqttPacket>();

        lock (_gate)
        {
            connection = _connection;
            if (connection is null || _disposed)
            {
                return;
            }

            foreach (var entry in _inflight.Values)
            {
                entry.LastSent = DateTimeOffset.UtcNow;
                entry.Attempts++;
                packets.Add(entry.State == OutboundState.AwaitingPubComp
                    ? new PubRelPacket { PacketId = entry.Packet.PacketId }
                    : ClonePublish(entry.Packet, duplicate: true));

                _metrics.Retransmitted();
            }

            while (_queue.Count > 0 && _inflight.Count < _options.MaxInflightMessages)
            {
                var queued = _queue.Dequeue();

                if (queued.Message.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
                {
                    _metrics.MessageDropped();
                    continue;
                }

                if (queued.Message.QoS == MqttQoS.AtMostOnce)
                {
                    packets.Add(BuildPublish(queued.Message, 0, duplicate: false));
                    continue;
                }

                var packetId = AllocatePacketIdLocked();
                var publish = BuildPublish(queued.Message, packetId, duplicate: false);
                _inflight[packetId] = new OutboundInflight(publish, queued.Message.QoS);
                packets.Add(publish);
            }
        }

        foreach (var packet in packets)
        {
            if (connection.TrySend(packet))
            {
                _metrics.PublishSent();
            }
        }
    }

    public void OnPubAck(ushort packetId)
    {
        lock (_gate)
        {
            if (_inflight.TryGetValue(packetId, out var entry) && entry.State == OutboundState.AwaitingPubAck)
            {
                _inflight.Remove(packetId);
            }
        }
    }

    public void OnPubRec(ushort packetId)
    {
        PubRelPacket? pubRel = null;
        IClientConnection? connection;

        lock (_gate)
        {
            connection = _connection;

            if (connection is null || !_inflight.TryGetValue(packetId, out var entry))
            {
                return;
            }

            if (entry.State == OutboundState.AwaitingPubRec)
            {
                entry.State = OutboundState.AwaitingPubComp;
            }

            if (entry.State == OutboundState.AwaitingPubComp)
            {
                entry.LastSent = DateTimeOffset.UtcNow;
                pubRel = new PubRelPacket { PacketId = packetId };
            }
        }

        if (pubRel is not null && connection is not null)
        {
            connection.TrySend(pubRel);
        }
    }

    public void OnPubComp(ushort packetId)
    {
        lock (_gate)
        {
            if (_inflight.TryGetValue(packetId, out var entry) && entry.State == OutboundState.AwaitingPubComp)
            {
                _inflight.Remove(packetId);
            }
        }
    }

    /// <summary>Registers an inbound QoS 2 PUBLISH. Returns false for a duplicate id.</summary>
    public bool RegisterInboundQos2(ushort packetId, PublishPacket publish)
    {
        lock (_gate)
        {
            if (_inboundQos2.ContainsKey(packetId))
            {
                return false;
            }

            if (_inboundQos2.Count >= _options.MaxInflightMessages * 4)
            {
                return false;
            }

            _inboundQos2[packetId] = publish;
            return true;
        }
    }

    public bool TryCompleteInboundQos2(ushort packetId, out PublishPacket? publish)
    {
        lock (_gate)
        {
            return _inboundQos2.Remove(packetId, out publish!);
        }
    }

    public bool HasInboundQos2(ushort packetId)
    {
        lock (_gate)
        {
            return _inboundQos2.ContainsKey(packetId);
        }
    }

    public void RetransmitDue(DateTimeOffset now)
    {
        IClientConnection? connection;
        var packets = new List<MqttPacket>();

        lock (_gate)
        {
            connection = _connection;
            if (connection is null || _disposed)
            {
                return;
            }

            foreach (var entry in _inflight.Values)
            {
                if (now - entry.LastSent < _options.RetransmitInterval)
                {
                    continue;
                }

                entry.LastSent = now;
                entry.Attempts++;
                packets.Add(entry.State == OutboundState.AwaitingPubComp
                    ? new PubRelPacket { PacketId = entry.Packet.PacketId }
                    : ClonePublish(entry.Packet, duplicate: true));

                _metrics.Retransmitted();
            }
        }

        foreach (var packet in packets)
        {
            connection.TrySend(packet);
        }
    }

    public ClientSnapshot Snapshot(int subscriptionCount)
    {
        lock (_gate)
        {
            return new ClientSnapshot(
                ClientId,
                Principal.Username,
                ProtocolVersion == MqttProtocolVersion.V500 ? "5.0" : "3.1.1",
                _connection is not null,
                _connection?.RemoteAddress,
                ConnectedAt,
                LastSeen,
                SessionExpiryInterval,
                subscriptionCount,
                _inflight.Count,
                _queue.Count,
                Principal.IsSuperuser);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _connection = null;
            _queue.Clear();
            _inflight.Clear();
            _inboundQos2.Clear();
        }
    }

    private void EnqueueLocked(OutboundMessage message)
    {
        while (_queue.Count >= _options.MaxQueuedMessages)
        {
            _queue.Dequeue();
            _metrics.MessageDropped();
        }

        _queue.Enqueue(new QueuedMessage(message, DateTimeOffset.UtcNow));
    }

    private ushort AllocatePacketIdLocked()
    {
        for (var i = 0; i < ushort.MaxValue; i++)
        {
            _nextPacketId = _nextPacketId == ushort.MaxValue ? (ushort)1 : (ushort)(_nextPacketId + 1);

            if (!_inflight.ContainsKey(_nextPacketId))
            {
                return _nextPacketId;
            }
        }

        throw new InvalidOperationException("No packet identifiers available for the session.");
    }

    private PublishPacket BuildPublish(OutboundMessage message, ushort packetId, bool duplicate)
    {
        return new PublishPacket
        {
            Topic = message.Topic,
            Payload = message.Payload,
            QoS = message.QoS,
            Retain = message.Retain,
            Duplicate = duplicate,
            PacketId = packetId,
            Properties = ProtocolVersion == MqttProtocolVersion.V500 ? message.Properties : new MqttProperties(),
        };
    }

    private static PublishPacket ClonePublish(PublishPacket packet, bool duplicate)
    {
        return new PublishPacket
        {
            Topic = packet.Topic,
            Payload = packet.Payload,
            QoS = packet.QoS,
            Retain = packet.Retain,
            Duplicate = duplicate,
            PacketId = packet.PacketId,
            Properties = packet.Properties,
        };
    }

    private sealed record QueuedMessage(OutboundMessage Message, DateTimeOffset EnqueuedAt);

    private enum OutboundState
    {
        AwaitingPubAck,
        AwaitingPubRec,
        AwaitingPubComp,
    }

    private sealed class OutboundInflight
    {
        public OutboundInflight(PublishPacket packet, MqttQoS qos)
        {
            Packet = packet;
            State = qos == MqttQoS.AtLeastOnce ? OutboundState.AwaitingPubAck : OutboundState.AwaitingPubRec;
        }

        public PublishPacket Packet { get; }

        public OutboundState State { get; set; }

        public DateTimeOffset LastSent { get; set; } = DateTimeOffset.UtcNow;

        public int Attempts { get; set; }
    }
}
