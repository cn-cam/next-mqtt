using System.Collections.Concurrent;
using System.Diagnostics;
using NextMqtt.Broker.Options;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker.RateLimiting;

/// <summary>
/// Lock-protected token bucket. Tokens refill continuously at a fixed rate up to
/// a burst capacity, so a short spike is allowed while the long-term rate holds.
/// </summary>
public sealed class TokenBucket
{
    private readonly double _capacity;
    private readonly double _refillPerSecond;
    private readonly object _gate = new();
    private double _tokens;
    private long _lastTimestamp = Stopwatch.GetTimestamp();

    public TokenBucket(int refillPerSecond, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refillPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _refillPerSecond = refillPerSecond;
        _capacity = capacity;
        _tokens = capacity;
    }

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
public sealed class TopicQuotaLimiter
{
    private readonly List<(TopicFilter Filter, TokenBucket Bucket)> _rules = [];
    private readonly object _gate = new();

    public TopicQuotaLimiter(RateLimitOptions options)
    {
        foreach (var rule in options.Topics)
        {
            if (rule.PublishPerSecond <= 0 || !MqttTopic.IsValidFilter(rule.Filter, out _))
            {
                continue;
            }

            var burst = rule.Burst > 0 ? rule.Burst : rule.PublishPerSecond;
            _rules.Add((TopicFilter.Parse(rule.Filter), new TokenBucket(rule.PublishPerSecond, burst)));
        }
    }

    public bool Enabled => _rules.Count > 0;

    public int RuleCount => _rules.Count;

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
public sealed class ConnectionRateLimiter
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new(StringComparer.Ordinal);
    private readonly int _rate;
    private readonly int _burst;
    private long _lastSweep = Stopwatch.GetTimestamp();

    public ConnectionRateLimiter(RateLimitOptions options)
    {
        _rate = options.ConnectPerSecondPerIp;
        _burst = options.ConnectBurst > 0 ? options.ConnectBurst : Math.Max(1, options.ConnectPerSecondPerIp);
    }

    public bool Enabled => _rate > 0;

    public bool TryAcquire(string remoteAddress)
    {
        if (!Enabled)
        {
            return true;
        }

        SweepIfDue();

        var bucket = _buckets.GetOrAdd(Normalize(remoteAddress), _ => new TokenBucket(_rate, _burst));
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
