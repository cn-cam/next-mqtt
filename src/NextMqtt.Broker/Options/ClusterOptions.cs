namespace NextMqtt.Broker.Options;

/// <summary>Cluster membership and internal transport settings.</summary>
public sealed class ClusterOptions
{
    public const string SectionName = "Cluster";

    public bool Enabled { get; set; }

    /// <summary>Stable node identifier. Defaults to host name plus a short suffix.</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>URL peers use to reach this node, for example http://node-a:8080.</summary>
    public string AdvertiseUrl { get; set; } = string.Empty;

    /// <summary>Initial contact points. Any live node can introduce the rest.</summary>
    public List<string> Seeds { get; set; } = [];

    /// <summary>Shared HMAC secret. Required when clustering is enabled (16+ characters).</summary>
    public string Secret { get; set; } = string.Empty;

    public int HeartbeatSeconds { get; set; } = 5;

    public int NodeTimeoutSeconds { get; set; } = 20;

    public int RequestTimeoutSeconds { get; set; } = 5;

    /// <summary>Upper bound on filters advertised per heartbeat.</summary>
    public int MaxFiltersPerHeartbeat { get; set; } = 10_000;
}
