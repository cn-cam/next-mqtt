using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NextMqtt.Broker.Clustering;

/// <summary>JSON contracts for the signed node-to-node API.</summary>
public static class ClusterJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };
}

public sealed record ClusterHeartbeatRequest(
    string NodeId,
    string AdvertiseUrl,
    long Timestamp,
    string[] Filters);

public sealed record ClusterHeartbeatResponse(
    string NodeId,
    long Timestamp,
    ClusterMemberDto[] Members);

public sealed record ClusterMemberDto(
    string NodeId,
    string AdvertiseUrl,
    string Status,
    long LastSeenUnix,
    string[] Filters);

public sealed record ClusterPropertyDto(
    int Id,
    string? Text,
    long? Number,
    string? Binary,
    string? UserValue);

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
    public const string TimestampHeader = "X-NextMQTT-Timestamp";
    public const string SignatureHeader = "X-NextMQTT-Signature";
    private static readonly TimeSpan AllowedSkew = TimeSpan.FromMinutes(5);

    public static string Sign(string secret, string timestamp, string body)
    {
        var payload = Encoding.UTF8.GetBytes($"{timestamp}.{body}");
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        return Convert.ToHexString(hash);
    }

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
