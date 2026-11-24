using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Sessions;

/// <summary>Abstraction over one live client connection.</summary>
public interface IClientConnection
{
    string RemoteAddress { get; }

    MqttProtocolVersion ProtocolVersion { get; }

    bool TrySend(MqttPacket packet);

    Task DisconnectAsync(byte reasonCode, string reason, CancellationToken cancellationToken = default);
}

/// <summary>A message ready for delivery to one session.</summary>
public sealed class OutboundMessage
{
    public required string Topic { get; init; }

    public required byte[] Payload { get; init; }

    public MqttQoS QoS { get; init; }

    public bool Retain { get; init; }

    public MqttProperties Properties { get; init; } = new();

    public DateTimeOffset? ExpiresAt { get; init; }
}
