using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace NextMqtt.Core.Topics;

/// <summary>Validation helpers for MQTT topic names and filters.</summary>
public static class MqttTopic
{
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

    public static string[] SplitLevels(string value) => value.Split('/');
}

/// <summary>
/// A parsed topic filter, including optional shared-subscription metadata.
/// </summary>
public sealed class TopicFilter
{
    private const string SharedPrefix = "$share/";

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

    public bool IsShared => SharedGroup is not null;

    public string[] Levels { get; }

    public static TopicFilter Parse(string value)
    {
        if (!TryParse(value, out var filter, out var error))
        {
            throw new ArgumentException(error, nameof(value));
        }

        return filter;
    }

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

    public override string ToString() => Original;
}
