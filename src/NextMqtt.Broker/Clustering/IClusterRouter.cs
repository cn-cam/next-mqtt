using NextMqtt.Broker.Diagnostics;
using NextMqtt.Broker.Storage;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Clustering;

/// <summary>Cross-node publish forwarding.</summary>
/// <remarks>实现负责成员发现、过滤条件广告与消息转发；单机时使用 <see cref="NullClusterRouter"/>。</remarks>
public interface IClusterRouter
{
    /// <summary>True when clustering is configured and running.</summary>
    /// <remarks>开启后仍可能因密钥或地址错误停在未启动状态。</remarks>
    bool Enabled { get; }

    /// <summary>Membership as last observed by the local node.</summary>
    /// <remarks>读的是本地视图，不发起网络请求。</remarks>
    /// <returns>Snapshot including the local node and every known peer.</returns>
    ClusterSnapshot GetSnapshot();

    /// <summary>Forwards a published message to remote nodes that have matching subscriptions.</summary>
    /// <remarks>转发为尽力而为：对端不可达时只记录日志，不回滚本地投递。</remarks>
    /// <param name="publish">Message already accepted from a client.</param>
    /// <param name="messageId">Stable id used to pick one node per shared group.</param>
    /// <param name="cancellationToken">Cancels the fan-out.</param>
    /// <returns>Completes when every reachable peer was contacted or skipped.</returns>
    ValueTask RouteAsync(PublishPacket publish, string messageId, CancellationToken cancellationToken = default);

    /// <summary>True when this node should deliver the shared group for this message.</summary>
    /// <remarks>按 (messageId, group) 哈希在持有该组的节点间选一个，避免集群内重复投递。</remarks>
    /// <param name="topic">Topic being published.</param>
    /// <param name="group">Shared subscription group name.</param>
    /// <param name="messageId">Stable id for the message.</param>
    /// <returns><c>true</c> when the local node owns this delivery.</returns>
    bool OwnsSharedGroup(string topic, string group, string messageId);

    /// <summary>Asks peers whether they hold a session and takes it over.</summary>
    /// <remarks>只有 CleanStart 为 false 时调用；对端交还会话并断开自己的连接。</remarks>
    /// <param name="clientId">Session to claim.</param>
    /// <param name="cancellationToken">Cancels the peer requests.</param>
    /// <returns>The transferred session, or null when no peer holds it.</returns>
    Task<PersistedSession?> ClaimSessionAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>True when this node pushes session replicas to its peers.</summary>
    /// <remarks>关闭复制时，节点崩溃会丢掉它持有的会话。</remarks>
    bool ReplicatesSessions { get; }

    /// <summary>How often replicas are pushed, as configured.</summary>
    /// <remarks>0 表示关闭复制。</remarks>
    TimeSpan ReplicaInterval { get; }

    /// <summary>Sessions pushed per replica round.</summary>
    /// <remarks>一轮推送这么多会话，下一轮从上次位置继续；值过大会增加带宽。</remarks>
    int ReplicaBatchSize { get; }

    /// <summary>Sends one session replica to every live peer.</summary>
    /// <remarks>尽力而为：失败只记日志，下一次轮询会重发。</remarks>
    /// <param name="replica">Snapshot to send.</param>
    /// <param name="cancellationToken">Cancels the fan-out.</param>
    Task ReplicateSessionAsync(ClusterSessionReplica replica, CancellationToken cancellationToken = default);

    /// <summary>Asks peers for the newest replica of a session.</summary>
    /// <remarks>只在原节点不可用时使用，调用方负责判断所有者是否已死。</remarks>
    /// <param name="clientId">Session to look up.</param>
    /// <param name="cancellationToken">Cancels the peer requests.</param>
    Task<ClusterSessionReplica?> FindSessionReplicaAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>True when the peer is currently considered alive.</summary>
    /// <remarks>用于判断副本记录的属主是否还活着。</remarks>
    /// <param name="nodeId">Node identifier to test.</param>
    bool IsNodeAlive(string nodeId);

    /// <summary>Finds the newest copy of a session across live peers.</summary>
    /// <remarks>只读探测，不交还会话，也不会断开在线客户端。</remarks>
    /// <param name="clientId">Session to look for.</param>
    /// <param name="cancellationToken">Cancels the peer requests.</param>
    Task<ClusterSessionOwner?> FindSessionOwnerAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>Starts the heartbeat loop and joins the cluster.</summary>
    /// <remarks>幂等；重复调用不会起第二个循环。</remarks>
    /// <param name="cancellationToken">Stops the startup handshake.</param>
    ValueTask StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops heartbeats and leaves the cluster.</summary>
    /// <remarks>尽力广播离开消息，失败只记日志。</remarks>
    ValueTask StopAsync();
}

/// <summary>Single-node cluster router used when clustering is disabled.</summary>
/// <remarks>所有调用都被接受，但不产生任何网络活动。</remarks>
public sealed class NullClusterRouter : IClusterRouter
{
    /// <summary>Shared stateless instance.</summary>
    /// <remarks>无状态，可以全进程共用一个。</remarks>
    public static NullClusterRouter Instance { get; } = new();

    /// <summary>Always false: there are no peers.</summary>
    /// <remarks>Broker 用它跳过集群路径。</remarks>
    public bool Enabled => false;

    /// <summary>Returns an empty standalone membership view.</summary>
    /// <remarks>状态固定为 standalone，节点列表为空。</remarks>
    public ClusterSnapshot GetSnapshot() => new("standalone", "standalone", []);

    /// <summary>Discards the publish; nothing to forward to.</summary>
    /// <remarks>直接完成，不产生等待。</remarks>
    /// <param name="publish">Ignored.</param>
    /// <param name="messageId">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    public ValueTask RouteAsync(PublishPacket publish, string messageId, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    /// <summary>Always owns the group: single node means local delivery.</summary>
    /// <remarks>保证共享订阅在单机模式下照常投递。</remarks>
    /// <param name="topic">Ignored.</param>
    /// <param name="group">Ignored.</param>
    /// <param name="messageId">Ignored.</param>
    public bool OwnsSharedGroup(string topic, string group, string messageId) => true;

    /// <summary>No peers, so there is never a session to claim.</summary>
    /// <remarks>返回 null 让调用方按普通方式新建会话。</remarks>
    /// <param name="clientId">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    public Task<PersistedSession?> ClaimSessionAsync(string clientId, CancellationToken cancellationToken = default)
        => Task.FromResult<PersistedSession?>(null);

    /// <summary>Single node, so replicas have nowhere to go.</summary>
    /// <remarks>Broker 据此跳过复制路径。</remarks>
    public bool ReplicatesSessions => false;

    /// <summary>No replication, so the interval is unused.</summary>
    /// <remarks>固定返回 0。</remarks>
    public TimeSpan ReplicaInterval => TimeSpan.Zero;

    /// <summary>No replication, so the batch size is unused.</summary>
    /// <remarks>返回 1，避免调用方除以零。</remarks>
    public int ReplicaBatchSize => 1;

    /// <summary>Discards the replica.</summary>
    /// <remarks>直接完成，不产生等待。</remarks>
    /// <param name="replica">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    public Task ReplicateSessionAsync(ClusterSessionReplica replica, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>No peers, so there is no replica to find.</summary>
    /// <remarks>返回 null。</remarks>
    /// <param name="clientId">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    public Task<ClusterSessionReplica?> FindSessionReplicaAsync(string clientId, CancellationToken cancellationToken = default)
        => Task.FromResult<ClusterSessionReplica?>(null);

    /// <summary>Single node, so nothing else can be alive.</summary>
    /// <remarks>始终返回 false。</remarks>
    /// <param name="nodeId">Ignored.</param>
    public bool IsNodeAlive(string nodeId) => false;

    /// <summary>No peers, so no other copy exists.</summary>
    /// <remarks>返回 null。</remarks>
    /// <param name="clientId">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    public Task<ClusterSessionOwner?> FindSessionOwnerAsync(string clientId, CancellationToken cancellationToken = default)
        => Task.FromResult<ClusterSessionOwner?>(null);

    /// <summary>No-op start.</summary>
    /// <remarks>返回已完成的任务。</remarks>
    /// <param name="cancellationToken">Ignored.</param>
    public ValueTask StartAsync(CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    /// <summary>No-op stop.</summary>
    /// <remarks>同上。</remarks>
    public ValueTask StopAsync() => ValueTask.CompletedTask;
}
