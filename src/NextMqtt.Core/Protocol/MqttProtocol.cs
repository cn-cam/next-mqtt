namespace NextMqtt.Core.Protocol;

/// <summary>MQTT protocol levels supported by the broker.</summary>
public enum MqttProtocolVersion : byte
{
    /// <summary>MQTT 3.1.1 (protocol level 4).</summary>
    V311 = 4,

    /// <summary>MQTT 5.0 (protocol level 5).</summary>
    V500 = 5,
}

/// <summary>MQTT control packet types (fixed header bits 7-4).</summary>
public enum MqttPacketType : byte
{
    Connect = 1,
    ConnAck = 2,
    Publish = 3,
    PubAck = 4,
    PubRec = 5,
    PubRel = 6,
    PubComp = 7,
    Subscribe = 8,
    SubAck = 9,
    Unsubscribe = 10,
    UnsubAck = 11,
    PingReq = 12,
    PingResp = 13,
    Disconnect = 14,
    Auth = 15,
}

/// <summary>Quality of service levels.</summary>
public enum MqttQoS : byte
{
    AtMostOnce = 0,
    AtLeastOnce = 1,
    ExactlyOnce = 2,
}

/// <summary>MQTT 5 retain handling options for a subscription.</summary>
public enum MqttRetainHandling : byte
{
    SendAtSubscribe = 0,
    SendAtSubscribeIfNew = 1,
    DoNotSend = 2,
}

/// <summary>
/// MQTT 5 reason codes, plus the MQTT 3.1.1 CONNACK return codes which share
/// the same byte positions.
/// </summary>
public static class MqttReasonCodes
{
    public const byte Success = 0x00;
    public const byte GrantedQoS1 = 0x01;
    public const byte GrantedQoS2 = 0x02;
    public const byte DisconnectWithWill = 0x04;
    public const byte NoMatchingSubscribers = 0x10;
    public const byte NoSubscriptionExisted = 0x11;
    public const byte ContinueAuthentication = 0x18;
    public const byte ReAuthenticate = 0x19;
    public const byte UnspecifiedError = 0x80;
    public const byte MalformedPacket = 0x81;
    public const byte ProtocolError = 0x82;
    public const byte ImplementationSpecificError = 0x83;
    public const byte UnsupportedProtocolVersion = 0x84;
    public const byte ClientIdentifierNotValid = 0x85;
    public const byte BadUserNameOrPassword = 0x86;
    public const byte NotAuthorized = 0x87;
    public const byte ServerUnavailable = 0x88;
    public const byte ServerBusy = 0x89;
    public const byte Banned = 0x8A;
    public const byte ServerShuttingDown = 0x8B;
    public const byte BadAuthenticationMethod = 0x8C;
    public const byte KeepAliveTimeout = 0x8D;
    public const byte SessionTakenOver = 0x8E;
    public const byte TopicFilterInvalid = 0x8F;
    public const byte TopicNameInvalid = 0x90;
    public const byte PacketIdentifierInUse = 0x91;
    public const byte PacketIdentifierNotFound = 0x92;
    public const byte ReceiveMaximumExceeded = 0x93;
    public const byte TopicAliasInvalid = 0x94;
    public const byte PacketTooLarge = 0x95;
    public const byte MessageRateTooHigh = 0x96;
    public const byte QuotaExceeded = 0x97;
    public const byte AdministrativeAction = 0x98;
    public const byte PayloadFormatInvalid = 0x99;
    public const byte RetainNotSupported = 0x9A;
    public const byte QoSNotSupported = 0x9B;
    public const byte UseAnotherServer = 0x9C;
    public const byte ServerMoved = 0x9D;
    public const byte SharedSubscriptionsNotSupported = 0x9E;
    public const byte ConnectionRateExceeded = 0x9F;
    public const byte MaximumConnectTime = 0xA0;
    public const byte SubscriptionIdentifiersNotSupported = 0xA1;
    public const byte WildcardSubscriptionsNotSupported = 0xA2;
}

/// <summary>Implementation limits from the MQTT 5 specification.</summary>
public static class MqttLimits
{
    /// <summary>Maximum encodable remaining length (268,435,455 bytes).</summary>
    public const int MaxRemainingLength = 268_435_455;

    /// <summary>Default maximum packet size accepted by a listener (1 MiB).</summary>
    public const int DefaultMaxPacketSize = 1024 * 1024;

    /// <summary>Maximum size of a UTF-8 encoded string or binary field.</summary>
    public const int MaxFieldLength = 65_535;
}

/// <summary>Raised when a peer violates the protocol or a configured limit.</summary>
public sealed class MqttProtocolException : Exception
{
    public MqttProtocolException(byte reasonCode, string message)
        : base(message)
    {
        ReasonCode = reasonCode;
    }

    public byte ReasonCode { get; }

    public static MqttProtocolException Malformed(string message)
        => new(MqttReasonCodes.MalformedPacket, message);

    public static MqttProtocolException Protocol(string message)
        => new(MqttReasonCodes.ProtocolError, message);

    public static MqttProtocolException TooLarge(string message)
        => new(MqttReasonCodes.PacketTooLarge, message);
}

/// <summary>Options attached to a subscription by SUBSCRIBE.</summary>
public readonly record struct MqttSubscriptionOptions(
    MqttQoS QoS,
    bool NoLocal = false,
    bool RetainAsPublished = false,
    MqttRetainHandling RetainHandling = MqttRetainHandling.SendAtSubscribe)
{
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
