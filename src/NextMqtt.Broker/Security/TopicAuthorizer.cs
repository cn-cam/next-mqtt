using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using NextMqtt.Broker.Options;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker.Security;

/// <summary>Decides whether a principal may publish or subscribe to a topic.</summary>
/// <remarks>返回布尔值不抛异常，拒绝原因由调用方映射成原因码。</remarks>
public interface ITopicAuthorizer
{
    /// <summary>True when the principal may publish to the topic.</summary>
    /// <remarks>每次发布都检查，不缓存结果。</remarks>
    /// <param name="principal">Authenticated identity.</param>
    /// <param name="topic">Concrete topic name.</param>
    bool CanPublish(ClientPrincipal principal, string topic);

    /// <summary>True when the principal may subscribe to the filter.</summary>
    /// <remarks>共享订阅前缀会被剥掉，再按普通过滤器判断。</remarks>
    /// <param name="principal">Authenticated identity.</param>
    /// <param name="filter">Requested filter, wildcards allowed.</param>
    bool CanSubscribe(ClientPrincipal principal, string filter);
}

/// <summary>
/// Ordered ACL rules. The first rule whose action, username and client id
/// patterns match decides; otherwise the default policy applies.
/// Topic filters may contain <c>${username}</c> and <c>${clientid}</c>.
/// </summary>
/// <remarks>规则顺序即优先级；先按动作与主体筛选，再判断主题是否被覆盖，第一条命中即返回。</remarks>
public sealed class TopicAuthorizer : ITopicAuthorizer
{
    /// <summary>Compiled rules in configuration order.</summary>
    private readonly List<CompiledRule> _rules;
    /// <summary>Applied when no rule matches.</summary>
    private readonly AclDefaultPolicy _defaultPolicy;
    /// <summary>Expanded topic patterns, parsed once per distinct string.</summary>
    private readonly ConcurrentDictionary<string, TopicFilter?> _filterCache = new(StringComparer.Ordinal);

    /// <summary>Compiles the rule list once at startup.</summary>
    /// <remarks>用户名与客户端 ID 的 glob 只编译一次；主题模式含占位符，只能按请求展开。</remarks>
    /// <param name="options">Ordered ACL rules and the default policy.</param>
    public TopicAuthorizer(AclOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _rules = options.Rules.Select(Compile).ToList();
        _defaultPolicy = options.DefaultPolicy;
    }

    /// <summary>Evaluates publish access against the compiled rules.</summary>
    /// <remarks>发布只需通配匹配。</remarks>
    /// <param name="principal">Authenticated identity.</param>
    /// <param name="topic">Concrete topic name.</param>
    public bool CanPublish(ClientPrincipal principal, string topic)
        => Evaluate(principal, topic, isSubscription: false);

    /// <summary>Evaluates subscribe access, including filter containment.</summary>
    /// <remarks>请求 audit/# 而规则只允许 audit/1 时必须拒绝，否则可以用更宽的过滤器绕过。</remarks>
    /// <param name="principal">Authenticated identity.</param>
    /// <param name="filter">Requested filter.</param>
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

    /// <summary>Shared rule loop for publish and subscribe checks.</summary>
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

    /// <summary>Precompiles the globs and rejects an empty topic pattern.</summary>
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

    /// <summary>Substitutes username and client id into the rule's filter.</summary>
    private static string ExpandPlaceholders(string topic, ClientPrincipal principal)
        => topic
            .Replace("${username}", principal.Username ?? string.Empty, StringComparison.Ordinal)
            .Replace("${clientid}", principal.ClientId ?? string.Empty, StringComparison.Ordinal);

    /// <summary>A null pattern means no restriction on that field.</summary>
    private static bool Matches(Regex? pattern, string? value)
    {
        if (pattern is null)
        {
            return true;
        }

        return value is not null && pattern.IsMatch(value);
    }

    /// <summary>Anchors * and ? into a case-sensitive regular expression.</summary>
    private static Regex GlobToRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal);

        return new Regex($"^{escaped}$", RegexOptions.CultureInvariant);
    }

    /// <summary>True when the rule covers every topic the requested filter could match.</summary>
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
