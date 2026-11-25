namespace NextMqtt.Broker.Options;

/// <summary>Core broker limits and timings.</summary>
/// <remarks>这些值直接决定内存上限与公平性；默认值面向小规模部署，上量前先调 inflight 与离线队列。</remarks>
public sealed class BrokerOptions
{
    /// <summary>Configuration section name (<c>Broker</c> / <c>NEXTMQTT_BROKER__</c>).</summary>
    /// <remarks>环境变量用双下划线分层，配置文件里就是 Broker 段。</remarks>
    public const string SectionName = "Broker";

    /// <summary>Largest packet accepted from a client, in bytes.</summary>
    /// <remarks>超过直接断开并回 0x95；调大会放大单连接内存占用，因为整包要读完才能解析。</remarks>
    public int MaxPacketSize { get; set; } = 1024 * 1024;

    /// <summary>Maximum unacknowledged QoS 1/2 messages per session.</summary>
    /// <remarks>窗口满后新消息转离线队列而不是阻塞发送；设太小会让高延迟链路吞吐骤降。</remarks>
    public int MaxInflightMessages { get; set; } = 32;

    /// <summary>Maximum messages buffered for an offline persistent session.</summary>
    /// <remarks>溢出时丢最旧的并计入丢弃指标；队列只在内存里，重启后随快照恢复。</remarks>
    public int MaxQueuedMessages { get; set; } = 1000;

    /// <summary>Maximum subscriptions allowed per session.</summary>
    /// <remarks>达到上限后新的 SUBSCRIBE 被拒（0x97），更新已有订阅仍然允许。</remarks>
    public int MaxSubscriptionsPerSession { get; set; } = 1000;

    /// <summary>Maximum inbound topic aliases accepted per connection.</summary>
    /// <remarks>只限制客户端声明的入站别名数量；别名表随连接建立，断开即释放。</remarks>
    public int MaxTopicAlias { get; set; } = 32;

    /// <summary>
    /// Largest number of subscriptions per client that may carry an MQTT 5
    /// subscription identifier; 0 disables the quota.
    /// </summary>
    /// <remarks>订阅标识要随下发报文一起保存，无限量会让客户端用订阅把服务端内存撑满。</remarks>
    public int MaxSubscriptionIdentifiersPerClient { get; set; }

    /// <summary>How long the server waits for a CONNECT packet.</summary>
    /// <remarks>从 TCP 建立时算起，超时直接关闭，避免半开连接长期占住句柄。</remarks>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How often unacknowledged outbound messages are retransmitted.</summary>
    /// <remarks>只重发 QoS 1/2 且超过该间隔仍未确认的消息；值太小会在慢链路上放大重传。</remarks>
    public TimeSpan RetransmitInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How often expired sessions and retained messages are pruned.</summary>
    /// <remarks>清扫单线程批量执行，间隔越短过期数据留存越少、CPU 唤醒越频繁。</remarks>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum accepted client identifier length.</summary>
    /// <remarks>超长 CONNECT 直接拒绝；这是服务端策略，协议本身的字段上限是 65535 字节。</remarks>
    public int MaxClientIdLength { get; set; } = 256;

    /// <summary>Whether the broker assigns identifiers for zero-length client ids.</summary>
    /// <remarks>关闭后空 ID 客户端被拒；开启时生成随机 ID 并在 CONNACK 里返回给客户端。</remarks>
    public bool AllowZeroLengthClientId { get; set; } = true;

    /// <summary>Connection and publish rate limits; both are unlimited by default.</summary>
    /// <remarks>三类限额彼此独立，默认全关表示只受系统资源约束。</remarks>
    public RateLimitOptions RateLimits { get; set; } = new();
}

/// <summary>Token-bucket rate limits applied per remote address and per client.</summary>
/// <remarks>连接速率、单客户端发布速率、单主题配额三项互不依赖，可以只开其中一项。</remarks>
public sealed class RateLimitOptions
{
    /// <summary>Configuration section name (<c>RateLimits</c> / <c>NEXTMQTT_RATELIMITS__</c>).</summary>
    /// <remarks>与 Broker 同级；写成 Broker:RateLimits 同样能被读到。</remarks>
    public const string SectionName = "RateLimits";

    /// <summary>Accepted CONNECTs per second per remote address; 0 disables the limit.</summary>
    /// <remarks>按 TCP 源地址计数，NAT 后面的客户端共用一个桶；令牌桶允许突发。</remarks>
    public int ConnectPerSecondPerIp { get; set; }

    /// <summary>Burst size for connect limiting; 0 uses the per-second rate.</summary>
    /// <remarks>突发值等于速率时，桶只能攒下一秒的量，起不到削峰作用。</remarks>
    public int ConnectBurst { get; set; }

    /// <summary>Accepted PUBLISH packets per second per client; 0 disables the limit.</summary>
    /// <remarks>只对 PUBLISH 计数，PUBACK 等控制报文不受影响。</remarks>
    public int PublishPerSecondPerClient { get; set; }

    /// <summary>Burst size for publish limiting; 0 uses the per-second rate.</summary>
    /// <remarks>传感器批量上报靠突发容量通过，设得比速率略高更稳。</remarks>
    public int PublishBurst { get; set; }

    /// <summary>True when the per-address CONNECT limit is active.</summary>
    /// <remarks>启动时算一次，运行期改配置需要重启才生效。</remarks>
    public bool LimitsConnections => ConnectPerSecondPerIp > 0;

    /// <summary>True when the per-client publish limit is active.</summary>
    /// <remarks>未开启时发布路径完全跳过令牌桶检查。</remarks>
    public bool LimitsPublishes => PublishPerSecondPerClient > 0;

    /// <summary>Per-topic publish quotas; empty disables topic quotas.</summary>
    /// <remarks>与客户端维度同时生效，任一超限就丢。</remarks>
    public List<TopicQuotaOptions> Topics { get; set; } = [];
}

/// <summary>A publish quota applied to every topic matching a filter.</summary>
/// <remarks>同一主题可能命中多条规则，每条都会检查，实际速率取最严格的那个。</remarks>
public sealed class TopicQuotaOptions
{
    /// <summary>Topic filter that selects the quota; invalid filters are ignored.</summary>
    /// <remarks>写错只在日志里告警，不会让节点启动失败。</remarks>
    public string Filter { get; set; } = string.Empty;

    /// <summary>Accepted publishes per second across all clients; 0 disables the rule.</summary>
    /// <remarks>这是集群预算，会按在线节点数均分，不是全局精确计数器。</remarks>
    public int PublishPerSecond { get; set; }

    /// <summary>Burst size; 0 uses the per-second rate.</summary>
    /// <remarks>与客户端桶同理，突发容量决定短时尖峰能否通过。</remarks>
    public int Burst { get; set; }
}

/// <summary>Authentication configuration.</summary>
/// <remarks>即使只有一种凭据来源也要显式配置，未配置时默认拒绝匿名。</remarks>
public sealed class AuthenticationOptions
{
    /// <summary>Configuration section name (<c>Authentication</c>).</summary>
    /// <remarks>环境变量前缀是 NEXTMQTT_AUTHENTICATION__。</remarks>
    public const string SectionName = "Authentication";

    /// <summary>Accept clients without credentials.</summary>
    /// <remarks>生产环境应关闭；开启后匿名连接仍要过 ACL。</remarks>
    public bool AllowAnonymous { get; set; }

    /// <summary>Local username/password accounts with PBKDF2 hashes.</summary>
    /// <remarks>只在启动时读入，改密码或增删账号都要重启。</remarks>
    public List<LocalUserOptions> Users { get; set; } = [];

    /// <summary>HS256 JSON Web Token validation.</summary>
    /// <remarks>与本地账号并存：密码看起来像 JWT 时先验签，失败再查本地表。</remarks>
    public JwtOptions Jwt { get; set; } = new();

    /// <summary>Usernames treated as superusers, bypassing ACL checks.</summary>
    /// <remarks>按用户名完全匹配，JWT 的 sub 也参与比较。</remarks>
    public List<string> Superusers { get; set; } = [];
}

/// <summary>One local username/password account.</summary>
/// <remarks>密码只存哈希，明文不会出现在配置文件里。</remarks>
public sealed class LocalUserOptions
{
    /// <summary>Login name.</summary>
    /// <remarks>与 JWT 的 sub 共享命名空间，重名时会互相遮蔽。</remarks>
    public string Username { get; set; } = string.Empty;

    /// <summary>Password encoded with <c>PasswordHasher.Hash</c>.</summary>
    /// <remarks>哈希带盐，比较使用定时安全算法。</remarks>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Roles attached to the principal for ACL placeholder expansion.</summary>
    /// <remarks>目前只用于 ${...} 展开与日志，不做角色继承。</remarks>
    public string[] Roles { get; set; } = [];
}

/// <summary>HS256 JSON Web Token validation settings.</summary>
/// <remarks>只支持 HS256；非对称算法需要额外依赖，暂不引入。</remarks>
public sealed class JwtOptions
{
    /// <summary>HMAC-SHA256 signing secret. JWT authentication is disabled when empty.</summary>
    /// <remarks>空值表示完全不启用；密钥建议至少 32 字节。</remarks>
    public string? Secret { get; set; }

    /// <summary>Required issuer claim (<c>iss</c>) when set.</summary>
    /// <remarks>设置后 iss 必须完全相等，不做前缀或子串匹配。</remarks>
    public string? Issuer { get; set; }

    /// <summary>Required audience claim (<c>aud</c>) when set.</summary>
    /// <remarks>支持 aud 为数组，任一元素匹配即可。</remarks>
    public string? Audience { get; set; }

    /// <summary>Claim that carries the username.</summary>
    /// <remarks>默认取 sub；取不到时按匿名处理，不会回退到随机值。</remarks>
    public string UsernameClaim { get; set; } = "sub";

    /// <summary>Claim that carries roles.</summary>
    /// <remarks>同时接受字符串与字符串数组两种写法。</remarks>
    public string RoleClaim { get; set; } = "roles";

    /// <summary>Reject tokens without an <c>exp</c> claim.</summary>
    /// <remarks>默认开启；关掉后长期有效的令牌无法靠过期时间失效。</remarks>
    public bool RequireExpiration { get; set; } = true;

    /// <summary>True when a secret is configured.</summary>
    /// <remarks>只检查是否有值，不校验长度。</remarks>
    public bool Enabled => !string.IsNullOrEmpty(Secret);
}

/// <summary>What happens to a topic that no ACL rule matches.</summary>
/// <remarks>默认拒绝；只有开放型部署才应改成允许。</remarks>
public enum AclDefaultPolicy
{
    /// <summary>Publish and subscribe are refused.</summary>
    /// <remarks>拒绝时 PUBLISH 直接丢弃，SUBSCRIBE 回 0x87。</remarks>
    Deny = 0,

    /// <summary>Publish and subscribe are allowed.</summary>
    /// <remarks>允许时仍受主题配额与订阅上限约束。</remarks>
    Allow = 1,
}

/// <summary>Whether a matching ACL rule allows or refuses the action.</summary>
/// <remarks>顺序求值，第一条命中的规则决定结果，后面的不再看。</remarks>
public enum AclEffect
{
    /// <summary>The rule allows the action.</summary>
    /// <remarks>允许规则通常用来先放通自己的命名空间。</remarks>
    Allow = 0,

    /// <summary>The rule refuses the action.</summary>
    /// <remarks>拒绝规则要放在允许之前，才能盖住更宽的通配。</remarks>
    Deny = 1,
}

/// <summary>Actions an ACL rule can cover.</summary>
/// <remarks>位标志，可以同时覆盖发布与订阅两个方向。</remarks>
[Flags]
public enum AclAction
{
    /// <summary>Rule applies to PUBLISH.</summary>
    /// <remarks>只检查发布方向，订阅不受这条规则影响。</remarks>
    Publish = 1,

    /// <summary>Rule applies to SUBSCRIBE.</summary>
    /// <remarks>只检查订阅方向，发布仍按其他规则判断。</remarks>
    Subscribe = 2,

    /// <summary>Rule applies to both directions.</summary>
    /// <remarks>默认值；大多数规则都应该用它。</remarks>
    Both = Publish | Subscribe,
}

/// <summary>Ordered topic access-control list.</summary>
/// <remarks>列表顺序就是优先级，配置合并时不会重新排序。</remarks>
public sealed class AclOptions
{
    /// <summary>Configuration section name (<c>Acl</c>).</summary>
    /// <remarks>与 Broker 同级，环境变量前缀是 NEXTMQTT_ACL__。</remarks>
    public const string SectionName = "Acl";

    /// <summary>Policy applied when no rule matches.</summary>
    /// <remarks>只影响未命中任何规则的主题，不影响已被允许的主题。</remarks>
    public AclDefaultPolicy DefaultPolicy { get; set; } = AclDefaultPolicy.Deny;

    /// <summary>Rules evaluated in order; the first match wins.</summary>
    /// <remarks>想收紧权限就把拒绝规则放前面，别依赖默认策略兜底。</remarks>
    public List<AclRuleOptions> Rules { get; set; } = [];
}

/// <summary>One ordered ACL rule.</summary>
/// <remarks>主题、用户名、客户端 ID 三个条件是“与”关系，留空表示不限制。</remarks>
public sealed class AclRuleOptions
{
    /// <summary>Whether the rule allows or denies.</summary>
    /// <remarks>默认允许；写拒绝规则时务必同时限定主题与主体。</remarks>
    public AclEffect Effect { get; set; } = AclEffect.Allow;

    /// <summary>Directions the rule applies to.</summary>
    /// <remarks>分开方向后可以做到允许订阅但禁止发布。</remarks>
    public AclAction Action { get; set; } = AclAction.Both;

    /// <summary>Topic filter. Supports <c>${username}</c> and <c>${clientid}</c> placeholders.</summary>
    /// <remarks>占位符在每次检查时展开，客户端 ID 中的 / 会被替换成 _。</remarks>
    public string Topic { get; set; } = "#";

    /// <summary>Optional username glob, <c>*</c> and <c>?</c> supported.</summary>
    /// <remarks>通配只作用于用户名，不作用于主题过滤器。</remarks>
    public string? Username { get; set; }

    /// <summary>Optional client identifier glob.</summary>
    /// <remarks>用于按设备型号批量放通，匹配区分大小写。</remarks>
    public string? ClientId { get; set; }
}
