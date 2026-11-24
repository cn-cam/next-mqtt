namespace NextMqtt.Core.Topics;

/// <summary>
/// A prefix tree over topic levels. It supports both directions:
/// lookup by topic (subscription matching) and lookup by filter (retained
/// message retrieval).
/// </summary>
/// <remarks>Instances are not thread-safe; callers provide synchronization.</remarks>
public sealed class TopicTrie<TValue>
{
    private readonly Node _root = new();
    private readonly EqualityComparer<TValue> _comparer = EqualityComparer<TValue>.Default;

    public int Count { get; private set; }

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

    public bool Remove(string filter, TValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(filter);

        var levels = MqttTopic.SplitLevels(filter);
        return Remove(_root, levels, 0, value);
    }

    /// <summary>Returns every value whose filter matches <paramref name="topic"/>.</summary>
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

    public List<TValue> ToList()
    {
        var results = new List<TValue>(Count);
        Collect(_root, results);
        return results;
    }

    public void Clear()
    {
        _root.Children = null;
        _root.Values = null;
        Count = 0;
    }

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

    private static void AddValues(Node node, List<TValue> results)
    {
        if (node.Values is { Count: > 0 })
        {
            results.AddRange(node.Values);
        }
    }

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

    private sealed class Node
    {
        public Dictionary<string, Node>? Children { get; set; }

        public List<TValue>? Values { get; set; }

        public bool IsEmpty => Values is null && (Children is null || Children.Count == 0);

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
