namespace NextMqtt.Core.Topics;

/// <summary>
/// A prefix tree over topic levels. It supports both directions:
/// lookup by topic (subscription matching) and lookup by filter (retained
/// message retrieval).
/// </summary>
/// <remarks>Instances are not thread-safe; callers provide synchronization.</remarks>
public sealed class TopicTrie<TValue>
{
    /// <summary>Root of the level tree; never null, even when empty.</summary>
    private readonly Node _root = new();
    /// <summary>Equality used when removing one specific value.</summary>
    private readonly EqualityComparer<TValue> _comparer = EqualityComparer<TValue>.Default;

    /// <summary>Number of values currently stored in the tree.</summary>
    /// <remarks>存了多少个值，空节点不计。</remarks>
    public int Count { get; private set; }

    /// <summary>Stores a value under the given filter (topic or wildcard).</summary>
    /// <remarks>同一过滤器可挂多个值，查找时按插入顺序返回。</remarks>
    /// <param name="filter">Topic name or filter whose levels form the path.</param>
    /// <param name="value">Value to attach to that path.</param>
    public void Add(string filter, TValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(filter);

        var node = _root;
        foreach (var level in MqttTopic.SplitLevels(filter))
        {
            node = node.GetOrAddChild(level);
        }

        node.Values ??= [];
        node.Values.Add(value);
        Count++;
    }

    /// <summary>Removes one value from the given filter's path.</summary>
    /// <remarks>只移除匹配到的那一个值，节点空了就剪掉，避免内存里堆积空路径。</remarks>
    /// <param name="filter">Path that was used by <see cref="Add"/>.</param>
    /// <param name="value">Value to remove.</param>
    /// <returns><c>true</c> when a value was removed.</returns>
    public bool Remove(string filter, TValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(filter);

        var levels = MqttTopic.SplitLevels(filter);
        return Remove(_root, levels, 0, value);
    }

    /// <summary>Returns every value whose filter matches <paramref name="topic"/>.</summary>
    /// <remarks>按具体主题查询；$ 开头的主题不会命中 # 与 + 的通配。</remarks>
    public List<TValue> Match(string topic)
    {
        if (string.IsNullOrEmpty(topic))
        {
            return [];
        }

        var results = new List<TValue>();
        MatchNode(_root, MqttTopic.SplitLevels(topic), 0, results, isRoot: true, topicIsDollar: topic[0] == '$');
        return results;
    }

    /// <summary>Returns every value whose topic matches <paramref name="filter"/>.</summary>
    /// <remarks>用过滤器反向查询；非法过滤器直接返回空列表。</remarks>
    public List<TValue> Search(string filter)
    {
        if (!MqttTopic.IsValidFilter(filter, out _))
        {
            return [];
        }

        var results = new List<TValue>();
        SearchNode(_root, MqttTopic.SplitLevels(filter), 0, results, isRoot: true);
        return results;
    }

    /// <summary>Returns every stored value in depth-first order.</summary>
    /// <remarks>快照，遍历期间树可以继续变化。</remarks>
    /// <returns>Snapshot list; the tree may change afterwards.</returns>
    public List<TValue> ToList()
    {
        var results = new List<TValue>(Count);
        Collect(_root, results);
        return results;
    }

    /// <summary>Drops every stored value and the nodes that held them.</summary>
    /// <remarks>根节点保留，计数归零；用于关闭或测试。</remarks>
    public void Clear()
    {
        _root.Children = null;
        _root.Values = null;
        Count = 0;
    }

    /// <summary>Walks topic levels, collecting exact, plus and hash matches.</summary>
    /// <remarks>根层跳过 `$` 系统主题，这是规范 4.7.2 的要求而不是优化。</remarks>
    private static void MatchNode(Node node, string[] levels, int index, List<TValue> results, bool isRoot, bool topicIsDollar)
    {
        if (index == levels.Length)
        {
            AddValues(node, results);

            if (!(isRoot && topicIsDollar) && node.TryGetChild("#", out var hash))
            {
                AddValues(hash, results);
            }

            return;
        }

        if (!(isRoot && topicIsDollar) && node.TryGetChild("#", out var hashNode))
        {
            AddValues(hashNode, results);
        }

        if (node.TryGetChild(levels[index], out var exact))
        {
            MatchNode(exact, levels, index + 1, results, isRoot: false, topicIsDollar);
        }

        if (!(isRoot && topicIsDollar) && node.TryGetChild("+", out var plus))
        {
            MatchNode(plus, levels, index + 1, results, isRoot: false, topicIsDollar);
        }
    }

    /// <summary>Walks a filter's levels, expanding wildcards across the tree.</summary>
    /// <remarks>保留消息反查用：过滤器含通配符时展开所有候选节点。</remarks>
    private static void SearchNode(Node node, string[] levels, int index, List<TValue> results, bool isRoot)
    {
        if (index == levels.Length)
        {
            AddValues(node, results);
            return;
        }

        var level = levels[index];

        if (level == "#")
        {
            if (isRoot)
            {
                if (node.Children is not null)
                {
                    foreach (var child in node.Children)
                    {
                        if (!child.Key.StartsWith('$'))
                        {
                            CollectIncludingSelf(child.Value, results);
                        }
                    }
                }

                AddValues(node, results);
            }
            else
            {
                CollectIncludingSelf(node, results);
            }

            return;
        }

        if (level == "+")
        {
            if (node.Children is not null)
            {
                foreach (var child in node.Children)
                {
                    if (isRoot && child.Key.StartsWith('$'))
                    {
                        continue;
                    }

                    SearchNode(child.Value, levels, index + 1, results, isRoot: false);
                }
            }

            return;
        }

        if (node.TryGetChild(level, out var exact))
        {
            SearchNode(exact, levels, index + 1, results, isRoot: false);
        }
    }

    /// <summary>Adds this node's values and everything below it.</summary>
    private static void CollectIncludingSelf(Node node, List<TValue> results)
    {
        AddValues(node, results);

        if (node.Children is null)
        {
            return;
        }

        foreach (var child in node.Children.Values)
        {
            CollectIncludingSelf(child, results);
        }
    }

    /// <summary>Adds this node's values and everything below it, used for snapshots.</summary>
    private static void Collect(Node node, List<TValue> results)
    {
        AddValues(node, results);

        if (node.Children is null)
        {
            return;
        }

        foreach (var child in node.Children.Values)
        {
            Collect(child, results);
        }
    }

    /// <summary>Copies the node's own values into the result list.</summary>
    private static void AddValues(Node node, List<TValue> results)
    {
        if (node.Values is { Count: > 0 })
        {
            results.AddRange(node.Values);
        }
    }

    /// <summary>Removes one value along the level path and prunes empty branches.</summary>
    private bool Remove(Node node, string[] levels, int index, TValue value)
    {
        if (index == levels.Length)
        {
            if (node.Values is null)
            {
                return false;
            }

            for (var i = 0; i < node.Values.Count; i++)
            {
                if (_comparer.Equals(node.Values[i], value))
                {
                    node.Values.RemoveAt(i);
                    Count--;

                    if (node.Values.Count == 0)
                    {
                        node.Values = null;
                    }

                    return true;
                }
            }

            return false;
        }

        if (!node.TryGetChild(levels[index], out var child))
        {
            return false;
        }

        var removed = Remove(child, levels, index + 1, value);

        if (removed && child.IsEmpty)
        {
            node.Children!.Remove(levels[index]);
            if (node.Children.Count == 0)
            {
                node.Children = null;
            }
        }

        return removed;
    }

    /// <summary>Classifies one trie node: children keyed by level, values at the leaf.</summary>
    /// <remarks>每个节点只管一层；值是挂在路径末端的具体对象。</remarks>
    private sealed class Node
    {
        /// <summary>Child nodes by topic level; null when the node has no children.</summary>
        /// <remarks>字典按层名索引，用 ordinal 比较保证大小写敏感。</remarks>
        public Dictionary<string, Node>? Children { get; set; }

        /// <summary>Values stored exactly at this node; null when empty.</summary>
        /// <remarks>只有在路径末端才会有值。</remarks>
        public List<TValue>? Values { get; set; }

        /// <summary>True when the node holds neither values nor children.</summary>
        /// <remarks>用于删除后剪枝。</remarks>
        public bool IsEmpty => Values is null && (Children is null || Children.Count == 0);

        /// <summary>Returns the child for a level, creating it on first use.</summary>
        public Node GetOrAddChild(string level)
        {
            Children ??= new Dictionary<string, Node>(StringComparer.Ordinal);

            if (!Children.TryGetValue(level, out var child))
            {
                child = new Node();
                Children[level] = child;
            }

            return child;
        }

        /// <summary>Looks up a child without allocating a new node.</summary>
        public bool TryGetChild(string level, out Node child)
        {
            child = null!;

            if (Children is null)
            {
                return false;
            }

            return Children.TryGetValue(level, out child!);
        }
    }
}
