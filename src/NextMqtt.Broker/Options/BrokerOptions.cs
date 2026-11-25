namespace NextMqtt.Broker.Options;

/// <summary>Core broker limits and timings.</summary>
public sealed class BrokerOptions
{
    /// <summary>Configuration section name (<c>Broker</c> / <c>NEXTMQTT_BROKER__</c>).</summary>
    public const string SectionName = "Broker";

    /// <summary>Largest packet accepted from a client, in bytes.</summary>
    public int MaxPacketSize { get; set; } = 1024 * 1024;

    /// <summary>Maximum unacknowledged QoS 1/2 messages per session.</summary>
    public int MaxInflightMessages { get; set; } = 32;

    /// <summary>Maximum messages buffered for an offline persistent session.</summary>
    public int MaxQueuedMessages { get; set; } = 1000;

    /// <summary>Maximum subscriptions allowed per session.</summary>
    public int MaxSubscriptionsPerSession { get; set; } = 1000;

    /// <summary>Maximum inbound topic aliases accepted per connection.</summary>
    public int MaxTopicAlias { get; set; } = 32;

    /// <summary>
    /// Largest number of subscriptions per client that may carry an MQTT 5
    /// subscription identifier; 0 disables the quota.
    /// </summary>
    public int MaxSubscriptionIdentifiersPerClient { get; set; }

    /// <summary>How long the server waits for a CONNECT packet.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How often unacknowledged outbound messages are retransmitted.</summary>
    public TimeSpan RetransmitInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How often expired sessions and retained messages are pruned.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum accepted client identifier length.</summary>
    public int MaxClientIdLength { get; set; } = 256;

    /// <summary>Whether the broker assigns identifiers for zero-length client ids.</summary>
    public bool AllowZeroLengthClientId { get; set; } = true;

    /// <summary>Connection and publish rate limits; both are unlimited by default.</summary>
    public RateLimitOptions RateLimits { get; set; } = new();
}

/// <summary>Token-bucket rate limits applied per remote address and per client.</summary>
/// <remarks>多选题：连接速率、单客户端发布速率、单主题配额可分别开启。</remarks>
public sealed class RateLimitOptions
{
    /// <summary>Configuration section name (<c>RateLimits</c> / <c>NEXTMQTT_RATELIMITS__</c>).</summary>
    public const string SectionName = "RateLimits";

    /// <summary>Accepted CONNECTs per second per remote address; 0 disables the limit.</summary>
    public int ConnectPerSecondPerIp { get; set; }

    /// <summary>Burst size for connect limiting; 0 uses the per-second rate.</summary>
    public int ConnectBurst { get; set; }

    /// <summary>Accepted PUBLISH packets per second per client; 0 disables the limit.</summary>
    public int PublishPerSecondPerClient { get; set; }

    /// <summary>Burst size for publish limiting; 0 uses the per-second rate.</summary>
    public int PublishBurst { get; set; }

    /// <summary>True when the per-address CONNECT limit is active.</summary>
    public bool LimitsConnections => ConnectPerSecondPerIp > 0;

    /// <summary>True when the per-client publish limit is active.</summary>
    public bool LimitsPublishes => PublishPerSecondPerClient > 0;

    /// <summary>Per-topic publish quotas; empty disables topic quotas.</summary>
    public List<TopicQuotaOptions> Topics { get; set; } = [];
}

/// <summary>A publish quota applied to every topic matching a filter.</summary>
public sealed class TopicQuotaOptions
{
    /// <summary>Topic filter that selects the quota; invalid filters are ignored.</summary>
    public string Filter { get; set; } = string.Empty;

    /// <summary>Accepted publishes per second across all clients; 0 disables the rule.</summary>
    public int PublishPerSecond { get; set; }

    /// <summary>Burst size; 0 uses the per-second rate.</summary>
    public int Burst { get; set; }
}

/// <summary>Authentication configuration.</summary>
public sealed class AuthenticationOptions
{
    /// <summary>Configuration section name (<c>Authentication</c>).</summary>
    public const string SectionName = "Authentication";

    /// <summary>Accept clients without credentials.</summary>
    public bool AllowAnonymous { get; set; }

    /// <summary>Local username/password accounts with PBKDF2 hashes.</summary>
    public List<LocalUserOptions> Users { get; set; } = [];

    /// <summary>HS256 JSON Web Token validation.</summary>
    public JwtOptions Jwt { get; set; } = new();

    /// <summary>Usernames treated as superusers, bypassing ACL checks.</summary>
    public List<string> Superusers { get; set; } = [];
}

/// <summary>One local username/password account.</summary>
public sealed class LocalUserOptions
{
    /// <summary>Login name.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Password encoded with <c>PasswordHasher.Hash</c>.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Roles attached to the principal for ACL placeholder expansion.</summary>
    public string[] Roles { get; set; } = [];
}

/// <summary>HS256 JSON Web Token validation settings.</summary>
public sealed class JwtOptions
{
    /// <summary>HMAC-SHA256 signing secret. JWT authentication is disabled when empty.</summary>
    public string? Secret { get; set; }

    /// <summary>Required issuer claim (<c>iss</c>) when set.</summary>
    public string? Issuer { get; set; }

    /// <summary>Required audience claim (<c>aud</c>) when set.</summary>
    public string? Audience { get; set; }

    /// <summary>Claim that carries the username.</summary>
    public string UsernameClaim { get; set; } = "sub";

    /// <summary>Claim that carries roles.</summary>
    public string RoleClaim { get; set; } = "roles";

    /// <summary>Reject tokens without an <c>exp</c> claim.</summary>
    public bool RequireExpiration { get; set; } = true;

    /// <summary>True when a secret is configured.</summary>
    public bool Enabled => !string.IsNullOrEmpty(Secret);
}

/// <summary>What happens to a topic that no ACL rule matches.</summary>
public enum AclDefaultPolicy
{
    /// <summary>Publish and subscribe are refused.</summary>
    Deny = 0,

    /// <summary>Publish and subscribe are allowed.</summary>
    Allow = 1,
}

/// <summary>Whether a matching ACL rule allows or refuses the action.</summary>
public enum AclEffect
{
    /// <summary>The rule allows the action.</summary>
    Allow = 0,

    /// <summary>The rule refuses the action.</summary>
    Deny = 1,
}

/// <summary>Actions an ACL rule can cover.</summary>
[Flags]
public enum AclAction
{
    /// <summary>Rule applies to PUBLISH.</summary>
    Publish = 1,

    /// <summary>Rule applies to SUBSCRIBE.</summary>
    Subscribe = 2,

    /// <summary>Rule applies to both directions.</summary>
    Both = Publish | Subscribe,
}

/// <summary>Ordered topic access-control list.</summary>
public sealed class AclOptions
{
    /// <summary>Configuration section name (<c>Acl</c>).</summary>
    public const string SectionName = "Acl";

    /// <summary>Policy applied when no rule matches.</summary>
    public AclDefaultPolicy DefaultPolicy { get; set; } = AclDefaultPolicy.Deny;

    /// <summary>Rules evaluated in order; the first match wins.</summary>
    public List<AclRuleOptions> Rules { get; set; } = [];
}

/// <summary>One ordered ACL rule.</summary>
public sealed class AclRuleOptions
{
    /// <summary>Whether the rule allows or denies.</summary>
    public AclEffect Effect { get; set; } = AclEffect.Allow;

    /// <summary>Directions the rule applies to.</summary>
    public AclAction Action { get; set; } = AclAction.Both;

    /// <summary>Topic filter. Supports <c>${username}</c> and <c>${clientid}</c> placeholders.</summary>
    public string Topic { get; set; } = "#";

    /// <summary>Optional username glob, <c>*</c> and <c>?</c> supported.</summary>
    public string? Username { get; set; }

    /// <summary>Optional client identifier glob.</summary>
    public string? ClientId { get; set; }
}
