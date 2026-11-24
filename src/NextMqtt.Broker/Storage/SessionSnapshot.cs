using System.Text.Json;
using System.Text.Json.Serialization;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Storage;

/// <summary>One persistent session as written to disk.</summary>
public sealed class PersistedSession
{
    public string ClientId { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? Username { get; set; }

    public bool IsSuperuser { get; set; }

    public Dictionary<string, string>? Claims { get; set; }

    public int ProtocolVersion { get; set; }

    public uint SessionExpiryInterval { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset LastSeen { get; set; }

    public List<PersistedSubscription> Subscriptions { get; set; } = [];

    public List<PersistedMessage> Queue { get; set; } = [];
}

public sealed class PersistedSubscription
{
    public string Filter { get; set; } = string.Empty;

    public int QoS { get; set; }

    public bool NoLocal { get; set; }

    public bool RetainAsPublished { get; set; }

    public int RetainHandling { get; set; }

    public int? Identifier { get; set; }
}

public sealed class PersistedMessage
{
    public string Topic { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public int QoS { get; set; }

    public bool Retain { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public List<PropertyRecord>? Properties { get; set; }
}

/// <summary>Durable store for persistent session state.</summary>
public interface ISessionSnapshotStore
{
    IReadOnlyList<PersistedSession> Load();

    void Save(IReadOnlyList<PersistedSession> sessions);
}

/// <summary>
/// Atomic JSON snapshot of all persistent sessions. Snapshots are small enough
/// to rewrite completely, which keeps recovery logic trivial: the file always
/// describes the state as of the last snapshot.
/// </summary>
public sealed class FileSessionSnapshotStore : ISessionSnapshotStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly object _gate = new();

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

    public string FilePath => _path;

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
