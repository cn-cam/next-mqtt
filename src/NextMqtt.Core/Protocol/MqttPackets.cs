namespace NextMqtt.Core.Protocol;

/// <summary>Base type for every MQTT control packet.</summary>
public abstract class MqttPacket
{
    /// <summary>Control packet type taken from the fixed header.</summary>
    public abstract MqttPacketType Type { get; }
}

/// <summary>CONNECT packet.</summary>
public sealed class ConnectPacket : MqttPacket
{
    /// <summary>Always <see cref="MqttPacketType.Connect"/>.</summary>
    public override MqttPacketType Type => MqttPacketType.Connect;

    /// <summary>Protocol level requested by the client (4 or 5).</summary>
    public MqttProtocolVersion ProtocolVersion { get; init; } = MqttProtocolVersion.V500;

    /// <summary>True when the client asks for a fresh session.</summary>
    public bool CleanStart { get; init; }

    /// <summary>Keep-alive interval in seconds; 0 disables the timer.</summary>
    public ushort KeepAliveSeconds { get; init; }

    /// <summary>Client identifier; may be empty when the broker assigns one.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>MQTT 5 CONNECT properties (session expiry, limits, authentication).</summary>
    public MqttProperties Properties { get; init; } = new();

    /// <summary>Will message published when the connection ends abnormally.</summary>
    public MqttWill? Will { get; init; }

    /// <summary>Username, when the client supplies credentials.</summary>
    public string? Username { get; init; }

    /// <summary>Password bytes; the codec never logs or decodes them.</summary>
    public byte[]? Password { get; init; }
}

/// <summary>Will message carried by CONNECT.</summary>
public sealed class MqttWill
{
    /// <summary>Topic the will is published to.</summary>
    public required string Topic { get; init; }

    /// <summary>Will payload bytes.</summary>
    public required byte[] Payload { get; init; }

    /// <summary>QoS used when publishing the will.</summary>
    public MqttQoS QoS { get; init; }

    /// <summary>Whether the will is stored as a retained message.</summary>
    public bool Retain { get; init; }

    /// <summary>MQTT 5 will properties (delay, format, correlation).</summary>
    public MqttProperties Properties { get; init; } = new();
}

/// <summary>CONNACK packet.</summary>
public sealed class ConnAckPacket : MqttPacket
{
    /// <summary>Always <see cref="MqttPacketType.ConnAck"/>.</summary>
    public override MqttPacketType Type => MqttPacketType.ConnAck;

    /// <summary>True when the server resumed an existing session.</summary>
    public bool SessionPresent { get; init; }

    /// <summary>Success (0x00) or a rejection reason code.</summary>
    public byte ReasonCode { get; init; } = MqttReasonCodes.Success;

    /// <summary>MQTT 5 CONNACK properties (limits, assigned id, server reference).</summary>
    public MqttProperties Properties { get; init; } = new();
}

/// <summary>PUBLISH packet.</summary>
public sealed class PublishPacket : MqttPacket
{
    /// <summary>Always <see cref="MqttPacketType.Publish"/>.</summary>
    public override MqttPacketType Type => MqttPacketType.Publish;

    /// <summary>Topic name or alias target carried by the packet.</summary>
    public string Topic { get; init; } = string.Empty;

    /// <summary>Application payload; empty is legal.</summary>
    public byte[] Payload { get; init; } = [];

    /// <summary>Delivery guarantee requested by the sender.</summary>
    public MqttQoS QoS { get; init; }

    /// <summary>Whether the broker should retain the message.</summary>
    public bool Retain { get; init; }

    /// <summary>True for a retransmission of an unacknowledged packet.</summary>
    public bool Duplicate { get; init; }

    /// <summary>Packet identifier; zero for QoS 0.</summary>
    public ushort PacketId { get; init; }

    /// <summary>MQTT 5 publish properties (format, expiry, response topic, user data).</summary>
    public MqttProperties Properties { get; init; } = new();

    /// <summary>Payload size in bytes, without re-counting the array.</summary>
    public long PayloadLength => Payload.Length;
}

/// <summary>Base for PUBACK, PUBREC, PUBREL and PUBCOMP.</summary>
public abstract class MqttAcknowledgementPacket : MqttPacket
{
    /// <summary>Packet identifier being acknowledged.</summary>
    public ushort PacketId { get; init; }

    /// <summary>Success or a reason code describing the rejection.</summary>
    public byte ReasonCode { get; init; } = MqttReasonCodes.Success;

    /// <summary>MQTT 5 acknowledgement properties (reason string, user data).</summary>
    public MqttProperties Properties { get; init; } = new();
}

/// <summary>PUBACK — QoS 1 delivery acknowledged.</summary>
public sealed class PubAckPacket : MqttAcknowledgementPacket
{
    /// <summary>Always <see cref="MqttPacketType.PubAck"/>.</summary>
    public override MqttPacketType Type => MqttPacketType.PubAck;
}

/// <summary>PUBREC — QoS 2 publish received, awaiting PUBREL.</summary>
public sealed class PubRecPacket : MqttAcknowledgementPacket
{
    /// <summary>Always <see cref="MqttPacketType.PubRec"/>.</summary>
    public override MqttPacketType Type => MqttPacketType.PubRec;
}

/// <summary>PUBREL — QoS 2 release of a stored publish.</summary>
public sealed class PubRelPacket : MqttAcknowledgementPacket
{
    /// <summary>Always <see cref="MqttPacketType.PubRel"/>.</summary>
    public override MqttPacketType Type => MqttPacketType.PubRel;
}

/// <summary>PUBCOMP — QoS 2 handshake finished.</summary>
public sealed class PubCompPacket : MqttAcknowledgementPacket
{
    /// <summary>Always <see cref="MqttPacketType.PubComp"/>.</summary>
    public override MqttPacketType Type => MqttPacketType.PubComp;
}

/// <summary>A single topic filter and its options inside SUBSCRIBE.</summary>
/// <param name="Filter">Filter text, possibly a shared-subscription filter.</param>
/// <param name="Options">QoS, noLocal, retain-as-published and retain handling.</param>
public readonly record struct MqttSubscriptionRequest(string Filter, MqttSubscriptionOptions Options);

/// <summary>SUBSCRIBE packet.</summary>
public sealed class SubscribePacket : MqttPacket
{
    /// <summary>Always <see cref="MqttPacketType.Subscribe"/>.</summary>
    public override MqttPacketType Type => MqttPacketType.Subscribe;

    /// <summary>Packet identifier used by the matching SUBACK.</summary>
    public ushort PacketId { get; init; }

    /// <summary>MQTT 5 SUBSCRIBE properties (subscription identifier).</summary>
    public MqttProperties Properties { get; init; } = new();

    /// <summary>Filters in request order; SUBACK reason codes match by index.</summary>
    public List<MqttSubscriptionRequest> Subscriptions { get; init; } = [];
}

/// <summary>SUBACK packet.</summary>
public sealed class SubAckPacket : MqttPacket
{
    /// <summary>Always <see cref="MqttPacketType.SubAck"/>.</summary>
    public override MqttPacketType Type => MqttPacketType.SubAck;

    /// <summary>Identifier copied from the SUBSCRIBE being answered.</summary>
    public ushort PacketId { get; init; }

    /// <summary>MQTT 5 SUBACK properties (reason string, user data).</summary>
    public MqttProperties Properties { get; init; } = new();

    /// <summary>Granted QoS or failure code per requested filter.</summary>
    public List<byte> ReasonCodes { get; init; } = [];
}

/// <summary>UNSUBSCRIBE packet.</summary>
public sealed class UnsubscribePacket : MqttPacket
{
    /// <summary>Always <see cref="MqttPacketType.Unsubscribe"/>.</summary>
    public override MqttPacketType Type => MqttPacketType.Unsubscribe;

    /// <summary>Packet identifier used by the matching UNSUBACK.</summary>
    public ushort PacketId { get; init; }

    /// <summary>MQTT 5 UNSUBSCRIBE properties (user data).</summary>
    public MqttProperties Properties { get; init; } = new();

    /// <summary>Filters to remove, as originally supplied by the client.</summary>
    public List<string> Filters { get; init; } = [];
}

/// <summary>UNSUBACK packet.</summary>
public sealed class UnsubAckPacket : MqttPacket
{
    /// <summary>Always <see cref="MqttPacketType.UnsubAck"/>.</summary>
    public override MqttPacketType Type => MqttPacketType.UnsubAck;

    /// <summary>Identifier copied from the UNSUBSCRIBE being answered.</summary>
    public ushort PacketId { get; init; }

    /// <summary>MQTT 5 UNSUBACK properties.</summary>
    public MqttProperties Properties { get; init; } = new();

    /// <summary>Result per requested filter (0x00 or 0x11).</summary>
    public List<byte> ReasonCodes { get; init; } = [];
}

/// <summary>PINGREQ packet.</summary>
public sealed class PingReqPacket : MqttPacket
{
    /// <summary>Shared instance; the packet carries no data.</summary>
    public static PingReqPacket Instance { get; } = new();

    /// <summary>Always <see cref="MqttPacketType.PingReq"/>.</summary>
    public override MqttPacketType Type => MqttPacketType.PingReq;
}

/// <summary>PINGRESP packet.</summary>
public sealed class PingRespPacket : MqttPacket
{
    /// <summary>Shared instance; the packet carries no data.</summary>
    public static PingRespPacket Instance { get; } = new();

    /// <summary>Always <see cref="MqttPacketType.PingResp"/>.</summary>
    public override MqttPacketType Type => MqttPacketType.PingResp;
}

/// <summary>DISCONNECT packet.</summary>
public sealed class DisconnectPacket : MqttPacket
{
    /// <summary>Always <see cref="MqttPacketType.Disconnect"/>.</summary>
    public override MqttPacketType Type => MqttPacketType.Disconnect;

    /// <summary>Success, a protocol error or a code explaining the close.</summary>
    public byte ReasonCode { get; init; } = MqttReasonCodes.Success;

    /// <summary>MQTT 5 DISCONNECT properties (session expiry update, reason string).</summary>
    public MqttProperties Properties { get; init; } = new();
}

/// <summary>AUTH packet (MQTT 5 enhanced authentication).</summary>
public sealed class AuthPacket : MqttPacket
{
    /// <summary>Always <see cref="MqttPacketType.Auth"/>.</summary>
    public override MqttPacketType Type => MqttPacketType.Auth;

    /// <summary>Continue (0x18), re-authenticate (0x19) or success (0x00).</summary>
    public byte ReasonCode { get; init; } = MqttReasonCodes.ContinueAuthentication;

    /// <summary>Authentication method and data properties.</summary>
    public MqttProperties Properties { get; init; } = new();
}
