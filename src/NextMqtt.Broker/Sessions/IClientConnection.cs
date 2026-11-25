using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Sessions;

/// <summary>Abstraction over one live client connection.</summary>
/// <remarks>连接的具体传输（TCP、TLS、WebSocket）由 Server 层提供；Broker 只依赖此接口。</remarks>
public interface IClientConnection
{
    /// <summary>Peer address as reported by the transport, port included.</summary>
/// <remarks>用于按 IP 限流与日志；格式由传输层决定。</remarks>
    string RemoteAddress { get; }

    /// <summary>Protocol level negotiated by CONNECT.</summary>
/// <remarks>决定下发报文能否带 MQTT 5 属性。</remarks>
    MqttProtocolVersion ProtocolVersion { get; }

    /// <summary>Queues a packet for writing without blocking the caller.</summary>
/// <remarks>返回 false 表示连接正在关闭，调用方应停止投递。</remarks>
    /// <param name="packet">Packet to enqueue.</param>
    /// <returns><c>false</c> when the connection is closing.</returns>
    bool TrySend(MqttPacket packet);

    /// <summary>Sends a DISCONNECT (MQTT 5) and closes the transport.</summary>
/// <remarks>3.1.1 不发报文，直接断开。</remarks>
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
/// <remarks>已处理过主题别名，订阅端看到的是真实主题。</remarks>
    public required string Topic { get; init; }

    /// <summary>Payload bytes.</summary>
/// <remarks>与发布时一致，不做压缩或重编码。</remarks>
    public required byte[] Payload { get; init; }

    /// <summary>QoS used for delivery, capped by the subscription and the broker.</summary>
/// <remarks>取发布与订阅 QoS 的较小值，避免超承诺。</remarks>
    public MqttQoS QoS { get; init; }

    /// <summary>Retain flag after retain-as-published handling.</summary>
/// <remarks>订阅开启 RAP 才保留原发布位，否则按规则重写。</remarks>
    public bool Retain { get; init; }

    /// <summary>MQTT 5 properties copied from the publish.</summary>
/// <remarks>3.1.1 订阅者拿不到这些属性，编码时会被忽略。</remarks>
    public MqttProperties Properties { get; init; } = new();

    /// <summary>Absolute expiry; messages past it are dropped instead of queued.</summary>
/// <remarks>离线队列不保存过期消息，避免重连时补发垃圾。</remarks>
    public DateTimeOffset? ExpiresAt { get; init; }
}
