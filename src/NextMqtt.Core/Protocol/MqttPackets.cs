namespace NextMqtt.Core.Protocol;

/// <summary>Base type for every MQTT control packet.</summary>
/// <remarks>所有报文共有的只有类型；具体字段由派生类提供。</remarks>
public abstract class MqttPacket
{
    /// <summary>Control packet type taken from the fixed header.</summary>
    /// <remarks>用于分派编解码器，不参与业务判断。</remarks>
    public abstract MqttPacketType Type { get; }
}

/// <summary>CONNECT packet.</summary>
/// <remarks>会话的全部初始状态都来自这里：身份、保活、遗嘱和 MQTT 5 属性。</remarks>
public sealed class ConnectPacket : MqttPacket
{
    /// <summary>Always <see cref="MqttPacketType.Connect"/>.</summary>
    /// <remarks>固定头类型，恒为 1。</remarks>
    public override MqttPacketType Type => MqttPacketType.Connect;

    /// <summary>Protocol level requested by the client (4 or 5).</summary>
    /// <remarks>决定后续报文能否携带 MQTT 5 属性。</remarks>
    public MqttProtocolVersion ProtocolVersion { get; init; } = MqttProtocolVersion.V500;

    /// <summary>True when the client asks for a fresh session.</summary>
    /// <remarks>为 true 时服务端丢弃旧会话；为 false 才可能恢复订阅与队列。</remarks>
    public bool CleanStart { get; init; }

    /// <summary>Keep-alive interval in seconds; 0 disables the timer.</summary>
    /// <remarks>实际超时按协议放宽到 1.5 倍，避免网络抖动误杀。</remarks>
    public ushort KeepAliveSeconds { get; init; }

    /// <summary>Client identifier; may be empty when the broker assigns one.</summary>
    /// <remarks>空 ID 仅在允许分配且不是 3.1.1 持久会话时被接受。</remarks>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>MQTT 5 CONNECT properties (session expiry, limits, authentication).</summary>
    /// <remarks>3.1.1 报文如果带属性会直接按协议错误拒绝。</remarks>
    public MqttProperties Properties { get; init; } = new();

    /// <summary>Will message published when the connection ends abnormally.</summary>
    /// <remarks>正常 DISCONNECT 会抑制遗嘱，除非显式要求 0x04。</remarks>
    public MqttWill? Will { get; init; }

    /// <summary>Username, when the client supplies credentials.</summary>
    /// <remarks>为空表示匿名或仅用 JWT 密码字段。</remarks>
    public string? Username { get; init; }

    /// <summary>Password bytes; the codec never logs or decodes them.</summary>
    /// <remarks>保持原始字节，JWT 场景下也可能是令牌文本。</remarks>
    public byte[]? Password { get; init; }
}

/// <summary>Will message carried by CONNECT.</summary>
/// <remarks>与普通发布结构一致，但只在异常断开时生效。</remarks>
public sealed class MqttWill
{
    /// <summary>Topic the will is published to.</summary>
    /// <remarks>必须是合法主题名，不能含通配符。</remarks>
    public required string Topic { get; init; }

    /// <summary>Will payload bytes.</summary>
    /// <remarks>长度受 Maximum Packet Size 约束。</remarks>
    public required byte[] Payload { get; init; }

    /// <summary>QoS used when publishing the will.</summary>
    /// <remarks>取客户端请求的 QoS，服务端不再提升。</remarks>
    public MqttQoS QoS { get; init; }

    /// <summary>Whether the will is stored as a retained message.</summary>
    /// <remarks>为 true 时遗嘱会进入保留存储，并参与集群复制。</remarks>
    public bool Retain { get; init; }

    /// <summary>MQTT 5 will properties (delay, format, correlation).</summary>
    /// <remarks>延迟发布由 Will Delay Interval 控制，断开时会取消计时。</remarks>
    public MqttProperties Properties { get; init; } = new();
}

/// <summary>CONNACK packet.</summary>
/// <remarks>首个应答；失败时客户端只能读到这里。</remarks>
public sealed class ConnAckPacket : MqttPacket
{
    /// <summary>Always <see cref="MqttPacketType.ConnAck"/>.</summary>
    /// <remarks>固定头类型，恒为 2。</remarks>
    public override MqttPacketType Type => MqttPacketType.ConnAck;

    /// <summary>True when the server resumed an existing session.</summary>
    /// <remarks>只在客户端请求恢复且服务端确实找到会话时为 true。</remarks>
    public bool SessionPresent { get; init; }

    /// <summary>Success (0x00) or a rejection reason code.</summary>
    /// <remarks>3.1.1 只认 0x00-0x05，其余按服务不可用处理。</remarks>
    public byte ReasonCode { get; init; } = MqttReasonCodes.Success;

    /// <summary>MQTT 5 CONNACK properties (limits, assigned id, server reference).</summary>
    /// <remarks>服务端在这里公布 Receive Maximum、最大报文和主题别名上限。</remarks>
    public MqttProperties Properties { get; init; } = new();
}

/// <summary>PUBLISH packet.</summary>
/// <remarks>唯一携带应用数据的报文，QoS 决定后续握手。</remarks>
public sealed class PublishPacket : MqttPacket
{
    /// <summary>Always <see cref="MqttPacketType.Publish"/>.</summary>
    /// <remarks>固定头类型，恒为 3。</remarks>
    public override MqttPacketType Type => MqttPacketType.Publish;

    /// <summary>Topic name or alias target carried by the packet.</summary>
    /// <remarks>使用主题别名时接收端会把它替换成真实主题名。</remarks>
    public string Topic { get; init; } = string.Empty;

    /// <summary>Application payload; empty is legal.</summary>
    /// <remarks>空负载配合 retain 表示删除保留消息。</remarks>
    public byte[] Payload { get; init; } = [];

    /// <summary>Delivery guarantee requested by the sender.</summary>
    /// <remarks>实际下发取发布 QoS 与订阅 QoS 的较小值。</remarks>
    public MqttQoS QoS { get; init; }

    /// <summary>Whether the broker should retain the message.</summary>
    /// <remarks>retain 只在发布时有效，转发时按订阅的 RAP 选项决定保留位。</remarks>
    public bool Retain { get; init; }

    /// <summary>True for a retransmission of an unacknowledged packet.</summary>
    /// <remarks>仅 QoS 1/2 重传时置位，客户端据此去重。</remarks>
    public bool Duplicate { get; init; }

    /// <summary>Packet identifier; zero for QoS 0.</summary>
    /// <remarks>QoS 1/2 时在单个连接内唯一，重传沿用同一个值。</remarks>
    public ushort PacketId { get; init; }

    /// <summary>MQTT 5 publish properties (format, expiry, response topic, user data).</summary>
    /// <remarks>过期时间在这里是相对秒数，服务端会换算成绝对时间。</remarks>
    public MqttProperties Properties { get; init; } = new();

    /// <summary>Payload size in bytes, without re-counting the array.</summary>
    /// <remarks>直接读数组长度，用于日志和配额判断。</remarks>
    public long PayloadLength => Payload.Length;
}

/// <summary>Base for PUBACK, PUBREC, PUBREL and PUBCOMP.</summary>
/// <remarks>四个报文结构相同，只差类型与所在握手阶段。</remarks>
public abstract class MqttAcknowledgementPacket : MqttPacket
{
    /// <summary>Packet identifier being acknowledged.</summary>
    /// <remarks>与对应的 PUBLISH 或 PUBREC 保持一致。</remarks>
    public ushort PacketId { get; init; }

    /// <summary>Success or a reason code describing the rejection.</summary>
    /// <remarks>3.1.1 没有原因码字段，这里恒为成功。</remarks>
    public byte ReasonCode { get; init; } = MqttReasonCodes.Success;

    /// <summary>MQTT 5 acknowledgement properties (reason string, user data).</summary>
    /// <remarks>仅在 MQTT 5 连接上编码，避免污染 3.1.1 报文。</remarks>
    public MqttProperties Properties { get; init; } = new();
}

/// <summary>PUBACK — QoS 1 delivery acknowledged.</summary>
/// <remarks>收到即表示该消息不会再重传。</remarks>
public sealed class PubAckPacket : MqttAcknowledgementPacket
{
    /// <summary>Always <see cref="MqttPacketType.PubAck"/>.</summary>
    /// <remarks>固定头类型，恒为 4。</remarks>
    public override MqttPacketType Type => MqttPacketType.PubAck;
}

/// <summary>PUBREC — QoS 2 publish received, awaiting PUBREL.</summary>
/// <remarks>发送方收到后丢弃原消息，只保留标识等待 PUBREL。</remarks>
public sealed class PubRecPacket : MqttAcknowledgementPacket
{
    /// <summary>Always <see cref="MqttPacketType.PubRec"/>.</summary>
    /// <remarks>固定头类型，恒为 5。</remarks>
    public override MqttPacketType Type => MqttPacketType.PubRec;
}

/// <summary>PUBREL — QoS 2 release of a stored publish.</summary>
/// <remarks>接收方此时才把消息交给订阅者，保证不重复。</remarks>
public sealed class PubRelPacket : MqttAcknowledgementPacket
{
    /// <summary>Always <see cref="MqttPacketType.PubRel"/>.</summary>
    /// <remarks>固定头类型，恒为 6。</remarks>
    public override MqttPacketType Type => MqttPacketType.PubRel;
}

/// <summary>PUBCOMP — QoS 2 handshake finished.</summary>
/// <remarks>收到后双方都可以释放该报文标识。</remarks>
public sealed class PubCompPacket : MqttAcknowledgementPacket
{
    /// <summary>Always <see cref="MqttPacketType.PubComp"/>.</summary>
    /// <remarks>固定头类型，恒为 7。</remarks>
    public override MqttPacketType Type => MqttPacketType.PubComp;
}

/// <summary>A single topic filter and its options inside SUBSCRIBE.</summary>
/// <remarks>一个 SUBSCRIBE 可以带多个过滤器，SUBACK 按同样顺序返回每个的结果。</remarks>
/// <param name="Filter">Filter text, possibly a shared-subscription filter.</param>
/// <param name="Options">QoS, noLocal, retain-as-published and retain handling.</param>
public readonly record struct MqttSubscriptionRequest(string Filter, MqttSubscriptionOptions Options);

/// <summary>SUBSCRIBE packet.</summary>
/// <remarks>订阅是幂等的：重复订阅同一过滤器会替换选项而不是报错。</remarks>
public sealed class SubscribePacket : MqttPacket
{
    /// <summary>Always <see cref="MqttPacketType.Subscribe"/>.</summary>
    /// <remarks>固定头类型，恒为 8。</remarks>
    public override MqttPacketType Type => MqttPacketType.Subscribe;

    /// <summary>Packet identifier used by the matching SUBACK.</summary>
    /// <remarks>在连接内唯一，SUBACK 原样返回。</remarks>
    public ushort PacketId { get; init; }

    /// <summary>MQTT 5 SUBSCRIBE properties (subscription identifier).</summary>
    /// <remarks>订阅标识会随匹配的消息一起下发，便于客户端区分来源。</remarks>
    public MqttProperties Properties { get; init; } = new();

    /// <summary>Filters in request order; SUBACK reason codes match by index.</summary>
    /// <remarks>顺序不能变，否则客户端无法把结果对应回请求。</remarks>
    public List<MqttSubscriptionRequest> Subscriptions { get; init; } = [];
}

/// <summary>SUBACK packet.</summary>
/// <remarks>每个过滤器一个字节：授予的 QoS 或拒绝原因。</remarks>
public sealed class SubAckPacket : MqttPacket
{
    /// <summary>Always <see cref="MqttPacketType.SubAck"/>.</summary>
    /// <remarks>固定头类型，恒为 9。</remarks>
    public override MqttPacketType Type => MqttPacketType.SubAck;

    /// <summary>Identifier copied from the SUBSCRIBE being answered.</summary>
    /// <remarks>与请求一一对应，客户端据此匹配等待中的订阅。</remarks>
    public ushort PacketId { get; init; }

    /// <summary>MQTT 5 SUBACK properties (reason string, user data).</summary>
    /// <remarks>仅在 MQTT 5 连接上编码。</remarks>
    public MqttProperties Properties { get; init; } = new();

    /// <summary>Granted QoS or failure code per requested filter.</summary>
    /// <remarks>0x80 以上表示该过滤器被拒绝，不允许订阅。</remarks>
    public List<byte> ReasonCodes { get; init; } = [];
}

/// <summary>UNSUBSCRIBE packet.</summary>
/// <remarks>取消不存在的订阅不算错误，只是返回 0x11。</remarks>
public sealed class UnsubscribePacket : MqttPacket
{
    /// <summary>Always <see cref="MqttPacketType.Unsubscribe"/>.</summary>
    /// <remarks>固定头类型，恒为 10。</remarks>
    public override MqttPacketType Type => MqttPacketType.Unsubscribe;

    /// <summary>Packet identifier used by the matching UNSUBACK.</summary>
    /// <remarks>在连接内唯一，UNSUBACK 原样返回。</remarks>
    public ushort PacketId { get; init; }

    /// <summary>MQTT 5 UNSUBSCRIBE properties (user data).</summary>
    /// <remarks>目前只透传用户属性，不参与匹配。</remarks>
    public MqttProperties Properties { get; init; } = new();

    /// <summary>Filters to remove, as originally supplied by the client.</summary>
    /// <remarks>必须与订阅时的原文一致，共享订阅也不能省略前缀。</remarks>
    public List<string> Filters { get; init; } = [];
}

/// <summary>UNSUBACK packet.</summary>
/// <remarks>每个过滤器一个结果字节，顺序与请求一致。</remarks>
public sealed class UnsubAckPacket : MqttPacket
{
    /// <summary>Always <see cref="MqttPacketType.UnsubAck"/>.</summary>
    /// <remarks>固定头类型，恒为 11。</remarks>
    public override MqttPacketType Type => MqttPacketType.UnsubAck;

    /// <summary>Identifier copied from the UNSUBSCRIBE being answered.</summary>
    /// <remarks>客户端据此结束等待。</remarks>
    public ushort PacketId { get; init; }

    /// <summary>MQTT 5 UNSUBACK properties.</summary>
    /// <remarks>失败时可能带 Reason String，方便排查。</remarks>
    public MqttProperties Properties { get; init; } = new();

    /// <summary>Result per requested filter (0x00 or 0x11).</summary>
    /// <remarks>0x00 表示已删除，0x11 表示本来就没有。</remarks>
    public List<byte> ReasonCodes { get; init; } = [];
}

/// <summary>PINGREQ packet.</summary>
/// <remarks>无字段，单例复用避免每次分配。</remarks>
public sealed class PingReqPacket : MqttPacket
{
    /// <summary>Shared instance; the packet carries no data.</summary>
    /// <remarks>线程安全，因为没有可变状态。</remarks>
    public static PingReqPacket Instance { get; } = new();

    /// <summary>Always <see cref="MqttPacketType.PingReq"/>.</summary>
    /// <remarks>固定头类型，恒为 12。</remarks>
    public override MqttPacketType Type => MqttPacketType.PingReq;
}

/// <summary>PINGRESP packet.</summary>
/// <remarks>服务端必须在保活窗口内回复，否则客户端会重连。</remarks>
public sealed class PingRespPacket : MqttPacket
{
    /// <summary>Shared instance; the packet carries no data.</summary>
    /// <remarks>线程安全，因为没有可变状态。</remarks>
    public static PingRespPacket Instance { get; } = new();

    /// <summary>Always <see cref="MqttPacketType.PingResp"/>.</summary>
    /// <remarks>固定头类型，恒为 13。</remarks>
    public override MqttPacketType Type => MqttPacketType.PingResp;
}

/// <summary>DISCONNECT packet.</summary>
/// <remarks>3.1.1 只有“正常断开”一种含义；5.0 用它说明关闭原因。</remarks>
public sealed class DisconnectPacket : MqttPacket
{
    /// <summary>Always <see cref="MqttPacketType.Disconnect"/>.</summary>
    /// <remarks>固定头类型，恒为 14。</remarks>
    public override MqttPacketType Type => MqttPacketType.Disconnect;

    /// <summary>Success, a protocol error or a code explaining the close.</summary>
    /// <remarks>0x00 表示正常关闭，会抑制遗嘱。</remarks>
    public byte ReasonCode { get; init; } = MqttReasonCodes.Success;

    /// <summary>MQTT 5 DISCONNECT properties (session expiry update, reason string).</summary>
    /// <remarks>可以在断开时修改会话过期时间。</remarks>
    public MqttProperties Properties { get; init; } = new();
}

/// <summary>AUTH packet (MQTT 5 enhanced authentication).</summary>
/// <remarks>只在 CONNECT 声明了认证方法时出现，3.1.1 没有这个报文。</remarks>
public sealed class AuthPacket : MqttPacket
{
    /// <summary>Always <see cref="MqttPacketType.Auth"/>.</summary>
    /// <remarks>固定头类型，恒为 15。</remarks>
    public override MqttPacketType Type => MqttPacketType.Auth;

    /// <summary>Continue (0x18), re-authenticate (0x19) or success (0x00).</summary>
    /// <remarks>0x18 表示还需要一轮，客户端必须继续回应。</remarks>
    public byte ReasonCode { get; init; } = MqttReasonCodes.ContinueAuthentication;

    /// <summary>Authentication method and data properties.</summary>
    /// <remarks>方法名必须与 CONNECT 中声明的一致，否则按协议错误处理。</remarks>
    public MqttProperties Properties { get; init; } = new();
}
