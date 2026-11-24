using System.Buffers;
using System.IO.Pipelines;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Core.Tests;

internal static class PacketTestHelpers
{
    public static byte[] Encode(MqttPacket packet, MqttProtocolVersion version)
    {
        var writer = new ArrayBufferWriter<byte>();
        MqttPacketWriter.Write(writer, packet, version);
        return writer.WrittenSpan.ToArray();
    }

    public static async Task<MqttPacket?> ReadAsync(
        byte[] bytes,
        MqttProtocolVersion version,
        int maxPacketSize = MqttLimits.DefaultMaxPacketSize)
    {
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(bytes);
        await pipe.Writer.CompleteAsync();

        var reader = new MqttPacketReader(pipe.Reader, maxPacketSize);
        return await reader.ReadAsync(version);
    }

    public static async Task<MqttPacket> RoundTripAsync(MqttPacket packet, MqttProtocolVersion version)
    {
        var encoded = Encode(packet, version);
        var decoded = await ReadAsync(encoded, version);
        Assert.NotNull(decoded);
        return decoded;
    }
}

public class ProtocolCodecTests
{
    [Fact]
    public async Task Connect_v5_roundtrips_all_fields()
    {
        var connect = new ConnectPacket
        {
            ProtocolVersion = MqttProtocolVersion.V500,
            ClientId = "sensor-42",
            CleanStart = true,
            KeepAliveSeconds = 30,
            Username = "device",
            Password = "secret"u8.ToArray(),
            Properties = new MqttProperties()
                .AddUInt32(MqttPropertyId.SessionExpiryInterval, 120)
                .AddUInt16(MqttPropertyId.ReceiveMaximum, 10)
                .AddByte(MqttPropertyId.RequestProblemInformation, 0)
                .AddUserProperty("region", "eu-west"),
            Will = new MqttWill
            {
                Topic = "devices/sensor-42/status",
                Payload = "offline"u8.ToArray(),
                QoS = MqttQoS.AtLeastOnce,
                Retain = true,
                Properties = new MqttProperties()
                    .AddUInt32(MqttPropertyId.WillDelayInterval, 15)
                    .AddString(MqttPropertyId.ContentType, "text/plain"),
            },
        };

        var decoded = Assert.IsType<ConnectPacket>(await PacketTestHelpers.RoundTripAsync(connect, MqttProtocolVersion.V500));

        Assert.Equal(MqttProtocolVersion.V500, decoded.ProtocolVersion);
        Assert.Equal("sensor-42", decoded.ClientId);
        Assert.True(decoded.CleanStart);
        Assert.Equal(30, decoded.KeepAliveSeconds);
        Assert.Equal("device", decoded.Username);
        Assert.Equal("secret"u8.ToArray(), decoded.Password);
        Assert.Equal(120u, decoded.Properties.GetUInt32(MqttPropertyId.SessionExpiryInterval));
        Assert.Equal((ushort)10, decoded.Properties.GetUInt16(MqttPropertyId.ReceiveMaximum));
        Assert.Equal("eu-west", decoded.Properties.GetUserProperties().Single().Value);
        Assert.NotNull(decoded.Will);
        Assert.Equal("devices/sensor-42/status", decoded.Will.Topic);
        Assert.Equal("offline"u8.ToArray(), decoded.Will.Payload);
        Assert.Equal(MqttQoS.AtLeastOnce, decoded.Will.QoS);
        Assert.True(decoded.Will.Retain);
        Assert.Equal(15u, decoded.Will.Properties.GetUInt32(MqttPropertyId.WillDelayInterval));
    }

    [Fact]
    public async Task Connect_v311_roundtrips_without_properties()
    {
        var connect = new ConnectPacket
        {
            ProtocolVersion = MqttProtocolVersion.V311,
            ClientId = "legacy",
            CleanStart = false,
            KeepAliveSeconds = 60,
            Username = "legacy-user",
            Password = "pw"u8.ToArray(),
            Will = new MqttWill
            {
                Topic = "legacy/status",
                Payload = "bye"u8.ToArray(),
                QoS = MqttQoS.AtMostOnce,
            },
        };

        var decoded = Assert.IsType<ConnectPacket>(await PacketTestHelpers.RoundTripAsync(connect, MqttProtocolVersion.V311));

        Assert.Equal(MqttProtocolVersion.V311, decoded.ProtocolVersion);
        Assert.False(decoded.CleanStart);
        Assert.Equal(0, decoded.Properties.Count);
        Assert.Equal(MqttQoS.AtMostOnce, decoded.Will!.QoS);
    }

    [Fact]
    public async Task Publish_v5_roundtrips_qos_and_properties()
    {
        var publish = new PublishPacket
        {
            Topic = "telemetry/temperature",
            Payload = "21.5"u8.ToArray(),
            QoS = MqttQoS.ExactlyOnce,
            Retain = true,
            PacketId = 77,
            Properties = new MqttProperties()
                .AddUInt32(MqttPropertyId.MessageExpiryInterval, 300)
                .AddString(MqttPropertyId.ResponseTopic, "replies/temperature")
                .AddBinary(MqttPropertyId.CorrelationData, "abc"u8)
                .AddVariableByteInteger(MqttPropertyId.SubscriptionIdentifier, 9),
        };

        var decoded = Assert.IsType<PublishPacket>(await PacketTestHelpers.RoundTripAsync(publish, MqttProtocolVersion.V500));

        Assert.Equal("telemetry/temperature", decoded.Topic);
        Assert.Equal("21.5"u8.ToArray(), decoded.Payload);
        Assert.Equal(MqttQoS.ExactlyOnce, decoded.QoS);
        Assert.True(decoded.Retain);
        Assert.Equal((ushort)77, decoded.PacketId);
        Assert.Equal(300u, decoded.Properties.GetUInt32(MqttPropertyId.MessageExpiryInterval));
        Assert.Equal(9, decoded.Properties.GetVariableByteInteger(MqttPropertyId.SubscriptionIdentifier)!.Value);
    }

    [Fact]
    public async Task Subscribe_v5_roundtrips_options()
    {
        var subscribe = new SubscribePacket
        {
            PacketId = 12,
            Properties = new MqttProperties().AddVariableByteInteger(MqttPropertyId.SubscriptionIdentifier, 3),
            Subscriptions =
            [
                new MqttSubscriptionRequest("a/+/c", new MqttSubscriptionOptions(MqttQoS.AtLeastOnce, NoLocal: true, RetainAsPublished: true, MqttRetainHandling.DoNotSend)),
                new MqttSubscriptionRequest("$share/workers/jobs/#", new MqttSubscriptionOptions(MqttQoS.ExactlyOnce)),
            ],
        };

        var decoded = Assert.IsType<SubscribePacket>(await PacketTestHelpers.RoundTripAsync(subscribe, MqttProtocolVersion.V500));

        Assert.Equal(2, decoded.Subscriptions.Count);
        Assert.Equal("a/+/c", decoded.Subscriptions[0].Filter);
        Assert.True(decoded.Subscriptions[0].Options.NoLocal);
        Assert.True(decoded.Subscriptions[0].Options.RetainAsPublished);
        Assert.Equal(MqttRetainHandling.DoNotSend, decoded.Subscriptions[0].Options.RetainHandling);
        Assert.Equal(MqttQoS.ExactlyOnce, decoded.Subscriptions[1].Options.QoS);
        Assert.Equal(3, decoded.Properties.GetVariableByteInteger(MqttPropertyId.SubscriptionIdentifier)!.Value);
    }

    [Fact]
    public async Task Acknowledgements_roundtrip_v5_reason_and_properties()
    {
        var ack = new PubAckPacket
        {
            PacketId = 5,
            ReasonCode = MqttReasonCodes.NoMatchingSubscribers,
            Properties = new MqttProperties().AddString(MqttPropertyId.ReasonString, "no subscribers"),
        };

        var decoded = Assert.IsType<PubAckPacket>(await PacketTestHelpers.RoundTripAsync(ack, MqttProtocolVersion.V500));
        Assert.Equal((ushort)5, decoded.PacketId);
        Assert.Equal(MqttReasonCodes.NoMatchingSubscribers, decoded.ReasonCode);
        Assert.Equal("no subscribers", decoded.Properties.GetString(MqttPropertyId.ReasonString));

        var pubRel = new PubRelPacket { PacketId = 9 };
        var decodedRel = Assert.IsType<PubRelPacket>(await PacketTestHelpers.RoundTripAsync(pubRel, MqttProtocolVersion.V500));
        Assert.Equal((ushort)9, decodedRel.PacketId);
        Assert.Equal(MqttReasonCodes.Success, decodedRel.ReasonCode);
    }

    [Fact]
    public async Task SubAck_and_UnsubAck_roundtrip_v5()
    {
        var subAck = new SubAckPacket
        {
            PacketId = 33,
            ReasonCodes = [MqttReasonCodes.GrantedQoS1, MqttReasonCodes.NotAuthorized],
        };

        var decoded = Assert.IsType<SubAckPacket>(await PacketTestHelpers.RoundTripAsync(subAck, MqttProtocolVersion.V500));
        Assert.Equal(new byte[] { MqttReasonCodes.GrantedQoS1, MqttReasonCodes.NotAuthorized }, decoded.ReasonCodes);

        var unsubAck = new UnsubAckPacket
        {
            PacketId = 34,
            ReasonCodes = [MqttReasonCodes.Success, MqttReasonCodes.NoSubscriptionExisted],
        };

        var decodedUnsub = Assert.IsType<UnsubAckPacket>(await PacketTestHelpers.RoundTripAsync(unsubAck, MqttProtocolVersion.V500));
        Assert.Equal(new byte[] { MqttReasonCodes.Success, MqttReasonCodes.NoSubscriptionExisted }, decodedUnsub.ReasonCodes);
    }

    [Fact]
    public async Task Disconnect_v5_roundtrips_reason()
    {
        var disconnect = new DisconnectPacket
        {
            ReasonCode = MqttReasonCodes.SessionTakenOver,
            Properties = new MqttProperties().AddString(MqttPropertyId.ReasonString, "another connection"),
        };

        var decoded = Assert.IsType<DisconnectPacket>(await PacketTestHelpers.RoundTripAsync(disconnect, MqttProtocolVersion.V500));
        Assert.Equal(MqttReasonCodes.SessionTakenOver, decoded.ReasonCode);
        Assert.Equal("another connection", decoded.Properties.GetString(MqttPropertyId.ReasonString));
    }

    [Fact]
    public async Task Ping_roundtrips()
    {
        var decoded = await PacketTestHelpers.RoundTripAsync(PingReqPacket.Instance, MqttProtocolVersion.V311);
        Assert.IsType<PingReqPacket>(decoded);
    }

    [Fact]
    public async Task Fragmented_packet_is_reassembled()
    {
        var publish = new PublishPacket
        {
            Topic = "fragmented/topic",
            Payload = new byte[4096],
            QoS = MqttQoS.AtLeastOnce,
            PacketId = 1,
        };

        var bytes = PacketTestHelpers.Encode(publish, MqttProtocolVersion.V500);
        var pipe = new Pipe();
        var reader = new MqttPacketReader(pipe.Reader);

        await pipe.Writer.WriteAsync(bytes.AsMemory(0, 3));
        await pipe.Writer.FlushAsync();

        var task = reader.ReadAsync(MqttProtocolVersion.V500).AsTask();
        await Task.Delay(30);
        Assert.False(task.IsCompleted);

        await pipe.Writer.WriteAsync(bytes.AsMemory(3));
        await pipe.Writer.FlushAsync();

        var decoded = Assert.IsType<PublishPacket>(await task);
        Assert.Equal(publish.Payload.Length, decoded.Payload.Length);
        Assert.Equal("fragmented/topic", decoded.Topic);
    }

    [Fact]
    public async Task Publish_with_qos3_is_rejected()
    {
        // Fixed header 0x36 => PUBLISH, QoS 3. Empty topic, no payload.
        var bytes = new byte[] { 0x36, 0x03, 0x00, 0x00, 0x00 };
        var exception = await Assert.ThrowsAsync<MqttProtocolException>(
            async () => await PacketTestHelpers.ReadAsync(bytes, MqttProtocolVersion.V500));
        Assert.Equal(MqttReasonCodes.MalformedPacket, exception.ReasonCode);
    }

    [Fact]
    public async Task Oversized_packet_is_rejected_before_buffering()
    {
        var bytes = new byte[] { 0x30, 0xFF, 0xFF, 0x7F };
        var exception = await Assert.ThrowsAsync<MqttProtocolException>(
            async () => await PacketTestHelpers.ReadAsync(bytes, MqttProtocolVersion.V500, maxPacketSize: 1024));
        Assert.Equal(MqttReasonCodes.PacketTooLarge, exception.ReasonCode);
    }

    [Fact]
    public async Task Invalid_utf8_string_is_rejected()
    {
        // CONNECT body with an invalid protocol name is rejected earlier; use a
        // PUBLISH with a topic containing an invalid UTF-8 byte.
        var bytes = new byte[] { 0x30, 0x04, 0x00, 0x02, 0xC3, 0x28 };
        var exception = await Assert.ThrowsAsync<MqttProtocolException>(
            async () => await PacketTestHelpers.ReadAsync(bytes, MqttProtocolVersion.V500));
        Assert.Equal(MqttReasonCodes.MalformedPacket, exception.ReasonCode);
    }

    [Fact]
    public void Duplicate_scalar_property_is_rejected()
    {
        var properties = new MqttProperties().AddUInt32(MqttPropertyId.MessageExpiryInterval, 10);
        Assert.Throws<MqttProtocolException>(() => properties.AddUInt32(MqttPropertyId.MessageExpiryInterval, 20));
    }

    [Fact]
    public void User_properties_may_repeat()
    {
        var properties = new MqttProperties()
            .AddUserProperty("a", "1")
            .AddUserProperty("a", "2");

        Assert.Equal(2, properties.GetUserProperties().Count());
    }

    [Fact]
    public void V311_rejects_properties_on_write()
    {
        var publish = new PublishPacket
        {
            Topic = "a",
            Properties = new MqttProperties().AddString(MqttPropertyId.ContentType, "text/plain"),
        };

        Assert.Throws<InvalidOperationException>(
            () => PacketTestHelpers.Encode(publish, MqttProtocolVersion.V311));
    }
}
