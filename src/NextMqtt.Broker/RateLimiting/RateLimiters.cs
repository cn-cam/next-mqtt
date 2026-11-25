using System.Collections.Concurrent;
using System.Diagnostics;
using NextMqtt.Broker.Options;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker.RateLimiting;

/// <summary>
/// Implemented by objects that hold token buckets and must rescale them when
/// cluster membership changes.
/// </summary>
/// <remarks>集群成员变化时按新的节点数重新分摊配额。</remarks>
public interface IRateScalable
{
    /// <summary>Recomputes the local share for the given number of live nodes.</summary>
    /// <param name="nodes">Live node count; values below one are treated as one.</param>
    void SetClusterSize(int nodes);
}

/// <summary>
/// Lock-protected token bucket. Tokens refill continuously at a fixed rate up to
/// a burst capacity, so a short spike is allowed while the long-term rate holds.
/// </summary>
public sealed class TokenBucket
{
    private double _capacity;
    private double _refillPerSecond;
    private readonly object _gate = new();
    private double _tokens;
    private long _lastTimestamp = Stopwatch.GetTimestamp();

    /// <summary>Creates a full bucket.</summary>
    /// <param name="refillPerSecond">Sustained rate; must be positive.</param>
    /// <param name="capacity">Burst size; must be positive.</param>
    public TokenBucket(int refillPerSecond, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refillPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _refillPerSecond = refillPerSecond;
        _capacity = capacity;
        _tokens = capacity;
    }

    /// <summary>Current burst capacity after any cluster rescaling.</summary>
    public double Capacity => _capacity;

    /// <summary>Consumes one token when available.</summary>
    public bool TryTake()
    {
        lock (_gate)
        {
            Refill();

            if (_tokens < 1)
            {
                return false;
            }

            _tokens -= 1;
            return true;
        }
    }

    /// <summary>
    /// Rescales the bucket, keeping the currently available tokens but clamping
    /// them to the new capacity.
    /// </summary>
    /// <remarks>用于集群扩缩容：按节点数分摊配额后重新设置速率与容量。</remarks>
    public void Reconfigure(double refillPerSecond, double capacity)
    {
        if (refillPerSecond <= 0 || capacity <= 0)
        {
            return;
        }

        lock (_gate)
        {
            Refill();
            _refillPerSecond = refillPerSecond;
            _capacity = capacity;
            _tokens = Math.Min(_tokens, capacity);
        }
    }

    /// <summary>True when the bucket has fully refilled, so it can be evicted.</summary>
    public bool IsFull
    {
        get
        {
            lock (_gate)
            {
                Refill();
                return _tokens >= _capacity - 0.001;
            }
        }
    }

    private void Refill()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - _lastTimestamp) / (double)Stopwatch.Frequency;

        if (elapsed <= 0)
        {
            return;
        }

        _lastTimestamp = now;
        _tokens = Math.Min(_capacity, _tokens + (elapsed * _refillPerSecond));
    }
}

/// <summary>
/// Global publish quota per topic filter. One bucket per rule, shared by every
/// client, so a topic cannot be flooded by opening more connections.
/// </summary>
public sealed class TopicQuotaLimiter : IRateScalable
{
    private readonly List<(TopicFilter Filter, TokenBucket Bucket, int Rate, int Burst)> _rules = [];
    private readonly object _gate = new();
    private int _clusterSize = 1;

    /// <summary>Builds one shared bucket per configured topic rule.</summary>
    /// <remarks>无效或速率为 0 的规则会被忽略，便于配置中临时关闭某条配额。</remarks>
    public TopicQuotaLimiter(RateLimitOptions options)
    {
        foreach (var rule in options.Topics)
        {
            if (rule.PublishPerSecond <= 0 || !MqttTopic.IsValidFilter(rule.Filter, out _))
            {
                continue;
            }

            var burst = rule.Burst > 0 ? rule.Burst : rule.PublishPerSecond;
            _rules.Add((TopicFilter.Parse(rule.Filter), new TokenBucket(rule.PublishPerSecond, burst), rule.PublishPerSecond, burst));
        }
    }

    /// <summary>True when at least one valid topic rule was configured.</summary>
    public bool Enabled => _rules.Count > 0;

    /// <summary>Number of active topic rules.</summary>
    public int RuleCount => _rules.Count;

    /// <summary>
    /// Splits each rule's budget across the live cluster so the aggregate rate
    /// stays at the configured value.
    /// </summary>
    /// <remarks>按存活节点数均分配额；成员变化时近似生效，不做全局精确计数。</remarks>
    public void SetClusterSize(int nodes)
    {
        var size = Math.Max(1, nodes);

        lock (_gate)
        {
            if (size == _clusterSize)
            {
                return;
            }

            _clusterSize = size;

            foreach (var rule in _rules)
            {
                rule.Bucket.Reconfigure(
                    Math.Max(1.0, (double)rule.Rate / size),
                    Math.Max(1.0, (double)rule.Burst / size));
            }
        }
    }

    /// <summary>Consumes quota for every matching rule; false when any is exhausted.</summary>
    public bool TryAcquire(string topic)
    {
        if (_rules.Count == 0)
        {
            return true;
        }

        lock (_gate)
        {
            foreach (var rule in _rules)
            {
                if (rule.Filter.Matches(topic) && !rule.Bucket.TryTake())
                {
                    return false;
                }
            }

            return true;
        }
    }
}

/// <summary>
/// Per-remote-address connection limiter. Buckets for addresses that have fully
/// refilled are swept periodically so the table stays bounded by active peers.
/// </summary>
public sealed class ConnectionRateLimiter : IRateScalable
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new(StringComparer.Ordinal);
    private readonly int _rate;
    private readonly int _burst;
    private long _lastSweep = Stopwatch.GetTimestamp();
    private int _clusterSize = 1;

    /// <summary>Creates the limiter from configuration; rate 0 disables it.</summary>
    /// <remarks>令牌桶按来源地址创建，容量取 burst（缺省为每秒速率）。</remarks>
    public ConnectionRateLimiter(RateLimitOptions options)
    {
        _rate = options.ConnectPerSecondPerIp;
        _burst = options.ConnectBurst > 0 ? options.ConnectBurst : Math.Max(1, options.ConnectPerSecondPerIp);
    }

    /// <summary>True when a per-address CONNECT rate was configured.</summary>
    public bool Enabled => _rate > 0;

    /// <summary>
    /// Divides the per-address budget across live nodes so a cluster does not
    /// multiply the configured limit by its size.
    /// </summary>
    /// <remarks>成员变化后每个节点按新份额重新计算，聚合速率约等于配置值。</remarks>
    public void SetClusterSize(int nodes)
    {
        if (!Enabled)
        {
            return;
        }

        var size = Math.Max(1, nodes);

        if (size == _clusterSize)
        {
            return;
        }

        _clusterSize = size;

        foreach (var bucket in _buckets.Values)
        {
            bucket.Reconfigure(EffectiveRate, EffectiveBurst);
        }
    }

    /// <summary>Effective refill rate per node after cluster scaling.</summary>
    private double EffectiveRate => Math.Max(1.0, (double)_rate / _clusterSize);

    /// <summary>Effective burst per node after cluster scaling.</summary>
    private double EffectiveBurst => Math.Max(1.0, (double)_burst / _clusterSize);

    /// <summary>
    /// Takes a token for the address, treating IPv4/IPv6 ports as one client.
    /// </summary>
    /// <remarks>返回 false 表示超出连接速率，调用方应以 0x9F 拒绝并关闭连接。</remarks>
    public bool TryAcquire(string remoteAddress)
    {
        if (!Enabled)
        {
            return true;
        }

        SweepIfDue();

        var bucket = _buckets.GetOrAdd(Normalize(remoteAddress), _ => new TokenBucket(
            (int)Math.Ceiling(EffectiveRate),
            (int)Math.Ceiling(EffectiveBurst)));
        return bucket.TryTake();
    }

    /// <summary>Number of tracked addresses; exposed for tests and metrics.</summary>
    public int TrackedAddresses => _buckets.Count;

    /// <summary>Strips an optional port from an IPv4 or bracketed IPv6 address.</summary>
    public static string Normalize(string remoteAddress)
    {
        if (string.IsNullOrEmpty(remoteAddress))
        {
            return "unknown";
        }

        // "[::1]:5678" and "127.0.0.1:5678" both collapse to the bare address.
        if (remoteAddress[0] == '[')
        {
            var end = remoteAddress.IndexOf(']');
            return end > 0 ? remoteAddress[1..end] : remoteAddress;
        }

        var first = remoteAddress.IndexOf(':');
        if (first > 0 && first == remoteAddress.LastIndexOf(':'))
        {
            return remoteAddress[..first];
        }

        return remoteAddress;
    }

    private void SweepIfDue()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = TimeSpan.FromSeconds((now - _lastSweep) / (double)Stopwatch.Frequency);

        if (elapsed < SweepInterval)
        {
            return;
        }

        _lastSweep = now;

        foreach (var entry in _buckets)
        {
            if (entry.Value.IsFull)
            {
                _buckets.TryRemove(entry.Key, out _);
            }
        }
    }
}
