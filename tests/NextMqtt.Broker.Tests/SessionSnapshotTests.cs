using NextMqtt.Broker.Messaging;
using NextMqtt.Broker.Options;
using NextMqtt.Broker.Storage;
using NextMqtt.Broker.Subscriptions;
using NextMqtt.Core.Protocol;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker.Tests;

public sealed class SessionSnapshotTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nextmqtt-tests",
        Guid.NewGuid().ToString("N"));

    private string SnapshotPath => Path.Combine(_directory, "sessions.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Persistent_session_survives_a_restart_with_queue_and_subscriptions()
    {
        var store = new FileSessionSnapshotStore(SnapshotPath);
        var broker = TestBroker.Create(sessionStore: store);

        var (_, session, connection) = await TestBroker.ConnectAsync(
            "durable",
            sessionExpiry: 300,
            broker: broker);

        broker.Subscriptions.AddOrReplace(new Subscription
        {
            ClientId = "durable",
            Filter = TopicFilter.Parse("jobs/#"),
            Options = new MqttSubscriptionOptions(MqttQoS.AtLeastOnce),
            Identifier = 7,
        });

        await broker.OnConnectionClosedAsync(session, connection, disconnect: null, graceful: true);

        await broker.PublishAsync("jobs/1", "first"u8.ToArray(), MqttQoS.AtLeastOnce);

        Assert.Equal(1, session.QueuedCount);

        await broker.DisposeAsync();

        var restoredBroker = TestBroker.Create(sessionStore: new FileSessionSnapshotStore(SnapshotPath));

        try
        {
            var restored = restoredBroker.Sessions.Get("durable");
            Assert.NotNull(restored);
            Assert.True(restored.Persistent);
            Assert.Equal(300u, restored.SessionExpiryInterval);
            Assert.Equal(1, restored.QueuedCount);

            var subscription = Assert.Single(restoredBroker.Subscriptions.GetAll());
            Assert.Equal("jobs/#", subscription.Filter.Original);
            Assert.Equal(MqttQoS.AtLeastOnce, subscription.Options.QoS);
            Assert.Equal(7, subscription.Identifier);

            var (_, resumed, _) = await TestBroker.ConnectAsync(
                "durable",
                cleanStart: false,
                sessionExpiry: 300,
                broker: restoredBroker);

            Assert.Same(restored, resumed);
        }
        finally
        {
            await restoredBroker.DisposeAsync();
        }
    }

    [Fact]
    public async Task Expired_sessions_are_not_restored()
    {
        var store = new FileSessionSnapshotStore(SnapshotPath);
        store.Save(
        [
            new PersistedSession
            {
                ClientId = "stale",
                SessionExpiryInterval = 60,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                LastSeen = DateTimeOffset.UtcNow.AddMinutes(-6),
            },
        ]);

        var broker = TestBroker.Create(sessionStore: new FileSessionSnapshotStore(SnapshotPath));
        await using var _ = broker;

        Assert.Null(broker.Sessions.Get("stale"));
    }

    [Fact]
    public async Task Retained_and_session_state_restore_together()
    {
        var store = new FileSessionSnapshotStore(SnapshotPath);
        var journal = new FileRetainedJournal(Path.Combine(_directory, "retained.log"));
        var broker = new MqttBroker(
            new BrokerOptions { SweepInterval = TimeSpan.FromSeconds(1), RetransmitInterval = TimeSpan.FromSeconds(1) },
            new AllowAllAuthenticator(),
            new AllowAllAuthorizer(),
            NextMqtt.Broker.Clustering.NullClusterRouter.Instance,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MqttBroker>.Instance,
            journal,
            store);

        broker.Retained.Set(new RetainedMessage
        {
            Topic = "state/device",
            Payload = "online"u8.ToArray(),
            QoS = MqttQoS.AtLeastOnce,
        });

        await broker.DisposeAsync();
        journal.Dispose();

        var journal2 = new FileRetainedJournal(Path.Combine(_directory, "retained.log"));
        var broker2 = new MqttBroker(
            new BrokerOptions { SweepInterval = TimeSpan.FromSeconds(1), RetransmitInterval = TimeSpan.FromSeconds(1) },
            new AllowAllAuthenticator(),
            new AllowAllAuthorizer(),
            NextMqtt.Broker.Clustering.NullClusterRouter.Instance,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MqttBroker>.Instance,
            journal2,
            new FileSessionSnapshotStore(SnapshotPath));

        await using var _ = broker2;

        var retained = Assert.Single(broker2.Retained.Snapshot());
        Assert.Equal("state/device", retained.Topic);
        journal2.Dispose();
    }
}
