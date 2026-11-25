using System.Text;
using NextMqtt.Broker.Messaging;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Tests;

public sealed class RetainedConflictTests
{
    private static RetainedMessage Message(string topic, string payload, DateTimeOffset at)
        => new()
        {
            Topic = topic,
            Payload = Encoding.UTF8.GetBytes(payload),
            QoS = MqttQoS.AtMostOnce,
            StoredAt = at,
        };

    [Fact]
    public void Older_write_does_not_replace_a_newer_value()
    {
        var store = new RetainedMessageStore();
        var now = DateTimeOffset.UtcNow;

        store.Set(Message("state/a", "new", now));
        store.Set(Message("state/a", "old", now.AddMinutes(-5)));

        Assert.Equal("new", Encoding.UTF8.GetString(Assert.Single(store.Snapshot()).Payload));
    }

    [Fact]
    public async Task Administrative_clear_removes_every_retained_message()
    {
        await using var broker = TestBroker.Create();

        await broker.PublishAsync("state/a", "one"u8.ToArray(), MqttQoS.AtMostOnce, retain: true);
        await broker.PublishAsync("state/b", "two"u8.ToArray(), MqttQoS.AtMostOnce, retain: true);

        Assert.Equal(2, broker.ClearRetained());
        Assert.Empty(broker.GetRetained());
    }

    [Fact]
    public void Delete_blocks_resurrection_by_an_older_replica()
    {
        var store = new RetainedMessageStore();
        var now = DateTimeOffset.UtcNow;

        store.Set(Message("state/a", "value", now.AddMinutes(-10)));
        store.Remove("state/a", now);

        // A peer that missed the delete now forwards its older copy.
        store.Set(Message("state/a", "stale", now.AddMinutes(-10)));

        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void Write_after_delete_with_a_newer_timestamp_is_kept()
    {
        var store = new RetainedMessageStore();
        var now = DateTimeOffset.UtcNow;

        store.Remove("state/a", now);
        store.Set(Message("state/a", "fresh", now.AddSeconds(1)));

        Assert.Equal("fresh", Encoding.UTF8.GetString(Assert.Single(store.Snapshot()).Payload));
    }

    [Fact]
    public void Older_delete_does_not_remove_a_newer_value()
    {
        var store = new RetainedMessageStore();
        var now = DateTimeOffset.UtcNow;

        store.Set(Message("state/a", "value", now));
        var removed = store.Remove("state/a", now.AddMinutes(-1));

        Assert.False(removed);
        Assert.Single(store.Snapshot());
    }
}
