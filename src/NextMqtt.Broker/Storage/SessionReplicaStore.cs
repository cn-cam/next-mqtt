using System.Text;
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

/// <summary>Atomic, sharded JSON snapshot of the session replicas.</summary>
/// <remarks>
/// 副本按客户端 ID 分片，只有内容变化的分片会被重写；否则每 5 秒重写整表在会话多时
/// 会把磁盘写满。旧版本的单文件布局在启动时仍可读入，首次写入后转为分片文件。
/// </remarks>
public sealed class FileSessionReplicaStore : ISessionReplicaStore
{
    /// <summary>Serializer settings; null fields are omitted.</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Absolute path template; shard suffix is inserted before the extension.</summary>
    private readonly string _path;

    /// <summary>Number of shard files; one means a single file.</summary>
    private readonly int _shardCount;

    /// <summary>Serialises concurrent readers and writers.</summary>
    private readonly object _gate = new();

    /// <summary>Signature of the last content written to each shard.</summary>
    private readonly string?[] _signatures;

    /// <summary>Shards rewritten by the last save, for diagnostics and tests.</summary>
    private int _shardWrites;

    /// <summary>Creates the store and the parent directory when missing.</summary>
    /// <remarks>目录不存在时会创建；路径按进程工作目录解析。</remarks>
    /// <param name="path">Snapshot path; relative paths resolve against the working directory.</param>
    /// <param name="shardCount">Number of shard files; values below one are treated as one.</param>
    public FileSessionReplicaStore(string path, int shardCount = 16)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = Path.GetFullPath(path);
        _shardCount = Math.Max(1, shardCount);
        _signatures = new string?[_shardCount];

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>Absolute snapshot path, exposed for diagnostics and tests.</summary>
    /// <remarks>构造时已解析，之后不受工作目录变化影响。</remarks>
    public string FilePath => _path;

    /// <summary>Number of shard files configured.</summary>
    /// <remarks>为 1 时退化为单文件写入。</remarks>
    public int ShardCount => _shardCount;

    /// <summary>Shards rewritten by the most recent save.</summary>
    /// <remarks>未变化的会话集不会产生写入，因此该值为 0。</remarks>
    public int ShardWrites => _shardWrites;

    /// <summary>Reads and deserializes every shard, plus the legacy single file.</summary>
    /// <remarks>文件不存在或损坏都按空处理，避免阻塞启动。</remarks>
    /// <returns>Stored replicas, or an empty list when nothing readable exists.</returns>
    public IReadOnlyList<ClusterSessionReplica> Load()
    {
        lock (_gate)
        {
            var replicas = new List<ClusterSessionReplica>();
            var shardFound = false;

            for (var index = 0; index < _shardCount; index++)
            {
                var path = ShardPath(index);

                if (!File.Exists(path))
                {
                    continue;
                }

                shardFound = true;
                replicas.AddRange(ReadFile(path));
            }

            if (!shardFound && File.Exists(_path))
            {
                replicas.AddRange(ReadFile(_path));
            }

            return replicas;
        }
    }

    /// <summary>Writes the shards whose content changed.</summary>
    /// <remarks>整表覆盖写；没有 fsync，掉电可能丢掉最后一次写入。</remarks>
    /// <param name="replicas">Replicas to persist.</param>
    public void Save(IReadOnlyList<ClusterSessionReplica> replicas)
    {
        lock (_gate)
        {
            var buckets = new List<ClusterSessionReplica>?[_shardCount];

            foreach (var replica in replicas)
            {
                var index = (int)(Hash(replica.Session.ClientId) % (uint)_shardCount);
                (buckets[index] ??= []).Add(replica);
            }

            _shardWrites = 0;

            for (var index = 0; index < _shardCount; index++)
            {
                var bucket = buckets[index] ?? [];
                var signature = Signature(bucket);

                if (string.Equals(signature, _signatures[index], StringComparison.Ordinal))
                {
                    continue;
                }

                var path = ShardPath(index);

                if (bucket.Count == 0)
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }

                    _signatures[index] = signature;
                    _shardWrites++;
                    continue;
                }

                WriteFile(path, bucket);
                _signatures[index] = signature;
                _shardWrites++;
            }

            // The legacy single file is replaced by shards after the first save.
            if (_shardCount > 1 && File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
    }

    /// <summary>Reads one shard, treating a corrupt file as empty.</summary>
    /// <param name="path">Shard file to read.</param>
    /// <returns>Replicas in that shard, or an empty list.</returns>
    private static IReadOnlyList<ClusterSessionReplica> ReadFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<ClusterSessionReplica>>(json, SerializerOptions) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A corrupt replica file must not stop the broker from starting.
            return [];
        }
    }

    /// <summary>Writes one shard through a temporary file and renames it.</summary>
    /// <param name="path">Shard file to replace.</param>
    /// <param name="replicas">Replicas assigned to that shard.</param>
    private static void WriteFile(string path, List<ClusterSessionReplica> replicas)
    {
        var temporary = path + ".tmp";
        var json = JsonSerializer.Serialize(replicas, SerializerOptions);

        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Builds the shard path by inserting the index before the extension.</summary>
    /// <param name="index">Shard number.</param>
    private string ShardPath(int index)
    {
        if (_shardCount == 1)
        {
            return _path;
        }

        var directory = Path.GetDirectoryName(_path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(_path);
        var extension = Path.GetExtension(_path);

        return Path.Combine(directory, $"{name}.{index:00}{extension}");
    }

    /// <summary>Cheap content signature so unchanged shards are skipped.</summary>
    /// <param name="replicas">Replicas in one shard.</param>
    private static string Signature(List<ClusterSessionReplica> replicas)
    {
        if (replicas.Count == 0)
        {
            return "empty";
        }

        var builder = new StringBuilder();

        foreach (var replica in replicas.OrderBy(item => item.Session.ClientId, StringComparer.Ordinal))
        {
            builder.Append(replica.Session.ClientId)
                .Append(':')
                .Append(replica.Version)
                .Append(':')
                .Append(replica.UpdatedAtUnix)
                .Append(';');
        }

        return builder.ToString();
    }

    /// <summary>FNV-1a hash used to spread client ids across shards.</summary>
    /// <param name="value">Client identifier.</param>
    private static uint Hash(string value)
    {
        var hash = 2166136261u;

        foreach (var character in value)
        {
            hash ^= character;
            hash *= 16777619u;
        }

        return hash;
    }
}
