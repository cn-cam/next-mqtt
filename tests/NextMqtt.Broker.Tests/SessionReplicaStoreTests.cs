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
        => ReplicaAt(clientId, owner, version, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    private static ClusterSessionReplica ReplicaAt(
        string clientId,
        string owner,
        long version,
        long updatedAtUnix)
        => new(
            new PersistedSession { ClientId = clientId, SessionExpiryInterval = 300 },
            owner,
            version,
            updatedAtUnix);

    [Fact]
    public void Newer_timestamp_wins_after_a_version_reset()
    {
        var broker = TestBroker.Create();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        try
        {
            // A restarted owner starts its version counter from zero again.
            broker.StoreSessionReplica(ReplicaAt("s", "node-a", 500, now - 10));
            broker.StoreSessionReplica(ReplicaAt("s", "node-a", 1, now));

            var replica = broker.GetSessionReplica("s");
            Assert.NotNull(replica);
            Assert.Equal(1, replica.Version);
        }
        finally
        {
            broker.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public void A_delayed_push_from_the_previous_owner_does_not_win()
    {
        var broker = TestBroker.Create();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        try
        {
            broker.StoreSessionReplica(ReplicaAt("s", "node-a", 900, now - 2));
            broker.StoreSessionReplica(ReplicaAt("s", "node-b", 1, now));

            // The old owner pushed late, but its snapshot predates the handover.
            broker.StoreSessionReplica(ReplicaAt("s", "node-a", 999, now - 3));

            var replica = broker.GetSessionReplica("s");
            Assert.NotNull(replica);
            Assert.Equal("node-b", replica.OwnerNodeId);
        }
        finally
        {
            broker.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

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
