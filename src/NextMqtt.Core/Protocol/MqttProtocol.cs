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
    /// <summary>Client request that opens a session (type 1).</summary>
    Connect = 1,

    /// <summary>Server acknowledgement of CONNECT (type 2).</summary>
    ConnAck = 2,

    /// <summary>Application message in either direction (type 3).</summary>
    Publish = 3,

    /// <summary>QoS 1 acknowledgement of a PUBLISH (type 4).</summary>
    PubAck = 4,

    /// <summary>QoS 2 first acknowledgement (type 5).</summary>
    PubRec = 5,

    /// <summary>QoS 2 release after PUBREC (type 6).</summary>
    PubRel = 6,

    /// <summary>QoS 2 completion after PUBREL (type 7).</summary>
    PubComp = 7,

    /// <summary>Subscription request (type 8).</summary>
    Subscribe = 8,

    /// <summary>Subscription acknowledgement (type 9).</summary>
    SubAck = 9,

    /// <summary>Subscription removal request (type 10).</summary>
    Unsubscribe = 10,

    /// <summary>Subscription removal acknowledgement (type 11).</summary>
    UnsubAck = 11,

    /// <summary>Keep-alive ping from the client (type 12).</summary>
    PingReq = 12,

    /// <summary>Keep-alive answer from the server (type 13).</summary>
    PingResp = 13,

    /// <summary>Either peer closing the connection (type 14).</summary>
    Disconnect = 14,

    /// <summary>MQTT 5 enhanced authentication exchange (type 15).</summary>
    Auth = 15,
}

/// <summary>Quality of service levels.</summary>
public enum MqttQoS : byte
{
    /// <summary>Fire and forget; no acknowledgement, duplicates possible.</summary>
    AtMostOnce = 0,

    /// <summary>Delivered at least once; PUBACK confirms receipt.</summary>
    AtLeastOnce = 1,

    /// <summary>Delivered exactly once; four-way handshake removes duplicates.</summary>
    ExactlyOnce = 2,
}

/// <summary>MQTT 5 retain handling options for a subscription.</summary>
public enum MqttRetainHandling : byte
{
    /// <summary>Send retained messages on every subscribe.</summary>
    SendAtSubscribe = 0,

    /// <summary>Send retained messages only when the subscription is new.</summary>
    SendAtSubscribeIfNew = 1,

    /// <summary>Never send retained messages for this subscription.</summary>
    DoNotSend = 2,
}

/// <summary>
/// MQTT 5 reason codes, plus the MQTT 3.1.1 CONNACK return codes which share
/// the same byte positions.
/// </summary>
public static class MqttReasonCodes
{
    /// <summary>0x00 — the request completed successfully.</summary>
    public const byte Success = 0x00;

    /// <summary>0x01 — SUBACK granted QoS 1 (MQTT 3.1.1 grant, not a reason code).</summary>
    public const byte GrantedQoS1 = 0x01;

    /// <summary>0x02 — SUBACK granted QoS 2 (MQTT 3.1.1 grant, not a reason code).</summary>
    public const byte GrantedQoS2 = 0x02;

    /// <summary>0x04 — DISCONNECT that still publishes the will message.</summary>
    public const byte DisconnectWithWill = 0x04;

    /// <summary>0x10 — PUBACK/PUBREC accepted but nobody is subscribed.</summary>
    public const byte NoMatchingSubscribers = 0x10;

    /// <summary>0x11 — UNSUBACK for a filter that was not subscribed.</summary>
    public const byte NoSubscriptionExisted = 0x11;

    /// <summary>0x18 — AUTH continues the authentication exchange.</summary>
    public const byte ContinueAuthentication = 0x18;

    /// <summary>0x19 — AUTH re-authenticates an established connection.</summary>
    public const byte ReAuthenticate = 0x19;

    /// <summary>0x80 — failure without a more specific reason.</summary>
    public const byte UnspecifiedError = 0x80;

    /// <summary>0x81 — the packet does not follow the wire format.</summary>
    public const byte MalformedPacket = 0x81;

    /// <summary>0x82 — a state-machine or reserved-bit violation.</summary>
    public const byte ProtocolError = 0x82;

    /// <summary>0x83 — rejected for reasons only the implementation knows.</summary>
    public const byte ImplementationSpecificError = 0x83;

    /// <summary>0x84 — CONNECT used a protocol level this broker does not serve.</summary>
    public const byte UnsupportedProtocolVersion = 0x84;

    /// <summary>0x85 — empty or over-long client identifier.</summary>
    public const byte ClientIdentifierNotValid = 0x85;

    /// <summary>0x86 — username or password rejected.</summary>
    public const byte BadUserNameOrPassword = 0x86;

    /// <summary>0x87 — the client is not allowed to perform the action.</summary>
    public const byte NotAuthorized = 0x87;

    /// <summary>0x88 — the broker is not accepting connections right now.</summary>
    public const byte ServerUnavailable = 0x88;

    /// <summary>0x89 — the broker is overloaded.</summary>
    public const byte ServerBusy = 0x89;

    /// <summary>0x8A — the client identifier or address is banned.</summary>
    public const byte Banned = 0x8A;

    /// <summary>0x8B — the broker is shutting down and will not return.</summary>
    public const byte ServerShuttingDown = 0x8B;

    /// <summary>0x8C — the requested AUTH method is not supported.</summary>
    public const byte BadAuthenticationMethod = 0x8C;

    /// <summary>0x8D — keep-alive expired; DISCONNECT carries this code.</summary>
    public const byte KeepAliveTimeout = 0x8D;

    /// <summary>0x8E — another connection took over this session.</summary>
    public const byte SessionTakenOver = 0x8E;

    /// <summary>0x8F — the subscription filter is syntactically invalid.</summary>
    public const byte TopicFilterInvalid = 0x8F;

    /// <summary>0x90 — the publication topic name is invalid.</summary>
    public const byte TopicNameInvalid = 0x90;

    /// <summary>0x91 — the packet identifier is already in flight.</summary>
    public const byte PacketIdentifierInUse = 0x91;

    /// <summary>0x92 — the packet identifier is unknown to the receiver.</summary>
    public const byte PacketIdentifierNotFound = 0x92;

    /// <summary>0x93 — the sender exceeded the receiver's Receive Maximum.</summary>
    public const byte ReceiveMaximumExceeded = 0x93;

    /// <summary>0x94 — the topic alias is zero or above the negotiated maximum.</summary>
    public const byte TopicAliasInvalid = 0x94;

    /// <summary>0x95 — the packet exceeds Maximum Packet Size.</summary>
    public const byte PacketTooLarge = 0x95;

    /// <summary>0x96 — the client published faster than its quota.</summary>
    public const byte MessageRateTooHigh = 0x96;

    /// <summary>0x97 — an administrative quota was exhausted.</summary>
    public const byte QuotaExceeded = 0x97;

    /// <summary>0x98 — an operator closed or blocked the connection.</summary>
    public const byte AdministrativeAction = 0x98;

    /// <summary>0x99 — the payload does not match the declared format indicator.</summary>
    public const byte PayloadFormatInvalid = 0x99;

    /// <summary>0x9A — retained messages are disabled on this broker.</summary>
    public const byte RetainNotSupported = 0x9A;

    /// <summary>0x9B — the requested QoS is higher than the broker allows.</summary>
    public const byte QoSNotSupported = 0x9B;

    /// <summary>0x9C — the client should reconnect to a different broker.</summary>
    public const byte UseAnotherServer = 0x9C;

    /// <summary>0x9D — the broker moved permanently; reconnect elsewhere.</summary>
    public const byte ServerMoved = 0x9D;

    /// <summary>0x9E — shared subscriptions are disabled.</summary>
    public const byte SharedSubscriptionsNotSupported = 0x9E;

    /// <summary>0x9F — too many CONNECTs from the same address.</summary>
    public const byte ConnectionRateExceeded = 0x9F;

    /// <summary>0xA0 — the maximum connection time for this session was reached.</summary>
    public const byte MaximumConnectTime = 0xA0;

    /// <summary>0xA1 — subscription identifiers are disabled.</summary>
    public const byte SubscriptionIdentifiersNotSupported = 0xA1;

    /// <summary>0xA2 — wildcard subscriptions are disabled.</summary>
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
    /// <summary>Creates the exception with the DISCONNECT reason code to report.</summary>
    /// <param name="reasonCode">MQTT 5 reason code, sent to the peer when possible.</param>
    /// <param name="message">Operator-facing explanation.</param>
    public MqttProtocolException(byte reasonCode, string message)
        : base(message)
    {
        ReasonCode = reasonCode;
    }

    /// <summary>Reason code that should accompany the DISCONNECT.</summary>
    public byte ReasonCode { get; }

    /// <summary>Creates an exception for a packet that does not parse.</summary>
    /// <param name="message">What the decoder rejected.</param>
    /// <returns>Exception carrying 0x81.</returns>
    public static MqttProtocolException Malformed(string message)
        => new(MqttReasonCodes.MalformedPacket, message);

    /// <summary>Creates an exception for a state-machine violation.</summary>
    /// <param name="message">What the peer did out of order.</param>
    /// <returns>Exception carrying 0x82.</returns>
    public static MqttProtocolException Protocol(string message)
        => new(MqttReasonCodes.ProtocolError, message);

    /// <summary>Creates an exception for a packet above the size limit.</summary>
    /// <param name="message">Which limit was exceeded.</param>
    /// <returns>Exception carrying 0x95.</returns>
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
    /// <summary>Packs the options into the single subscription-options byte.</summary>
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
