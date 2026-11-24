using NextMqtt.Broker.Security;
using NextMqtt.Broker.Subscriptions;
using NextMqtt.Core.Protocol;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker.Tests;

public class BrokerRoutingTests
{
    private static Subscription Subscribe(string clientId, string filter, MqttQoS qos = MqttQoS.AtMostOnce, bool noLocal = false, int? identifier = null)
        => new()
        {
            ClientId = clientId,
            Filter = TopicFilter.Parse(filter),
            Options = new MqttSubscriptionOptions(qos, NoLocal: noLocal),
            Identifier = identifier,
        };

    private static PublishPacket Publish(string topic, string payload = "data", MqttQoS qos = MqttQoS.AtMostOnce, bool retain = false)
        => new()
        {
            Topic = topic,
            Payload = System.Text.Encoding.UTF8.GetBytes(payload),
            QoS = qos,
            Retain = retain,
        };

    [Fact]
    public async Task Routes_to_matching_subscription()
    {
        var (broker, _, connection) = await TestBroker.ConnectAsync("c1");
        broker.Subscriptions.AddOrReplace(Subscribe("c1", "sensors/#", MqttQoS.AtLeastOnce));

        await broker.RouteAsync(Publish("sensors/temp", qos: MqttQoS.AtLeastOnce), sourceClientId: "c2");

        var publish = Assert.Single(connection.Publishes());
        Assert.Equal("sensors/temp", publish.Topic);
        Assert.Equal(MqttQoS.AtLeastOnce, publish.QoS);
        Assert.False(publish.Retain);
    }

    [Fact]
    public async Task Respects_no_local_option()
    {
        var (broker, _, connection) = await TestBroker.ConnectAsync("c1");
        broker.Subscriptions.AddOrReplace(Subscribe("c1", "a/#", noLocal: true));

        await broker.RouteAsync(Publish("a/b"), sourceClientId: "c1");
        Assert.Empty(connection.Publishes());

        await broker.RouteAsync(Publish("a/b"), sourceClientId: "c2");
        Assert.Single(connection.Publishes());
    }

    [Fact]
    public async Task Delivery_qos_is_minimum_of_publish_and_subscription()
    {
        var (broker, _, connection) = await TestBroker.ConnectAsync("c1");
        broker.Subscriptions.AddOrReplace(Subscribe("c1", "a/#", MqttQoS.ExactlyOnce));

        await broker.RouteAsync(Publish("a/b", qos: MqttQoS.AtMostOnce), null);

        var publish = Assert.Single(connection.Publishes());
        Assert.Equal(MqttQoS.AtMostOnce, publish.QoS);
        Assert.Equal((ushort)0, publish.PacketId);
    }

    [Fact]
    public async Task Overlapping_subscriptions_deliver_one_message_at_highest_qos()
    {
        var (broker, _, connection) = await TestBroker.ConnectAsync("c1");
        broker.Subscriptions.AddOrReplace(Subscribe("c1", "a/#", MqttQoS.AtMostOnce));
        broker.Subscriptions.AddOrReplace(Subscribe("c1", "a/b", MqttQoS.AtLeastOnce, identifier: 5));

        await broker.RouteAsync(Publish("a/b", qos: MqttQoS.AtLeastOnce), null);

        var publish = Assert.Single(connection.Publishes());
        Assert.Equal(MqttQoS.AtLeastOnce, publish.QoS);
        Assert.Equal(5, publish.Properties.GetVariableByteInteger(MqttPropertyId.SubscriptionIdentifier)!.Value);
    }

    [Fact]
    public async Task Shared_group_delivers_to_exactly_one_member_per_message()
    {
        var broker = TestBroker.Create();
        var clients = new List<FakeConnection>();

        for (var i = 0; i < 3; i++)
        {
            var (_, _, connection) = await TestBroker.ConnectAsync($"c{i}", broker: broker);
            clients.Add(connection);
            broker.Subscriptions.AddOrReplace(Subscribe($"c{i}", "$share/workers/jobs/#"));
        }

        for (var i = 0; i < 9; i++)
        {
            await broker.RouteAsync(Publish($"jobs/{i}"), null);
        }

        Assert.Equal(9, clients.Sum(client => client.Publishes().Count));
        Assert.All(clients, client => Assert.Equal(3, client.Publishes().Count));
    }

    [Fact]
    public async Task Retain_as_published_controls_forwarded_retain_flag()
    {
        var (broker, _, plain) = await TestBroker.ConnectAsync("plain");
        var (_, _, rapConnection) = await TestBroker.ConnectAsync("rap", broker: broker);

        broker.Subscriptions.AddOrReplace(Subscribe("plain", "a/#"));
        broker.Subscriptions.AddOrReplace(new Subscription
        {
            ClientId = "rap",
            Filter = TopicFilter.Parse("a/#"),
            Options = new MqttSubscriptionOptions(MqttQoS.AtMostOnce, RetainAsPublished: true),
        });

        await broker.RouteAsync(Publish("a/b", retain: true), null);

        Assert.False(Assert.Single(plain.Publishes()).Retain);
        Assert.True(Assert.Single(rapConnection.Publishes()).Retain);
        Assert.Single(broker.GetRetained());
    }

    [Fact]
    public async Task Expired_message_is_not_delivered()
    {
        var (broker, _, connection) = await TestBroker.ConnectAsync("c1");
        broker.Subscriptions.AddOrReplace(Subscribe("c1", "a/#"));

        var publish = new PublishPacket
        {
            Topic = "a/b",
            Payload = [1],
            QoS = MqttQoS.AtMostOnce,
            Properties = new MqttProperties().AddUInt32(MqttPropertyId.MessageExpiryInterval, 0),
        };

        await broker.RouteAsync(publish, null);

        // Expiry 0 means the message expires immediately.
        Assert.Empty(connection.Publishes());
    }

    [Fact]
    public async Task Will_message_is_published_after_abnormal_disconnect()
    {
        var broker = TestBroker.Create();
        var will = new MqttWill
        {
            Topic = "status/c1",
            Payload = "offline"u8.ToArray(),
            QoS = MqttQoS.AtMostOnce,
            Retain = true,
        };

        var (_, session, connection) = await TestBroker.ConnectAsync("c1", will: will, broker: broker);
        await broker.OnConnectionClosedAsync(session, connection, null, graceful: false);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline && broker.GetRetained().Count == 0)
        {
            await Task.Delay(25);
        }

        var retained = Assert.Single(broker.GetRetained());
        Assert.Equal("status/c1", retained.Topic);
    }

    [Fact]
    public async Task Graceful_disconnect_does_not_publish_will()
    {
        var broker = TestBroker.Create();
        var will = new MqttWill
        {
            Topic = "status/c1",
            Payload = "offline"u8.ToArray(),
            QoS = MqttQoS.AtMostOnce,
            Retain = true,
        };

        var (_, session, connection) = await TestBroker.ConnectAsync("c1", will: will, broker: broker);
        await broker.OnConnectionClosedAsync(
            session,
            connection,
            new DisconnectPacket { ReasonCode = MqttReasonCodes.Success },
            graceful: true);

        await Task.Delay(150);
        Assert.Empty(broker.GetRetained());
    }

    [Fact]
    public async Task Admin_publish_reaches_clients()
    {
        var (broker, _, connection) = await TestBroker.ConnectAsync("c1");
        broker.Subscriptions.AddOrReplace(Subscribe("c1", "admin/#", MqttQoS.AtLeastOnce));

        await broker.PublishAsync("admin/test", "payload"u8.ToArray(), MqttQoS.AtLeastOnce);

        var publish = Assert.Single(connection.Publishes());
        Assert.Equal("admin/test", publish.Topic);
        Assert.Equal("payload"u8.ToArray(), publish.Payload);
    }

    [Fact]
    public async Task Kick_disconnects_online_client()
    {
        var (broker, _, connection) = await TestBroker.ConnectAsync("c1");

        Assert.True(await broker.KickClientAsync("c1"));
        Assert.True(connection.Disconnected);
        Assert.Equal(MqttReasonCodes.AdministrativeAction, connection.DisconnectReason);
    }
}
