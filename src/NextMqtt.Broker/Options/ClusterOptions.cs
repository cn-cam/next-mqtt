namespace NextMqtt.Broker.Options;

/// <summary>Cluster membership and internal transport settings.</summary>
public sealed class ClusterOptions
{
    /// <summary>Configuration section name (<c>Cluster</c> / <c>NEXTMQTT_CLUSTER__</c>).</summary>
    public const string SectionName = "Cluster";

    /// <summary>Turns gossip membership and cross-node forwarding on.</summary>
    public bool Enabled { get; set; }

    /// <summary>Stable node identifier. Defaults to host name plus a short suffix.</summary>
    /// <summary>Unique identifier for this node; pods usually use the pod name.</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>URL peers use to reach this node, for example http://node-a:8080.</summary>
    /// <summary>URL peers use to reach this node (scheme, host and port).</summary>
    public string AdvertiseUrl { get; set; } = string.Empty;

    /// <summary>Initial contact points. Any live node can introduce the rest.</summary>
    /// <summary>Initial peers to contact; membership spreads through gossip.</summary>
    public List<string> Seeds { get; set; } = [];

    /// <summary>Shared HMAC secret. Required when clustering is enabled (16+ characters).</summary>
    /// <summary>Shared HMAC secret; every node must use the same value.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Gossip heartbeat interval in seconds.</summary>
    public int HeartbeatSeconds { get; set; } = 5;

    /// <summary>Silence after which a peer becomes suspect, then dead.</summary>
    public int NodeTimeoutSeconds { get; set; } = 20;

    /// <summary>Timeout for heartbeats and forwarded publishes.</summary>
    public int RequestTimeoutSeconds { get; set; } = 5;

    /// <summary>Upper bound on filters advertised per heartbeat.</summary>
    /// <summary>Cap on filters advertised per heartbeat to bound message size.</summary>
    public int MaxFiltersPerHeartbeat { get; set; } = 10_000;
}
