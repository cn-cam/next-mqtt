using System.Buffers;

namespace NextMqtt.Core.Protocol;

/// <summary>MQTT 5 property identifiers.</summary>
/// <remarks>编号与规范一致；存储与集群转发都按这个值解释，不能重排。</remarks>
public enum MqttPropertyId
{
    /// <summary>0x01 — payload is UTF-8 text (0) or unspecified binary (1).</summary>
    /// <remarks>负载格式提示：1 表示 UTF-8 文本，0 表示未指定；不影响存储，只影响接收端解析。</remarks>
    PayloadFormatIndicator = 1,

    /// <summary>0x02 — seconds until the message expires.</summary>
    /// <remarks>消息过期秒数；服务端会换算成绝对时间，过期后不再投递。</remarks>
    MessageExpiryInterval = 2,

    /// <summary>0x03 — MIME type of the payload.</summary>
    /// <remarks>负载的 MIME 类型，纯提示信息。</remarks>
    ContentType = 3,

    /// <summary>0x08 — topic the receiver should reply to.</summary>
    /// <remarks>请求/响应模式里的回复主题，接收端据此回消息。</remarks>
    ResponseTopic = 8,

    /// <summary>0x09 — opaque data correlating a reply with its request.</summary>
    /// <remarks>不透明关联数据，回复时原样带回，用来匹配请求。</remarks>
    CorrelationData = 9,

    /// <summary>0x0B — identifier echoed back with matching deliveries.</summary>
    /// <remarks>订阅标识；匹配的消息下发时会带上它，便于客户端区分来源。</remarks>
    SubscriptionIdentifier = 11,

    /// <summary>0x11 — how long the session outlives the connection.</summary>
    /// <remarks>会话过期秒数；0 表示断开即结束，非 0 才保留订阅与队列。</remarks>
    SessionExpiryInterval = 17,

    /// <summary>0x12 — client identifier chosen by the server.</summary>
    /// <remarks>服务端分配的客户端 ID，返回给用了空 ID 的客户端。</remarks>
    AssignedClientIdentifier = 18,

    /// <summary>0x13 — keep-alive the server wants the client to use.</summary>
    /// <remarks>服务端要求的保活秒数，客户端应改用这个值。</remarks>
    ServerKeepAlive = 19,

    /// <summary>0x15 — name of the enhanced authentication method.</summary>
    /// <remarks>增强认证方法名；必须与服务端注册的 provider 一致。</remarks>
    AuthenticationMethod = 21,

    /// <summary>0x16 — method-specific authentication bytes.</summary>
    /// <remarks>认证方法的负载，内容由 provider 自行解释。</remarks>
    AuthenticationData = 22,

    /// <summary>0x17 — whether the client wants reason strings and user properties.</summary>
    /// <remarks>为 0 时服务端应省略原因字符串和用户属性，节省带宽。</remarks>
    RequestProblemInformation = 23,

    /// <summary>0x18 — seconds to wait before publishing the will.</summary>
    /// <remarks>遗嘱延迟秒数；会话提前恢复则取消发布。</remarks>
    WillDelayInterval = 24,

    /// <summary>0x19 — whether the client wants response information.</summary>
    /// <remarks>是否请求响应信息，1 表示需要。</remarks>
    RequestResponseInformation = 25,

    /// <summary>0x1A — response information from the server.</summary>
    /// <remarks>服务端返回的响应信息，通常是一个可用的回复主题前缀。</remarks>
    ResponseInformation = 26,

    /// <summary>0x1C — another server the client may use.</summary>
    /// <remarks>引导客户端改用另一个节点，配合 0x9C/0x9D 使用。</remarks>
    ServerReference = 28,

    /// <summary>0x1F — human-readable reason for a failure.</summary>
    /// <remarks>面向人的失败原因，排障时看这里。</remarks>
    ReasonString = 31,

    /// <summary>0x21 — receive maximum (inflight window) the sender allows.</summary>
    /// <remarks>发送方允许的未确认消息数，即 inflight 窗口。</remarks>
    ReceiveMaximum = 33,

    /// <summary>0x22 — largest topic alias the sender accepts.</summary>
    /// <remarks>发送方接受的最大主题别名数量，0 表示不支持别名。</remarks>
    TopicAliasMaximum = 34,

    /// <summary>0x23 — numeric alias replacing the topic name on later publishes.</summary>
    /// <remarks>主题别名；首次出现必须带主题名，之后可只带别名。</remarks>
    TopicAlias = 35,

    /// <summary>0x24 — highest QoS the server supports.</summary>
    /// <remarks>服务端支持的最高 QoS，客户端不应超过。</remarks>
    MaximumQoS = 36,

    /// <summary>0x25 — whether retained messages are available.</summary>
    /// <remarks>是否支持保留消息，1 表示支持。</remarks>
    RetainAvailable = 37,

    /// <summary>0x26 — application-defined key/value pair; may repeat.</summary>
    /// <remarks>应用自定义键值对，可以重复，顺序保留。</remarks>
    UserProperty = 38,

    /// <summary>0x27 — largest packet the sender will accept.</summary>
    /// <remarks>发送方接受的最大报文长度，超过会被拒绝。</remarks>
    MaximumPacketSize = 39,

    /// <summary>0x28 — whether wildcard subscriptions are available.</summary>
    /// <remarks>是否支持通配符订阅。</remarks>
    WildcardSubscriptionAvailable = 40,

    /// <summary>0x29 — whether subscription identifiers are available.</summary>
    /// <remarks>是否支持订阅标识。</remarks>
    SubscriptionIdentifierAvailable = 41,

    /// <summary>0x2A — whether shared subscriptions are available.</summary>
    /// <remarks>是否支持共享订阅。</remarks>
    SharedSubscriptionAvailable = 42,
}

/// <summary>Wire encoding of an MQTT 5 property value.</summary>
/// <remarks>决定解码时读几个字节，也决定持久化后怎么还原。</remarks>
public enum MqttPropertyDataType
{
    /// <summary>Single byte.</summary>
    /// <remarks>单字节，用于布尔式提示。</remarks>
    Byte,

    /// <summary>Two-byte unsigned integer, big endian.</summary>
    /// <remarks>两字节无符号整数，网络字节序。</remarks>
    UInt16,

    /// <summary>Four-byte unsigned integer, big endian.</summary>
    /// <remarks>四字节无符号整数，网络字节序。</remarks>
    UInt32,

    /// <summary>1-4 byte variable byte integer.</summary>
    /// <remarks>1–4 字节变长整数，MQTT 专用编码。</remarks>
    VariableByteInteger,

    /// <summary>Length-prefixed UTF-8 string.</summary>
    /// <remarks>长度前缀的 UTF-8 字符串，上限 65535 字节。</remarks>
    String,

    /// <summary>Length-prefixed binary data.</summary>
    /// <remarks>长度前缀的二进制数据。</remarks>
    Binary,

    /// <summary>Two length-prefixed strings (user property).</summary>
    /// <remarks>两个长度前缀字符串组成一个用户属性。</remarks>
    StringPair,
}

/// <summary>A single decoded MQTT 5 property.</summary>
/// <remarks>解码后的属性条目；重复用户属性各自一条。</remarks>
public readonly record struct MqttProperty(MqttPropertyId Id, object Value);

/// <summary>
/// Ordered MQTT 5 property bag. User properties may repeat; all other
/// properties are unique.
/// </summary>
/// <remarks>顺序保留，因为用户属性可能有业务含义；其余属性重复出现按协议错误处理。</remarks>
public sealed class MqttProperties
{
    /// <summary>Properties in insertion order; user properties may repeat.</summary>
    private readonly List<MqttProperty> _items = [];

    /// <summary>Properties in insertion order; user properties may repeat.</summary>
    /// <remarks>按插入顺序保存；用户属性允许重复，其余属性唯一。</remarks>
    public IReadOnlyList<MqttProperty> Items => _items;

    /// <summary>Number of individual property entries.</summary>
    /// <remarks>属性条目数，重复的用户属性各算一条。</remarks>
    public int Count => _items.Count;

    /// <summary>True when the bag holds no properties.</summary>
    /// <remarks>空属性包，编码时直接写长度 0。</remarks>
    public bool IsEmpty => _items.Count == 0;

    /// <summary>Adds a property after validating its type and uniqueness.</summary>
    /// <remarks>除 UserProperty/SubscriptionIdentifier 外，重复添加会抛出 0x82。</remarks>
    /// <param name="id">Property identifier.</param>
    /// <param name="value">Boxed value matching <see cref="MqttProperties.GetDataType"/>.</param>
    /// <returns>The same bag, for chaining.</returns>
    public MqttProperties Add(MqttPropertyId id, object value)
    {
        MqttPropertyValidation.ValidateValue(id, value);

        if (id is not (MqttPropertyId.UserProperty or MqttPropertyId.SubscriptionIdentifier))
        {
            foreach (var existing in _items)
            {
                if (existing.Id == id)
                {
                    throw MqttProtocolException.Protocol($"Property {id} must not be repeated.");
                }
            }
        }

        _items.Add(new MqttProperty(id, value));
        return this;
    }

    /// <summary>Adds a byte-valued property.</summary>
    /// <remarks>单字节属性，例如 Payload Format Indicator；值域 0–255。</remarks>
    /// <param name="id">Property identifier.</param>
    /// <param name="value">Value in the range 0-255.</param>
    /// <returns>The same bag, for chaining.</returns>
    public MqttProperties AddByte(MqttPropertyId id, byte value) => Add(id, value);

    /// <summary>Adds a two-byte unsigned property.</summary>
    /// <remarks>两字节属性，例如 Receive Maximum 与 Topic Alias。</remarks>
    /// <param name="id">Property identifier.</param>
    /// <param name="value">Value in the range 0-65535.</param>
    /// <returns>The same bag, for chaining.</returns>
    public MqttProperties AddUInt16(MqttPropertyId id, ushort value) => Add(id, value);

    /// <summary>Adds a four-byte unsigned property.</summary>
    /// <remarks>四字节属性，例如消息过期与会话过期秒数。</remarks>
    /// <param name="id">Property identifier.</param>
    /// <param name="value">Value in the range 0-4294967295.</param>
    /// <returns>The same bag, for chaining.</returns>
    public MqttProperties AddUInt32(MqttPropertyId id, uint value) => Add(id, value);

    /// <summary>Adds a variable byte integer property.</summary>
    /// <remarks>变长整数属性，目前只用于订阅标识。</remarks>
    /// <param name="id">Property identifier.</param>
    /// <param name="value">Value in the range 0-268435455.</param>
    /// <returns>The same bag, for chaining.</returns>
    public MqttProperties AddVariableByteInteger(MqttPropertyId id, int value) => Add(id, value);

    /// <summary>Adds a UTF-8 string property.</summary>
    /// <remarks>字符串属性；超过 65535 字节会在编码前被拒绝。</remarks>
    /// <param name="id">Property identifier.</param>
    /// <param name="value">String value; must fit the 65535-byte field limit.</param>
    /// <returns>The same bag, for chaining.</returns>
    public MqttProperties AddString(MqttPropertyId id, string value) => Add(id, value);

    /// <summary>Adds a binary property, copying the span.</summary>
    /// <remarks>二进制属性；复制一份，调用方可以复用缓冲区。</remarks>
    /// <param name="id">Property identifier.</param>
    /// <param name="value">Bytes; must fit the 65535-byte field limit.</param>
    /// <returns>The same bag, for chaining.</returns>
    public MqttProperties AddBinary(MqttPropertyId id, ReadOnlySpan<byte> value) => Add(id, value.ToArray());

    /// <summary>Adds one application-defined user property.</summary>
    /// <remarks>用户属性可重复，键值都原样透传，顺序保留。</remarks>
    /// <param name="name">Key; may repeat with different values.</param>
    /// <param name="value">Value paired with the key.</param>
    /// <returns>The same bag, for chaining.</returns>
    public MqttProperties AddUserProperty(string name, string value)
        => Add(MqttPropertyId.UserProperty, new KeyValuePair<string, string>(name, value));

    /// <summary>Reads a byte property, or null when it is absent.</summary>
    /// <remarks>缺失返回 null，而不是默认值，避免把“没设置”当成 0。</remarks>
    /// <param name="id">Property identifier.</param>
    public byte? GetByte(MqttPropertyId id)
    {
        foreach (var property in _items)
        {
            if (property.Id == id)
            {
                return (byte)property.Value;
            }
        }

        return null;
    }

    /// <summary>Reads a two-byte unsigned property, or null when absent.</summary>
    /// <remarks>缺失返回 null；类型不匹配会抛异常，属于编码 bug。</remarks>
    /// <param name="id">Property identifier.</param>
    public ushort? GetUInt16(MqttPropertyId id)
    {
        foreach (var property in _items)
        {
            if (property.Id == id)
            {
                return (ushort)property.Value;
            }
        }

        return null;
    }

    /// <summary>Reads a four-byte unsigned property, or null when absent.</summary>
    /// <remarks>缺失返回 null，调用方需自行决定默认值。</remarks>
    /// <param name="id">Property identifier.</param>
    public uint? GetUInt32(MqttPropertyId id)
    {
        foreach (var property in _items)
        {
            if (property.Id == id)
            {
                return (uint)property.Value;
            }
        }

        return null;
    }

    /// <summary>Reads a variable byte integer property, or null when absent.</summary>
    /// <remarks>缺失返回 null；值域 0–268435455。</remarks>
    /// <param name="id">Property identifier.</param>
    public int? GetVariableByteInteger(MqttPropertyId id)
    {
        foreach (var property in _items)
        {
            if (property.Id == id)
            {
                return (int)property.Value;
            }
        }

        return null;
    }

    /// <summary>Reads a string property, or null when absent.</summary>
    /// <remarks>缺失返回 null；空字符串与缺失是两种情况。</remarks>
    /// <param name="id">Property identifier.</param>
    public string? GetString(MqttPropertyId id)
    {
        foreach (var property in _items)
        {
            if (property.Id == id)
            {
                return (string)property.Value;
            }
        }

        return null;
    }

    /// <summary>Reads a binary property, or null when absent.</summary>
    /// <remarks>返回内部数组，不要原地修改；需要写入时先复制一份。</remarks>
    /// <param name="id">Property identifier.</param>
    public byte[]? GetBinary(MqttPropertyId id)
    {
        foreach (var property in _items)
        {
            if (property.Id == id)
            {
                return (byte[])property.Value;
            }
        }

        return null;
    }

    /// <summary>Enumerates every user property in insertion order.</summary>
    /// <remarks>用户属性可重复，这里按写入顺序全部返回。</remarks>
    public IEnumerable<KeyValuePair<string, string>> GetUserProperties()
    {
        foreach (var property in _items)
        {
            if (property.Id == MqttPropertyId.UserProperty)
            {
                yield return (KeyValuePair<string, string>)property.Value;
            }
        }
    }

    /// <summary>Reads the property length prefix, then every property in it.</summary>
    internal static MqttProperties Read(ref MqttSpanReader reader)
    {
        var properties = new MqttProperties();
        var length = reader.ReadVariableByteInteger();
        var section = new MqttSpanReader(reader.ReadBytes(length));

        while (!section.IsEmpty)
        {
            var rawId = section.ReadVariableByteInteger();
            if (!Enum.IsDefined(typeof(MqttPropertyId), rawId))
            {
                throw MqttProtocolException.Malformed($"Unknown MQTT property identifier 0x{rawId:X2}.");
            }

            var id = (MqttPropertyId)rawId;
            var type = GetDataType(id);
            object value = type switch
            {
                MqttPropertyDataType.Byte => section.ReadByte(),
                MqttPropertyDataType.UInt16 => section.ReadUInt16(),
                MqttPropertyDataType.UInt32 => section.ReadUInt32(),
                MqttPropertyDataType.VariableByteInteger => section.ReadVariableByteInteger(),
                MqttPropertyDataType.String => section.ReadString(),
                MqttPropertyDataType.Binary => section.ReadBinary(),
                MqttPropertyDataType.StringPair => new KeyValuePair<string, string>(section.ReadString(), section.ReadString()),
                _ => throw MqttProtocolException.Malformed("Unsupported property type."),
            };

            properties.Add(id, value);
        }

        return properties;
    }

    /// <summary>Writes the length prefix followed by each property.</summary>
    internal static void Write(IBufferWriter<byte> writer, MqttProperties properties)
    {
        var scratch = new ArrayBufferWriter<byte>(64);

        foreach (var property in properties.Items)
        {
            var type = GetDataType(property.Id);
            MqttWire.WriteVariableByteInteger(scratch, (int)property.Id);

            switch (type)
            {
                case MqttPropertyDataType.Byte:
                    MqttWire.WriteByte(scratch, (byte)property.Value);
                    break;
                case MqttPropertyDataType.UInt16:
                    MqttWire.WriteUInt16(scratch, (ushort)property.Value);
                    break;
                case MqttPropertyDataType.UInt32:
                    MqttWire.WriteUInt32(scratch, (uint)property.Value);
                    break;
                case MqttPropertyDataType.VariableByteInteger:
                    MqttWire.WriteVariableByteInteger(scratch, (int)property.Value);
                    break;
                case MqttPropertyDataType.String:
                    MqttWire.WriteString(scratch, (string)property.Value);
                    break;
                case MqttPropertyDataType.Binary:
                    MqttWire.WriteBinary(scratch, (byte[])property.Value);
                    break;
                case MqttPropertyDataType.StringPair:
                    var pair = (KeyValuePair<string, string>)property.Value;
                    MqttWire.WriteString(scratch, pair.Key);
                    MqttWire.WriteString(scratch, pair.Value);
                    break;
                default:
                    throw MqttProtocolException.Malformed("Unsupported property type.");
            }
        }

        if (scratch.WrittenCount > MqttLimits.MaxRemainingLength)
        {
            throw MqttProtocolException.TooLarge("Property section is too large.");
        }

        MqttWire.WriteVariableByteInteger(writer, scratch.WrittenCount);
        MqttWire.WriteBytes(writer, scratch.WrittenSpan);
    }

    /// <summary>Maps a property identifier to its wire encoding.</summary>
    /// <remarks>未知属性抛出 0x81；调用方应先校验标识符。</remarks>
    /// <param name="id">Property identifier.</param>
    /// <returns>Encoding used by the codec.</returns>
    public static MqttPropertyDataType GetDataType(MqttPropertyId id) => id switch
    {
        MqttPropertyId.PayloadFormatIndicator => MqttPropertyDataType.Byte,
        MqttPropertyId.RequestProblemInformation => MqttPropertyDataType.Byte,
        MqttPropertyId.RequestResponseInformation => MqttPropertyDataType.Byte,
        MqttPropertyId.MaximumQoS => MqttPropertyDataType.Byte,
        MqttPropertyId.RetainAvailable => MqttPropertyDataType.Byte,
        MqttPropertyId.WildcardSubscriptionAvailable => MqttPropertyDataType.Byte,
        MqttPropertyId.SubscriptionIdentifierAvailable => MqttPropertyDataType.Byte,
        MqttPropertyId.SharedSubscriptionAvailable => MqttPropertyDataType.Byte,
        MqttPropertyId.ServerKeepAlive => MqttPropertyDataType.UInt16,
        MqttPropertyId.ReceiveMaximum => MqttPropertyDataType.UInt16,
        MqttPropertyId.TopicAliasMaximum => MqttPropertyDataType.UInt16,
        MqttPropertyId.TopicAlias => MqttPropertyDataType.UInt16,
        MqttPropertyId.MessageExpiryInterval => MqttPropertyDataType.UInt32,
        MqttPropertyId.SessionExpiryInterval => MqttPropertyDataType.UInt32,
        MqttPropertyId.WillDelayInterval => MqttPropertyDataType.UInt32,
        MqttPropertyId.MaximumPacketSize => MqttPropertyDataType.UInt32,
        MqttPropertyId.SubscriptionIdentifier => MqttPropertyDataType.VariableByteInteger,
        MqttPropertyId.ContentType => MqttPropertyDataType.String,
        MqttPropertyId.ResponseTopic => MqttPropertyDataType.String,
        MqttPropertyId.AssignedClientIdentifier => MqttPropertyDataType.String,
        MqttPropertyId.AuthenticationMethod => MqttPropertyDataType.String,
        MqttPropertyId.ResponseInformation => MqttPropertyDataType.String,
        MqttPropertyId.ServerReference => MqttPropertyDataType.String,
        MqttPropertyId.ReasonString => MqttPropertyDataType.String,
        MqttPropertyId.CorrelationData => MqttPropertyDataType.Binary,
        MqttPropertyId.AuthenticationData => MqttPropertyDataType.Binary,
        MqttPropertyId.UserProperty => MqttPropertyDataType.StringPair,
        _ => throw MqttProtocolException.Malformed($"Unknown MQTT property identifier {(int)id}."),
    };
}

/// <summary>Validates property usage per packet type and per value.</summary>
/// <remarks>属性出现在不允许的报文里按 0x81 拒绝；每种报文的允许集合是一张静态表。</remarks>
public static class MqttPropertyValidation
{
    /// <summary>Properties a CONNECT packet may carry.</summary>
    private static readonly HashSet<MqttPropertyId> ConnectProperties =
    [
        MqttPropertyId.SessionExpiryInterval,
        MqttPropertyId.ReceiveMaximum,
        MqttPropertyId.MaximumPacketSize,
        MqttPropertyId.TopicAliasMaximum,
        MqttPropertyId.RequestResponseInformation,
        MqttPropertyId.RequestProblemInformation,
        MqttPropertyId.UserProperty,
        MqttPropertyId.AuthenticationMethod,
        MqttPropertyId.AuthenticationData,
    ];

    /// <summary>Properties a CONNACK packet may carry.</summary>
    private static readonly HashSet<MqttPropertyId> ConnAckProperties =
    [
        MqttPropertyId.SessionExpiryInterval,
        MqttPropertyId.ReceiveMaximum,
        MqttPropertyId.MaximumQoS,
        MqttPropertyId.RetainAvailable,
        MqttPropertyId.MaximumPacketSize,
        MqttPropertyId.AssignedClientIdentifier,
        MqttPropertyId.TopicAliasMaximum,
        MqttPropertyId.ReasonString,
        MqttPropertyId.UserProperty,
        MqttPropertyId.WildcardSubscriptionAvailable,
        MqttPropertyId.SubscriptionIdentifierAvailable,
        MqttPropertyId.SharedSubscriptionAvailable,
        MqttPropertyId.ServerKeepAlive,
        MqttPropertyId.ResponseInformation,
        MqttPropertyId.ServerReference,
        MqttPropertyId.AuthenticationMethod,
        MqttPropertyId.AuthenticationData,
    ];

    /// <summary>Properties a PUBLISH packet may carry.</summary>
    private static readonly HashSet<MqttPropertyId> PublishProperties =
    [
        MqttPropertyId.PayloadFormatIndicator,
        MqttPropertyId.MessageExpiryInterval,
        MqttPropertyId.TopicAlias,
        MqttPropertyId.ResponseTopic,
        MqttPropertyId.CorrelationData,
        MqttPropertyId.UserProperty,
        MqttPropertyId.SubscriptionIdentifier,
        MqttPropertyId.ContentType,
    ];

    /// <summary>Properties the four acknowledgement packets may carry.</summary>
    private static readonly HashSet<MqttPropertyId> AckProperties =
    [
        MqttPropertyId.ReasonString,
        MqttPropertyId.UserProperty,
    ];

    /// <summary>Properties a SUBSCRIBE packet may carry.</summary>
    private static readonly HashSet<MqttPropertyId> SubscribeProperties =
    [
        MqttPropertyId.SubscriptionIdentifier,
        MqttPropertyId.UserProperty,
    ];

    /// <summary>Properties an UNSUBSCRIBE packet may carry.</summary>
    private static readonly HashSet<MqttPropertyId> UnsubscribeProperties =
    [
        MqttPropertyId.UserProperty,
    ];

    /// <summary>Properties a DISCONNECT packet may carry.</summary>
    private static readonly HashSet<MqttPropertyId> DisconnectProperties =
    [
        MqttPropertyId.SessionExpiryInterval,
        MqttPropertyId.ReasonString,
        MqttPropertyId.UserProperty,
        MqttPropertyId.ServerReference,
    ];

    /// <summary>Properties an AUTH packet may carry.</summary>
    private static readonly HashSet<MqttPropertyId> AuthProperties =
    [
        MqttPropertyId.AuthenticationMethod,
        MqttPropertyId.AuthenticationData,
        MqttPropertyId.ReasonString,
        MqttPropertyId.UserProperty,
    ];

    /// <summary>Checks a value against the range the property allows.</summary>
    internal static void ValidateValue(MqttPropertyId id, object value)
    {
        switch (id)
        {
            case MqttPropertyId.PayloadFormatIndicator:
            case MqttPropertyId.RequestProblemInformation:
            case MqttPropertyId.RequestResponseInformation:
            case MqttPropertyId.MaximumQoS:
            case MqttPropertyId.RetainAvailable:
            case MqttPropertyId.WildcardSubscriptionAvailable:
            case MqttPropertyId.SubscriptionIdentifierAvailable:
            case MqttPropertyId.SharedSubscriptionAvailable:
                if ((byte)value > 1)
                {
                    throw MqttProtocolException.Protocol($"Property {id} must be 0 or 1.");
                }

                break;
            case MqttPropertyId.ReceiveMaximum:
            case MqttPropertyId.TopicAlias:
                if ((ushort)value == 0)
                {
                    throw MqttProtocolException.Protocol($"Property {id} must not be zero.");
                }

                break;
            case MqttPropertyId.MaximumPacketSize:
                if ((uint)value == 0)
                {
                    throw MqttProtocolException.Protocol("Maximum packet size must not be zero.");
                }

                break;
            case MqttPropertyId.SubscriptionIdentifier:
                if ((int)value <= 0)
                {
                    throw MqttProtocolException.Protocol("Subscription identifier must be greater than zero.");
                }

                break;
            case MqttPropertyId.UserProperty:
                if (value is not KeyValuePair<string, string>)
                {
                    throw MqttProtocolException.Protocol("User property must be a string pair.");
                }

                break;
        }
    }

    /// <summary>Rejects properties that are illegal for the packet type.</summary>
    /// <remarks>3.1.1 报文携带属性、或属性不属于该报文，都会按 0x82 拒绝。</remarks>
    /// <param name="packetType">Packet whose property set is being checked.</param>
    /// <param name="properties">Properties decoded from the wire.</param>
    /// <param name="version">Protocol level of the connection.</param>
    public static void Validate(MqttPacketType packetType, MqttProperties properties, MqttProtocolVersion version)
    {
        if (properties.IsEmpty)
        {
            return;
        }

        if (version == MqttProtocolVersion.V311)
        {
            throw MqttProtocolException.Protocol("MQTT 3.1.1 packets must not contain MQTT 5 properties.");
        }

        var allowed = packetType switch
        {
            MqttPacketType.Connect => ConnectProperties,
            MqttPacketType.ConnAck => ConnAckProperties,
            MqttPacketType.Publish => PublishProperties,
            MqttPacketType.PubAck or MqttPacketType.PubRec or MqttPacketType.PubRel or MqttPacketType.PubComp => AckProperties,
            MqttPacketType.Subscribe => SubscribeProperties,
            MqttPacketType.SubAck => AckProperties,
            MqttPacketType.Unsubscribe => UnsubscribeProperties,
            MqttPacketType.UnsubAck => AckProperties,
            MqttPacketType.Disconnect => DisconnectProperties,
            MqttPacketType.Auth => AuthProperties,
            _ => null,
        };

        if (allowed is null)
        {
            throw MqttProtocolException.Protocol($"Packets of type {packetType} must not contain properties.");
        }

        var subscriptionIdentifiers = 0;

        foreach (var property in properties.Items)
        {
            if (!allowed.Contains(property.Id))
            {
                throw MqttProtocolException.Protocol($"Property {property.Id} is not allowed on {packetType}.");
            }

            if (property.Id == MqttPropertyId.SubscriptionIdentifier)
            {
                subscriptionIdentifiers++;
            }
        }

        if (subscriptionIdentifiers > 1 && packetType != MqttPacketType.Publish)
        {
            throw MqttProtocolException.Protocol("Subscription identifier must not be repeated on this packet.");
        }
    }
}
