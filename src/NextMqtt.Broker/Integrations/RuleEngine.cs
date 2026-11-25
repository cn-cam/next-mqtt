using Microsoft.Extensions.Logging;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker.Integrations;

/// <summary>Routing rules applied before a publish is stored, delivered or forwarded.</summary>
/// <remarks>
/// ``drop`` 丢弃命中过滤器的消息；``rewrite`` 把消息改写到目标主题（保留过滤器通配符之后的部分）。
/// 规则按配置顺序求值，drop 优先于 rewrite，改写结果必须在后续环节仍然合法。
/// </remarks>
public sealed class RuleEngine
{
    private readonly List<TopicFilter> _dropFilters;
    private readonly List<RewriteRule> _rewriteRules;

    /// <summary>Compiles the configured rules once at startup.</summary>
    /// <param name="options">Rules from configuration; invalid rules are skipped with a warning.</param>
    /// <param name="logger">Logger for skipped rules.</param>
    public RuleEngine(IntegrationsOptions options, ILogger logger)
    {
        _dropFilters = [];
        _rewriteRules = [];

        foreach (var rule in options.Rules)
        {
            if (!MqttTopic.IsValidFilter(rule.Filter, out var error))
            {
                logger.LogWarning("Ignoring rule for {Filter}: {Error}", rule.Filter, error);
                continue;
            }

            if (string.Equals(rule.Action, "drop", StringComparison.OrdinalIgnoreCase))
            {
                _dropFilters.Add(TopicFilter.Parse(rule.Filter));
                continue;
            }

            if (string.Equals(rule.Action, "rewrite", StringComparison.OrdinalIgnoreCase))
            {
                var target = rule.Target;
                string? targetError = null;

                if (string.IsNullOrWhiteSpace(target)
                    || !MqttTopic.IsValidTopicName(target!, out targetError))
                {
                    logger.LogWarning(
                        "Ignoring rewrite rule for {Filter}: {Error}",
                        rule.Filter,
                        targetError ?? "Target is required");
                    continue;
                }

                _rewriteRules.Add(new RewriteRule(
                    TopicFilter.Parse(rule.Filter),
                    target!,
                    LiteralPrefix(rule.Filter)));
                continue;
            }

            logger.LogWarning(
                "Ignoring rule for {Filter}: unknown action {Action}",
                rule.Filter,
                rule.Action);
        }
    }

    /// <summary>Number of compiled drop rules.</summary>
    public int DropRuleCount => _dropFilters.Count;

    /// <summary>Number of compiled rewrite rules.</summary>
    public int RewriteRuleCount => _rewriteRules.Count;

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

    /// <summary>Applies the first matching rewrite rule.</summary>
    /// <param name="topic">Original topic name.</param>
    /// <param name="rewritten">New topic when a rule matched.</param>
    /// <returns><c>true</c> when the topic must be replaced.</returns>
    public bool TryRewrite(string topic, out string rewritten)
    {
        foreach (var rule in _rewriteRules)
        {
            if (!rule.Filter.Matches(topic))
            {
                continue;
            }

            if (rule.Prefix is null)
            {
                rewritten = rule.Target;
                return true;
            }

            var remainder = topic.Length > rule.Prefix.Length ? topic[rule.Prefix.Length..] : string.Empty;
            rewritten = rule.Target + remainder;
            return true;
        }

        rewritten = topic;
        return false;
    }

    /// <summary>Returns the literal part of a filter before the first wildcard level.</summary>
    /// <param name="filter">Validated filter text.</param>
    /// <returns>Prefix including its trailing slash, or null when the filter starts with a wildcard.</returns>
    private static string? LiteralPrefix(string filter)
    {
        var levels = filter.Split('/');
        var literal = new List<string>(levels.Length);

        foreach (var level in levels)
        {
            if (level is "+" or "#")
            {
                break;
            }

            literal.Add(level);
        }

        if (literal.Count == levels.Length)
        {
            // No wildcard: the whole topic is replaced by the target.
            return null;
        }

        return literal.Count == 0 ? null : string.Join('/', literal) + "/";
    }

    /// <summary>One rewrite rule with its precomputed literal prefix.</summary>
    /// <param name="Filter">Filter that selects messages.</param>
    /// <param name="Target">Replacement prefix or full topic.</param>
    /// <param name="Prefix">Literal prefix to strip, or null for a full replacement.</param>
    private sealed record RewriteRule(TopicFilter Filter, string Target, string? Prefix);
}
