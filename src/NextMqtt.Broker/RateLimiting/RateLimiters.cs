using System.Collections.Concurrent;
using System.Diagnostics;
using NextMqtt.Broker.Options;

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
