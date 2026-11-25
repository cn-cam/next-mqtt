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

    /// <summary>Creates an empty session; state is filled by <see cref="Bind"/> or <see cref="Restore"/>.</summary>
    /// <param name="clientId">Identifier that owns the session.</param>
    /// <param name="options">Broker limits used for inflight and queue bounds.</param>
    /// <param name="metrics">Counters updated when messages are dropped.</param>
    public BrokerSession(string clientId, BrokerOptions options, BrokerMetrics metrics)
    {
        ClientId = clientId;
        _options = options;
        _metrics = metrics;
    }

    /// <summary>Client identifier this session belongs to.</summary>
    public string ClientId { get; }

    /// <summary>Protocol level negotiated by the last CONNECT.</summary>
    public MqttProtocolVersion ProtocolVersion { get; private set; } = MqttProtocolVersion.V311;

    /// <summary>Authenticated identity, or the anonymous placeholder.</summary>
    public ClientPrincipal Principal { get; private set; } = ClientPrincipal.Anonymous(string.Empty);

    /// <summary>Username of the current principal, when there is one.</summary>
    public string? Username => Principal.Username;

    /// <summary>True when the session outlives the connection.</summary>
    public bool Persistent { get; private set; }

    /// <summary>Session expiry in seconds; zero means the session ends with the connection.</summary>
    public uint SessionExpiryInterval { get; private set; }

    /// <summary>Absolute expiry while offline; null while connected or non-persistent.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>When the current connection was established.</summary>
    public DateTimeOffset ConnectedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Timestamp of the last packet or disconnect.</summary>
    public DateTimeOffset LastSeen { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Will message registered by the current connection.</summary>
    public MqttWill? Will { get; private set; }

    /// <summary>Attached connection, or null when the session is offline.</summary>
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

    /// <summary>True while a connection is attached.</summary>
    public bool IsOnline => Connection is not null;

    /// <summary>Number of unacknowledged outbound QoS 1/2 messages.</summary>
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

    /// <summary>Number of messages waiting in the offline queue.</summary>
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

    /// <summary>Inbound QoS 2 publishes received but not yet released by PUBREL.</summary>
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

    /// <summary>Peer address of the current connection, when online.</summary>
    public string? RemoteAddress => Connection?.RemoteAddress;

    /// <summary>Attaches a connection and applies the CONNECT state.</summary>
    /// <remarks>绑定后清空过期时间；会话在断开时才重新计算 ExpiresAt。</remarks>
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

    /// <summary>Applies an expiry requested by an MQTT 5 DISCONNECT.</summary>
    /// <param name="seconds">New session expiry in seconds; 0 makes it non-persistent.</param>
    public void UpdateSessionExpiry(uint seconds)
    {
        lock (_gate)
        {
            SessionExpiryInterval = seconds;
            Persistent = seconds > 0;
        }
    }

    /// <summary>Drops the will, for example after a clean DISCONNECT.</summary>
    public void ClearWill()
    {
        lock (_gate)
        {
            Will = null;
        }
    }

    /// <summary>Clears queued and inflight state when a session is reset.</summary>
    public void ClearQueues()
    {
        lock (_gate)
        {
            _queue.Clear();
            _inflight.Clear();
            _inboundQos2.Clear();
        }
    }

    /// <summary>Copies the offline queue for persistence.</summary>
    public List<OutboundMessage> SnapshotQueue()
    {
        lock (_gate)
        {
            return _queue.Select(entry => entry.Message).ToList();
        }
    }

    /// <summary>Rebuilds a restored session from disk (always offline).</summary>
    public void Restore(
        ClientPrincipal principal,
        MqttProtocolVersion protocolVersion,
        uint sessionExpiryInterval,
        DateTimeOffset? expiresAt,
        DateTimeOffset lastSeen,
        IEnumerable<OutboundMessage> queued)
    {
        lock (_gate)
        {
            Principal = principal;
            ProtocolVersion = protocolVersion;
            Persistent = sessionExpiryInterval > 0;
            SessionExpiryInterval = sessionExpiryInterval;
            ExpiresAt = expiresAt;
            LastSeen = lastSeen;

            foreach (var message in queued)
            {
                EnqueueLocked(message);
            }
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

    /// <summary>Completes an outbound QoS 1 message.</summary>
    /// <param name="packetId">Identifier acknowledged by the client.</param>
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

    /// <summary>Starts the QoS 2 release when the client answers PUBREC.</summary>
    /// <param name="packetId">Identifier acknowledged by the client.</param>
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

    /// <summary>Completes an outbound QoS 2 message.</summary>
    /// <param name="packetId">Identifier acknowledged by the client.</param>
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

    /// <summary>Completes an inbound QoS 2 publish on PUBREL.</summary>
    /// <param name="packetId">Identifier from PUBREL.</param>
    /// <param name="publish">Stored publish when the handshake completes.</param>
    /// <returns><c>true</c> when a stored publish existed.</returns>
    public bool TryCompleteInboundQos2(ushort packetId, out PublishPacket? publish)
    {
        lock (_gate)
        {
            return _inboundQos2.Remove(packetId, out publish!);
        }
    }

    /// <summary>True when an inbound QoS 2 publish is awaiting PUBREL.</summary>
    /// <param name="packetId">Identifier to test.</param>
    public bool HasInboundQos2(ushort packetId)
    {
        lock (_gate)
        {
            return _inboundQos2.ContainsKey(packetId);
        }
    }

    /// <summary>Retransmits unacknowledged messages whose timer elapsed.</summary>
    /// <param name="now">Current time, used against RetransmitInterval.</param>
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

    /// <summary>Projects this session into the clients table.</summary>
    /// <param name="subscriptionCount">Subscriptions indexed for this client.</param>
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

    /// <summary>Releases the connection and pending timers.</summary>
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
