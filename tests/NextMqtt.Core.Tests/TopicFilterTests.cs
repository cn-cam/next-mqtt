using NextMqtt.Core.Topics;

namespace NextMqtt.Core.Tests;

public class TopicFilterTests
{
    [Theory]
    [InlineData("sport/tennis/player1", "sport/tennis/player1", true)]
    [InlineData("sport/tennis/player1", "sport/tennis/player2", false)]
    [InlineData("sport/tennis/#", "sport/tennis/player1", true)]
    [InlineData("sport/tennis/#", "sport/tennis", true)]
    [InlineData("sport/#", "sport", true)]
    [InlineData("sport/+/player1", "sport/tennis/player1", true)]
    [InlineData("sport/+/player1", "sport/tennis/clay/player1", false)]
    [InlineData("+/+", "a/b", true)]
    [InlineData("+", "a", true)]
    [InlineData("+", "a/b", false)]
    [InlineData("#", "anything/at/all", true)]
    [InlineData("#", "$SYS/broker/load", false)]
    [InlineData("+/monitor", "$SYS/monitor", false)]
    [InlineData("$SYS/#", "$SYS/broker/load", true)]
    [InlineData("$SYS/+", "$SYS/broker", true)]
    [InlineData("a/", "a/", true)]
    [InlineData("a/+", "a/", true)]
    [InlineData("/+", "/finance", true)]
    public void Matches_returns_expected_result(string filter, string topic, bool expected)
    {
        Assert.Equal(expected, TopicFilter.Parse(filter).Matches(topic));
    }

    [Theory]
    [InlineData("sport/#/ranking")]
    [InlineData("sport+")]
    [InlineData("sport/tennis#")]
    [InlineData("sport/#/")]
    [InlineData("")]
    [InlineData("$share/")]
    [InlineData("$share/group")]
    [InlineData("$share/group/")]
    [InlineData("$share/gr+oup/topic")]
    public void Invalid_filters_are_rejected(string filter)
    {
        Assert.False(TopicFilter.TryParse(filter, out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a/+/b")]
    [InlineData("a/#")]
    [InlineData("#")]
    [InlineData("$SYS/#")]
    public void Valid_filters_are_accepted(string filter)
    {
        Assert.True(TopicFilter.TryParse(filter, out var parsed, out var error));
        Assert.Null(error);
        Assert.Equal(filter, parsed.Original);
    }

    [Fact]
    public void Shared_subscription_is_parsed()
    {
        var filter = TopicFilter.Parse("$share/workers/jobs/+/ready");

        Assert.True(filter.IsShared);
        Assert.Equal("workers", filter.SharedGroup);
        Assert.Equal("jobs/+/ready", filter.Filter);
        Assert.True(filter.Matches("jobs/42/ready"));
    }

    [Fact]
    public void Shared_subscription_group_is_preserved()
    {
        var filter = TopicFilter.Parse("$share/analytics/metrics/#");
        Assert.Equal("$share/analytics/metrics/#", filter.ToString());
        Assert.Equal("analytics", filter.SharedGroup);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/+")]
    [InlineData("a/#")]
    [InlineData("a/\0b")]
    public void Invalid_topic_names_are_rejected(string topic)
    {
        Assert.False(MqttTopic.IsValidTopicName(topic, out _));
    }

    [Fact]
    public void Topic_matching_is_case_sensitive()
    {
        Assert.False(TopicFilter.Parse("Sensor/#").Matches("sensor/1"));
    }
}
