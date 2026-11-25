using System.Text.Json;
using System.Text.Json.Serialization;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Storage;

/// <summary>One persistent session as written to disk.</summary>
/// <remarks>仅保存不依赖连接的会话状态；inflight 握手不落盘。</remarks>
public sealed class PersistedSession
{
    /// <summary>Client identifier that owns the session.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Principal name captured at authentication time.</summary>
    public string? Name { get; set; }

    /// <summary>Authenticated username, when credentials were used.</summary>
    public string? Username { get; set; }

    /// <summary>True when the principal bypasses ACL checks.</summary>
    public bool IsSuperuser { get; set; }

    /// <summary>Additional identity claims used by ACL placeholders.</summary>
    public Dictionary<string, string>? Claims { get; set; }

    /// <summary>Protocol level as an integer (4 or 5).</summary>
    public int ProtocolVersion { get; set; }

    /// <summary>Session expiry in seconds.</summary>
    public uint SessionExpiryInterval { get; set; }

    /// <summary>Absolute expiry timestamp, or null while the session is online.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Last time the session was seen.</summary>
    public DateTimeOffset LastSeen { get; set; }

    /// <summary>Subscriptions restored into the index on startup.</summary>
    public List<PersistedSubscription> Subscriptions { get; set; } = [];

    /// <summary>Offline queue in delivery order.</summary>
    public List<PersistedMessage> Queue { get; set; } = [];
}

/// <summary>One subscription inside a persisted session.</summary>
public sealed class PersistedSubscription
{
    /// <summary>Filter text as originally supplied.</summary>
    public string Filter { get; set; } = string.Empty;

    /// <summary>Granted QoS (0-2).</summary>
    public int QoS { get; set; }

    /// <summary>Suppress the client's own publications.</summary>
    public bool NoLocal { get; set; }

    /// <summary>Preserve the retain flag on delivery.</summary>
    public bool RetainAsPublished { get; set; }

    /// <summary>MQTT 5 retain handling option (0-2).</summary>
    public int RetainHandling { get; set; }

    /// <summary>Subscription identifier, when the client supplied one.</summary>
    public int? Identifier { get; set; }
}

/// <summary>One queued message inside a persisted session.</summary>
public sealed class PersistedMessage
{
    /// <summary>Topic name.</summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>Base64 payload.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>QoS stored with the message.</summary>
    public int QoS { get; set; }

    /// <summary>Retain flag from the original publish.</summary>
    public bool Retain { get; set; }

    /// <summary>Absolute expiry timestamp, or null when it never expires.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>MQTT 5 properties to replay with the message.</summary>
    public List<PropertyRecord>? Properties { get; set; }
}

/// <summary>Durable store for persistent session state.</summary>
public interface ISessionSnapshotStore
{
    /// <summary>Reads the last snapshot; an unreadable file yields an empty list.</summary>
    /// <returns>Persisted sessions in file order.</returns>
    IReadOnlyList<PersistedSession> Load();

    /// <summary>Replaces the snapshot atomically.</summary>
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
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Absolute path of the snapshot file.</summary>
    private readonly string _path;

    /// <summary>Serializes concurrent readers and writers of the file.</summary>
    private readonly object _gate = new();

    /// <summary>Creates the store and the parent directory when missing.</summary>
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
    public string FilePath => _path;

    /// <summary>Reads and deserializes the snapshot.</summary>
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
