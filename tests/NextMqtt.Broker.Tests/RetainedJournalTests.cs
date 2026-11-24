using System.Text;
using NextMqtt.Broker.Messaging;
using NextMqtt.Broker.Storage;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Tests;

public sealed class RetainedJournalTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nextmqtt-tests",
        Guid.NewGuid().ToString("N"));

    private string JournalPath => Path.Combine(_directory, "retained.log");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static RetainedMessage Message(string topic, string payload, MqttQoS qos = MqttQoS.AtLeastOnce)
    {
        var properties = new MqttProperties();
        properties.AddString(MqttPropertyId.ContentType, "text/plain");
        properties.AddByte(MqttPropertyId.PayloadFormatIndicator, 1);
        properties.AddUserProperty("source", "test");

        return new RetainedMessage
        {
            Topic = topic,
            Payload = Encoding.UTF8.GetBytes(payload),
            QoS = qos,
            Properties = properties,
            StoredAt = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public void Messages_survive_a_restart_with_properties()
    {
        using (var journal = new FileRetainedJournal(JournalPath))
        {
            var store = new RetainedMessageStore(journal);
            store.Set(Message("factory/line1/state", "running"));
        }

        using var reopened = new FileRetainedJournal(JournalPath);
        var restored = new RetainedMessageStore(reopened);

        var message = Assert.Single(restored.Snapshot());
        Assert.Equal("factory/line1/state", message.Topic);
        Assert.Equal("running", Encoding.UTF8.GetString(message.Payload));
        Assert.Equal(MqttQoS.AtLeastOnce, message.QoS);
        Assert.Equal("text/plain", message.Properties.GetString(MqttPropertyId.ContentType));
        Assert.Equal((byte)1, message.Properties.GetByte(MqttPropertyId.PayloadFormatIndicator));
        Assert.Contains(
            message.Properties.GetUserProperties(),
            pair => pair.Key == "source" && pair.Value == "test");
    }

    [Fact]
    public void Removal_is_journaled()
    {
        using (var journal = new FileRetainedJournal(JournalPath))
        {
            var store = new RetainedMessageStore(journal);
            store.Set(Message("a/b", "one"));
            store.Set(Message("a/c", "two"));
            store.Remove("a/b");
        }

        using var reopened = new FileRetainedJournal(JournalPath);
        var restored = new RetainedMessageStore(reopened);

        var message = Assert.Single(restored.Snapshot());
        Assert.Equal("a/c", message.Topic);
    }

    [Fact]
    public void Clear_is_journaled()
    {
        using (var journal = new FileRetainedJournal(JournalPath))
        {
            var store = new RetainedMessageStore(journal);
            store.Set(Message("a/b", "one"));
            store.Set(Message("a/c", "two"));
            store.Clear();
        }

        using var reopened = new FileRetainedJournal(JournalPath);
        var restored = new RetainedMessageStore(reopened);

        Assert.Empty(restored.Snapshot());
    }

    [Fact]
    public void Expired_messages_are_not_restored()
    {
        using (var journal = new FileRetainedJournal(JournalPath))
        {
            var store = new RetainedMessageStore(journal);
            store.Set(new RetainedMessage
            {
                Topic = "a/expired",
                Payload = "x"u8.ToArray(),
                StoredAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            });
            store.Set(Message("a/live", "y"));
        }

        using var reopened = new FileRetainedJournal(JournalPath);
        var restored = new RetainedMessageStore(reopened);

        var message = Assert.Single(restored.Snapshot());
        Assert.Equal("a/live", message.Topic);
    }

    [Fact]
    public void Journal_compacts_after_the_threshold()
    {
        var path = JournalPath;

        using (var journal = new FileRetainedJournal(path, compactThresholdBytes: 2048))
        {
            var store = new RetainedMessageStore(journal);

            for (var index = 0; index < 200; index++)
            {
                store.Set(Message("churn/value", new string('x', 64) + index));
            }
        }

        var length = new FileInfo(path).Length;
        Assert.True(length < 4096, $"journal should have been compacted, was {length} bytes");

        using var reopened = new FileRetainedJournal(path);
        var restored = new RetainedMessageStore(reopened);

        var message = Assert.Single(restored.Snapshot());
        Assert.Equal("churn/value", message.Topic);
    }

    [Fact]
    public void Torn_last_line_is_ignored()
    {
        using (var journal = new FileRetainedJournal(JournalPath))
        {
            var store = new RetainedMessageStore(journal);
            store.Set(Message("a/b", "one"));
        }

        File.AppendAllText(JournalPath, "{\"op\":\"set\",\"topic\":\"a/c\"");

        using var reopened = new FileRetainedJournal(JournalPath);
        var restored = new RetainedMessageStore(reopened);

        Assert.Equal("a/b", Assert.Single(restored.Snapshot()).Topic);
    }
}
