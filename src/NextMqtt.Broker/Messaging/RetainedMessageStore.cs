using NextMqtt.Broker.Storage;
using NextMqtt.Core.Protocol;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker.Messaging;

/// <summary>A message retained for future subscribers.</summary>
/// <remarks>保留消息不随订阅者存在；空负载表示删除该主题的保留值。</remarks>
public sealed class RetainedMessage
{
    /// <summary>Topic the message is stored under.</summary>
    public required string Topic { get; init; }

    /// <summary>Payload bytes; never empty for a stored message.</summary>
    public required byte[] Payload { get; init; }

    /// <summary>QoS stored with the message and used for delivery.</summary>
    public MqttQoS QoS { get; init; }

    /// <summary>MQTT 5 properties that survive storage (expiry is tracked separately).</summary>
    public MqttProperties Properties { get; init; } = new();

    /// <summary>When the message was retained.</summary>
    public DateTimeOffset StoredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Absolute expiry derived from the publish, or null when it never expires.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>True when the stored message is past its expiry.</summary>
    /// <param name="now">Reference time.</param>
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expiry && expiry <= now;

    /// <summary>Builds the retained form of an incoming PUBLISH.</summary>
    /// <remarks>把相对的消息过期间隔换算为绝对时间，便于重启后判断。</remarks>
    /// <param name="publish">Retained publish received from a client.</param>
    /// <param name="now">Time the publish was accepted.</param>
    /// <returns>Message ready for the store.</returns>
    public static RetainedMessage FromPublish(PublishPacket publish, DateTimeOffset now)
    {
        var expirySeconds = publish.Properties.GetUInt32(MqttPropertyId.MessageExpiryInterval);

        return new RetainedMessage
        {
            Topic = publish.Topic,
            Payload = publish.Payload,
            QoS = publish.QoS,
            Properties = publish.Properties,
            StoredAt = now,
            ExpiresAt = expirySeconds is { } seconds ? now.AddSeconds(seconds) : null,
        };
    }
}

/// <summary>Thread-safe retained message index with wildcard retrieval.</summary>
/// <remarks>可选挂载 <see cref="IRetainedJournal"/>，写操作同时落盘；读路径只锁内存。</remarks>
public sealed class RetainedMessageStore
{
    /// <summary>Prefix tree keyed by topic levels; guarded by <see cref="_gate"/>.</summary>
    private readonly TopicTrie<RetainedMessage> _trie = new();

    /// <summary>Serializes mutations and snapshot reads.</summary>
    private readonly object _gate = new();

    /// <summary>Optional durable journal; null keeps the store in memory.</summary>
    private readonly IRetainedJournal? _journal;

    /// <summary>
    /// Deletion timestamps used to reject late replicas that would resurrect a
    /// removed message. Bounded by <see cref="TombstoneTtl"/>.
    /// </summary>
    private readonly Dictionary<string, DateTimeOffset> _tombstones = new(StringComparer.Ordinal);

    /// <summary>How long a deletion blocks older writes for the same topic.</summary>
    private static readonly TimeSpan TombstoneTtl = TimeSpan.FromHours(24);

    /// <summary>Creates the store and replays the journal when one is supplied.</summary>
    /// <param name="journal">Durable journal, or null for a memory-only store.</param>
    public RetainedMessageStore(IRetainedJournal? journal = null)
    {
        _journal = journal;

        if (journal is null)
        {
            return;
        }

        foreach (var message in journal.Load())
        {
            _trie.Add(message.Topic, message);
        }
    }

    /// <summary>Number of stored messages, including expired ones not yet pruned.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _trie.Count;
            }
        }
    }

    /// <summary>Stores a retained message. An empty payload removes the entry.</summary>
    /// <param name="publish">Publish received with the retain flag set.</param>
    /// <param name="now">Time used for expiry calculation.</param>
    public void Publish(PublishPacket publish, DateTimeOffset now)
    {
        if (publish.Payload.Length == 0)
        {
            Remove(publish.Topic, now);
            return;
        }

        Set(RetainedMessage.FromPublish(publish, now));
    }

    /// <summary>Stores or replaces a retained message.</summary>
    /// <param name="message">Message to store; same-topic entries are replaced.</param>
    public void Set(RetainedMessage message)
    {
        lock (_gate)
        {
            if (_tombstones.TryGetValue(message.Topic, out var deletedAt) && deletedAt >= message.StoredAt)
            {
                return;
            }

            var existing = FindExact(message.Topic);
            if (existing is not null && existing.StoredAt > message.StoredAt)
            {
                return;
            }

            RemoveCore(message.Topic);
            _trie.Add(message.Topic, message);
            _journal?.AppendSet(message);
            _tombstones.Remove(message.Topic);
        }
    }

    /// <summary>Removes the retained message for one topic.</summary>
    /// <param name="topic">Exact topic name.</param>
    /// <returns><c>true</c> when a message was removed.</returns>
    public bool Remove(string topic) => Remove(topic, DateTimeOffset.UtcNow);

    /// <summary>Removes a retained message stamped with the writer's clock.</summary>
    /// <remarks>比删除更旧的副本会被拒绝，避免离线节点把已删除的消息带回来。</remarks>
    /// <param name="topic">Exact topic name.</param>
    /// <param name="at">When the delete happened.</param>
    public bool Remove(string topic, DateTimeOffset at)
    {
        lock (_gate)
        {
            var existing = FindExact(topic);

            if (existing is not null && existing.StoredAt > at)
            {
                return false;
            }

            var removed = RemoveCore(topic);
            _tombstones[topic] = at;

            if (removed)
            {
                _journal?.AppendRemove(topic, at);
            }

            return removed;
        }
    }

    /// <summary>Returns non-expired messages whose topic matches a filter.</summary>
    /// <param name="filter">Subscription filter, wildcards allowed.</param>
    /// <param name="now">Time used to skip expired entries.</param>
    public List<RetainedMessage> Match(string filter, DateTimeOffset now)
    {
        lock (_gate)
        {
            return _trie.Search(filter)
                .Where(message => !message.IsExpired(now))
                .ToList();
        }
    }

    /// <summary>Copies every stored message, including expired ones.</summary>
    public List<RetainedMessage> Snapshot()
    {
        lock (_gate)
        {
            return _trie.ToList();
        }
    }

    /// <summary>Removes expired messages during the maintenance sweep.</summary>
    /// <param name="now">Reference time.</param>
    /// <returns>Number of messages removed.</returns>
    public int PruneExpired(DateTimeOffset now)
    {
        lock (_gate)
        {
            var expired = _trie.ToList().Where(message => message.IsExpired(now)).ToList();

            foreach (var message in expired)
            {
                RemoveCore(message.Topic);
            }

            var cutoff = now - TombstoneTtl;
            foreach (var topic in _tombstones.Where(pair => pair.Value <= cutoff).Select(pair => pair.Key).ToList())
            {
                _tombstones.Remove(topic);
            }

            return expired.Count;
        }
    }

    /// <summary>Removes every retained message and journals the removals.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (_journal is not null)
            {
                foreach (var message in _trie.ToList())
                {
                    _journal.AppendRemove(message.Topic);
                }
            }

            _trie.Clear();
        }
    }

    /// <summary>Deletes the trie entry for a topic; caller holds the lock.</summary>
    /// <param name="topic">Exact topic name.</param>
    /// <returns><c>true</c> when an entry was deleted.</returns>
    /// <summary>Finds the entry stored exactly under a topic.</summary>
    /// <param name="topic">Exact topic name.</param>
    private RetainedMessage? FindExact(string topic)
        => _trie.Search(topic).FirstOrDefault(message =>
            string.Equals(message.Topic, topic, StringComparison.Ordinal));

    /// <summary>Deletes the trie entry for a topic; caller holds the lock.</summary>
    /// <param name="topic">Exact topic name.</param>
    private bool RemoveCore(string topic)
    {
        var removed = false;

        foreach (var existing in _trie.Search(topic))
        {
            if (string.Equals(existing.Topic, topic, StringComparison.Ordinal))
            {
                removed |= _trie.Remove(topic, existing);
            }
        }

        return removed;
    }
}
