using NextMqtt.Broker.Subscriptions;
using NextMqtt.Core.Protocol;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker.Tests;

public class SubscriptionIndexTests
{
    private static Subscription Sub(string clientId, string filter, MqttQoS qos = MqttQoS.AtMostOnce, int? identifier = null)
        => new()
        {
            ClientId = clientId,
            Filter = TopicFilter.Parse(filter),
            Options = new MqttSubscriptionOptions(qos),
            Identifier = identifier,
        };

    [Fact]
    public void Matches_wildcards_and_reports_identifier()
    {
        var index = new SubscriptionIndex();
        index.AddOrReplace(Sub("c1", "a/+/c", MqttQoS.AtLeastOnce, 7));
        index.AddOrReplace(Sub("c2", "a/#"));

        var matches = index.Match("a/b/c");

        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, match => match.Subscription.ClientId == "c1" && match.Subscription.Identifier == 7);
        Assert.Contains(matches, match => match.Subscription.ClientId == "c2");
    }

    [Fact]
    public void Replace_updates_options_without_duplicates()
    {
        var index = new SubscriptionIndex();
        index.AddOrReplace(Sub("c1", "a/#", MqttQoS.AtMostOnce));
        index.AddOrReplace(Sub("c1", "a/#", MqttQoS.ExactlyOnce));

        var matches = index.Match("a/b");

        var match = Assert.Single(matches);
        Assert.Equal(MqttQoS.ExactlyOnce, match.Subscription.Options.QoS);
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public void Shared_subscriptions_round_robin()
    {
        var index = new SubscriptionIndex();
        index.AddOrReplace(Sub("c1", "$share/work/jobs/#"));
        index.AddOrReplace(Sub("c2", "$share/work/jobs/#"));
        index.AddOrReplace(Sub("c3", "$share/work/jobs/#"));

        var seen = new HashSet<string>();

        for (var i = 0; i < 6; i++)
        {
            var match = Assert.Single(index.Match("jobs/1"));
            Assert.Equal("work", match.SharedGroup);
            seen.Add(match.Subscription.ClientId);
        }

        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public void Different_groups_each_receive_one_copy()
    {
        var index = new SubscriptionIndex();
        index.AddOrReplace(Sub("c1", "$share/g1/jobs/#"));
        index.AddOrReplace(Sub("c2", "$share/g2/jobs/#"));

        var matches = index.Match("jobs/1");

        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void Remove_all_cleans_shared_groups()
    {
        var index = new SubscriptionIndex();
        index.AddOrReplace(Sub("c1", "$share/work/jobs/#"));
        index.AddOrReplace(Sub("c1", "a/b"));

        Assert.Equal(2, index.RemoveAll("c1").Count);
        Assert.Empty(index.Match("jobs/1"));
        Assert.Empty(index.Match("a/b"));
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void No_local_is_exposed_to_the_router()
    {
        var index = new SubscriptionIndex();
        index.AddOrReplace(new Subscription
        {
            ClientId = "c1",
            Filter = TopicFilter.Parse("a/#"),
            Options = new MqttSubscriptionOptions(MqttQoS.AtMostOnce, NoLocal: true),
        });

        var match = Assert.Single(index.Match("a/b"));
        Assert.True(match.Subscription.Options.NoLocal);
    }

    [Fact]
    public void Case_sensitive_matching()
    {
        var index = new SubscriptionIndex();
        index.AddOrReplace(Sub("c1", "A/#"));

        Assert.Empty(index.Match("a/b"));
    }
}
