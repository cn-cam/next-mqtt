using NextMqtt.Broker.Diagnostics;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Clustering;

/// <summary>Cross-node publish forwarding.</summary>
public interface IClusterRouter
{
    bool Enabled { get; }

    ClusterSnapshot GetSnapshot();

    /// <summary>Forwards a published message to remote nodes that have matching subscriptions.</summary>
    ValueTask RouteAsync(PublishPacket publish, CancellationToken cancellationToken = default);

    ValueTask StartAsync(CancellationToken cancellationToken = default);

    ValueTask StopAsync();
}

/// <summary>Single-node cluster router used when clustering is disabled.</summary>
public sealed class NullClusterRouter : IClusterRouter
{
    public static NullClusterRouter Instance { get; } = new();

    public bool Enabled => false;

    public ClusterSnapshot GetSnapshot() => new("standalone", "standalone", []);

    public ValueTask RouteAsync(PublishPacket publish, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask StopAsync() => ValueTask.CompletedTask;
}
