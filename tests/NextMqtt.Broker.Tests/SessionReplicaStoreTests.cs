using NextMqtt.Broker.Clustering;
using NextMqtt.Broker.Storage;

namespace NextMqtt.Broker.Tests;

public sealed class SessionReplicaStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "nextmqtt-replicas-" + Guid.NewGuid().ToString("N"));

    private string PathFor(string name)
    {
        Directory.CreateDirectory(_directory);
        return Path.Combine(_directory, name);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Left behind when a handle is still open.
        }
    }

    private static ClusterSessionReplica Replica(string clientId, string owner, long version)
        => new(
            new PersistedSession { ClientId = clientId, SessionExpiryInterval = 300 },
            owner,
            version,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    [Fact]
    public void Snapshot_round_trips()
    {
        var store = new FileSessionReplicaStore(PathFor("replicas.json"));

        store.Save([Replica("a", "node-a", 1), Replica("b", "node-b", 2)]);
        var loaded = store.Load();

        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, replica => replica.Session.ClientId == "b" && replica.OwnerNodeId == "node-b");
    }

    [Fact]
    public void Missing_and_corrupt_files_load_as_empty()
    {
        var missing = new FileSessionReplicaStore(PathFor("absent.json"));
        Assert.Empty(missing.Load());

        var path = PathFor("broken.json");
        File.WriteAllText(path, "{ not json");

        var corrupt = new FileSessionReplicaStore(path);
        Assert.Empty(corrupt.Load());
    }

    [Fact]
    public void Broker_restores_replicas_from_the_store()
    {
        var store = new FileSessionReplicaStore(PathFor("restore.json"));
        store.Save([Replica("roamer", "node-b", 7)]);

        var broker = TestBroker.Create(replicaStore: store);

        try
        {
            Assert.Equal(1, broker.SessionReplicaCount);

            var replica = broker.GetSessionReplica("roamer");
            Assert.NotNull(replica);
            Assert.Equal("node-b", replica.OwnerNodeId);
        }
        finally
        {
            broker.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
