using NextMqtt.Broker.Diagnostics;
using NextMqtt.Broker.Options;
using NextMqtt.Broker.Security;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Sessions;

/// <summary>
/// Persistent client state: identity, subscriptions (indexed elsewhere),
/// outbound inflight window, offline queue and inbound QoS 2 handshake.
/// </summary>
/// <remarks>只保存与连接无关的状态；连接由 Bind 挂上、Detach 摘掉，订阅索引单独维护，避免会话锁住路由表。</remarks>
public sealed class BrokerSession : IDisposable
{
    /// <summary>Guards the queue, inflight window and inbound QoS 2 table.</summary>
    private readonly object _gate = new();
    /// <summary>Limits copied from the broker at creation time.</summary>
    private readonly BrokerOptions _options;
    /// <summary>Counters updated when messages are dropped or retransmitted.</summary>
    private readonly BrokerMetrics _metrics;
    /// <summary>Messages waiting for a connection, oldest first.</summary>
    private readonly Queue<QueuedMessage> _queue = new();
    /// <summary>Sent but unacknowledged messages, keyed by packet identifier.</summary>
    private readonly Dictionary<ushort, OutboundInflight> _inflight = new();
    /// <summary>Inbound QoS 2 messages waiting for PUBREL.</summary>
    private readonly Dictionary<ushort, PublishPacket> _inboundQos2 = new();
    /// <summary>Attached connection, null while offline.</summary>
    private IClientConnection? _connection;
    /// <summary>Last packet identifier handed out; wraps at 65535.</summary>
    private ushort _nextPacketId;
    /// <summary>Set once so dispose cannot run twice.</summary>
    private bool _disposed;

    /// <summary>Creates an empty session; state is filled by <see cref="Bind"/> or <see cref="Restore"/>.</summary>
    /// <remarks>构造时不分配队列也不注册订阅，离线恢复后同样从空开始。</remarks>
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
    /// <remarks>会话的归属键；同一 ID 重连就是恢复这个会话。</remarks>
    public string ClientId { get; }

    /// <summary>Protocol level negotiated by the last CONNECT.</summary>
    /// <remarks>决定下发报文是否带 MQTT 5 属性。</remarks>
    public MqttProtocolVersion ProtocolVersion { get; private set; } = MqttProtocolVersion.V311;

    /// <summary>Authenticated identity, or the anonymous placeholder.</summary>
    /// <remarks>ACL 判断与占位符展开都用它；匿名时只有客户端 ID。</remarks>
    public ClientPrincipal Principal { get; private set; } = ClientPrincipal.Anonymous(string.Empty);

    /// <summary>Username of the current principal, when there is one.</summary>
    /// <remarks>便于日志与后台检索，匿名会话为 null。</remarks>
    public string? Username => Principal.Username;

    /// <summary>True when the session outlives the connection.</summary>
    /// <remarks>由会话过期时间决定，不是 cleanStart。</remarks>
    public bool Persistent { get; private set; }

    /// <summary>Session expiry in seconds; zero means the session ends with the connection.</summary>
    /// <remarks>0 表示断开即删；非 0 才保留订阅与离线队列。</remarks>
    public uint SessionExpiryInterval { get; private set; }

    /// <summary>Absolute expiry while offline; null while connected or non-persistent.</summary>
    /// <remarks>在线时为空，断开时按下发值计算，巡检据此清理。</remarks>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>When the current connection was established.</summary>
    /// <remarks>用于后台展示“已连接时长”，重连会刷新。</remarks>
    public DateTimeOffset ConnectedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Timestamp of the last packet or disconnect.</summary>
    /// <remarks>保活与集群对账都看这个时间。</remarks>
    public DateTimeOffset LastSeen { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Will message registered by the current connection.</summary>
    /// <remarks>只对当前连接生效；正常断开时清掉，避免误发。</remarks>
    public MqttWill? Will { get; private set; }

    /// <summary>Attached connection, or null when the session is offline.</summary>
    /// <remarks>加锁读取；离线时消息走队列而不是直接发送。</remarks>
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
    /// <remarks>等价于 Connection 非空，后台列表用它区分在线状态。</remarks>
    public bool IsOnline => Connection is not null;

    /// <summary>Number of unacknowledged outbound QoS 1/2 messages.</summary>
    /// <remarks>受 MaxInflightMessages 限制，满了就转队列。</remarks>
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
    /// <remarks>受 MaxQueuedMessages 限制，溢出时丢最旧的。</remarks>
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
    /// <remarks>等待 PUBREL 的消息；连接断开后清空。</remarks>
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
    /// <remarks>离线会话没有地址，返回 null。</remarks>
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
    /// <remarks>只处理当前连接，避免旧连接的清理覆盖新连接。</remarks>
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
    /// <remarks>5.0 允许断开时改过期时间；改 0 即立即失效。</remarks>
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
    /// <remarks>正常断开或已发布遗嘱后调用。</remarks>
    public void ClearWill()
    {
        lock (_gate)
        {
            Will = null;
        }
    }

    /// <summary>Clears queued and inflight state when a session is reset.</summary>
    /// <remarks>cleanStart 或管理员清理时使用。</remarks>
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
    /// <remarks>快照是浅拷贝，消息对象本身不可变。</remarks>
    public List<OutboundMessage> SnapshotQueue()
    {
        lock (_gate)
        {
            return _queue.Select(entry => entry.Message).ToList();
        }
    }

    /// <summary>Rebuilds a restored session from disk (always offline).</summary>
    /// <remarks>恢复后没有连接，重连时再由 Bind 接管。</remarks>
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
    /// <remarks>在线直接写连接，离线且持久才入队；入队后按顺序补发，过期消息不再补发。</remarks>
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
    /// <remarks>重连后先补发未确认的，再按顺序清队列。</remarks>
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
    /// <remarks>释放 inflight 槽位并触发队列发送。</remarks>
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
    /// <remarks>把消息状态推进到等待 PUBCOMP。</remarks>
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
    /// <remarks>握手结束，释放槽位。</remarks>
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
    /// <remarks>重复 ID 说明客户端在重传，服务端只需回 PUBREC。</remarks>
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
    /// <remarks>返回已存消息，由调用方负责路由。</remarks>
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
    /// <remarks>用于判断 PUBREL 是否合法。</remarks>
    /// <param name="packetId">Identifier to test.</param>
    public bool HasInboundQos2(ushort packetId)
    {
        lock (_gate)
        {
            return _inboundQos2.ContainsKey(packetId);
        }
    }

    /// <summary>Exports queued and unacknowledged messages for a handover to another node.</summary>
    /// <remarks>
    /// 未确认的消息排在最前：它们在原节点上先于队列被发送，重放顺序必须保持。
    /// 入站 QoS 2 握手不迁移，客户端会带 DUP 重发，新节点按新消息处理。
    /// </remarks>
    /// <param name="now">Used to turn a relative expiry into an absolute one.</param>
    /// <returns>Messages in delivery order.</returns>
    public IReadOnlyList<OutboundMessage> SnapshotForTransfer(DateTimeOffset now)
    {
        lock (_gate)
        {
            var result = new List<OutboundMessage>(_inflight.Count + _queue.Count);

            foreach (var entry in _inflight.Values.OrderBy(item => item.LastSent))
            {
                var packet = entry.Packet;
                DateTimeOffset? expiresAt = null;

                if (packet.Properties.GetUInt32(MqttPropertyId.MessageExpiryInterval) is { } seconds and > 0)
                {
                    expiresAt = now.AddSeconds(seconds);
                }

                result.Add(new OutboundMessage
                {
                    Topic = packet.Topic,
                    Payload = packet.Payload,
                    QoS = packet.QoS,
                    Retain = packet.Retain,
                    Properties = packet.Properties,
                    ExpiresAt = expiresAt,
                });
            }

            foreach (var queued in _queue)
            {
                result.Add(queued.Message);
            }

            return result;
        }
    }

    /// <summary>Drops the will and both queues before the session moves to another node.</summary>
    /// <remarks>与会话迁移配合：迁移不是异常断开，不能因此发布遗嘱。</remarks>
    public void PrepareForTransfer()
    {
        lock (_gate)
        {
            Will = null;
            _queue.Clear();
            _inflight.Clear();
            _inboundQos2.Clear();
        }
    }

    /// <summary>Retransmits unacknowledged messages whose timer elapsed.</summary>
    /// <remarks>只重发超时的，避免把整个窗口一次性打爆。</remarks>
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
    /// <remarks>只做只读投影，不改会话状态。</remarks>
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
    /// <remarks>幂等；关闭过程中可能被调用多次。</remarks>
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

    /// <summary>Appends to the offline queue, dropping the oldest entry when it is full.</summary>
    private void EnqueueLocked(OutboundMessage message)
    {
        while (_queue.Count >= _options.MaxQueuedMessages)
        {
            _queue.Dequeue();
            _metrics.MessageDropped();
        }

        _queue.Enqueue(new QueuedMessage(message, DateTimeOffset.UtcNow));
    }

    /// <summary>Returns the next identifier that is not in flight; throws when the window is full.</summary>
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

    /// <summary>Builds the outbound packet; properties are dropped for 3.1.1 peers.</summary>
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

    /// <summary>Copies a packet to resend it with the duplicate flag set.</summary>
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

    /// <summary>A queued message plus when it entered the queue.</summary>
    private sealed record QueuedMessage(OutboundMessage Message, DateTimeOffset EnqueuedAt);

    /// <summary>Which acknowledgement the outbound window is waiting for.</summary>
    private enum OutboundState
    {
        /// <summary>QoS 1 message waiting for PUBACK.</summary>
        AwaitingPubAck,
        /// <summary>QoS 2 message waiting for PUBREC.</summary>
        AwaitingPubRec,
        /// <summary>QoS 2 message waiting for PUBCOMP.</summary>
        AwaitingPubComp,
    }

    /// <summary>One unacknowledged outbound message and its retry bookkeeping.</summary>
    private sealed class OutboundInflight
    {
        /// <summary>Starts tracking a message and picks the handshake step from its QoS.</summary>
        public OutboundInflight(PublishPacket packet, MqttQoS qos)
        {
            Packet = packet;
            State = qos == MqttQoS.AtLeastOnce ? OutboundState.AwaitingPubAck : OutboundState.AwaitingPubRec;
        }

        /// <summary>Packet that was sent; resent verbatim with the duplicate flag.</summary>
        public PublishPacket Packet { get; }

        /// <summary>Current step of the QoS handshake.</summary>
        public OutboundState State { get; set; }

        /// <summary>When it was last written, used to schedule retransmission.</summary>
        public DateTimeOffset LastSent { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>How many times it has been sent.</summary>
        public int Attempts { get; set; }
    }
}
