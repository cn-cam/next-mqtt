using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using NextMqtt.Broker.Options;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker.Security;

public interface ITopicAuthorizer
{
    bool CanPublish(ClientPrincipal principal, string topic);

    bool CanSubscribe(ClientPrincipal principal, string filter);
}

/// <summary>
/// Ordered ACL rules. The first rule whose action, username and client id
/// patterns match decides; otherwise the default policy applies.
/// Topic filters may contain <c>${username}</c> and <c>${clientid}</c>.
/// </summary>
public sealed class TopicAuthorizer : ITopicAuthorizer
{
    private readonly List<CompiledRule> _rules;
    private readonly AclDefaultPolicy _defaultPolicy;
    private readonly ConcurrentDictionary<string, TopicFilter?> _filterCache = new(StringComparer.Ordinal);

    public TopicAuthorizer(AclOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _rules = options.Rules.Select(Compile).ToList();
        _defaultPolicy = options.DefaultPolicy;
    }

    public bool CanPublish(ClientPrincipal principal, string topic)
        => Evaluate(principal, topic, isSubscription: false);

    public bool CanSubscribe(ClientPrincipal principal, string filter)
    {
        if (filter.StartsWith("$share/", StringComparison.Ordinal))
        {
            if (!TopicFilter.TryParse(filter, out var parsed, out _))
            {
                return false;
            }

            filter = parsed.Filter;
        }

        return Evaluate(principal, filter, isSubscription: true);
    }

    private bool Evaluate(ClientPrincipal principal, string topic, bool isSubscription)
    {
        if (principal.IsSuperuser)
        {
            return true;
        }

        foreach (var rule in _rules)
        {
            if (rule.Action != AclAction.Both && rule.Action != (isSubscription ? AclAction.Subscribe : AclAction.Publish))
            {
                continue;
            }

            if (!Matches(rule.Username, principal.Username) || !Matches(rule.ClientId, principal.ClientId))
            {
                continue;
            }

            var expanded = ExpandPlaceholders(rule.TopicPattern, principal);
            var filter = _filterCache.GetOrAdd(expanded, static value => TopicFilter.TryParse(value, out var parsed, out _) ? parsed : null);

            if (filter is null)
            {
                continue;
            }

            var allowed = isSubscription
                ? FilterCovers(filter, topic)
                : filter.Matches(topic);

            if (allowed)
            {
                return rule.Effect == AclEffect.Allow;
            }
        }

        return _defaultPolicy == AclDefaultPolicy.Allow;
    }

    private static CompiledRule Compile(AclRuleOptions rule)
    {
        if (string.IsNullOrEmpty(rule.Topic))
        {
            throw new ArgumentException("ACL topic filter must not be empty.");
        }

        return new CompiledRule(
            rule.Topic,
            rule.Effect,
            rule.Action,
            rule.Username is null ? null : GlobToRegex(rule.Username),
            rule.ClientId is null ? null : GlobToRegex(rule.ClientId));
    }

    private static string ExpandPlaceholders(string topic, ClientPrincipal principal)
        => topic
            .Replace("${username}", principal.Username ?? string.Empty, StringComparison.Ordinal)
            .Replace("${clientid}", principal.ClientId ?? string.Empty, StringComparison.Ordinal);

    private static bool Matches(Regex? pattern, string? value)
    {
        if (pattern is null)
        {
            return true;
        }

        return value is not null && pattern.IsMatch(value);
    }

    private static Regex GlobToRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal);

        return new Regex($"^{escaped}$", RegexOptions.CultureInvariant);
    }

    private static bool FilterCovers(TopicFilter aclFilter, string requestedFilter)
    {
        if (!MqttTopic.IsValidFilter(requestedFilter, out _))
        {
            return false;
        }

        if (requestedFilter.StartsWith('$')
            && (aclFilter.Levels[0] == "#" || aclFilter.Levels[0] == "+"))
        {
            return false;
        }

        var requested = MqttTopic.SplitLevels(requestedFilter);
        var allowed = aclFilter.Levels;

        for (var i = 0; i < requested.Length; i++)
        {
            if (i >= allowed.Length)
            {
                return false;
            }

            var aclLevel = allowed[i];
            var requestLevel = requested[i];

            if (aclLevel == "#")
            {
                return true;
            }

            if (requestLevel == "#")
            {
                return false;
            }

            if (requestLevel == "+")
            {
                if (aclLevel != "+")
                {
                    return false;
                }

                continue;
            }

            if (aclLevel != "+" && !string.Equals(aclLevel, requestLevel, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return allowed.Length == requested.Length
            || (allowed.Length == requested.Length + 1 && allowed[^1] == "#");
    }

    private sealed record CompiledRule(
        string TopicPattern,
        AclEffect Effect,
        AclAction Action,
        Regex? Username,
        Regex? ClientId);
}
