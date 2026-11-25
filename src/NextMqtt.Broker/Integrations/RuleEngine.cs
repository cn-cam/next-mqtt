using Microsoft.Extensions.Logging;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker.Integrations;

/// <summary>Routing rules applied before a publish is stored or delivered.</summary>
/// <remarks>
/// 目前支持丢弃动作：命中过滤器的消息不进入保留存储、不分发给订阅者、不转发到集群，
/// 但仍计入接收与丢弃计数。适合屏蔽调试主题或设备噪声。
/// </remarks>
public sealed class RuleEngine
{
    private readonly List<TopicFilter> _dropFilters;

    /// <summary>Compiles the configured rules once at startup.</summary>
    /// <param name="options">Rules from configuration; invalid filters are skipped.</param>
    /// <param name="logger">Logger for skipped rules.</param>
    public RuleEngine(IntegrationsOptions options, Microsoft.Extensions.Logging.ILogger logger)
    {
        _dropFilters = [];

        foreach (var rule in options.Rules)
        {
            if (!string.Equals(rule.Action, "drop", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Ignoring rule for {Filter}: unknown action {Action}",
                    rule.Filter,
                    rule.Action);
                continue;
            }

            if (!MqttTopic.IsValidFilter(rule.Filter, out var error))
            {
                logger.LogWarning("Ignoring drop rule: {Error}", error);
                continue;
            }

            _dropFilters.Add(TopicFilter.Parse(rule.Filter));
        }
    }

    /// <summary>Number of compiled drop rules.</summary>
    public int DropRuleCount => _dropFilters.Count;

    /// <summary>True when a rule discards messages for this topic.</summary>
    /// <param name="topic">Concrete topic name.</param>
    public bool ShouldDrop(string topic)
    {
        foreach (var filter in _dropFilters)
        {
            if (filter.Matches(topic))
            {
                return true;
            }
        }

        return false;
    }
}
