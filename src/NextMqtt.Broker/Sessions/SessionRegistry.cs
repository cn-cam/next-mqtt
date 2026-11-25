using System.Collections.Concurrent;

namespace NextMqtt.Broker.Sessions;

/// <summary>Thread-safe registry of sessions keyed by client identifier.</summary>
/// <remarks>会话对象自身负责字段级同步；注册表只保证查找与替换的原子性。</remarks>
public sealed class SessionRegistry
{
    /// <summary>Sessions by client identifier, ordinal comparison.</summary>
    /// <remarks>按客户端 ID 索引，区分大小写，避免 A 与 a 互相覆盖。</remarks>
    private readonly ConcurrentDictionary<string, BrokerSession> _sessions = new(StringComparer.Ordinal);

    /// <summary>Number of sessions, online and offline.</summary>
    /// <remarks>在线与离线都算；后台“会话数”用这个值。</remarks>
    public int Count => _sessions.Count;

    /// <summary>Looks up a session without allocating.</summary>
    /// <remarks>热路径使用，返回 false 时不分配对象。</remarks>
    /// <param name="clientId">Client identifier.</param>
    /// <param name="session">Session when found.</param>
    /// <returns><c>true</c> when the session exists.</returns>
    public bool TryGet(string clientId, out BrokerSession session)
        => _sessions.TryGetValue(clientId, out session!);

    /// <summary>Returns the session or null.</summary>
    /// <remarks>调用方必须处理 null，通常表示会话已过期。</remarks>
    /// <param name="clientId">Client identifier.</param>
    public BrokerSession? Get(string clientId)
        => _sessions.TryGetValue(clientId, out var session) ? session : null;

    /// <summary>Registers a new session; fails when the identifier is taken.</summary>
    /// <remarks>失败说明并发建连，调用方应改为接管旧会话。</remarks>
    /// <param name="session">Session to add.</param>
    /// <returns><c>true</c> when the session was added.</returns>
    public bool TryAdd(BrokerSession session) => _sessions.TryAdd(session.ClientId, session);

    /// <summary>Removes a session without returning it.</summary>
    /// <remarks>用于过期清理，不需要再访问会话对象。</remarks>
    /// <param name="clientId">Client identifier.</param>
    /// <returns><c>true</c> when a session was removed.</returns>
    public bool Remove(string clientId) => _sessions.TryRemove(clientId, out _);

    /// <summary>Removes a session and returns it so the caller can dispose it.</summary>
    /// <remarks>先摘除再释放，避免释放过程中又被查到。</remarks>
    /// <param name="clientId">Client identifier.</param>
    /// <param name="session">Removed session, when present.</param>
    /// <returns><c>true</c> when a session was removed.</returns>
    public bool TryRemove(string clientId, out BrokerSession? session)
    {
        var removed = _sessions.TryRemove(clientId, out var value);
        session = value;
        return removed;
    }

    /// <summary>Takes a point-in-time list of every session.</summary>
    /// <remarks>快照之后的新增/删除不影响这次遍历。</remarks>
    /// <returns>Snapshot list; later changes are not reflected.</returns>
    public IReadOnlyList<BrokerSession> Snapshot() => _sessions.Values.ToList();

    /// <summary>Counts sessions that currently have a connection.</summary>
    /// <remarks>扫描全部会话得出在线数，接口频率低可以接受。</remarks>
    public int ConnectedCount() => _sessions.Values.Count(session => session.IsOnline);

    /// <summary>Drops every session without disposing it; used on shutdown.</summary>
    /// <remarks>关闭时由调用方统一释放，避免重复释放。</remarks>
    public void Clear() => _sessions.Clear();
}
