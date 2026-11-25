using NextMqtt.Core.Protocol;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker.Subscriptions;

/// <summary>A subscription owned by a session.</summary>
public sealed class Subscription
{
    /// <summary>Subscriber that owns the filter.</summary>
    public required string ClientId { get; init; }

    /// <summary>Parsed filter, including a shared-subscription prefix when present.</summary>
    public required TopicFilter Filter { get; init; }

    /// <summary>Granted QoS and MQTT 5 subscription options.</summary>
    public required MqttSubscriptionOptions Options { get; init; }

    /// <summary>MQTT 5 subscription identifier, when supplied by the client.</summary>
    /// <summary>Subscription identifier echoed on deliveries, when supplied.</summary>
    public int? Identifier { get; init; }

    /// <summary>Filter text without the shared-subscription prefix.</summary>
    public string EffectiveFilter => Filter.Filter;

    /// <summary>Stable key combining client id and the original filter text.</summary>
    public string Key => $"{ClientId}\0{Filter.Original}";
}

/// <summary>A subscription selected for a topic, optionally from a shared group.</summary>
/// <summary>A subscription that matched a topic, with its shared group.</summary>
/// <param name="Subscription">Matched subscription.</param>
/// <param name="SharedGroup">Group name when the match came from a shared filter.</param>
public readonly record struct SubscriptionMatch(Subscription Subscription, string? SharedGroup);

/// <summary>
/// Cluster-wide (per-node) subscription index. Normal subscriptions are
/// stored in a topic trie; shared subscriptions additionally round-robin
/// within each group.
/// </summary>
public sealed class SubscriptionIndex
{
    private readonly object _gate = new();
    private readonly TopicTrie<Subscription> _normal = new();
    private readonly TopicTrie<SharedSubscriptionGroup> _sharedTrie = new();
    private readonly Dictionary<string, SharedSubscriptionGroup> _sharedGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, Subscription>> _byClient = new(StringComparer.Ordinal);

    /// <summary>Total subscriptions, normal and shared.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _normal.Count + _sharedGroups.Values.Sum(group => group.Count);
            }
        }
    }

    /// <summary>Number of clients that hold at least one subscription.</summary>
    public int ClientCount
    {
        get
        {
            lock (_gate)
            {
                return _byClient.Count;
            }
        }
    }

    /// <summary>Adds a subscription, replacing an existing one with the same key.</summary>
    public void AddOrReplace(Subscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        lock (_gate)
        {
            RemoveCore(subscription.ClientId, subscription.Filter.Original);

            if (!_byClient.TryGetValue(subscription.ClientId, out var filters))
            {
                filters = new Dictionary<string, Subscription>(StringComparer.Ordinal);
                _byClient[subscription.ClientId] = filters;
            }

            filters[subscription.Filter.Original] = subscription;

            if (subscription.Filter.SharedGroup is { } group)
            {
                var key = SharedKey(group, subscription.EffectiveFilter);

                if (!_sharedGroups.TryGetValue(key, out var sharedGroup))
                {
                    sharedGroup = new SharedSubscriptionGroup(group, subscription.EffectiveFilter);
                    _sharedGroups[key] = sharedGroup;
                    _sharedTrie.Add(subscription.EffectiveFilter, sharedGroup);
                }

                sharedGroup.Add(subscription);
            }
            else
            {
                _normal.Add(subscription.EffectiveFilter, subscription);
            }
        }
    }

    /// <summary>Removes one subscription by client and original filter text.</summary>
    /// <param name="clientId">Subscriber.</param>
    /// <param name="originalFilter">Filter exactly as subscribed.</param>
    public bool Remove(string clientId, string originalFilter)
    {
        lock (_gate)
        {
            return RemoveCore(clientId, originalFilter);
        }
    }

    /// <summary>Removes every subscription of a client.</summary>
    /// <param name="clientId">Subscriber.</param>
    /// <returns>Removed subscriptions.</returns>
    public List<Subscription> RemoveAll(string clientId)
    {
        lock (_gate)
        {
            if (!_byClient.TryGetValue(clientId, out var filters))
            {
                return [];
            }

            var removed = filters.Values.ToList();
            _byClient.Remove(clientId);

            foreach (var subscription in removed)
            {
                if (subscription.Filter.SharedGroup is { } group)
                {
                    RemoveShared(subscription, group);
                }
                else
                {
                    _normal.Remove(subscription.EffectiveFilter, subscription);
                }
            }

            return removed;
        }
    }

    /// <summary>Finds subscriptions matching one topic, shared groups included.</summary>
    /// <param name="topic">Concrete topic name.</param>
    public List<SubscriptionMatch> Match(string topic)
    {
        lock (_gate)
        {
            var results = new List<SubscriptionMatch>();

            foreach (var subscription in _normal.Match(topic))
            {
                results.Add(new SubscriptionMatch(subscription, null));
            }

            foreach (var group in _sharedTrie.Match(topic))
            {
                var member = group.Pick();
                if (member is not null)
                {
                    results.Add(new SubscriptionMatch(member, group.Group));
                }
            }

            return results;
        }
    }

    /// <summary>Lists one client's subscriptions.</summary>
    /// <param name="clientId">Subscriber.</param>
    public List<Subscription> GetByClient(string clientId)
    {
        lock (_gate)
        {
            return _byClient.TryGetValue(clientId, out var filters)
                ? filters.Values.ToList()
                : [];
        }
    }

    /// <summary>Copies every subscription, for heartbeat and admin views.</summary>
    public List<Subscription> GetAll()
    {
        lock (_gate)
        {
            var results = _normal.ToList();

            foreach (var group in _sharedGroups.Values)
            {
                results.AddRange(group.Members);
            }

            return results;
        }
    }

    /// <summary>Drops every subscription, for example on shutdown.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _normal.Clear();
            _sharedTrie.Clear();
            _sharedGroups.Clear();
            _byClient.Clear();
        }
    }

    private bool RemoveCore(string clientId, string originalFilter)
    {
        if (!_byClient.TryGetValue(clientId, out var filters)
            || !filters.Remove(originalFilter, out var subscription))
        {
            return false;
        }

        if (filters.Count == 0)
        {
            _byClient.Remove(clientId);
        }

        if (subscription.Filter.SharedGroup is { } group)
        {
            RemoveShared(subscription, group);
        }
        else
        {
            _normal.Remove(subscription.EffectiveFilter, subscription);
        }

        return true;
    }

    private void RemoveShared(Subscription subscription, string group)
    {
        var key = SharedKey(group, subscription.EffectiveFilter);

        if (!_sharedGroups.TryGetValue(key, out var sharedGroup))
        {
            return;
        }

        sharedGroup.Remove(subscription);

        if (sharedGroup.Count == 0)
        {
            _sharedGroups.Remove(key);
            _sharedTrie.Remove(subscription.EffectiveFilter, sharedGroup);
        }
    }

    private static string SharedKey(string group, string filter) => $"{group}\0{filter}";
}

/// <summary>Round-robin membership for one shared subscription group.</summary>
internal sealed class SharedSubscriptionGroup
{
    private readonly List<Subscription> _members = [];
    private int _next;

    public SharedSubscriptionGroup(string group, string filter)
    {
        Group = group;
        Filter = filter;
    }

    public string Group { get; }

    public string Filter { get; }

    /// <summary>Total subscriptions, normal and shared.</summary>
    public int Count => _members.Count;

    public IReadOnlyList<Subscription> Members => _members;

    public void Add(Subscription subscription) => _members.Add(subscription);

    public void Remove(Subscription subscription)
    {
        _members.Remove(subscription);
        if (_next >= _members.Count && _members.Count > 0)
        {
            _next = 0;
        }
    }

    public Subscription? Pick()
    {
        if (_members.Count == 0)
        {
            return null;
        }

        var index = _next++ % _members.Count;
        return _members[index];
    }
}
