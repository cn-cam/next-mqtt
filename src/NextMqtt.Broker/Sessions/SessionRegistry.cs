using System.Collections.Concurrent;

namespace NextMqtt.Broker.Sessions;

/// <summary>Thread-safe registry of sessions keyed by client identifier.</summary>
public sealed class SessionRegistry
{
    private readonly ConcurrentDictionary<string, BrokerSession> _sessions = new(StringComparer.Ordinal);

    public int Count => _sessions.Count;

    public bool TryGet(string clientId, out BrokerSession session)
        => _sessions.TryGetValue(clientId, out session!);

    public BrokerSession? Get(string clientId)
        => _sessions.TryGetValue(clientId, out var session) ? session : null;

    public bool TryAdd(BrokerSession session) => _sessions.TryAdd(session.ClientId, session);

    public bool Remove(string clientId) => _sessions.TryRemove(clientId, out _);

    public bool TryRemove(string clientId, out BrokerSession? session)
    {
        var removed = _sessions.TryRemove(clientId, out var value);
        session = value;
        return removed;
    }

    public IReadOnlyList<BrokerSession> Snapshot() => _sessions.Values.ToList();

    public int ConnectedCount() => _sessions.Values.Count(session => session.IsOnline);

    public void Clear() => _sessions.Clear();
}
