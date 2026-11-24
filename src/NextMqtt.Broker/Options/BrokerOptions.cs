namespace NextMqtt.Broker.Options;

/// <summary>Core broker limits and timings.</summary>
public sealed class BrokerOptions
{
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
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>Accepted CONNECTs per second per remote address; 0 disables the limit.</summary>
    public int ConnectPerSecondPerIp { get; set; }

    /// <summary>Burst size for connect limiting; 0 uses the per-second rate.</summary>
    public int ConnectBurst { get; set; }

    /// <summary>Accepted PUBLISH packets per second per client; 0 disables the limit.</summary>
    public int PublishPerSecondPerClient { get; set; }

    /// <summary>Burst size for publish limiting; 0 uses the per-second rate.</summary>
    public int PublishBurst { get; set; }

    public bool LimitsConnections => ConnectPerSecondPerIp > 0;

    public bool LimitsPublishes => PublishPerSecondPerClient > 0;

    /// <summary>Per-topic publish quotas; empty disables topic quotas.</summary>
    public List<TopicQuotaOptions> Topics { get; set; } = [];
}

/// <summary>A publish quota applied to every topic matching a filter.</summary>
public sealed class TopicQuotaOptions
{
    public string Filter { get; set; } = string.Empty;

    public int PublishPerSecond { get; set; }

    public int Burst { get; set; }
}

/// <summary>Authentication configuration.</summary>
public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    public bool AllowAnonymous { get; set; }

    public List<LocalUserOptions> Users { get; set; } = [];

    public JwtOptions Jwt { get; set; } = new();

    /// <summary>Usernames treated as superusers, bypassing ACL checks.</summary>
    public List<string> Superusers { get; set; } = [];
}

public sealed class LocalUserOptions
{
    public string Username { get; set; } = string.Empty;

    /// <summary>Password encoded with <c>PasswordHasher.Hash</c>.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string[] Roles { get; set; } = [];
}

public sealed class JwtOptions
{
    /// <summary>HMAC-SHA256 signing secret. JWT authentication is disabled when empty.</summary>
    public string? Secret { get; set; }

    public string? Issuer { get; set; }

    public string? Audience { get; set; }

    public string UsernameClaim { get; set; } = "sub";

    public string RoleClaim { get; set; } = "roles";

    public bool RequireExpiration { get; set; } = true;

    public bool Enabled => !string.IsNullOrEmpty(Secret);
}

public enum AclDefaultPolicy
{
    Deny = 0,
    Allow = 1,
}

public enum AclEffect
{
    Allow = 0,
    Deny = 1,
}

[Flags]
public enum AclAction
{
    Publish = 1,
    Subscribe = 2,
    Both = Publish | Subscribe,
}

public sealed class AclOptions
{
    public const string SectionName = "Acl";

    public AclDefaultPolicy DefaultPolicy { get; set; } = AclDefaultPolicy.Deny;

    public List<AclRuleOptions> Rules { get; set; } = [];
}

public sealed class AclRuleOptions
{
    public AclEffect Effect { get; set; } = AclEffect.Allow;

    public AclAction Action { get; set; } = AclAction.Both;

    /// <summary>Topic filter. Supports <c>${username}</c> and <c>${clientid}</c> placeholders.</summary>
    public string Topic { get; set; } = "#";

    /// <summary>Optional username glob, <c>*</c> and <c>?</c> supported.</summary>
    public string? Username { get; set; }

    /// <summary>Optional client identifier glob.</summary>
    public string? ClientId { get; set; }
}
