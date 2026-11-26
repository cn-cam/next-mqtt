using System.Text.Json;
using System.Text.Json.Serialization;
using NextMqtt.Broker.Clustering;

namespace NextMqtt.Broker.Storage;

/// <summary>Durable store for session replicas received from peers.</summary>
/// <remarks>
/// 副本本来是内存结构；落盘后即便所有节点同时重启，也不会因为内存清空而丢失。
/// </remarks>
public interface ISessionReplicaStore
{
    /// <summary>Reads the last snapshot; an unreadable file yields an empty list.</summary>
    /// <returns>Replicas as of the last write.</returns>
    IReadOnlyList<ClusterSessionReplica> Load();

    /// <summary>Replaces the snapshot atomically.</summary>
    /// <param name="replicas">All replicas this node currently holds.</param>
    void Save(IReadOnlyList<ClusterSessionReplica> replicas);
}

/// <summary>Atomic JSON snapshot of the session replicas.</summary>
/// <remarks>与本地会话快照相同的写法：临时文件 + 改名，损坏的文件按空处理。</remarks>
public sealed class FileSessionReplicaStore : ISessionReplicaStore
{
    /// <summary>Serializer settings; null fields are omitted.</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Absolute path of the snapshot file.</summary>
    private readonly string _path;

    /// <summary>Serialises concurrent readers and writers.</summary>
    private readonly object _gate = new();

    /// <summary>Creates the store and the parent directory when missing.</summary>
    /// <remarks>目录不存在时会创建；路径按进程工作目录解析。</remarks>
    /// <param name="path">Snapshot path; relative paths resolve against the working directory.</param>
    public FileSessionReplicaStore(string path)
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
    /// <remarks>构造时已解析，之后不受工作目录变化影响。</remarks>
    public string FilePath => _path;

    /// <summary>Reads and deserializes the snapshot.</summary>
    /// <remarks>文件不存在或损坏都按空处理，避免阻塞启动。</remarks>
    /// <returns>Stored replicas, or an empty list when the file is absent or corrupt.</returns>
    public IReadOnlyList<ClusterSessionReplica> Load()
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
                return JsonSerializer.Deserialize<List<ClusterSessionReplica>>(json, SerializerOptions) ?? [];
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // A corrupt replica file must not stop the broker from starting.
                return [];
            }
        }
    }

    /// <summary>Writes the snapshot through a temporary file and renames it.</summary>
    /// <remarks>整表覆盖写；没有 fsync，掉电可能丢掉最后一次写入。</remarks>
    /// <param name="replicas">Replicas to persist.</param>
    public void Save(IReadOnlyList<ClusterSessionReplica> replicas)
    {
        lock (_gate)
        {
            var temporary = _path + ".tmp";
            var json = JsonSerializer.Serialize(replicas, SerializerOptions);

            File.WriteAllText(temporary, json);
            File.Move(temporary, _path, overwrite: true);
        }
    }
}
