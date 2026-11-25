using Microsoft.Extensions.Logging;

namespace NextMqtt.Server.Diagnostics;

/// <summary>One captured log line.</summary>
/// <remarks>字段与 <c>GET /api/v1/logs</c> 的返回结构一一对应。</remarks>
/// <param name="Timestamp">When the entry was written, UTC.</param>
/// <param name="Level">Trace, Debug, Information, Warning, Error or Critical.</param>
/// <param name="Category">Logger category, usually the full type name.</param>
/// <param name="Message">Formatted message.</param>
/// <param name="Exception">Exception text, when one was attached.</param>
public sealed record LogEntry(
    DateTimeOffset Timestamp,
    string Level,
    string Category,
    string Message,
    string? Exception);

/// <summary>Bounded in-memory copy of recent log entries for the dashboard.</summary>
/// <remarks>只保留最后一圈；不落盘、不影响控制台输出，进程重启即清空。</remarks>
public sealed class LogBuffer
{
    /// <summary>Ring storage; the newest entry replaces the oldest.</summary>
    private readonly LogEntry[] _entries;

    /// <summary>Guards the ring position and the slot being written.</summary>
    private readonly object _gate = new();

    /// <summary>Slot the next entry will overwrite.</summary>
    private int _next;

    /// <summary>Entries written since start, including evicted ones.</summary>
    private long _total;

    /// <summary>Creates a buffer that keeps the given number of entries.</summary>
    /// <remarks>容量只影响内存占用；查询始终返回最新的若干条。</remarks>
    /// <param name="capacity">Ring size; at least one.</param>
    public LogBuffer(int capacity = 2000)
    {
        _entries = new LogEntry[Math.Max(1, capacity)];
    }

    /// <summary>Number of entries that fit in the ring.</summary>
    /// <remarks>写入超过这个数量后最旧的条目会被覆盖。</remarks>
    public int Capacity => _entries.Length;

    /// <summary>Total entries written since start, including evicted ones.</summary>
    /// <remarks>用它可以判断中间是否丢过日志。</remarks>
    public long Total => Interlocked.Read(ref _total);

    /// <summary>Records one entry, overwriting the oldest when full.</summary>
    /// <remarks>只加锁拷贝引用，不做格式化，避免拖慢业务线程。</remarks>
    /// <param name="entry">Entry to keep.</param>
    public void Add(LogEntry entry)
    {
        lock (_gate)
        {
            _entries[_next] = entry;
            _next = (_next + 1) % _entries.Length;
        }

        Interlocked.Increment(ref _total);
    }

    /// <summary>Copies matching entries, newest last.</summary>
    /// <remarks>先按时间倒序扫描再反转，命中数到达上限就停止，避免整表排序。</remarks>
    /// <param name="minimumLevel">Lowest level to include, or null for everything.</param>
    /// <param name="search">Case-insensitive substring filter over category and message.</param>
    /// <param name="limit">Maximum entries to return.</param>
    /// <returns>Oldest to newest, capped at <paramref name="limit"/>.</returns>
    public IReadOnlyList<LogEntry> Snapshot(string? minimumLevel, string? search, int limit)
    {
        var floor = ParseLevel(minimumLevel);
        var take = Math.Clamp(limit, 1, _entries.Length);
        var matches = new List<LogEntry>(take);

        lock (_gate)
        {
            for (var offset = 0; offset < _entries.Length; offset++)
            {
                var index = (_next - 1 - offset + _entries.Length) % _entries.Length;
                var entry = _entries[index];

                if (entry is null)
                {
                    continue;
                }

                if (floor is not null && Rank(entry.Level) < Rank(floor))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(search)
                    && entry.Category.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                    && entry.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                matches.Add(entry);

                if (matches.Count >= take)
                {
                    break;
                }
            }
        }

        matches.Reverse();
        return matches;
    }

    /// <summary>Maps a level name to its canonical form, or null when unrecognised.</summary>
    /// <remarks>接受 info、warn、fatal 等常见简写；无法识别时按“不过滤”处理。</remarks>
    /// <param name="level">Configured level name.</param>
    public static string? ParseLevel(string? level)
        => level?.Trim().ToLowerInvariant() switch
        {
            "trace" => "Trace",
            "debug" => "Debug",
            "information" or "info" => "Information",
            "warning" or "warn" => "Warning",
            "error" => "Error",
            "critical" or "fatal" => "Critical",
            null or "" => null,
            _ => null,
        };

    /// <summary>Orders levels so a minimum filter can be applied.</summary>
    /// <remarks>未知级别按 Information 处理，避免因为拼写错误把错误日志过滤掉。</remarks>
    /// <param name="level">Canonical level name.</param>
    public static int Rank(string level)
        => level switch
        {
            "Trace" => 0,
            "Debug" => 1,
            "Information" => 2,
            "Warning" => 3,
            "Error" => 4,
            "Critical" => 5,
            _ => 2,
        };
}

/// <summary>Pipes the application's own logging into a <see cref="LogBuffer"/>.</summary>
/// <remarks>只做观测：写缓冲区不会失败，也不会递归产生新的日志。</remarks>
public sealed class LogBufferProvider : ILoggerProvider
{
    /// <summary>Buffer shared by every logger.</summary>
    private readonly LogBuffer _buffer;

    /// <summary>Creates the provider.</summary>
    /// <remarks>缓冲区由调用方持有，provider 不负责清理。</remarks>
    /// <param name="buffer">Buffer that receives the entries.</param>
    public LogBufferProvider(LogBuffer buffer)
    {
        _buffer = buffer;
    }

    /// <summary>Creates a logger that also feeds the buffer.</summary>
    /// <remarks>类别名原样保存，面板据此按组件过滤。</remarks>
    /// <param name="categoryName">Logger category.</param>
    public ILogger CreateLogger(string categoryName) => new BufferedLogger(categoryName, _buffer);

    /// <summary>Releases nothing; the buffer belongs to the host.</summary>
    /// <remarks>日志基础设施通常在进程结束时统一释放，这里刻意留空。</remarks>
    public void Dispose()
    {
    }

    /// <summary>Logger that formats a message and copies it into the buffer.</summary>
    private sealed class BufferedLogger : ILogger
    {
        /// <summary>Category reported on every entry.</summary>
        private readonly string _category;

        /// <summary>Shared ring buffer.</summary>
        private readonly LogBuffer _buffer;

        /// <summary>Creates the logger for one category.</summary>
        /// <param name="category">Logger category.</param>
        /// <param name="buffer">Buffer that receives the entries.</param>
        public BufferedLogger(string category, LogBuffer buffer)
        {
            _category = category;
            _buffer = buffer;
        }

        /// <summary>Scopes are not captured; the dashboard shows flat entries.</summary>
        /// <typeparam name="TState">Scope state type.</typeparam>
        /// <param name="state">Scope value.</param>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <summary>Everything except None is captured.</summary>
        /// <param name="logLevel">Level being written.</param>
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        /// <summary>Formats and stores one entry; never throws.</summary>
        /// <remarks>格式化失败时退回 state 的文本，保证缓冲区始终有内容。</remarks>
        /// <typeparam name="TState">Log state type.</typeparam>
        /// <param name="logLevel">Level being written.</param>
        /// <param name="eventId">Event identifier.</param>
        /// <param name="state">Log state.</param>
        /// <param name="exception">Attached exception, if any.</param>
        /// <param name="formatter">Formatter supplied by the caller.</param>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message;

            try
            {
                message = formatter(state, exception);
            }
            catch (Exception)
            {
                message = state?.ToString() ?? string.Empty;
            }

            _buffer.Add(new LogEntry(
                DateTimeOffset.UtcNow,
                logLevel.ToString(),
                _category,
                message,
                exception?.ToString()));
        }
    }
}
