using NextMqtt.Core.Protocol;

namespace NextMqtt.Server.Tests;

public class SubscriptionIdentifierTests
{
    [Fact]
    public async Task Identifier_quota_is_enforced_per_client()
    {
        await using var server = MqttTestServer.Start(configure: options =>
            options.MaxSubscriptionIdentifiersPerClient = 1);

        await using var client = await server.ConnectAsync(new ConnectPacket
        {
            ClientId = "identified",
            ProtocolVersion = MqttProtocolVersion.V500,
            CleanStart = true,
            KeepAliveSeconds = 30,
            Properties = new MqttProperties(),
        });

        var first = await SubscribeAsync(client, 1, "a/#", identifier: 7);
        Assert.Equal(new byte[] { 0 }, first.ReasonCodes);

        var second = await SubscribeAsync(client, 2, "b/#", identifier: 8);
        Assert.Equal(new byte[] { MqttReasonCodes.QuotaExceeded }, second.ReasonCodes);

        // Subscriptions without an identifier are not limited by this quota.
        var third = await SubscribeAsync(client, 3, "c/#", identifier: null);
        Assert.Equal(new byte[] { 0 }, third.ReasonCodes);
    }

    private static async Task<SubAckPacket> SubscribeAsync(
        MqttTestClient client,
        ushort packetId,
        string filter,
        int? identifier)
    {
        var properties = new MqttProperties();

        if (identifier is { } value)
        {
            properties.AddVariableByteInteger(MqttPropertyId.SubscriptionIdentifier, value);
        }

        await client.SendAsync(new SubscribePacket
        {
            PacketId = packetId,
            Properties = properties,
            Subscriptions = [new MqttSubscriptionRequest(filter, new MqttSubscriptionOptions(MqttQoS.AtMostOnce))],
        });

        return await client.ReadAsync<SubAckPacket>();
    }
}
