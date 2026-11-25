using System.Text;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Server.Tests;

public class TopicAliasTests
{
    private static ConnectPacket Connect(
        string clientId,
        ushort aliasMaximum = 0,
        MqttProtocolVersion version = MqttProtocolVersion.V500)
    {
        var properties = new MqttProperties();

        if (version == MqttProtocolVersion.V500 && aliasMaximum > 0)
        {
            properties.AddUInt16(MqttPropertyId.TopicAliasMaximum, aliasMaximum);
        }

        return new ConnectPacket
        {
            ClientId = clientId,
            ProtocolVersion = version,
            CleanStart = true,
            KeepAliveSeconds = 30,
            Properties = properties,
        };
    }

    [Fact]
    public async Task Repeated_topics_use_the_advertised_alias_budget()
    {
        await using var server = MqttTestServer.Start();
        await using var subscriber = await server.ConnectAsync(Connect("alias-sub", aliasMaximum: 2));
        await using var publisher = await server.ConnectAsync(Connect("alias-pub"));

        await subscriber.SubscribeAsync("alias/#", MqttQoS.AtMostOnce);

        await publisher.PublishAsync("alias/a", "one");
        var first = await subscriber.ReadAsync<PublishPacket>();
        Assert.Equal("alias/a", first.Topic);
        Assert.Equal((ushort)1, first.Properties.GetUInt16(MqttPropertyId.TopicAlias));

        await publisher.PublishAsync("alias/a", "two");
        var second = await subscriber.ReadAsync<PublishPacket>();
        Assert.Equal(string.Empty, second.Topic);
        Assert.Equal((ushort)1, second.Properties.GetUInt16(MqttPropertyId.TopicAlias));
        Assert.Equal("two", Encoding.UTF8.GetString(second.Payload));

        await publisher.PublishAsync("alias/b", "three");
        var third = await subscriber.ReadAsync<PublishPacket>();
        Assert.Equal("alias/b", third.Topic);
        Assert.Equal((ushort)2, third.Properties.GetUInt16(MqttPropertyId.TopicAlias));

        // The budget is two aliases, so a third topic keeps its full name.
        await publisher.PublishAsync("alias/c", "four");
        var fourth = await subscriber.ReadAsync<PublishPacket>();
        Assert.Equal("alias/c", fourth.Topic);
        Assert.Null(fourth.Properties.GetUInt16(MqttPropertyId.TopicAlias));
    }

    [Fact]
    public async Task Clients_without_an_alias_budget_get_full_topics()
    {
        await using var server = MqttTestServer.Start();
        await using var subscriber = await server.ConnectAsync(Connect("plain-sub", aliasMaximum: 0));
        await using var publisher = await server.ConnectAsync(Connect("plain-pub"));

        await subscriber.SubscribeAsync("plain/#", MqttQoS.AtMostOnce);

        await publisher.PublishAsync("plain/a", "one");
        var first = await subscriber.ReadAsync<PublishPacket>();
        Assert.Equal("plain/a", first.Topic);
        Assert.Null(first.Properties.GetUInt16(MqttPropertyId.TopicAlias));

        await publisher.PublishAsync("plain/a", "two");
        var second = await subscriber.ReadAsync<PublishPacket>();
        Assert.Equal("plain/a", second.Topic);
        Assert.Null(second.Properties.GetUInt16(MqttPropertyId.TopicAlias));
    }
}
