using System.Text;
using NextMqtt.Broker.Options;
using NextMqtt.Broker.Security;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Server.Tests;

public class ProtocolIntegrationTests
{
    private static ConnectPacket Connect(
        string clientId,
        MqttProtocolVersion version = MqttProtocolVersion.V500,
        bool cleanStart = true,
        uint sessionExpiry = 0,
        MqttWill? will = null)
    {
        var properties = new MqttProperties();

        if (version == MqttProtocolVersion.V500 && sessionExpiry > 0)
        {
            properties.AddUInt32(MqttPropertyId.SessionExpiryInterval, sessionExpiry);
        }

        return new ConnectPacket
        {
            ClientId = clientId,
            ProtocolVersion = version,
            CleanStart = cleanStart,
            KeepAliveSeconds = 30,
            Properties = properties,
            Will = will,
        };
    }

    [Fact]
    public async Task Connects_pings_and_disconnects()
    {
        await using var server = MqttTestServer.Start();
        await using var client = await server.ConnectAsync(Connect("basic"));

        Assert.Equal(MqttReasonCodes.Success, client.ConnAck.ReasonCode);
        Assert.False(client.ConnAck.SessionPresent);
        Assert.Equal((ushort)32, client.ConnAck.Properties.GetUInt16(MqttPropertyId.ReceiveMaximum));

        await client.SendAsync(PingReqPacket.Instance);
        await client.ReadAsync<PingRespPacket>();
        await client.DisconnectAsync();
    }

    [Fact]
    public async Task Qos0_publish_reaches_subscriber()
    {
        await using var server = MqttTestServer.Start();
        await using var subscriber = await server.ConnectAsync(Connect("sub"));
        await using var publisher = await server.ConnectAsync(Connect("pub"));

        var subAck = await subscriber.SubscribeAsync("sensors/#", MqttQoS.AtMostOnce);
        Assert.Equal(new byte[] { 0 }, subAck.ReasonCodes);

        await publisher.PublishAsync("sensors/temp", "21.5");

        var message = await subscriber.ReadAsync<PublishPacket>();
        Assert.Equal("sensors/temp", message.Topic);
        Assert.Equal("21.5", Encoding.UTF8.GetString(message.Payload));
        Assert.Equal(MqttQoS.AtMostOnce, message.QoS);
        Assert.Equal((ushort)0, message.PacketId);
    }

    [Fact]
    public async Task Qos1_flow_acknowledges_both_directions()
    {
        await using var server = MqttTestServer.Start();
        await using var subscriber = await server.ConnectAsync(Connect("sub"));
        await using var publisher = await server.ConnectAsync(Connect("pub"));

        await subscriber.SubscribeAsync("q1/#", MqttQoS.AtLeastOnce);
        var packetId = await publisher.PublishAsync("q1/a", "payload", MqttQoS.AtLeastOnce);

        var pubAck = await publisher.ReadAsync<PubAckPacket>();
        Assert.Equal(packetId, pubAck.PacketId);
        Assert.Equal(MqttReasonCodes.Success, pubAck.ReasonCode);

        var message = await subscriber.ReadAsync<PublishPacket>();
        Assert.Equal(MqttQoS.AtLeastOnce, message.QoS);
        Assert.NotEqual((ushort)0, message.PacketId);

        await subscriber.SendAsync(new PubAckPacket { PacketId = message.PacketId });
    }

    [Fact]
    public async Task Qos2_flow_completes_handshakes()
    {
        await using var server = MqttTestServer.Start();
        await using var subscriber = await server.ConnectAsync(Connect("sub"));
        await using var publisher = await server.ConnectAsync(Connect("pub"));

        await subscriber.SubscribeAsync("q2/#", MqttQoS.ExactlyOnce);
        await publisher.PublishAsync("q2/a", "exactly-once", MqttQoS.ExactlyOnce);

        var pubRec = await publisher.ReadAsync<PubRecPacket>();
        Assert.Equal(MqttReasonCodes.Success, pubRec.ReasonCode);

        await publisher.SendAsync(new PubRelPacket { PacketId = pubRec.PacketId });
        var pubComp = await publisher.ReadAsync<PubCompPacket>();
        Assert.Equal(pubRec.PacketId, pubComp.PacketId);

        var message = await subscriber.ReadAsync<PublishPacket>();
        Assert.Equal(MqttQoS.ExactlyOnce, message.QoS);

        await subscriber.SendAsync(new PubRecPacket { PacketId = message.PacketId });
        var pubRel = await subscriber.ReadAsync<PubRelPacket>();
        await subscriber.SendAsync(new PubCompPacket { PacketId = pubRel.PacketId });
    }

    [Fact]
    public async Task Retained_message_is_delivered_after_suback()
    {
        await using var server = MqttTestServer.Start();
        await using var publisher = await server.ConnectAsync(Connect("pub"));
        await using var subscriber = await server.ConnectAsync(Connect("sub"));

        await publisher.PublishAsync("retained/a", "sticky", retain: true);
        Assert.True(await server.WaitForAsync(() => server.Broker.GetRetained().Count == 1));

        var subAck = await subscriber.SubscribeAsync("retained/#");
        Assert.Equal(MqttReasonCodes.Success, subAck.ReasonCodes[0]);

        var message = await subscriber.ReadAsync<PublishPacket>();
        Assert.Equal("retained/a", message.Topic);
        Assert.True(message.Retain);
        Assert.Equal("sticky", Encoding.UTF8.GetString(message.Payload));
    }

    [Fact]
    public async Task Persistent_session_queues_messages_while_offline()
    {
        await using var server = MqttTestServer.Start();
        await using var first = await server.ConnectAsync(Connect("persist", sessionExpiry: 60));

        await first.SubscribeAsync("queue/#", MqttQoS.AtLeastOnce);
        await first.DisconnectAsync();
        await first.DisposeAsync();

        Assert.True(await server.WaitForAsync(
            () => server.Broker.Sessions.Get("persist") is { IsOnline: false }));

        await using var publisher = await server.ConnectAsync(Connect("pub"));
        await publisher.PublishAsync("queue/x", "queued", MqttQoS.AtLeastOnce);
        await publisher.ReadAsync<PubAckPacket>();

        await using var second = await server.ConnectAsync(Connect("persist", cleanStart: false, sessionExpiry: 60));

        Assert.True(second.ConnAck.SessionPresent);

        var message = await second.ReadAsync<PublishPacket>();
        Assert.Equal("queue/x", message.Topic);
        Assert.Equal("queued", Encoding.UTF8.GetString(message.Payload));
        Assert.Equal(MqttQoS.AtLeastOnce, message.QoS);
    }

    [Fact]
    public async Task Will_is_published_after_abrupt_disconnect()
    {
        await using var server = MqttTestServer.Start();
        await using var subscriber = await server.ConnectAsync(Connect("sub"));
        await subscriber.SubscribeAsync("will/#", MqttQoS.AtLeastOnce);

        var willClient = await server.ConnectAsync(Connect(
            "will-client",
            will: new MqttWill
            {
                Topic = "will/client",
                Payload = "offline"u8.ToArray(),
                QoS = MqttQoS.AtLeastOnce,
            }));

        await willClient.DisposeAsync();

        var message = await subscriber.ReadAsync<PublishPacket>();

        Assert.Equal("will/client", message.Topic);
        Assert.Equal("offline", Encoding.UTF8.GetString(message.Payload));
        Assert.Equal(MqttQoS.AtLeastOnce, message.QoS);
    }

    [Fact]
    public async Task Invalid_filter_gets_topic_filter_invalid()
    {
        await using var server = MqttTestServer.Start();
        await using var client = await server.ConnectAsync(Connect("c1"));

        var subAck = await client.SubscribeAsync("bad/#/filter");

        Assert.Equal(MqttReasonCodes.TopicFilterInvalid, subAck.ReasonCodes[0]);
    }

    [Fact]
    public async Task Authentication_failure_returns_bad_credentials()
    {
        await using var server = MqttTestServer.Start(authentication: new AuthenticationOptions
        {
            AllowAnonymous = false,
            Users =
            [
                new LocalUserOptions
                {
                    Username = "alice",
                    PasswordHash = PasswordHasher.Hash("secret", 10_000),
                },
            ],
        });

        var connect = new ConnectPacket
        {
            ClientId = "bad-auth",
            ProtocolVersion = MqttProtocolVersion.V500,
            CleanStart = true,
            Username = "alice",
            Password = "wrong"u8.ToArray(),
        };

        await using var client = await server.ConnectAsync(connect);

        Assert.Equal(MqttReasonCodes.BadUserNameOrPassword, client.ConnAck.ReasonCode);
    }

    [Fact]
    public async Task Acl_denies_publish_and_subscribe()
    {
        await using var server = MqttTestServer.Start(acl: new AclOptions
        {
            DefaultPolicy = AclDefaultPolicy.Deny,
        });

        await using var client = await server.ConnectAsync(Connect("c1"));

        var subAck = await client.SubscribeAsync("a/#");
        Assert.Equal(MqttReasonCodes.NotAuthorized, subAck.ReasonCodes[0]);

        await client.PublishAsync("a/b", "denied", MqttQoS.AtLeastOnce);
        var pubAck = await client.ReadAsync<PubAckPacket>();
        Assert.Equal(MqttReasonCodes.NotAuthorized, pubAck.ReasonCode);
    }

    [Fact]
    public async Task Unsubscribe_stops_delivery()
    {
        await using var server = MqttTestServer.Start();
        await using var subscriber = await server.ConnectAsync(Connect("sub"));
        await using var publisher = await server.ConnectAsync(Connect("pub"));

        await subscriber.SubscribeAsync("remove/#");
        await subscriber.SendAsync(new UnsubscribePacket
        {
            PacketId = subscriber.NextPacketId(),
            Filters = ["remove/#"],
        });

        var unsubAck = await subscriber.ReadAsync<UnsubAckPacket>();
        Assert.Equal(MqttReasonCodes.Success, unsubAck.ReasonCodes[0]);

        await publisher.PublishAsync("remove/x", "gone");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => subscriber.ReadPacketAsync(TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public async Task Session_takeover_notifies_previous_connection()
    {
        await using var server = MqttTestServer.Start();
        await using var first = await server.ConnectAsync(Connect("duplicate"));
        await using var second = await server.ConnectAsync(Connect("duplicate"));

        var disconnect = await first.ReadAsync<DisconnectPacket>();
        Assert.Equal(MqttReasonCodes.SessionTakenOver, disconnect.ReasonCode);
    }

    [Fact]
    public async Task Mqtt311_client_interoperates()
    {
        await using var server = MqttTestServer.Start();
        await using var subscriber = await server.ConnectAsync(Connect("v3-sub", MqttProtocolVersion.V311));
        await using var publisher = await server.ConnectAsync(Connect("v5-pub"));

        var subAck = await subscriber.SubscribeAsync("mixed/#", MqttQoS.AtLeastOnce);
        Assert.Equal(new byte[] { 1 }, subAck.ReasonCodes);

        await publisher.PublishAsync("mixed/data", "v311", MqttQoS.AtLeastOnce);
        await publisher.ReadAsync<PubAckPacket>();

        var message = await subscriber.ReadAsync<PublishPacket>();
        Assert.Equal("v311", Encoding.UTF8.GetString(message.Payload));
        Assert.Equal(MqttQoS.AtLeastOnce, message.QoS);
    }
}
