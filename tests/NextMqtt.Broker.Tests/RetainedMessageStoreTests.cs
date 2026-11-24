using NextMqtt.Broker.Messaging;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Tests;

public class RetainedMessageStoreTests
{
    private static PublishPacket Publish(string topic, string payload, MqttQoS qos = MqttQoS.AtMostOnce, uint? expiry = null)
    {
        var properties = new MqttProperties();
        if (expiry is { } seconds)
        {
            properties.AddUInt32(MqttPropertyId.MessageExpiryInterval, seconds);
        }

        return new PublishPacket
        {
            Topic = topic,
            Payload = System.Text.Encoding.UTF8.GetBytes(payload),
            QoS = qos,
            Retain = true,
            Properties = properties,
        };
    }

    [Fact]
    public void Stores_and_matches_by_filter()
    {
        var store = new RetainedMessageStore();
        store.Publish(Publish("sensors/room1/temp", "21"), DateTimeOffset.UtcNow);
        store.Publish(Publish("sensors/room2/temp", "22"), DateTimeOffset.UtcNow);
        store.Publish(Publish("other/data", "x"), DateTimeOffset.UtcNow);

        var matches = store.Match("sensors/+/temp", DateTimeOffset.UtcNow);

        Assert.Equal(2, matches.Count);
        Assert.All(matches, message => Assert.Equal(MqttQoS.AtMostOnce, message.QoS));
    }

    [Fact]
    public void Empty_payload_removes_retained_message()
    {
        var store = new RetainedMessageStore();
        store.Publish(Publish("a/b", "value"), DateTimeOffset.UtcNow);
        store.Publish(new PublishPacket { Topic = "a/b", Payload = [], Retain = true }, DateTimeOffset.UtcNow);

        Assert.Empty(store.Match("a/#", DateTimeOffset.UtcNow));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Replacing_topic_keeps_one_message()
    {
        var store = new RetainedMessageStore();
        store.Publish(Publish("a/b", "one"), DateTimeOffset.UtcNow);
        store.Publish(Publish("a/b", "two"), DateTimeOffset.UtcNow);

        var match = Assert.Single(store.Match("a/b", DateTimeOffset.UtcNow));
        Assert.Equal("two", System.Text.Encoding.UTF8.GetString(match.Payload));
    }

    [Fact]
    public void Expired_messages_are_not_returned_and_are_pruned()
    {
        var store = new RetainedMessageStore();
        var past = DateTimeOffset.UtcNow.AddMinutes(-1);
        store.Set(new RetainedMessage
        {
            Topic = "a/b",
            Payload = [1],
            ExpiresAt = past,
        });

        Assert.Empty(store.Match("a/#", DateTimeOffset.UtcNow));
        Assert.Equal(1, store.PruneExpired(DateTimeOffset.UtcNow));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Wildcard_does_not_expose_dollar_topics()
    {
        var store = new RetainedMessageStore();
        store.Publish(Publish("$SYS/internal", "x"), DateTimeOffset.UtcNow);
        store.Publish(Publish("app/data", "y"), DateTimeOffset.UtcNow);

        var matches = store.Match("#", DateTimeOffset.UtcNow);

        Assert.Single(matches);
        Assert.Equal("app/data", matches[0].Topic);
    }
}
