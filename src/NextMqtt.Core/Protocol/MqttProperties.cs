using System.Buffers;

namespace NextMqtt.Core.Protocol;

/// <summary>MQTT 5 property identifiers.</summary>
public enum MqttPropertyId
{
    /// <summary>0x01 — payload is UTF-8 text (0) or unspecified binary (1).</summary>
    PayloadFormatIndicator = 1,

    /// <summary>0x02 — seconds until the message expires.</summary>
    MessageExpiryInterval = 2,

    /// <summary>0x03 — MIME type of the payload.</summary>
    ContentType = 3,

    /// <summary>0x08 — topic the receiver should reply to.</summary>
    ResponseTopic = 8,

    /// <summary>0x09 — opaque data correlating a reply with its request.</summary>
    CorrelationData = 9,

    /// <summary>0x0B — identifier echoed back with matching deliveries.</summary>
    SubscriptionIdentifier = 11,

    /// <summary>0x11 — how long the session outlives the connection.</summary>
    SessionExpiryInterval = 17,

    /// <summary>0x12 — client identifier chosen by the server.</summary>
    AssignedClientIdentifier = 18,

    /// <summary>0x13 — keep-alive the server wants the client to use.</summary>
    ServerKeepAlive = 19,

    /// <summary>0x15 — name of the enhanced authentication method.</summary>
    AuthenticationMethod = 21,

    /// <summary>0x16 — method-specific authentication bytes.</summary>
    AuthenticationData = 22,

    /// <summary>0x17 — whether the client wants reason strings and user properties.</summary>
    RequestProblemInformation = 23,

    /// <summary>0x18 — seconds to wait before publishing the will.</summary>
    WillDelayInterval = 24,

    /// <summary>0x19 — whether the client wants response information.</summary>
    RequestResponseInformation = 25,

    /// <summary>0x1A — response information from the server.</summary>
    ResponseInformation = 26,

    /// <summary>0x1C — another server the client may use.</summary>
    ServerReference = 28,

    /// <summary>0x1F — human-readable reason for a failure.</summary>
    ReasonString = 31,

    /// <summary>0x21 — receive maximum (inflight window) the sender allows.</summary>
    ReceiveMaximum = 33,

    /// <summary>0x22 — largest topic alias the sender accepts.</summary>
    TopicAliasMaximum = 34,

    /// <summary>0x23 — numeric alias replacing the topic name on later publishes.</summary>
    TopicAlias = 35,

    /// <summary>0x24 — highest QoS the server supports.</summary>
    MaximumQoS = 36,

    /// <summary>0x25 — whether retained messages are available.</summary>
    RetainAvailable = 37,

    /// <summary>0x26 — application-defined key/value pair; may repeat.</summary>
    UserProperty = 38,

    /// <summary>0x27 — largest packet the sender will accept.</summary>
    MaximumPacketSize = 39,

    /// <summary>0x28 — whether wildcard subscriptions are available.</summary>
    WildcardSubscriptionAvailable = 40,

    /// <summary>0x29 — whether subscription identifiers are available.</summary>
    SubscriptionIdentifierAvailable = 41,

    /// <summary>0x2A — whether shared subscriptions are available.</summary>
    SharedSubscriptionAvailable = 42,
}

/// <summary>Wire encoding of an MQTT 5 property value.</summary>
public enum MqttPropertyDataType
{
    /// <summary>Single byte.</summary>
    Byte,

    /// <summary>Two-byte unsigned integer, big endian.</summary>
    UInt16,

    /// <summary>Four-byte unsigned integer, big endian.</summary>
    UInt32,

    /// <summary>1-4 byte variable byte integer.</summary>
    VariableByteInteger,

    /// <summary>Length-prefixed UTF-8 string.</summary>
    String,

    /// <summary>Length-prefixed binary data.</summary>
    Binary,

    /// <summary>Two length-prefixed strings (user property).</summary>
    StringPair,
}

/// <summary>A single decoded MQTT 5 property.</summary>
public readonly record struct MqttProperty(MqttPropertyId Id, object Value);

/// <summary>
/// Ordered MQTT 5 property bag. User properties may repeat; all other
/// properties are unique.
/// </summary>
public sealed class MqttProperties
{
    private readonly List<MqttProperty> _items = [];

    /// <summary>Properties in insertion order; user properties may repeat.</summary>
    public IReadOnlyList<MqttProperty> Items => _items;

    /// <summary>Number of individual property entries.</summary>
    public int Count => _items.Count;

    /// <summary>True when the bag holds no properties.</summary>
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
    /// <param name="id">Property identifier.</param>
    /// <param name="value">Value in the range 0-255.</param>
    /// <returns>The same bag, for chaining.</returns>
    public MqttProperties AddByte(MqttPropertyId id, byte value) => Add(id, value);

    /// <summary>Adds a two-byte unsigned property.</summary>
    /// <param name="id">Property identifier.</param>
    /// <param name="value">Value in the range 0-65535.</param>
    /// <returns>The same bag, for chaining.</returns>
    public MqttProperties AddUInt16(MqttPropertyId id, ushort value) => Add(id, value);

    /// <summary>Adds a four-byte unsigned property.</summary>
    /// <param name="id">Property identifier.</param>
    /// <param name="value">Value in the range 0-4294967295.</param>
    /// <returns>The same bag, for chaining.</returns>
    public MqttProperties AddUInt32(MqttPropertyId id, uint value) => Add(id, value);

    /// <summary>Adds a variable byte integer property.</summary>
    /// <param name="id">Property identifier.</param>
    /// <param name="value">Value in the range 0-268435455.</param>
    /// <returns>The same bag, for chaining.</returns>
    public MqttProperties AddVariableByteInteger(MqttPropertyId id, int value) => Add(id, value);

    /// <summary>Adds a UTF-8 string property.</summary>
    /// <param name="id">Property identifier.</param>
    /// <param name="value">String value; must fit the 65535-byte field limit.</param>
    /// <returns>The same bag, for chaining.</returns>
    public MqttProperties AddString(MqttPropertyId id, string value) => Add(id, value);

    /// <summary>Adds a binary property, copying the span.</summary>
    /// <param name="id">Property identifier.</param>
    /// <param name="value">Bytes; must fit the 65535-byte field limit.</param>
    /// <returns>The same bag, for chaining.</returns>
    public MqttProperties AddBinary(MqttPropertyId id, ReadOnlySpan<byte> value) => Add(id, value.ToArray());

    /// <summary>Adds one application-defined user property.</summary>
    /// <param name="name">Key; may repeat with different values.</param>
    /// <param name="value">Value paired with the key.</param>
    /// <returns>The same bag, for chaining.</returns>
    public MqttProperties AddUserProperty(string name, string value)
        => Add(MqttPropertyId.UserProperty, new KeyValuePair<string, string>(name, value));

    /// <summary>Reads a byte property, or null when it is absent.</summary>
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
    /// <remarks>返回内部数组，不要修改；需要写入时请复制。</remarks>
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
public static class MqttPropertyValidation
{
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

    private static readonly HashSet<MqttPropertyId> AckProperties =
    [
        MqttPropertyId.ReasonString,
        MqttPropertyId.UserProperty,
    ];

    private static readonly HashSet<MqttPropertyId> SubscribeProperties =
    [
        MqttPropertyId.SubscriptionIdentifier,
        MqttPropertyId.UserProperty,
    ];

    private static readonly HashSet<MqttPropertyId> UnsubscribeProperties =
    [
        MqttPropertyId.UserProperty,
    ];

    private static readonly HashSet<MqttPropertyId> DisconnectProperties =
    [
        MqttPropertyId.SessionExpiryInterval,
        MqttPropertyId.ReasonString,
        MqttPropertyId.UserProperty,
        MqttPropertyId.ServerReference,
    ];

    private static readonly HashSet<MqttPropertyId> AuthProperties =
    [
        MqttPropertyId.AuthenticationMethod,
        MqttPropertyId.AuthenticationData,
        MqttPropertyId.ReasonString,
        MqttPropertyId.UserProperty,
    ];

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
    /// <remarks>MQTT 3.1.1 报文携带属性、或属性不属于该报文时抛出 0x82。</remarks>
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
