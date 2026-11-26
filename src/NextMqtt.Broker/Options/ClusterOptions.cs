namespace NextMqtt.Broker.Options;

/// <summary>Cluster membership and internal transport settings.</summary>
/// <remarks>
/// 关闭时节点完全独立，跨节点投递是空操作；开启后必须同时给出节点标识、可达地址、
/// 至少一个种子和共享密钥，缺一项会在启动阶段失败，而不是等到心跳超时才暴露。
/// </remarks>
public sealed class ClusterOptions
{
    /// <summary>Configuration section name (<c>Cluster</c> / <c>NEXTMQTT_CLUSTER__</c>).</summary>
    /// <remarks>环境变量用双下划线分层，例如 <c>NEXTMQTT_CLUSTER__SECRET</c>。</remarks>
    public const string SectionName = "Cluster";

    /// <summary>Turns gossip membership and cross-node forwarding on.</summary>
    /// <remarks>关闭后不启用心跳线程，路由退化为只处理本地订阅。</remarks>
    public bool Enabled { get; set; }

    /// <summary>Unique identifier for this node; pods usually use the pod name.</summary>
    /// <remarks>必须稳定且唯一：用随机值重启会被当成新成员，旧条目要等超时才会消失。</remarks>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>URL peers use to reach this node (scheme, host and port).</summary>
    /// <remarks>写成 localhost 会让所有心跳失败；容器里应使用服务名或 Pod 名。</remarks>
    public string AdvertiseUrl { get; set; } = string.Empty;

    /// <summary>Initial peers to contact; membership spreads through gossip.</summary>
    /// <remarks>只需列出部分节点，重复与失效地址会被忽略，不会阻断启动。</remarks>
    public List<string> Seeds { get; set; } = [];

    /// <summary>Shared HMAC secret; every node must use the same value.</summary>
    /// <remarks>少于 16 个字符会被拒绝；轮换需要所有节点同时更换，否则心跳互相验签失败。</remarks>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Gossip heartbeat interval in seconds.</summary>
    /// <remarks>越短故障发现越快，跨节点请求量也按比例上升；几十个节点用 5 秒足够。</remarks>
    public int HeartbeatSeconds { get; set; } = 5;

    /// <summary>Silence after which a peer becomes suspect, then dead.</summary>
    /// <remarks>超过该时长没收到心跳先标记可疑，再超时才判死；建议不小于心跳间隔的三倍。</remarks>
    public int NodeTimeoutSeconds { get; set; } = 20;

    /// <summary>Timeout for heartbeats and forwarded publishes.</summary>
    /// <remarks>按单次请求计算，超时只影响这一次转发，本地投递照常完成。</remarks>
    public int RequestTimeoutSeconds { get; set; } = 5;

    /// <summary>How often a session replica is pushed to peers; 0 disables replication.</summary>
    /// <remarks>关闭后节点崩溃会丢失它持有的会话；开启时每轮按批大小推送，按顺序轮转。</remarks>
    public int SessionReplicaSeconds { get; set; } = 5;

    /// <summary>Sessions pushed per replica round, one round per replica interval.</summary>
    /// <remarks>
    /// 一轮最多推送这么多会话，下一轮从上次位置继续。默认 200 在会话数不超过 200 时保证
    /// 每轮覆盖全部；会话更多时，单个会话的副本最多滞后
    /// <c>ceil(会话数 / 批大小) × SessionReplicaSeconds</c>。调大增加带宽和 CPU。
    /// </remarks>
    public int SessionReplicaBatchSize { get; set; } = 200;

    /// <summary>Cap on filters advertised per heartbeat to bound message size.</summary>
    /// <remarks>订阅极多时会被截断，超出部分对端看不见，表现为跨节点漏投；调大意味着心跳报文更大。</remarks>
    public int MaxFiltersPerHeartbeat { get; set; } = 10_000;
}
