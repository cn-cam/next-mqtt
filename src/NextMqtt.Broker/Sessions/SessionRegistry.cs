using System.Collections.Concurrent;

namespace NextMqtt.Broker.Sessions;

/// <summary>Thread-safe registry of sessions keyed by client identifier.</summary>
/// <remarks>会话对象自身负责字段级同步；注册表只保证查找与替换的原子性。</remarks>
public sealed class SessionRegistry
{
    /// <summary>Sessions by client identifier, ordinal comparison.</summary>
    private readonly ConcurrentDictionary<string, BrokerSession> _sessions = new(StringComparer.Ordinal);

    /// <summary>Number of sessions, online and offline.</summary>
    public int Count => _sessions.Count;

    /// <summary>Looks up a session without allocating.</summary>
    /// <param name="clientId">Client identifier.</param>
    /// <param name="session">Session when found.</param>
    /// <returns><c>true</c> when the session exists.</returns>
    public bool TryGet(string clientId, out BrokerSession session)
        => _sessions.TryGetValue(clientId, out session!);

    /// <summary>Returns the session or null.</summary>
    /// <param name="clientId">Client identifier.</param>
    public BrokerSession? Get(string clientId)
        => _sessions.TryGetValue(clientId, out var session) ? session : null;

    /// <summary>Registers a new session; fails when the identifier is taken.</summary>
    /// <param name="session">Session to add.</param>
    /// <returns><c>true</c> when the session was added.</returns>
    public bool TryAdd(BrokerSession session) => _sessions.TryAdd(session.ClientId, session);

    /// <summary>Removes a session without returning it.</summary>
    /// <param name="clientId">Client identifier.</param>
    /// <returns><c>true</c> when a session was removed.</returns>
    public bool Remove(string clientId) => _sessions.TryRemove(clientId, out _);

    /// <summary>Removes a session and returns it so the caller can dispose it.</summary>
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
    /// <returns>Snapshot list; later changes are not reflected.</returns>
    public IReadOnlyList<BrokerSession> Snapshot() => _sessions.Values.ToList();

    /// <summary>Counts sessions that currently have a connection.</summary>
    public int ConnectedCount() => _sessions.Values.Count(session => session.IsOnline);

    /// <summary>Drops every session without disposing it; used on shutdown.</summary>
    public void Clear() => _sessions.Clear();
}
