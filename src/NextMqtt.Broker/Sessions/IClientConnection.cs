using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Sessions;

/// <summary>Abstraction over one live client connection.</summary>
/// <remarks>连接的具体传输（TCP、TLS、WebSocket）由 Server 层提供；Broker 只依赖此接口。</remarks>
public interface IClientConnection
{
    /// <summary>Peer address as reported by the transport, port included.</summary>
    string RemoteAddress { get; }

    /// <summary>Protocol level negotiated by CONNECT.</summary>
    MqttProtocolVersion ProtocolVersion { get; }

    /// <summary>Queues a packet for writing without blocking the caller.</summary>
    /// <param name="packet">Packet to enqueue.</param>
    /// <returns><c>false</c> when the connection is closing.</returns>
    bool TrySend(MqttPacket packet);

    /// <summary>Sends a DISCONNECT (MQTT 5) and closes the transport.</summary>
    /// <param name="reasonCode">Reason code reported to the client.</param>
    /// <param name="reason">Operator-facing explanation, logged locally.</param>
    /// <param name="cancellationToken">Cancels the close.</param>
    Task DisconnectAsync(byte reasonCode, string reason, CancellationToken cancellationToken = default);
}

/// <summary>A message ready for delivery to one session.</summary>
/// <remarks>由路由层构造，交给会话决定立即发送还是入队。</remarks>
public sealed class OutboundMessage
{
    /// <summary>Topic name delivered to the subscriber.</summary>
    public required string Topic { get; init; }

    /// <summary>Payload bytes.</summary>
    public required byte[] Payload { get; init; }

    /// <summary>QoS used for delivery, capped by the subscription and the broker.</summary>
    public MqttQoS QoS { get; init; }

    /// <summary>Retain flag after retain-as-published handling.</summary>
    public bool Retain { get; init; }

    /// <summary>MQTT 5 properties copied from the publish.</summary>
    public MqttProperties Properties { get; init; } = new();

    /// <summary>Absolute expiry; messages past it are dropped instead of queued.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}
