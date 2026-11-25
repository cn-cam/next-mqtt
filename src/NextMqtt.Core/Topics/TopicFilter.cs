using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace NextMqtt.Core.Topics;

/// <summary>Validation helpers for MQTT topic names and filters.</summary>
public static class MqttTopic
{
    /// <summary>
    /// Validates a topic name (no wildcards) and reports why it is rejected.
    /// </summary>
    /// <remarks>主题名不得包含通配符或 U+0000，且不超过 65535 个 UTF-8 字节。</remarks>
    /// <param name="topic">Topic name as published by the client.</param>
    /// <param name="error">Human-readable reason when validation fails.</param>
    /// <returns><c>true</c> when the name is a legal MQTT topic name.</returns>
    public static bool IsValidTopicName(string topic, [NotNullWhen(false)] out string? error)
    {
        if (string.IsNullOrEmpty(topic))
        {
            error = "Topic name must not be empty.";
            return false;
        }

        if (topic.Contains('+') || topic.Contains('#'))
        {
            error = "Topic names must not contain wildcard characters.";
            return false;
        }

        if (topic.Contains('\0'))
        {
            error = "Topic names must not contain U+0000.";
            return false;
        }

        if (Encoding.UTF8.GetByteCount(topic) > 65_535)
        {
            error = "Topic name exceeds 65535 UTF-8 bytes.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Validates a topic filter, including wildcard placement rules.
    /// </summary>
    /// <remarks>通配符必须独占整层：<c>#</c> 只能是最后一层。</remarks>
    /// <param name="filter">Filter as supplied by the client.</param>
    /// <param name="error">Human-readable reason when validation fails.</param>
    /// <returns><c>true</c> when the filter is legal.</returns>
    public static bool IsValidFilter(string filter, [NotNullWhen(false)] out string? error)
    {
        if (string.IsNullOrEmpty(filter))
        {
            error = "Topic filter must not be empty.";
            return false;
        }

        if (filter.Contains('\0'))
        {
            error = "Topic filters must not contain U+0000.";
            return false;
        }

        if (Encoding.UTF8.GetByteCount(filter) > 65_535)
        {
            error = "Topic filter exceeds 65535 UTF-8 bytes.";
            return false;
        }

        var levels = filter.Split('/');

        for (var i = 0; i < levels.Length; i++)
        {
            var level = levels[i];

            if (level.Contains('#'))
            {
                if (level != "#")
                {
                    error = "The multi-level wildcard must occupy an entire level.";
                    return false;
                }

                if (i != levels.Length - 1)
                {
                    error = "The multi-level wildcard must be the last level.";
                    return false;
                }
            }

            if (level.Contains('+') && level != "+")
            {
                error = "The single-level wildcard must occupy an entire level.";
                return false;
            }
        }

        error = null;
        return true;
    }

    /// <summary>Splits a topic or filter into levels on '/'.</summary>
    /// <remarks>保留空层，<c>a//b</c> 合法且与订阅匹配语义一致。</remarks>
    /// <param name="value">Topic name or filter.</param>
    /// <returns>Levels in order; empty entries represent empty levels.</returns>
    public static string[] SplitLevels(string value) => value.Split('/');
}

/// <summary>
/// A parsed topic filter, including optional shared-subscription metadata.
/// </summary>
public sealed class TopicFilter
{
    private const string SharedPrefix = "$share/";

    /// <summary>Creates a filter from already-validated parts.</summary>
    /// <param name="original">Text supplied by the client.</param>
    /// <param name="filter">Effective filter without the shared prefix.</param>
    /// <param name="sharedGroup">Group name for shared subscriptions.</param>
    /// <param name="levels">Split levels of the effective filter.</param>
    private TopicFilter(string original, string filter, string? sharedGroup, string[] levels)
    {
        Original = original;
        Filter = filter;
        SharedGroup = sharedGroup;
        Levels = levels;
    }

    /// <summary>The filter exactly as supplied by the client.</summary>
    public string Original { get; }

    /// <summary>The filter without the shared-subscription prefix.</summary>
    public string Filter { get; }

    /// <summary>Shared subscription group, or null for a normal subscription.</summary>
    public string? SharedGroup { get; }

    /// <summary>True when the filter belongs to a shared subscription group.</summary>
    /// <remarks>共享订阅按组内轮询投递，组信息由 <see cref="SharedGroup"/> 提供。</remarks>
    public bool IsShared => SharedGroup is not null;

    /// <summary>Levels of the effective filter, without the shared prefix.</summary>
    public string[] Levels { get; }

    /// <summary>Parses a filter, throwing when it is invalid.</summary>
    /// <remarks>解析失败时抛出 <see cref="ArgumentException"/>，消息包含具体原因。</remarks>
    /// <param name="value">Filter text, optionally starting with <c>$share/{group}/</c>.</param>
    /// <returns>The parsed filter.</returns>
    public static TopicFilter Parse(string value)
    {
        if (!TryParse(value, out var filter, out var error))
        {
            throw new ArgumentException(error, nameof(value));
        }

        return filter;
    }

    /// <summary>
    /// Parses a filter and strips an optional shared-subscription prefix.
    /// </summary>
    /// <remarks><c>$share/group/topic/#</c> 会被拆成 SharedGroup 与真实过滤器。</remarks>
    /// <param name="value">Filter text.</param>
    /// <param name="filter">Parsed filter when valid.</param>
    /// <param name="error">Reason when the filter is invalid.</param>
    /// <returns><c>true</c> when parsing succeeded.</returns>
    public static bool TryParse(string value, [NotNullWhen(true)] out TopicFilter? filter, [NotNullWhen(false)] out string? error)
    {
        filter = null;

        if (!MqttTopic.IsValidFilter(value, out error))
        {
            return false;
        }

        var effective = value;
        string? group = null;

        if (value.StartsWith(SharedPrefix, StringComparison.Ordinal))
        {
            var remainder = value[SharedPrefix.Length..];
            var separator = remainder.IndexOf('/');

            if (separator <= 0 || separator == remainder.Length - 1)
            {
                error = "Shared subscription must have the form $share/{group}/{filter}.";
                return false;
            }

            group = remainder[..separator];
            effective = remainder[(separator + 1)..];

            if (group.Contains('+') || group.Contains('#') || group.Contains('/'))
            {
                error = "Shared subscription group must not contain wildcards or slashes.";
                return false;
            }

            if (effective.StartsWith(SharedPrefix, StringComparison.Ordinal))
            {
                error = "Shared subscription filters cannot be nested.";
                return false;
            }

            if (!MqttTopic.IsValidFilter(effective, out error))
            {
                return false;
            }
        }

        filter = new TopicFilter(value, effective, group, effective.Split('/'));
        return true;
    }

    /// <summary>Returns true when this filter matches the supplied topic name.</summary>
    public bool Matches(string topic)
    {
        if (string.IsNullOrEmpty(topic))
        {
            return false;
        }

        var topicLevels = topic.Split('/');
        var topicIsDollar = topic[0] == '$';

        if (topicIsDollar && (Levels[0] == "#" || Levels[0] == "+"))
        {
            return false;
        }

        var index = 0;

        foreach (var level in Levels)
        {
            if (level == "#")
            {
                return true;
            }

            if (index >= topicLevels.Length)
            {
                return false;
            }

            if (level != "+" && !string.Equals(level, topicLevels[index], StringComparison.Ordinal))
            {
                return false;
            }

            index++;
        }

        return index == topicLevels.Length;
    }

    /// <summary>Returns the original filter text supplied by the client.</summary>
    public override string ToString() => Original;
}
