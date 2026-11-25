using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NextMqtt.Broker.Messaging;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Storage;

/// <summary>
/// Durable side of a retained-message store. Implementations persist mutations
/// as they happen and replay them when the broker starts.
/// </summary>
/// <remarks>只追加不回写；实现必须保证一次操作一行，重放时坏行可以跳过。</remarks>
public interface IRetainedJournal
{
    /// <summary>Replays the journal and returns the current live messages.</summary>
    /// <remarks>顺带打开追加流，超过阈值时触发一次压缩。</remarks>
    IReadOnlyList<RetainedMessage> Load();

    /// <summary>Journals a store operation.</summary>
    /// <remarks>追加一行 JSON；IO 失败会抛出，是否继续由调用方决定。</remarks>
    /// <param name="message">Message that was written to the index.</param>
    void AppendSet(RetainedMessage message);

    /// <summary>Journals a deletion.</summary>
    /// <remarks>时间戳取本机时钟；跨节点复制时应改用带时间戳的重载。</remarks>
    /// <param name="topic">Topic whose entry was removed.</param>
    void AppendRemove(string topic);

    /// <summary>Journals a deletion stamped with the writer's clock.</summary>
    /// <remarks>删除时间参与墓碑比较，必须使用发起节点的时钟。</remarks>
    /// <param name="topic">Topic that was deleted.</param>
    /// <param name="at">When the delete happened.</param>
    void AppendRemove(string topic, DateTimeOffset at);

    /// <summary>Rewrites the journal so it only contains <paramref name="messages"/>.</summary>
    /// <remarks>压缩期间持锁，写入会短暂阻塞。</remarks>
    void Compact(IReadOnlyList<RetainedMessage> messages);
}

/// <summary>
/// Append-only JSON-lines journal for retained messages. Each line is a complete
/// operation, so a torn last line is skipped instead of corrupting earlier
/// state. Compaction rewrites the file through a temporary sibling.
/// </summary>
/// <remarks>断电最多丢最后一行；压缩走临时文件替换，不会留下半个日志。</remarks>
public sealed class FileRetainedJournal : IRetainedJournal, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly long _compactThresholdBytes;
    private readonly object _gate = new();
    private FileStream? _stream;

    /// <summary>Creates the journal and the parent directory when missing.</summary>
    /// <remarks>只建目录不建文件，首次追加时才真正落盘。</remarks>
    /// <param name="path">Journal file path; relative paths resolve against the working directory.</param>
    /// <param name="compactThresholdBytes">Size at which the file is rewritten.</param>
    public FileRetainedJournal(string path, long compactThresholdBytes = 4 * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = Path.GetFullPath(path);
        _compactThresholdBytes = compactThresholdBytes;

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>Absolute journal path, exposed for diagnostics and tests.</summary>
    /// <remarks>构造时已解析，之后不受工作目录变化影响。</remarks>
    public string FilePath => _path;

    /// <summary>Replays the journal, skipping expired and unreadable lines.</summary>
    /// <remarks>遇到坏行就跳过，不因此放弃后面的记录。</remarks>
    /// <returns>Remaining messages, one entry per topic.</returns>
    public IReadOnlyList<RetainedMessage> Load()
    {
        lock (_gate)
        {
            var live = new Dictionary<string, RetainedMessage>(StringComparer.Ordinal);
            long length = 0;

            if (File.Exists(_path))
            {
                var now = DateTimeOffset.UtcNow;

                foreach (var line in ReadAllLines())
                {
                    length += Encoding.UTF8.GetByteCount(line) + 1;

                    RetainedRecord? record;

                    try
                    {
                        record = JsonSerializer.Deserialize<RetainedRecord>(line, SerializerOptions);
                    }
                    catch (JsonException)
                    {
                        // Torn or hand-edited line: skip it and keep replaying.
                        continue;
                    }

                    if (record?.Topic is null)
                    {
                        continue;
                    }

                    if (record.Op == RetainedRecord.RemoveOperation)
                    {
                        live.Remove(record.Topic);
                        continue;
                    }

                    var message = record.ToMessage();
                    if (message is null || message.IsExpired(now))
                    {
                        live.Remove(record.Topic);
                        continue;
                    }

                    live[record.Topic] = message;
                }
            }

            var messages = live.Values.ToList();

            OpenAppendStream();

            if (length > _compactThresholdBytes)
            {
                CompactCore(messages);
            }

            return messages;
        }
    }

    /// <summary>Appends a set operation.</summary>
    /// <remarks>直接序列化消息，包含有效期与属性。</remarks>
    /// <param name="message">Message to journal.</param>
    public void AppendSet(RetainedMessage message)
    {
        Append(RetainedRecord.FromMessage(message));
    }

    /// <summary>Appends a remove operation.</summary>
    /// <remarks>等价于用当前时间调用带时间戳的重载。</remarks>
    /// <param name="topic">Topic to delete on replay.</param>
    public void AppendRemove(string topic)
        => AppendRemove(topic, DateTimeOffset.UtcNow);

    /// <summary>Appends a remove operation carrying its timestamp.</summary>
    /// <remarks>写一条墓碑，重放时据此决定是否让旧消息复活。</remarks>
    /// <param name="topic">Topic to delete on replay.</param>
    /// <param name="at">When the delete happened.</param>
    public void AppendRemove(string topic, DateTimeOffset at)
        => Append(new RetainedRecord { Op = RetainedRecord.RemoveOperation, Topic = topic, At = at });

    /// <summary>Rewrites the journal so it only contains the given messages.</summary>
    /// <remarks>借临时文件替换，崩溃不会留下半个日志。</remarks>
    /// <param name="messages">Live set to persist.</param>
    public void Compact(IReadOnlyList<RetainedMessage> messages)
    {
        lock (_gate)
        {
            CompactCore(messages);
        }
    }

    /// <summary>Flushes pending writes and closes the journal file.</summary>
    /// <remarks>只 flush 到操作系统，没有 fsync，掉电可能丢最后几行。</remarks>
    public void Dispose()
    {
        lock (_gate)
        {
            _stream?.Dispose();
            _stream = null;
        }
    }

    private void Append(RetainedRecord record)
    {
        lock (_gate)
        {
            OpenAppendStream();

            var line = JsonSerializer.Serialize(record, SerializerOptions) + "\n";
            var bytes = Encoding.UTF8.GetBytes(line);

            _stream!.Write(bytes, 0, bytes.Length);
            _stream.Flush(flushToDisk: false);

            if (_stream.Length > _compactThresholdBytes)
            {
                CompactCore(ReadLiveUnsafe());
            }
        }
    }

    private void CompactCore(IReadOnlyList<RetainedMessage> messages)
    {
        _stream?.Dispose();
        _stream = null;

        var temporary = _path + ".tmp";

        using (var writer = new StreamWriter(temporary, append: false, Encoding.UTF8))
        {
            foreach (var message in messages)
            {
                writer.Write(JsonSerializer.Serialize(RetainedRecord.FromMessage(message), SerializerOptions));
                writer.Write('\n');
            }

            writer.Flush();
        }

        File.Move(temporary, _path, overwrite: true);
        OpenAppendStream();
    }

    /// <summary>Reads the journal without taking the lock; caller holds it.</summary>
    /// <remarks>内部使用；外部调用必须已持锁，否则会和追加并发。</remarks>
    private List<RetainedMessage> ReadLiveUnsafe()
    {
        var live = new Dictionary<string, RetainedMessage>(StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;

        foreach (var line in ReadAllLines())
        {
            RetainedRecord? record;

            try
            {
                record = JsonSerializer.Deserialize<RetainedRecord>(line, SerializerOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (record?.Topic is null)
            {
                continue;
            }

            if (record.Op == RetainedRecord.RemoveOperation)
            {
                live.Remove(record.Topic);
                continue;
            }

            var message = record.ToMessage();
            if (message is not null && !message.IsExpired(now))
            {
                live[record.Topic] = message;
            }
        }

        return live.Values.ToList();
    }

    private void OpenAppendStream()
    {
        _stream ??= new FileStream(
            _path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.None);
    }

    /// <summary>Reads the file while the append stream holds it open.</summary>
    /// <remarks>用 FileShare.ReadWrite 打开，读取期间不阻塞追加。</remarks>
    private List<string> ReadAllLines()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        var lines = new List<string>();

        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private sealed class RetainedRecord
    {
        public const string RemoveOperation = "remove";

        public string Op { get; set; } = "set";

        public string Topic { get; set; } = string.Empty;

        public string? Payload { get; set; }

        public int QoS { get; set; }

        public DateTimeOffset StoredAt { get; set; }

        public DateTimeOffset? At { get; set; }

        public DateTimeOffset? ExpiresAt { get; set; }

        public List<PropertyRecord>? Properties { get; set; }

        public static RetainedRecord FromMessage(RetainedMessage message) => new()
        {
            Op = "set",
            Topic = message.Topic,
            Payload = Convert.ToBase64String(message.Payload),
            QoS = (int)message.QoS,
            StoredAt = message.StoredAt,
            ExpiresAt = message.ExpiresAt,
            Properties = PropertyRecord.FromProperties(message.Properties),
        };

        public RetainedMessage? ToMessage()
        {
            MqttProperties properties;

            try
            {
                properties = PropertyRecord.ToProperties(Properties);
            }
            catch (MqttProtocolException)
            {
                return null;
            }

            return new RetainedMessage
            {
                Topic = Topic,
                Payload = Payload is null ? [] : Convert.FromBase64String(Payload),
                QoS = (MqttQoS)QoS,
                Properties = properties,
                StoredAt = StoredAt,
                ExpiresAt = ExpiresAt,
            };
        }
    }
}
