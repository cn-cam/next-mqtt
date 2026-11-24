using System.Buffers;

namespace NextMqtt.Core.Protocol;

/// <summary>MQTT 5 property identifiers.</summary>
public enum MqttPropertyId
{
    PayloadFormatIndicator = 1,
    MessageExpiryInterval = 2,
    ContentType = 3,
    ResponseTopic = 8,
    CorrelationData = 9,
    SubscriptionIdentifier = 11,
    SessionExpiryInterval = 17,
    AssignedClientIdentifier = 18,
    ServerKeepAlive = 19,
    AuthenticationMethod = 21,
    AuthenticationData = 22,
    RequestProblemInformation = 23,
    WillDelayInterval = 24,
    RequestResponseInformation = 25,
    ResponseInformation = 26,
    ServerReference = 28,
    ReasonString = 31,
    ReceiveMaximum = 33,
    TopicAliasMaximum = 34,
    TopicAlias = 35,
    MaximumQoS = 36,
    RetainAvailable = 37,
    UserProperty = 38,
    MaximumPacketSize = 39,
    WildcardSubscriptionAvailable = 40,
    SubscriptionIdentifierAvailable = 41,
    SharedSubscriptionAvailable = 42,
}

public enum MqttPropertyDataType
{
    Byte,
    UInt16,
    UInt32,
    VariableByteInteger,
    String,
    Binary,
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

    public IReadOnlyList<MqttProperty> Items => _items;

    public int Count => _items.Count;

    public bool IsEmpty => _items.Count == 0;

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

    public MqttProperties AddByte(MqttPropertyId id, byte value) => Add(id, value);

    public MqttProperties AddUInt16(MqttPropertyId id, ushort value) => Add(id, value);

    public MqttProperties AddUInt32(MqttPropertyId id, uint value) => Add(id, value);

    public MqttProperties AddVariableByteInteger(MqttPropertyId id, int value) => Add(id, value);

    public MqttProperties AddString(MqttPropertyId id, string value) => Add(id, value);

    public MqttProperties AddBinary(MqttPropertyId id, ReadOnlySpan<byte> value) => Add(id, value.ToArray());

    public MqttProperties AddUserProperty(string name, string value)
        => Add(MqttPropertyId.UserProperty, new KeyValuePair<string, string>(name, value));

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
