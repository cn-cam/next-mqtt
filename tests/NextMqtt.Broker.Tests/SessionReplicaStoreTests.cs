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
    public void Unchanged_content_is_not_rewritten()
    {
        var store = new FileSessionReplicaStore(PathFor("steady.json"), shardCount: 4);
        var replicas = new[] { Replica("a", "node-a", 1), Replica("b", "node-b", 2) };

        store.Save(replicas);
        Assert.True(store.ShardWrites > 0);

        store.Save(replicas);
        Assert.Equal(0, store.ShardWrites);
    }

    [Fact]
    public void Changing_one_replica_rewrites_only_its_shard()
    {
        var store = new FileSessionReplicaStore(PathFor("partial.json"), shardCount: 4);
        var replicas = new List<ClusterSessionReplica>
        {
            Replica("a", "node-a", 1),
            Replica("b", "node-b", 2),
        };

        store.Save(replicas);

        replicas[1] = Replica("b", "node-b", 3);
        store.Save(replicas);

        Assert.Equal(1, store.ShardWrites);
    }

    [Fact]
    public void Legacy_single_file_is_read_then_replaced_by_shards()
    {
        var path = PathFor("legacy.json");
        File.WriteAllText(
            path,
            """[{"Session":{"ClientId":"old"},"OwnerNodeId":"node-a","Version":1,"UpdatedAtUnix":1}]""");

        var store = new FileSessionReplicaStore(path, shardCount: 2);

        Assert.Single(store.Load());

        store.Save([Replica("new", "node-b", 2)]);

        Assert.False(File.Exists(path));
        Assert.Single(store.Load());
        Assert.Contains(store.Load(), replica => replica.Session.ClientId == "new");
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
