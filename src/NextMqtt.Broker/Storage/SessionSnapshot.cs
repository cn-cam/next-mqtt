using System.Text.Json;
using System.Text.Json.Serialization;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Storage;

/// <summary>One persistent session as written to disk.</summary>
/// <remarks>仅保存不依赖连接的会话状态；inflight 握手不落盘。</remarks>
public sealed class PersistedSession
{
    /// <summary>Client identifier that owns the session.</summary>
    /// <remarks>恢复时的主键，改名等于换一个会话。</remarks>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Principal name captured at authentication time.</summary>
    /// <remarks>断开期间没有凭据，用保存下来的身份继续做 ACL 判断。</remarks>
    public string? Name { get; set; }

    /// <summary>Authenticated username, when credentials were used.</summary>
    /// <remarks>匿名会话为 null，占位符展开时按空串处理。</remarks>
    public string? Username { get; set; }

    /// <summary>True when the principal bypasses ACL checks.</summary>
    /// <remarks>保留这一位是为了恢复后权限不降级。</remarks>
    public bool IsSuperuser { get; set; }

    /// <summary>Additional identity claims used by ACL placeholders.</summary>
    /// <remarks>只保存字符串值，复杂声明在认证时已经展开。</remarks>
    public Dictionary<string, string>? Claims { get; set; }

    /// <summary>Protocol level as an integer (4 or 5).</summary>
    /// <remarks>决定恢复后的报文格式；重连时级别不一致会重建会话。</remarks>
    public int ProtocolVersion { get; set; }

    /// <summary>Session expiry in seconds.</summary>
    /// <remarks>恢复后重新开始计时，不按上次断开的时间接着算。</remarks>
    public uint SessionExpiryInterval { get; set; }

    /// <summary>Absolute expiry timestamp, or null while the session is online.</summary>
    /// <remarks>落盘时通常有值；为 null 说明快照是在线时拍的。</remarks>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Last time the session was seen.</summary>
    /// <remarks>用于后台排序与过期巡检，不参与恢复逻辑。</remarks>
    public DateTimeOffset LastSeen { get; set; }

    /// <summary>Subscriptions restored into the index on startup.</summary>
    /// <remarks>顺序无关，恢复时按客户端加过滤器去重。</remarks>
    public List<PersistedSubscription> Subscriptions { get; set; } = [];

    /// <summary>Offline queue in delivery order.</summary>
    /// <remarks>顺序必须保留，否则重连后消息会乱序到达。</remarks>
    public List<PersistedMessage> Queue { get; set; } = [];
}

/// <summary>One subscription inside a persisted session.</summary>
/// <remarks>只存授予结果，不存协商过程；恢复后不再重新鉴权。</remarks>
public sealed class PersistedSubscription
{
    /// <summary>Filter text as originally supplied.</summary>
    /// <remarks>保留共享订阅前缀，恢复时重新解析。</remarks>
    public string Filter { get; set; } = string.Empty;

    /// <summary>Granted QoS (0-2).</summary>
    /// <remarks>沿用断开前的授予值，不重新协商。</remarks>
    public int QoS { get; set; }

    /// <summary>Suppress the client's own publications.</summary>
    /// <remarks>MQTT 5 特性；3.1.1 订阅没有这一项，恒为 false。</remarks>
    public bool NoLocal { get; set; }

    /// <summary>Preserve the retain flag on delivery.</summary>
    /// <remarks>与 RetainHandling 一起决定历史消息的表现。</remarks>
    public bool RetainAsPublished { get; set; }

    /// <summary>MQTT 5 retain handling option (0-2).</summary>
    /// <remarks>只影响订阅建立的那一刻是否下发保留消息。</remarks>
    public int RetainHandling { get; set; }

    /// <summary>Subscription identifier, when the client supplied one.</summary>
    /// <remarks>恢复后继续用同一个标识，客户端才能对应上。</remarks>
    public int? Identifier { get; set; }
}

/// <summary>One queued message inside a persisted session.</summary>
/// <remarks>队列顺序就是投递顺序，恢复时原样重放。</remarks>
public sealed class PersistedMessage
{
    /// <summary>Topic name.</summary>
    /// <remarks>存展开后的真实主题，主题别名不落盘。</remarks>
    public string Topic { get; set; } = string.Empty;

    /// <summary>Base64 payload.</summary>
    /// <remarks>JSON 里不能放原始字节，编码后体积约增大三分之一。</remarks>
    public string Payload { get; set; } = string.Empty;

    /// <summary>QoS stored with the message.</summary>
    /// <remarks>重放时再与订阅授予值取小。</remarks>
    public int QoS { get; set; }

    /// <summary>Retain flag from the original publish.</summary>
    /// <remarks>队列里的消息按 RAP 规则决定是否保留该位。</remarks>
    public bool Retain { get; set; }

    /// <summary>Absolute expiry timestamp, or null when it never expires.</summary>
    /// <remarks>恢复时丢弃已过期的消息，不做补发。</remarks>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>MQTT 5 properties to replay with the message.</summary>
    /// <remarks>只保存可持久化的属性，订阅标识等连接级属性不在这里。</remarks>
    public List<PropertyRecord>? Properties { get; set; }
}

/// <summary>Durable store for persistent session state.</summary>
/// <remarks>只有一个实现：整表 JSON 原子替换。</remarks>
public interface ISessionSnapshotStore
{
    /// <summary>Reads the last snapshot; an unreadable file yields an empty list.</summary>
    /// <remarks>损坏时按空处理并继续启动，宁可从零开始也不阻塞。</remarks>
    /// <returns>Persisted sessions in file order.</returns>
    IReadOnlyList<PersistedSession> Load();

    /// <summary>Replaces the snapshot atomically.</summary>
    /// <remarks>写临时文件再改名；崩溃最多丢掉最后一次未完成的写入。</remarks>
    /// <param name="sessions">Full session set to write.</param>
    void Save(IReadOnlyList<PersistedSession> sessions);
}

/// <summary>
/// Atomic JSON snapshot of all persistent sessions. Snapshots are small enough
/// to rewrite completely, which keeps recovery logic trivial: the file always
/// describes the state as of the last snapshot.
/// </summary>
/// <remarks>写临时文件后原子替换；损坏的快照不会阻止 Broker 启动。</remarks>
public sealed class FileSessionSnapshotStore : ISessionSnapshotStore
{
    /// <summary>Serializer settings used for the snapshot file.</summary>
    /// <remarks>省略 null 字段以缩小文件；字段名保持稳定，旧快照仍可读。</remarks>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Absolute path of the snapshot file.</summary>
    /// <remarks>构造时解析成绝对路径，之后不受工作目录变化影响。</remarks>
    private readonly string _path;

    /// <summary>Serializes concurrent readers and writers of the file.</summary>
    /// <remarks>只保证进程内串行；多进程同时写没有保护，部署时应只有一个写入者。</remarks>
    private readonly object _gate = new();

    /// <summary>Creates the store and the parent directory when missing.</summary>
    /// <remarks>目录建不出来会在启动时抛异常，属于配置错误。</remarks>
    /// <param name="path">Snapshot file path; relative paths resolve against the working directory.</param>
    public FileSessionSnapshotStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = Path.GetFullPath(path);

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>Absolute snapshot path, exposed for diagnostics and tests.</summary>
    /// <remarks>只读属性，便于日志与测试断言。</remarks>
    public string FilePath => _path;

    /// <summary>Reads and deserializes the snapshot.</summary>
    /// <remarks>文件不存在返回空列表；IO 错误与格式错误同样按空处理。</remarks>
    /// <returns>Stored sessions, or an empty list when the file is absent or corrupt.</returns>
    public IReadOnlyList<PersistedSession> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            try
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<List<PersistedSession>>(json, SerializerOptions) ?? [];
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // A corrupt snapshot must not stop the broker from starting.
                return [];
            }
        }
    }

    /// <summary>Writes the snapshot through a temporary file and renames it.</summary>
    /// <remarks>先写 .tmp 再覆盖，避免读到半个文件；没有 fsync，掉电可能丢最后一批。</remarks>
    /// <param name="sessions">Full session set to persist.</param>
    public void Save(IReadOnlyList<PersistedSession> sessions)
    {
        lock (_gate)
        {
            var temporary = _path + ".tmp";
            var json = JsonSerializer.Serialize(sessions, SerializerOptions);

            File.WriteAllText(temporary, json);
            File.Move(temporary, _path, overwrite: true);
        }
    }
}
