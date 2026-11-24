namespace NextMqtt.Core.Protocol;

/// <summary>Base type for every MQTT control packet.</summary>
public abstract class MqttPacket
{
    public abstract MqttPacketType Type { get; }
}

/// <summary>CONNECT packet.</summary>
public sealed class ConnectPacket : MqttPacket
{
    public override MqttPacketType Type => MqttPacketType.Connect;

    public MqttProtocolVersion ProtocolVersion { get; init; } = MqttProtocolVersion.V500;

    public bool CleanStart { get; init; }

    public ushort KeepAliveSeconds { get; init; }

    public string ClientId { get; init; } = string.Empty;

    public MqttProperties Properties { get; init; } = new();

    public MqttWill? Will { get; init; }

    public string? Username { get; init; }

    public byte[]? Password { get; init; }
}

/// <summary>Will message carried by CONNECT.</summary>
public sealed class MqttWill
{
    public required string Topic { get; init; }

    public required byte[] Payload { get; init; }

    public MqttQoS QoS { get; init; }

    public bool Retain { get; init; }

    public MqttProperties Properties { get; init; } = new();
}

/// <summary>CONNACK packet.</summary>
public sealed class ConnAckPacket : MqttPacket
{
    public override MqttPacketType Type => MqttPacketType.ConnAck;

    public bool SessionPresent { get; init; }

    public byte ReasonCode { get; init; } = MqttReasonCodes.Success;

    public MqttProperties Properties { get; init; } = new();
}

/// <summary>PUBLISH packet.</summary>
public sealed class PublishPacket : MqttPacket
{
    public override MqttPacketType Type => MqttPacketType.Publish;

    public string Topic { get; init; } = string.Empty;

    public byte[] Payload { get; init; } = [];

    public MqttQoS QoS { get; init; }

    public bool Retain { get; init; }

    public bool Duplicate { get; init; }

    public ushort PacketId { get; init; }

    public MqttProperties Properties { get; init; } = new();

    public long PayloadLength => Payload.Length;
}

/// <summary>Base for PUBACK, PUBREC, PUBREL and PUBCOMP.</summary>
public abstract class MqttAcknowledgementPacket : MqttPacket
{
    public ushort PacketId { get; init; }

    public byte ReasonCode { get; init; } = MqttReasonCodes.Success;

    public MqttProperties Properties { get; init; } = new();
}

public sealed class PubAckPacket : MqttAcknowledgementPacket
{
    public override MqttPacketType Type => MqttPacketType.PubAck;
}

public sealed class PubRecPacket : MqttAcknowledgementPacket
{
    public override MqttPacketType Type => MqttPacketType.PubRec;
}

public sealed class PubRelPacket : MqttAcknowledgementPacket
{
    public override MqttPacketType Type => MqttPacketType.PubRel;
}

public sealed class PubCompPacket : MqttAcknowledgementPacket
{
    public override MqttPacketType Type => MqttPacketType.PubComp;
}

/// <summary>A single topic filter and its options inside SUBSCRIBE.</summary>
public readonly record struct MqttSubscriptionRequest(string Filter, MqttSubscriptionOptions Options);

/// <summary>SUBSCRIBE packet.</summary>
public sealed class SubscribePacket : MqttPacket
{
    public override MqttPacketType Type => MqttPacketType.Subscribe;

    public ushort PacketId { get; init; }

    public MqttProperties Properties { get; init; } = new();

    public List<MqttSubscriptionRequest> Subscriptions { get; init; } = [];
}

/// <summary>SUBACK packet.</summary>
public sealed class SubAckPacket : MqttPacket
{
    public override MqttPacketType Type => MqttPacketType.SubAck;

    public ushort PacketId { get; init; }

    public MqttProperties Properties { get; init; } = new();

    public List<byte> ReasonCodes { get; init; } = [];
}

/// <summary>UNSUBSCRIBE packet.</summary>
public sealed class UnsubscribePacket : MqttPacket
{
    public override MqttPacketType Type => MqttPacketType.Unsubscribe;

    public ushort PacketId { get; init; }

    public MqttProperties Properties { get; init; } = new();

    public List<string> Filters { get; init; } = [];
}

/// <summary>UNSUBACK packet.</summary>
public sealed class UnsubAckPacket : MqttPacket
{
    public override MqttPacketType Type => MqttPacketType.UnsubAck;

    public ushort PacketId { get; init; }

    public MqttProperties Properties { get; init; } = new();

    public List<byte> ReasonCodes { get; init; } = [];
}

/// <summary>PINGREQ packet.</summary>
public sealed class PingReqPacket : MqttPacket
{
    public static PingReqPacket Instance { get; } = new();

    public override MqttPacketType Type => MqttPacketType.PingReq;
}

/// <summary>PINGRESP packet.</summary>
public sealed class PingRespPacket : MqttPacket
{
    public static PingRespPacket Instance { get; } = new();

    public override MqttPacketType Type => MqttPacketType.PingResp;
}

/// <summary>DISCONNECT packet.</summary>
public sealed class DisconnectPacket : MqttPacket
{
    public override MqttPacketType Type => MqttPacketType.Disconnect;

    public byte ReasonCode { get; init; } = MqttReasonCodes.Success;

    public MqttProperties Properties { get; init; } = new();
}

/// <summary>AUTH packet (MQTT 5 enhanced authentication).</summary>
public sealed class AuthPacket : MqttPacket
{
    public override MqttPacketType Type => MqttPacketType.Auth;

    public byte ReasonCode { get; init; } = MqttReasonCodes.ContinueAuthentication;

    public MqttProperties Properties { get; init; } = new();
}
