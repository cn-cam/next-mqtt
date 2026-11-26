namespace NextMqtt.Broker.Storage;

/// <summary>Where broker state lives between restarts.</summary>
/// <remarks>保留消息与持久会话分开配置，可以各自选择内存或落盘。</remarks>
public sealed class StorageOptions
{
    /// <summary>Configuration section name (<c>Storage</c> / <c>NEXTMQTT_STORAGE__</c>).</summary>
    /// <remarks>环境变量前缀 NEXTMQTT_STORAGE__，子段继续用双下划线下探。</remarks>
    public const string SectionName = "Storage";

    /// <summary>Retained message persistence.</summary>
    /// <remarks>不落盘时重启即清空，新订阅者不会收到旧的保留消息。</remarks>
    public RetainedStorageOptions Retained { get; set; } = new();

    /// <summary>Persistent session snapshots.</summary>
    /// <remarks>只管会话与订阅；在途 QoS 报文不落盘，重启后由客户端重传。</remarks>
    public SessionStorageOptions Session { get; set; } = new();

    /// <summary>Durable copy of session replicas received from peers.</summary>
    /// <remarks>落盘后所有节点同时重启也不会丢副本；内存模式适合能接受该风险的部署。</remarks>
    public SessionReplicaStorageOptions Replicas { get; set; } = new();
}

/// <summary>Selects between an in-memory store and a durable one.</summary>
/// <remarks>二选一，没有混合模式：落盘只是多了一层日志，读路径仍以内存为准。</remarks>
public enum StorageMode
{
    /// <summary>State lives only in process memory (default).</summary>
    /// <remarks>性能最好，但滚动重启会丢状态。</remarks>
    Memory,

    /// <summary>State is written to disk and replayed on startup.</summary>
    /// <remarks>启动时先重放再开始监听，避免对外提供半份数据。</remarks>
    File,
}

/// <summary>Retained message journal settings.</summary>
/// <remarks>日志追加写，超过阈值后整体重写以压制体积。</remarks>
public sealed class RetainedStorageOptions
{
    /// <summary>Memory or file-backed retained storage.</summary>
    /// <remarks>改成 File 会从原目录继续追加，不会迁移已经在内存里的数据。</remarks>
    public StorageMode Mode { get; set; } = StorageMode.Memory;

    /// <summary>Journal location; relative paths resolve against the working directory.</summary>
    /// <remarks>容器里必须挂卷，否则重建容器即丢；相对路径按进程工作目录解析。</remarks>
    public string Path { get; set; } = "data/retained.log";

    /// <summary>Rewrite the journal once it grows beyond this size, in bytes.</summary>
    /// <remarks>压缩期间会短暂持锁；值太小写放大明显，太大则启动重放变慢。</remarks>
    public long CompactThresholdBytes { get; set; } = 4 * 1024 * 1024;
}

/// <summary>Persistent session snapshot settings.</summary>
/// <remarks>快照整体覆盖写，重启恢复的是最后一次成功的快照。</remarks>
public sealed class SessionStorageOptions
{
    /// <summary>Memory or file-backed session storage.</summary>
    /// <remarks>只有持久会话（过期时间非零）才会进入快照。</remarks>
    public StorageMode Mode { get; set; } = StorageMode.Memory;

    /// <summary>Snapshot location; relative paths resolve against the working directory.</summary>
    /// <remarks>与保留日志放在同一卷上最省事。</remarks>
    public string Path { get; set; } = "data/sessions.json";

    /// <summary>How often a changed session set is written, in seconds.</summary>
    /// <remarks>有变更才写；间隔越短丢得越少，但每次都要整表序列化。</remarks>
    public int SnapshotSeconds { get; set; } = 30;
}

/// <summary>Session replica snapshot settings.</summary>
/// <remarks>副本由对端推送，落盘只是让它们在节点重启后仍然可用。</remarks>
public sealed class SessionReplicaStorageOptions
{
    /// <summary>Memory or file-backed replica storage.</summary>
    /// <remarks>内存模式在重启后需要重新等待对端推送。</remarks>
    public StorageMode Mode { get; set; } = StorageMode.Memory;

    /// <summary>Snapshot location; relative paths resolve against the working directory.</summary>
    /// <remarks>与会话快照放在同一卷上，重启恢复的顺序比较直观。</remarks>
    public string Path { get; set; } = "data/session-replicas.json";
}
