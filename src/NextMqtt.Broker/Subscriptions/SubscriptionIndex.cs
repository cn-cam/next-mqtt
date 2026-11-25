using NextMqtt.Core.Protocol;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker.Subscriptions;

/// <summary>A subscription owned by a session.</summary>
/// <remarks>记录归属、过滤器与授予选项，路由时直接用它决定下发 QoS。</remarks>
public sealed class Subscription
{
    /// <summary>Subscriber that owns the filter.</summary>
    /// <remarks>客户端 ID；断开后订阅是否保留取决于会话是否持久。</remarks>
    public required string ClientId { get; init; }

    /// <summary>Parsed filter, including a shared-subscription prefix when present.</summary>
    /// <remarks>解析一次存起来，匹配时不再反复拆字符串。</remarks>
    public required TopicFilter Filter { get; init; }

    /// <summary>Granted QoS and MQTT 5 subscription options.</summary>
    /// <remarks>实际下发的 QoS 取发布值与授予值的较小者。</remarks>
    public required MqttSubscriptionOptions Options { get; init; }

    /// <summary>MQTT 5 subscription identifier echoed on deliveries, when supplied.</summary>
    /// <remarks>原样带回下发的 PUBLISH；3.1.1 客户端没有这一项。</remarks>
    public int? Identifier { get; init; }

    /// <summary>Filter text without the shared-subscription prefix.</summary>
    /// <remarks>存进 trie 的是去掉 $share/组名 之后的部分。</remarks>
    public string EffectiveFilter => Filter.Filter;

    /// <summary>Stable key combining client id and the original filter text.</summary>
    /// <remarks>同一客户端重复订阅同一过滤器视为替换，不会留两份。</remarks>
    public string Key => $"{ClientId}\0{Filter.Original}";
}

/// <summary>A subscription that matched a topic, with its shared group.</summary>
/// <remarks>同一客户端只回一条，即使多个过滤器都命中；组名仅在共享订阅命中时非空。</remarks>
/// <param name="Subscription">Matched subscription.</param>
/// <param name="SharedGroup">Group name when the match came from a shared filter.</param>
public readonly record struct SubscriptionMatch(Subscription Subscription, string? SharedGroup);

/// <summary>
/// Cluster-wide (per-node) subscription index. Normal subscriptions are
/// stored in a topic trie; shared subscriptions additionally round-robin
/// within each group.
/// </summary>
/// <remarks>
/// 每个节点只索引本地订阅，跨节点靠心跳交换列表，因此“集群索引”是逻辑概念，
/// 没有共享存储：路由时把本地命中与远端成员的心跳视图合并。普通订阅走 trie
/// 做通配匹配；共享订阅另存一份分组表，投递前按组内轮转挑一个成员。
/// </remarks>
public sealed class SubscriptionIndex
{
    /// <summary>Guards every dictionary and both tries.</summary>
    private readonly object _gate = new();
    /// <summary>Non-shared subscriptions, indexed by effective filter.</summary>
    private readonly TopicTrie<Subscription> _normal = new();
    /// <summary>Shared groups, indexed by filter so routing finds them by topic.</summary>
    private readonly TopicTrie<SharedSubscriptionGroup> _sharedTrie = new();
    /// <summary>Groups keyed by group name plus filter, for removal.</summary>
    private readonly Dictionary<string, SharedSubscriptionGroup> _sharedGroups = new(StringComparer.Ordinal);
    /// <summary>Per-client subscriptions keyed by original filter text.</summary>
    private readonly Dictionary<string, Dictionary<string, Subscription>> _byClient = new(StringComparer.Ordinal);

    /// <summary>Total subscriptions, normal and shared.</summary>
    /// <remarks>普通订阅与共享订阅都计入，直接返回计数器，不走 trie。</remarks>
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
    /// <remarks>扫描归属表得出，管理接口调用频率低，可以接受。</remarks>
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
    /// <remarks>重复订阅只更新授予选项与订阅标识，不新增条目。</remarks>
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
    /// <remarks>共享订阅也走这个入口，前缀参与匹配。</remarks>
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
    /// <remarks>会话过期或 cleanStart 时调用，逐个从 trie 摘除。</remarks>
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
    /// <remarks>普通订阅全部返回，共享组每组只挑一个成员。</remarks>
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
    /// <remarks>后台详情页用，返回只读快照，不影响索引本身。</remarks>
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
    /// <remarks>心跳要带完整列表，所以这里返回深拷贝。</remarks>
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
    /// <remarks>关闭时不逐个从 trie 删除，直接丢弃整个索引更快。</remarks>
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

    /// <summary>Removes one subscription from both the client map and the matching index.</summary>
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

    /// <summary>Drops a member from its group and removes the group when it empties.</summary>
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

    /// <summary>Builds an unambiguous group key with a NUL separator.</summary>
    private static string SharedKey(string group, string filter) => $"{group}\0{filter}";
}

/// <summary>Round-robin membership for one shared subscription group.</summary>
/// <remarks>组内按下标轮转，保证同组成员被均匀使用。</remarks>
internal sealed class SharedSubscriptionGroup
{
    /// <summary>Subscriptions in this group, in join order.</summary>
    private readonly List<Subscription> _members = [];
    /// <summary>Index of the next member to pick.</summary>
    private int _next;

    /// <summary>Creates an empty group for one shared filter.</summary>
    public SharedSubscriptionGroup(string group, string filter)
    {
        Group = group;
        Filter = filter;
    }

    /// <summary>Group name from the $share prefix.</summary>
    public string Group { get; }

    /// <summary>Effective filter shared by every member.</summary>
    public string Filter { get; }

    /// <summary>Number of members currently in this group.</summary>
    /// <remarks>为 0 时组会从索引里移除；轮转下标随之重置。</remarks>
    public int Count => _members.Count;

    /// <summary>Members in join order, exposed for admin views.</summary>
    public IReadOnlyList<Subscription> Members => _members;

    /// <summary>Appends a member; duplicates are prevented by the caller.</summary>
    public void Add(Subscription subscription) => _members.Add(subscription);

    /// <summary>Removes a member and resets the cursor when it fell off the end.</summary>
    public void Remove(Subscription subscription)
    {
        _members.Remove(subscription);
        if (_next >= _members.Count && _members.Count > 0)
        {
            _next = 0;
        }
    }

    /// <summary>Returns the next member in rotation, or null when the group is empty.</summary>
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
