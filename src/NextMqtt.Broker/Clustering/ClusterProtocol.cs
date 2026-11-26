using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NextMqtt.Broker.Storage;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Clustering;

/// <summary>JSON contracts for the signed node-to-node API.</summary>
/// <remarks>只用于节点间通信，不是给客户端或第三方集成的公共 API。</remarks>
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
/// <remarks>同时携带自己的订阅过滤器，对端据此决定要不要转发。</remarks>
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
/// <remarks>响应体同样签名，避免伪造成员表。</remarks>
/// <param name="NodeId">Receiver identifier.</param>
/// <param name="Timestamp">Receiver clock in Unix seconds.</param>
/// <param name="Members">Membership table the sender merges into its own.</param>
public sealed record ClusterHeartbeatResponse(
    string NodeId,
    long Timestamp,
    ClusterMemberDto[] Members);

/// <summary>One membership row exchanged between nodes.</summary>
/// <remarks>状态由接收方按 lastSeen 重算，不直接信任对端标注的状态。</remarks>
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

/// <summary>Serializable form of one MQTT 5 property inside a forwarded publish.</summary>/// <remarks>属性种类决定用哪个字段，其余为 null。</remarks>
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
    string? UserValue)
{
    /// <summary>Packs every property of a publish for the wire.</summary>
    /// <remarks>属性种类决定用哪个字段，其余字段为 null。</remarks>
    /// <param name="properties">Properties to encode.</param>
    public static ClusterPropertyDto[] FromProperties(MqttProperties properties)
        => properties.Items.Select(Encode).ToArray();

    /// <summary>Packs one property for the wire.</summary>
    /// <remarks>供逐个属性编码时使用，语义与 <see cref="FromProperties"/> 相同。</remarks>
    /// <param name="property">Property to encode.</param>
    public static ClusterPropertyDto FromProperty(MqttProperty property) => Encode(property);

    /// <summary>Rebuilds a property bag from the wire form.</summary>
    /// <remarks>无法识别的属性会被跳过：主题与载荷仍然有效，不该因一个属性丢掉整条消息。</remarks>
    /// <param name="items">Encoded properties, or null.</param>
    public static MqttProperties ToProperties(ClusterPropertyDto[]? items)
    {
        var bag = new MqttProperties();

        if (items is null)
        {
            return bag;
        }

        foreach (var item in items)
        {
            try
            {
                bag.Add((MqttPropertyId)item.Id, Decode(item));
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or MqttProtocolException)
            {
                // Skip a property this build cannot represent.
            }
        }

        return bag;
    }

    /// <summary>Chooses the wire field that matches the property's data type.</summary>
    private static ClusterPropertyDto Encode(MqttProperty property)
        => MqttProperties.GetDataType(property.Id) switch
        {
            MqttPropertyDataType.Byte => new ClusterPropertyDto((int)property.Id, null, (byte)property.Value, null, null),
            MqttPropertyDataType.UInt16 => new ClusterPropertyDto((int)property.Id, null, (ushort)property.Value, null, null),
            MqttPropertyDataType.UInt32 => new ClusterPropertyDto((int)property.Id, null, (uint)property.Value, null, null),
            MqttPropertyDataType.VariableByteInteger => new ClusterPropertyDto((int)property.Id, null, (int)property.Value, null, null),
            MqttPropertyDataType.String => new ClusterPropertyDto((int)property.Id, (string)property.Value, null, null, null),
            MqttPropertyDataType.Binary => new ClusterPropertyDto(
                (int)property.Id,
                null,
                null,
                Convert.ToBase64String((byte[])property.Value),
                null),
            MqttPropertyDataType.StringPair => new ClusterPropertyDto(
                (int)property.Id,
                ((KeyValuePair<string, string>)property.Value).Key,
                null,
                null,
                ((KeyValuePair<string, string>)property.Value).Value),
            _ => throw new ArgumentOutOfRangeException(nameof(property)),
        };

    /// <summary>Turns the stored text back into the value the property expects.</summary>
    private static object Decode(ClusterPropertyDto property)
        => MqttProperties.GetDataType((MqttPropertyId)property.Id) switch
        {
            MqttPropertyDataType.Byte => (byte)(property.Number ?? 0),
            MqttPropertyDataType.UInt16 => (ushort)(property.Number ?? 0),
            MqttPropertyDataType.UInt32 => (uint)(property.Number ?? 0),
            MqttPropertyDataType.VariableByteInteger => (int)(property.Number ?? 0),
            MqttPropertyDataType.String => property.Text ?? string.Empty,
            MqttPropertyDataType.Binary => Convert.FromBase64String(property.Binary ?? string.Empty),
            MqttPropertyDataType.StringPair => new KeyValuePair<string, string>(
                property.Text ?? string.Empty,
                property.UserValue ?? string.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(property)),
        };
}

/// <summary>Publish forwarded to peers that have matching subscriptions.</summary>
/// <remarks>payload 用 base64 承载二进制；唯一 ID 让接收端能去重，也用于挑选共享组的所有者。</remarks>
/// <param name="MessageId">Identifier used for loop and duplicate suppression.</param>
/// <param name="OriginNodeId">Node that first accepted the message.</param>
/// <param name="Hops">Forwarding count; used to bound fan-out.</param>
/// <param name="Topic">Topic name.</param>
/// <param name="Payload">Base64 payload.</param>
/// <param name="Qos">QoS level stored with the message.</param>
/// <param name="Retain">Retain flag from the original publish.</param>
/// <param name="Duplicate">Duplicate flag from the original publish.</param>
/// <param name="Properties">MQTT 5 properties to preserve.</param>
/// <param name="SharedGroup">Shared group the message was addressed to, or null for a normal fan-out.</param>
/// <param name="RetainedAtUnix">Retention timestamp, so peers keep the originator's clock for last-write-wins.</param>
public sealed record ClusterPublishMessage(
    string MessageId,
    string OriginNodeId,
    int Hops,
    string Topic,
    string Payload,
    int Qos,
    bool Retain,
    bool Duplicate,
    ClusterPropertyDto[] Properties,
    string? SharedGroup = null,
    long? RetainedAtUnix = null);

/// <summary>One retained message sent to a node that just joined.</summary>
/// <remarks>用于新节点补齐保留消息；时间戳原样保留，才能继续按 last-write-wins 合并。</remarks>
/// <param name="Topic">Topic the value is stored under.</param>
/// <param name="Payload">Base64 payload.</param>
/// <param name="Qos">QoS stored with the message.</param>
/// <param name="StoredAtUnix">When the origin node retained it.</param>
/// <param name="ExpiresAtUnix">Absolute expiry, or null when it never expires.</param>
/// <param name="Properties">MQTT 5 properties to restore.</param>
public sealed record ClusterRetainedMessage(
    string Topic,
    string Payload,
    int Qos,
    long StoredAtUnix,
    long? ExpiresAtUnix,
    ClusterPropertyDto[] Properties);

/// <summary>Request for a session that may live on another node.</summary>
/// <remarks>只在 CleanStart 为 false 时发送；对方交还会话并断开自己的连接。</remarks>
/// <param name="ClientId">Session to take over.</param>
/// <param name="NodeId">Node asking for it, for logging and conflict detection.</param>
public sealed record ClusterSessionClaim(string ClientId, string NodeId);

/// <summary>HMAC-SHA256 request signing with a five minute clock skew window.</summary>
/// <remarks>时间戳超出窗口直接拒绝，用来挡住重放。</remarks>
public static class ClusterSignature
{
    /// <summary>Header carrying the Unix timestamp used in the signature.</summary>
    /// <remarks>与签名一起计算，改时间戳等于改签名内容。</remarks>
    public const string TimestampHeader = "X-NextMQTT-Timestamp";

    /// <summary>Header carrying the hex HMAC-SHA256 signature.</summary>
    /// <remarks>大写十六进制；校验时按固定时间比较。</remarks>
    public const string SignatureHeader = "X-NextMQTT-Signature";

    /// <summary>Largest accepted difference between node clocks.</summary>
    /// <remarks>节点之间需要基本的时间同步，偏差超过 5 分钟的全部请求都会被拒。</remarks>
    private static readonly TimeSpan AllowedSkew = TimeSpan.FromMinutes(5);

    /// <summary>Computes the uppercase hex HMAC over <c>timestamp.body</c>.</summary>
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
