namespace NextMqtt.Core.Protocol;

/// <summary>MQTT protocol levels supported by the broker.</summary>
/// <remarks>3.1.1 与 5.0；级别决定报文字段，不决定功能开关。</remarks>
public enum MqttProtocolVersion : byte
{
    /// <summary>MQTT 3.1.1 (protocol level 4).</summary>
    /// <remarks>MQTT 3.1.1，协议级别 4。</remarks>
    V311 = 4,

    /// <summary>MQTT 5.0 (protocol level 5).</summary>
    /// <remarks>MQTT 5.0，协议级别 5。</remarks>
    V500 = 5,
}

/// <summary>MQTT control packet types (fixed header bits 7-4).</summary>
/// <remarks>固定头 4 位类型字段，共 15 种。</remarks>
public enum MqttPacketType : byte
{
    /// <summary>Client request that opens a session (type 1).</summary>
    /// <remarks>客户端打开会话的请求，固定头类型 1。</remarks>
    Connect = 1,

    /// <summary>Server acknowledgement of CONNECT (type 2).</summary>
    /// <remarks>服务端对 CONNECT 的应答，类型 2。</remarks>
    ConnAck = 2,

    /// <summary>Application message in either direction (type 3).</summary>
    /// <remarks>双向传输的应用消息，类型 3。</remarks>
    Publish = 3,

    /// <summary>QoS 1 acknowledgement of a PUBLISH (type 4).</summary>
    /// <remarks>QoS 1 收到消息后的确认，类型 4。</remarks>
    PubAck = 4,

    /// <summary>QoS 2 first acknowledgement (type 5).</summary>
    /// <remarks>QoS 2 的第一阶段确认，类型 5。</remarks>
    PubRec = 5,

    /// <summary>QoS 2 release after PUBREC (type 6).</summary>
    /// <remarks>QoS 2 收到 PUBREC 后释放消息，类型 6。</remarks>
    PubRel = 6,

    /// <summary>QoS 2 completion after PUBREL (type 7).</summary>
    /// <remarks>QoS 2 在 PUBREL 之后收尾，类型 7。</remarks>
    PubComp = 7,

    /// <summary>Subscription request (type 8).</summary>
    /// <remarks>订阅请求，类型 8。</remarks>
    Subscribe = 8,

    /// <summary>Subscription acknowledgement (type 9).</summary>
    /// <remarks>订阅应答，按顺序给出每个过滤器的结果，类型 9。</remarks>
    SubAck = 9,

    /// <summary>Subscription removal request (type 10).</summary>
    /// <remarks>取消订阅请求，类型 10。</remarks>
    Unsubscribe = 10,

    /// <summary>Subscription removal acknowledgement (type 11).</summary>
    /// <remarks>取消订阅应答，类型 11。</remarks>
    UnsubAck = 11,

    /// <summary>Keep-alive ping from the client (type 12).</summary>
    /// <remarks>客户端保活探测，类型 12。</remarks>
    PingReq = 12,

    /// <summary>Keep-alive answer from the server (type 13).</summary>
    /// <remarks>服务端保活应答，类型 13。</remarks>
    PingResp = 13,

    /// <summary>Either peer closing the connection (type 14).</summary>
    /// <remarks>任一端关闭连接，类型 14。</remarks>
    Disconnect = 14,

    /// <summary>MQTT 5 enhanced authentication exchange (type 15).</summary>
    /// <remarks>MQTT 5 增强认证交换，类型 15。</remarks>
    Auth = 15,
}

/// <summary>Quality of service levels.</summary>
/// <remarks>QoS 决定握手轮次和是否可能重复。</remarks>
public enum MqttQoS : byte
{
    /// <summary>Fire and forget; no acknowledgement, duplicates possible.</summary>
    /// <remarks>最多一次：不确认，网络抖动时可能丢失。</remarks>
    AtMostOnce = 0,

    /// <summary>Delivered at least once; PUBACK confirms receipt.</summary>
    /// <remarks>至少一次：靠 PUBACK 确认，可能重复。</remarks>
    AtLeastOnce = 1,

    /// <summary>Delivered exactly once; four-way handshake removes duplicates.</summary>
    /// <remarks>恰好一次：四步握手去重，代价是延迟。</remarks>
    ExactlyOnce = 2,
}

/// <summary>MQTT 5 retain handling options for a subscription.</summary>
/// <remarks>控制订阅时是否下发历史保留消息。</remarks>
public enum MqttRetainHandling : byte
{
    /// <summary>Send retained messages on every subscribe.</summary>
    /// <remarks>每次订阅都下发保留消息。</remarks>
    SendAtSubscribe = 0,

    /// <summary>Send retained messages only when the subscription is new.</summary>
    /// <remarks>只有新建订阅时才下发保留消息。</remarks>
    SendAtSubscribeIfNew = 1,

    /// <summary>Never send retained messages for this subscription.</summary>
    /// <remarks>只收后续发布，忽略已保存的保留值。</remarks>
    DoNotSend = 2,
}

/// <summary>
/// MQTT 5 reason codes, plus the MQTT 3.1.1 CONNACK return codes which share
/// the same byte positions.
/// </summary>
/// <remarks>3.1.1 只用得到 0x00–0x05，其余值只出现在 5.0 连接上；编码前要按协议级别裁剪。</remarks>
public static class MqttReasonCodes
{
    /// <summary>0x00 — the request completed successfully.</summary>
    /// <remarks>请求成功完成。</remarks>
    public const byte Success = 0x00;

    /// <summary>0x01 — SUBACK granted QoS 1 (MQTT 3.1.1 grant, not a reason code).</summary>
    /// <remarks>3.1.1 的 SUBACK 授予 QoS 1；它不是错误码，只是返回值的占位。</remarks>
    public const byte GrantedQoS1 = 0x01;

    /// <summary>0x02 — SUBACK granted QoS 2 (MQTT 3.1.1 grant, not a reason code).</summary>
    /// <remarks>3.1.1 的 SUBACK 授予 QoS 2。</remarks>
    public const byte GrantedQoS2 = 0x02;

    /// <summary>0x04 — DISCONNECT that still publishes the will message.</summary>
    /// <remarks>客户端主动断开，但仍要求发布遗嘱。</remarks>
    public const byte DisconnectWithWill = 0x04;

    /// <summary>0x10 — PUBACK/PUBREC accepted but nobody is subscribed.</summary>
    /// <remarks>消息已受理，但当前没有订阅者，属于提示而非失败。</remarks>
    public const byte NoMatchingSubscribers = 0x10;

    /// <summary>0x11 — UNSUBACK for a filter that was not subscribed.</summary>
    /// <remarks>取消订阅时该过滤器本就不存在。</remarks>
    public const byte NoSubscriptionExisted = 0x11;

    /// <summary>0x18 — AUTH continues the authentication exchange.</summary>
    /// <remarks>增强认证还在进行，需要再一轮 AUTH。</remarks>
    public const byte ContinueAuthentication = 0x18;

    /// <summary>0x19 — AUTH re-authenticates an established connection.</summary>
    /// <remarks>对已建立的连接重新认证。</remarks>
    public const byte ReAuthenticate = 0x19;

    /// <summary>0x80 — failure without a more specific reason.</summary>
    /// <remarks>失败，但没有更具体的原因可报。</remarks>
    public const byte UnspecifiedError = 0x80;

    /// <summary>0x81 — the packet does not follow the wire format.</summary>
    /// <remarks>报文不符合线格式，通常是编码实现有 bug。</remarks>
    public const byte MalformedPacket = 0x81;

    /// <summary>0x82 — a state-machine or reserved-bit violation.</summary>
    /// <remarks>状态机顺序错误或使用了保留位。</remarks>
    public const byte ProtocolError = 0x82;

    /// <summary>0x83 — rejected for reasons only the implementation knows.</summary>
    /// <remarks>实现内部原因导致的拒绝，客户端无法自行纠正。</remarks>
    public const byte ImplementationSpecificError = 0x83;

    /// <summary>0x84 — CONNECT used a protocol level this broker does not serve.</summary>
    /// <remarks>客户端要求的协议版本不受支持。</remarks>
    public const byte UnsupportedProtocolVersion = 0x84;

    /// <summary>0x85 — empty or over-long client identifier.</summary>
    /// <remarks>客户端 ID 为空或超长。</remarks>
    public const byte ClientIdentifierNotValid = 0x85;

    /// <summary>0x86 — username or password rejected.</summary>
    /// <remarks>用户名或密码不正确。</remarks>
    public const byte BadUserNameOrPassword = 0x86;

    /// <summary>0x87 — the client is not allowed to perform the action.</summary>
    /// <remarks>没有权限执行该操作，通常是 ACL 拒绝。</remarks>
    public const byte NotAuthorized = 0x87;

    /// <summary>0x88 — the broker is not accepting connections right now.</summary>
    /// <remarks>服务暂不可用，连接被拒绝。</remarks>
    public const byte ServerUnavailable = 0x88;

    /// <summary>0x89 — the broker is overloaded.</summary>
    /// <remarks>服务过载，建议稍后重试。</remarks>
    public const byte ServerBusy = 0x89;

    /// <summary>0x8A — the client identifier or address is banned.</summary>
    /// <remarks>客户端 ID 或来源地址被封禁。</remarks>
    public const byte Banned = 0x8A;

    /// <summary>0x8B — the broker is shutting down and will not return.</summary>
    /// <remarks>服务正在关闭，不要再重连同一节点。</remarks>
    public const byte ServerShuttingDown = 0x8B;

    /// <summary>0x8C — the requested AUTH method is not supported.</summary>
    /// <remarks>请求的增强认证方法没有对应实现。</remarks>
    public const byte BadAuthenticationMethod = 0x8C;

    /// <summary>0x8D — keep-alive expired; DISCONNECT carries this code.</summary>
    /// <remarks>保活超时，服务端按规范主动断开。</remarks>
    public const byte KeepAliveTimeout = 0x8D;

    /// <summary>0x8E — another connection took over this session.</summary>
    /// <remarks>同一客户端 ID 的新连接接管了会话。</remarks>
    public const byte SessionTakenOver = 0x8E;

    /// <summary>0x8F — the subscription filter is syntactically invalid.</summary>
    /// <remarks>订阅过滤器语法不合法。</remarks>
    public const byte TopicFilterInvalid = 0x8F;

    /// <summary>0x90 — the publication topic name is invalid.</summary>
    /// <remarks>发布主题名不合法（例如含通配符）。</remarks>
    public const byte TopicNameInvalid = 0x90;

    /// <summary>0x91 — the packet identifier is already in flight.</summary>
    /// <remarks>报文标识已被占用，客户端复用了未完成的 ID。</remarks>
    public const byte PacketIdentifierInUse = 0x91;

    /// <summary>0x92 — the packet identifier is unknown to the receiver.</summary>
    /// <remarks>收到未知的报文标识，通常是对端状态不同步。</remarks>
    public const byte PacketIdentifierNotFound = 0x92;

    /// <summary>0x93 — the sender exceeded the receiver's Receive Maximum.</summary>
    /// <remarks>发送方超过了对端声明的 Receive Maximum。</remarks>
    public const byte ReceiveMaximumExceeded = 0x93;

    /// <summary>0x94 — the topic alias is zero or above the negotiated maximum.</summary>
    /// <remarks>主题别名超出协商范围。</remarks>
    public const byte TopicAliasInvalid = 0x94;

    /// <summary>0x95 — the packet exceeds Maximum Packet Size.</summary>
    /// <remarks>报文超过对端声明的最大长度。</remarks>
    public const byte PacketTooLarge = 0x95;

    /// <summary>0x96 — the client published faster than its quota.</summary>
    /// <remarks>发布速率超过配额，消息被丢弃。</remarks>
    public const byte MessageRateTooHigh = 0x96;

    /// <summary>0x97 — an administrative quota was exhausted.</summary>
    /// <remarks>管理侧配额用尽（例如订阅数量上限）。</remarks>
    public const byte QuotaExceeded = 0x97;

    /// <summary>0x98 — an operator closed or blocked the connection.</summary>
    /// <remarks>运维操作导致连接被关闭。</remarks>
    public const byte AdministrativeAction = 0x98;

    /// <summary>0x99 — the payload does not match the declared format indicator.</summary>
    /// <remarks>负载与声明的格式不符（例如声明 UTF-8 却不是）。</remarks>
    public const byte PayloadFormatInvalid = 0x99;

    /// <summary>0x9A — retained messages are disabled on this broker.</summary>
    /// <remarks>本 Broker 未开启保留消息。</remarks>
    public const byte RetainNotSupported = 0x9A;

    /// <summary>0x9B — the requested QoS is higher than the broker allows.</summary>
    /// <remarks>请求的 QoS 高于服务端允许值。</remarks>
    public const byte QoSNotSupported = 0x9B;

    /// <summary>0x9C — the client should reconnect to a different broker.</summary>
    /// <remarks>请改用另一个节点，通常配合 Server Reference 使用。</remarks>
    public const byte UseAnotherServer = 0x9C;

    /// <summary>0x9D — the broker moved permanently; reconnect elsewhere.</summary>
    /// <remarks>节点已永久迁移，客户端应更新地址。</remarks>
    public const byte ServerMoved = 0x9D;

    /// <summary>0x9E — shared subscriptions are disabled.</summary>
    /// <remarks>共享订阅未开启。</remarks>
    public const byte SharedSubscriptionsNotSupported = 0x9E;

    /// <summary>0x9F — too many CONNECTs from the same address.</summary>
    /// <remarks>同一来源地址建连过快，被限流。</remarks>
    public const byte ConnectionRateExceeded = 0x9F;

    /// <summary>0xA0 — the maximum connection time for this session was reached.</summary>
    /// <remarks>达到该会话允许的最长连接时间。</remarks>
    public const byte MaximumConnectTime = 0xA0;

    /// <summary>0xA1 — subscription identifiers are disabled.</summary>
    /// <remarks>未开启订阅标识。</remarks>
    public const byte SubscriptionIdentifiersNotSupported = 0xA1;

    /// <summary>0xA2 — wildcard subscriptions are disabled.</summary>
    /// <remarks>未开启通配符订阅。</remarks>
    public const byte WildcardSubscriptionsNotSupported = 0xA2;
}

/// <summary>Implementation limits from the MQTT 5 specification.</summary>
/// <remarks>这些是协议上限，不是可调策略。</remarks>
public static class MqttLimits
{
    /// <summary>Maximum encodable remaining length (268,435,455 bytes).</summary>
    /// <remarks>变长整数 4 字节的上限，超过无法编码。</remarks>
    public const int MaxRemainingLength = 268_435_455;

    /// <summary>Default maximum packet size accepted by a listener (1 MiB).</summary>
    /// <remarks>可按部署调整；超过直接断开，不缓冲。</remarks>
    public const int DefaultMaxPacketSize = 1024 * 1024;

    /// <summary>Maximum size of a UTF-8 encoded string or binary field.</summary>
    /// <remarks>长度前缀 2 字节，所以字段上限是 65535。</remarks>
    public const int MaxFieldLength = 65_535;
}

/// <summary>Raised when a peer violates the protocol or a configured limit.</summary>
/// <remarks>携带应回给对端的原因码，便于客户端定位。</remarks>
public sealed class MqttProtocolException : Exception
{
    /// <summary>Creates the exception with the DISCONNECT reason code to report.</summary>
    /// <remarks>原因码写进 DISCONNECT；3.1.1 只记日志。</remarks>
    /// <param name="reasonCode">MQTT 5 reason code, sent to the peer when possible.</param>
    /// <param name="message">Operator-facing explanation.</param>
    public MqttProtocolException(byte reasonCode, string message)
        : base(message)
    {
        ReasonCode = reasonCode;
    }

    /// <summary>Reason code that should accompany the DISCONNECT.</summary>
    /// <remarks>连接层据此决定关闭原因。</remarks>
    public byte ReasonCode { get; }

    /// <summary>Creates an exception for a packet that does not parse.</summary>
    /// <remarks>固定 0x81，表示报文格式错误。</remarks>
    /// <param name="message">What the decoder rejected.</param>
    /// <returns>Exception carrying 0x81.</returns>
    public static MqttProtocolException Malformed(string message)
        => new(MqttReasonCodes.MalformedPacket, message);

    /// <summary>Creates an exception for a state-machine violation.</summary>
    /// <remarks>固定 0x82，表示顺序或保留位错误。</remarks>
    /// <param name="message">What the peer did out of order.</param>
    /// <returns>Exception carrying 0x82.</returns>
    public static MqttProtocolException Protocol(string message)
        => new(MqttReasonCodes.ProtocolError, message);

    /// <summary>Creates an exception for a packet above the size limit.</summary>
    /// <remarks>固定 0x95，超过最大报文长度。</remarks>
    /// <param name="message">Which limit was exceeded.</param>
    /// <returns>Exception carrying 0x95.</returns>
    public static MqttProtocolException TooLarge(string message)
        => new(MqttReasonCodes.PacketTooLarge, message);
}

/// <summary>Options attached to a subscription by SUBSCRIBE.</summary>
/// <remarks>每个过滤器一份，影响下发 QoS 与保留位。</remarks>
public readonly record struct MqttSubscriptionOptions(
    MqttQoS QoS,
    bool NoLocal = false,
    bool RetainAsPublished = false,
    MqttRetainHandling RetainHandling = MqttRetainHandling.SendAtSubscribe)
{
    /// <summary>Packs the options into the single subscription-options byte.</summary>
    /// <remarks>位布局见 MQTT 5 规范 3.8.3.1。</remarks>
    /// <returns>Bits 0-1 QoS, 2 noLocal, 3 RAP, 4-5 retain handling.</returns>
    public byte Encode()
    {
        var value = (byte)QoS;
        if (NoLocal)
        {
            value |= 0x04;
        }

        if (RetainAsPublished)
        {
            value |= 0x08;
        }

        value |= (byte)((byte)RetainHandling << 4);
        return value;
    }

    /// <summary>Unpacks and validates the subscription-options byte.</summary>
    /// <remarks>保留位非零或 QoS/retain handling 越界时抛出 0x81。</remarks>
    /// <param name="value">Byte as received in SUBSCRIBE.</param>
    /// <returns>Decoded options.</returns>
    public static MqttSubscriptionOptions Decode(byte value)
    {
        var qos = (MqttQoS)(value & 0x03);
        if (qos is not (MqttQoS.AtMostOnce or MqttQoS.AtLeastOnce or MqttQoS.ExactlyOnce))
        {
            throw MqttProtocolException.Malformed("Invalid subscription QoS.");
        }

        var retainHandling = (MqttRetainHandling)((value >> 4) & 0x03);
        if (retainHandling is not (MqttRetainHandling.SendAtSubscribe
            or MqttRetainHandling.SendAtSubscribeIfNew
            or MqttRetainHandling.DoNotSend))
        {
            throw MqttProtocolException.Malformed("Invalid retain handling value.");
        }

        if ((value & 0xC0) != 0)
        {
            throw MqttProtocolException.Malformed("Reserved subscription option bits must be zero.");
        }

        return new MqttSubscriptionOptions(
            qos,
            (value & 0x04) != 0,
            (value & 0x08) != 0,
            retainHandling);
    }
}
