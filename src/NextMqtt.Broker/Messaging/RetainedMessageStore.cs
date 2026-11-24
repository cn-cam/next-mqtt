using NextMqtt.Broker.Storage;
using NextMqtt.Core.Protocol;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker.Messaging;

/// <summary>A message retained for future subscribers.</summary>
public sealed class RetainedMessage
{
    public required string Topic { get; init; }

    public required byte[] Payload { get; init; }

    public MqttQoS QoS { get; init; }

    public MqttProperties Properties { get; init; } = new();

    public DateTimeOffset StoredAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ExpiresAt { get; init; }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expiry && expiry <= now;

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
public sealed class RetainedMessageStore
{
    private readonly TopicTrie<RetainedMessage> _trie = new();
    private readonly object _gate = new();
    private readonly IRetainedJournal? _journal;

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
    public void Publish(PublishPacket publish, DateTimeOffset now)
    {
        if (publish.Payload.Length == 0)
        {
            Remove(publish.Topic);
            return;
        }

        Set(RetainedMessage.FromPublish(publish, now));
    }

    public void Set(RetainedMessage message)
    {
        lock (_gate)
        {
            RemoveCore(message.Topic);
            _trie.Add(message.Topic, message);
            _journal?.AppendSet(message);
        }
    }

    public bool Remove(string topic)
    {
        lock (_gate)
        {
            var removed = RemoveCore(topic);

            if (removed)
            {
                _journal?.AppendRemove(topic);
            }

            return removed;
        }
    }

    public List<RetainedMessage> Match(string filter, DateTimeOffset now)
    {
        lock (_gate)
        {
            return _trie.Search(filter)
                .Where(message => !message.IsExpired(now))
                .ToList();
        }
    }

    public List<RetainedMessage> Snapshot()
    {
        lock (_gate)
        {
            return _trie.ToList();
        }
    }

    public int PruneExpired(DateTimeOffset now)
    {
        lock (_gate)
        {
            var expired = _trie.ToList().Where(message => message.IsExpired(now)).ToList();

            foreach (var message in expired)
            {
                RemoveCore(message.Topic);
            }

            return expired.Count;
        }
    }

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
