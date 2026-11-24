using NextMqtt.Broker.Messaging;
using NextMqtt.Broker.Storage;
using NextMqtt.Broker.Subscriptions;
using NextMqtt.Core.Protocol;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker.Tests;

public sealed class BackupTests
{
    [Fact]
    public async Task Backup_exports_and_restores_retained_and_sessions()
    {
        var broker = TestBroker.Create();
        await using var _ = broker;

        broker.Retained.Set(new RetainedMessage
        {
            Topic = "state/a",
            Payload = "one"u8.ToArray(),
            QoS = MqttQoS.AtLeastOnce,
        });

        var (_, session, connection) = await TestBroker.ConnectAsync("client-1", sessionExpiry: 120, broker: broker);
        broker.Subscriptions.AddOrReplace(new Subscription
        {
            ClientId = "client-1",
            Filter = TopicFilter.Parse("alerts/#"),
            Options = new MqttSubscriptionOptions(MqttQoS.AtLeastOnce),
        });
        await broker.OnConnectionClosedAsync(session, connection, disconnect: null, graceful: true);

        var backup = broker.CreateBackup();
        Assert.Single(backup.Retained);
        Assert.Single(backup.Sessions);

        var target = TestBroker.Create();
        await using var __ = target;

        target.Retained.Set(new RetainedMessage
        {
            Topic = "stale/topic",
            Payload = "x"u8.ToArray(),
        });

        var result = target.RestoreBackup(backup);

        Assert.Equal(1, result.Retained);
        Assert.Equal(1, result.Sessions);

        var retained = Assert.Single(target.Retained.Snapshot());
        Assert.Equal("state/a", retained.Topic);

        Assert.NotNull(target.Sessions.Get("client-1"));
        Assert.Contains(target.Subscriptions.GetAll(), subscription => subscription.Filter.Original == "alerts/#");
    }

    [Fact]
    public async Task Restore_skips_live_sessions()
    {
        var broker = TestBroker.Create();
        await using var _ = broker;

        var (_, session, connection) = await TestBroker.ConnectAsync("live", sessionExpiry: 120, broker: broker);
        Assert.True(session.IsOnline);

        var backup = new BrokerBackup
        {
            Sessions =
            [
                new PersistedSession { ClientId = "live", SessionExpiryInterval = 60 },
                new PersistedSession { ClientId = "restored", SessionExpiryInterval = 60 },
            ],
        };

        var result = broker.RestoreBackup(backup);

        Assert.Equal(1, result.Sessions);
        Assert.Same(session, broker.Sessions.Get("live"));
        Assert.NotNull(broker.Sessions.Get("restored"));

        await broker.OnConnectionClosedAsync(session, connection, disconnect: null, graceful: true);
    }
}
