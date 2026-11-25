using Microsoft.Extensions.Logging.Abstractions;
using NextMqtt.Broker.Integrations;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Tests;

public sealed class RuleEngineTests
{
    private static RuleEngine Create(params (string Filter, string Action)[] rules)
        => new(
            new IntegrationsOptions
            {
                Rules = rules
                    .Select(rule => new RuleOptions { Filter = rule.Filter, Action = rule.Action })
                    .ToList(),
            },
            NullLogger.Instance);

    [Fact]
    public void Drop_rule_matches_its_filter_only()
    {
        var engine = Create(("debug/#", "drop"));

        Assert.True(engine.ShouldDrop("debug/verbose"));
        Assert.True(engine.ShouldDrop("debug"));
        Assert.False(engine.ShouldDrop("prod/state"));
    }

    [Fact]
    public void Unknown_actions_and_filters_are_ignored()
    {
        var engine = Create(("debug/#", "archive"), ("bad/#/filter", "drop"));

        Assert.Equal(0, engine.DropRuleCount);
        Assert.False(engine.ShouldDrop("debug/verbose"));
    }

    [Fact]
    public void Rewrite_replaces_the_literal_prefix()
    {
        var engine = new RuleEngine(
            new IntegrationsOptions
            {
                Rules = [new RuleOptions { Filter = "legacy/#", Action = "rewrite", Target = "modern/" }],
            },
            NullLogger.Instance);

        Assert.True(engine.TryRewrite("legacy/a/b", out var rewritten));
        Assert.Equal("modern/a/b", rewritten);
        Assert.False(engine.TryRewrite("prod/a", out _));
    }

    [Fact]
    public void Rewrite_without_a_wildcard_replaces_the_whole_topic()
    {
        var engine = new RuleEngine(
            new IntegrationsOptions
            {
                Rules = [new RuleOptions { Filter = "old/topic", Action = "rewrite", Target = "new/topic" }],
            },
            NullLogger.Instance);

        Assert.True(engine.TryRewrite("old/topic", out var rewritten));
        Assert.Equal("new/topic", rewritten);
    }

    [Fact]
    public async Task Rewritten_publish_reaches_the_target_subscribers()
    {
        var engine = new RuleEngine(
            new IntegrationsOptions
            {
                Rules = [new RuleOptions { Filter = "legacy/#", Action = "rewrite", Target = "modern/" }],
            },
            NullLogger.Instance);

        var broker = TestBroker.Create(rules: engine);
        await using var _ = broker;

        var (_, _, subscriber) = await TestBroker.ConnectAsync("sub", broker: broker);
        broker.Subscriptions.AddOrReplace(new NextMqtt.Broker.Subscriptions.Subscription
        {
            ClientId = "sub",
            Filter = NextMqtt.Core.Topics.TopicFilter.Parse("modern/#"),
            Options = new MqttSubscriptionOptions(MqttQoS.AtMostOnce),
        });

        await broker.PublishAsync("legacy/sensor", "1"u8.ToArray(), MqttQoS.AtMostOnce);

        var message = Assert.Single(subscriber.Publishes());
        Assert.Equal("modern/sensor", message.Topic);
    }

    [Fact]
    public async Task Dropped_publish_never_reaches_subscribers()
    {
        var broker = TestBroker.Create(rules: Create(("debug/#", "drop")));
        await using var _ = broker;

        var (_, _, subscriber) = await TestBroker.ConnectAsync("sub", broker: broker);
        broker.Subscriptions.AddOrReplace(new NextMqtt.Broker.Subscriptions.Subscription
        {
            ClientId = "sub",
            Filter = NextMqtt.Core.Topics.TopicFilter.Parse("#"),
            Options = new MqttSubscriptionOptions(MqttQoS.AtMostOnce),
        });

        await broker.PublishAsync("debug/trace", "noise"u8.ToArray(), MqttQoS.AtMostOnce);
        await broker.PublishAsync("prod/state", "value"u8.ToArray(), MqttQoS.AtMostOnce);

        var message = Assert.Single(subscriber.Publishes());
        Assert.Equal("prod/state", message.Topic);
        Assert.Equal(1, broker.Metrics.DroppedMessages);
    }
}
