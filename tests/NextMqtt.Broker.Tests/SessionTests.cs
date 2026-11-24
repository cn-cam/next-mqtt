using NextMqtt.Broker.Messaging;
using NextMqtt.Broker.Sessions;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Tests;

public class SessionTests
{
    private static OutboundMessage Message(
        string topic = "a/b",
        MqttQoS qos = MqttQoS.AtLeastOnce,
        string payload = "hello",
        bool retain = false)
        => new()
        {
            Topic = topic,
            Payload = System.Text.Encoding.UTF8.GetBytes(payload),
            QoS = qos,
            Retain = retain,
        };

    [Fact]
    public async Task Qos1_delivery_tracks_acknowledgement()
    {
        var (_, session, connection) = await TestBroker.ConnectAsync("c1");

        Assert.True(session.Deliver(Message()));

        var publish = Assert.Single(connection.Publishes());
        Assert.Equal(MqttQoS.AtLeastOnce, publish.QoS);
        Assert.NotEqual((ushort)0, publish.PacketId);
        Assert.Equal(1, session.InflightCount);

        session.OnPubAck(publish.PacketId);
        Assert.Equal(0, session.InflightCount);
    }

    [Fact]
    public async Task Qos2_delivery_completes_pubrec_pubcomp()
    {
        var (_, session, connection) = await TestBroker.ConnectAsync("c1");

        session.Deliver(Message(qos: MqttQoS.ExactlyOnce));
        var publish = Assert.Single(connection.Publishes());
        Assert.Equal(MqttQoS.ExactlyOnce, publish.QoS);

        session.OnPubRec(publish.PacketId);
        var pubRel = Assert.Single(connection.Sent.OfType<PubRelPacket>());
        Assert.Equal(publish.PacketId, pubRel.PacketId);

        session.OnPubComp(publish.PacketId);
        Assert.Equal(0, session.InflightCount);
    }

    [Fact]
    public async Task Offline_persistent_session_queues_and_resumes()
    {
        var broker = TestBroker.Create();
        var (_, session, first) = await TestBroker.ConnectAsync(
            "c1",
            cleanStart: true,
            sessionExpiry: 60,
            broker: broker);

        await broker.OnConnectionClosedAsync(session, first, null, graceful: false);

        Assert.False(session.IsOnline);
        Assert.True(session.Persistent);

        var delivered = session.Deliver(Message(qos: MqttQoS.AtLeastOnce));
        Assert.True(delivered);
        Assert.Equal(1, session.QueuedCount);

        var second = new FakeConnection();
        var attach = await broker.AttachSessionAsync(
            TestBroker.Connect("c1", cleanStart: false, sessionExpiry: 60),
            NextMqtt.Broker.Security.ClientPrincipal.Anonymous("c1"),
            second);

        Assert.True(attach.SessionPresent);

        var publish = Assert.Single(second.Publishes());
        Assert.Equal("a/b", publish.Topic);
        Assert.Equal(MqttQoS.AtLeastOnce, publish.QoS);
    }

    [Fact]
    public async Task Clean_session_does_not_queue_when_offline()
    {
        var broker = TestBroker.Create();
        var (_, session, connection) = await TestBroker.ConnectAsync("c1", cleanStart: true, sessionExpiry: 0, broker: broker);

        await broker.OnConnectionClosedAsync(session, connection, null, graceful: true);

        Assert.False(session.Deliver(Message()));
        Assert.Equal(0, session.QueuedCount);
    }

    [Fact]
    public async Task Inbound_qos2_deduplicates_packet_ids()
    {
        var (_, session, _) = await TestBroker.ConnectAsync("c1");
        var publish = new PublishPacket
        {
            Topic = "a",
            Payload = [1],
            QoS = MqttQoS.ExactlyOnce,
            PacketId = 7,
        };

        Assert.True(session.RegisterInboundQos2(7, publish));
        Assert.False(session.RegisterInboundQos2(7, publish));
        Assert.True(session.HasInboundQos2(7));

        Assert.True(session.TryCompleteInboundQos2(7, out var completed));
        Assert.Same(publish, completed);
        Assert.False(session.HasInboundQos2(7));
    }

    [Fact]
    public async Task Session_takeover_disconnects_previous_connection()
    {
        var broker = TestBroker.Create();
        var (_, _, first) = await TestBroker.ConnectAsync("c1", sessionExpiry: 60, broker: broker);

        var second = new FakeConnection();
        var attach = await broker.AttachSessionAsync(
            TestBroker.Connect("c1", cleanStart: false, sessionExpiry: 60),
            NextMqtt.Broker.Security.ClientPrincipal.Anonymous("c1"),
            second);

        Assert.True(attach.SessionPresent);
        Assert.True(first.Disconnected);
        Assert.Equal(MqttReasonCodes.SessionTakenOver, first.DisconnectReason);
    }

    [Fact]
    public async Task Inflight_is_retransmitted_with_duplicate_flag()
    {
        var (_, session, connection) = await TestBroker.ConnectAsync("c1");
        session.Deliver(Message());

        var original = Assert.Single(connection.Publishes());
        Assert.False(original.Duplicate);

        session.RetransmitDue(DateTimeOffset.UtcNow.AddMinutes(1));

        var retransmitted = connection.Publishes().Last();
        Assert.True(retransmitted.Duplicate);
        Assert.Equal(original.PacketId, retransmitted.PacketId);
    }

    [Fact]
    public async Task Offline_queue_is_bounded_dropping_oldest()
    {
        var broker = TestBroker.Create(new NextMqtt.Broker.Options.BrokerOptions { MaxQueuedMessages = 2 });
        var (_, session, connection) = await TestBroker.ConnectAsync(
            "c1", sessionExpiry: 60, broker: broker);

        await broker.OnConnectionClosedAsync(session, connection, null, graceful: false);

        session.Deliver(Message(payload: "1"));
        session.Deliver(Message(payload: "2"));
        session.Deliver(Message(payload: "3"));

        Assert.Equal(2, session.QueuedCount);
        Assert.Equal(1, broker.Metrics.DroppedMessages);
    }
}
