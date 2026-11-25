using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NextMqtt.Broker.Clustering;

/// <summary>JSON contracts for the signed node-to-node API.</summary>
public static class ClusterJson
{
    /// <summary>Serializer options shared by every cluster request and response.</summary>
    /// <remarks>web 命名策略（camelCase）、忽略 null、属性名不区分大小写。</remarks>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };
}

/// <summary>Heartbeat sent by every node on each gossip round.</summary>
/// <param name="NodeId">Sender identifier.</param>
/// <param name="AdvertiseUrl">URL peers should call back.</param>
/// <param name="Timestamp">Unix seconds; must fall inside the skew window.</param>
/// <param name="Filters">Local subscription filters the sender wants traffic for.</param>
public sealed record ClusterHeartbeatRequest(
    string NodeId,
    string AdvertiseUrl,
    long Timestamp,
    string[] Filters);

/// <summary>Heartbeat answer carrying the receiver's membership view.</summary>
/// <param name="NodeId">Receiver identifier.</param>
/// <param name="Timestamp">Receiver clock in Unix seconds.</param>
/// <param name="Members">Membership table the sender merges into its own.</param>
public sealed record ClusterHeartbeatResponse(
    string NodeId,
    long Timestamp,
    ClusterMemberDto[] Members);

/// <summary>One membership row exchanged between nodes.</summary>
/// <param name="NodeId">Member identifier.</param>
/// <param name="AdvertiseUrl">Base URL for heartbeats and forwarding.</param>
/// <param name="Status">alive, suspect or dead.</param>
/// <param name="LastSeenUnix">Last successful contact, Unix seconds.</param>
/// <param name="Filters">Filters advertised by that member.</param>
public sealed record ClusterMemberDto(
    string NodeId,
    string AdvertiseUrl,
    string Status,
    long LastSeenUnix,
    string[] Filters);

/// <summary>Serializable form of one MQTT 5 property inside a forwarded publish.</summary>
/// <param name="Id">Property identifier.</param>
/// <param name="Text">String value, when the property is textual.</param>
/// <param name="Number">Numeric value, when the property is numeric.</param>
/// <param name="Binary">Base64 value, when the property is binary.</param>
/// <param name="UserValue">Value of a user property (key is <paramref name="Text"/>).</param>
public sealed record ClusterPropertyDto(
    int Id,
    string? Text,
    long? Number,
    string? Binary,
    string? UserValue);

/// <summary>Publish forwarded to peers that have matching subscriptions.</summary>
/// <param name="MessageId">Identifier used for loop and duplicate suppression.</param>
/// <param name="OriginNodeId">Node that first accepted the message.</param>
/// <param name="Hops">Forwarding count; used to bound fan-out.</param>
/// <param name="Topic">Topic name.</param>
/// <param name="Payload">Base64 payload.</param>
/// <param name="Qos">QoS level stored with the message.</param>
/// <param name="Retain">Retain flag from the original publish.</param>
/// <param name="Duplicate">Duplicate flag from the original publish.</param>
/// <param name="Properties">MQTT 5 properties to preserve.</param>
public sealed record ClusterPublishMessage(
    string MessageId,
    string OriginNodeId,
    int Hops,
    string Topic,
    string Payload,
    int Qos,
    bool Retain,
    bool Duplicate,
    ClusterPropertyDto[] Properties);

/// <summary>HMAC-SHA256 request signing with a five minute clock skew window.</summary>
public static class ClusterSignature
{
    /// <summary>Header carrying the Unix timestamp used in the signature.</summary>
    public const string TimestampHeader = "X-NextMQTT-Timestamp";

    /// <summary>Header carrying the hex HMAC-SHA256 signature.</summary>
    public const string SignatureHeader = "X-NextMQTT-Signature";

    /// <summary>Largest accepted difference between node clocks.</summary>
    private static readonly TimeSpan AllowedSkew = TimeSpan.FromMinutes(5);

    /// <summary>Computes the lowercase hex HMAC over <c>timestamp.body</c>.</summary>
    /// <remarks>签名不包含密钥本身；密钥只在本机参与计算，绝不上线传输。</remarks>
    /// <param name="secret">Shared cluster secret.</param>
    /// <param name="timestamp">Unix seconds as sent in the header.</param>
    /// <param name="body">Exact request body bytes decoded as UTF-8.</param>
    /// <returns>Uppercase hex digest.</returns>
    public static string Sign(string secret, string timestamp, string body)
    {
        var payload = Encoding.UTF8.GetBytes($"{timestamp}.{body}");
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        return Convert.ToHexString(hash);
    }

    /// <summary>Validates the timestamp, hex encoding and constant-time signature.</summary>
    /// <remarks>使用固定时间比较，避免通过响应时间猜测签名。</remarks>
    /// <param name="secret">Shared cluster secret.</param>
    /// <param name="timestamp">Header value, Unix seconds.</param>
    /// <param name="signature">Header value, hex digest.</param>
    /// <param name="body">Raw request body.</param>
    /// <param name="error">Reason when verification fails; empty on success.</param>
    /// <returns><c>true</c> when the request is authentic and fresh.</returns>
    public static bool Verify(
        string secret,
        string? timestamp,
        string? signature,
        string body,
        out string error)
    {
        error = string.Empty;

        if (!long.TryParse(timestamp, out var unixSeconds))
        {
            error = "Missing or invalid timestamp.";
            return false;
        }

        var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        if (age > AllowedSkew || age < -AllowedSkew)
        {
            error = "Timestamp is outside the allowed clock skew.";
            return false;
        }

        if (string.IsNullOrEmpty(signature))
        {
            error = "Missing signature.";
            return false;
        }

        var expected = Sign(secret, timestamp!, body);
        byte[] expectedBytes;
        byte[] actualBytes;

        try
        {
            expectedBytes = Convert.FromHexString(expected);
            actualBytes = Convert.FromHexString(signature);
        }
        catch (FormatException)
        {
            error = "Signature is not valid hex.";
            return false;
        }

        if (expectedBytes.Length != actualBytes.Length
            || !CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
        {
            error = "Signature mismatch.";
            return false;
        }

        return true;
    }
}
