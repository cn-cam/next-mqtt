namespace NextMqtt.Broker.Integrations;

/// <summary>Outbound integration settings.</summary>
/// <remarks>出站集成的总开关；每个桥都有独立子配置，默认全部关闭。</remarks>
public sealed class IntegrationsOptions
{
    /// <summary>Configuration section name (<c>Integrations</c>).</summary>
    /// <remarks>环境变量前缀 NEXTMQTT_INTEGRATIONS__，四个子段可以各自单独开启。</remarks>
    public const string SectionName = "Integrations";

    /// <summary>HTTP webhooks fed by published messages.</summary>
    /// <remarks>按主题过滤器把消息 POST 到业务端点。</remarks>
    public List<WebhookOptions> Webhooks { get; set; } = [];

    /// <summary>Routing rules evaluated before storage and delivery.</summary>
    /// <remarks>先执行规则，再决定是否落保留、投递或转发。</remarks>
    public List<RuleOptions> Rules { get; set; } = [];

    /// <summary>Optional NATS forwarding.</summary>
    /// <remarks>文本协议桥，不需要额外依赖。</remarks>
    public NatsOptions Nats { get; set; } = new();

    /// <summary>Optional Kafka forwarding (ADR 0007).</summary>
    /// <remarks>需要 Confluent.Kafka；未启用时不会创建 producer。</remarks>
    public KafkaOptions Kafka { get; set; } = new();

    /// <summary>Optional RabbitMQ forwarding (ADR 0008).</summary>
    /// <remarks>需要 RabbitMQ.Client；使用 publisher confirm。</remarks>
    public RabbitOptions Rabbit { get; set; } = new();
}

/// <summary>One routing rule, evaluated on every inbound publish.</summary>
/// <remarks>按配置顺序求值，drop 优先于 rewrite。</remarks>
public sealed class RuleOptions
{
    /// <summary>Topic filter that selects messages.</summary>
    /// <remarks>过滤器语法非法时，该规则在启动阶段被忽略并记录警告。</remarks>
    public string Filter { get; set; } = string.Empty;

    /// <summary>Action to take: <c>drop</c> or <c>rewrite</c>.</summary>
    /// <remarks>大小写不敏感；无法识别的动作只记警告并跳过这一条规则。</remarks>
    public string Action { get; set; } = "drop";

    /// <summary>Target prefix or full topic for <c>rewrite</c> rules.</summary>
    /// <remarks>过滤器含通配符时，通配符之后的部分会拼到目标后面；不含通配符时整体替换。</remarks>
    public string? Target { get; set; }
}

/// <summary>One webhook rule: every publish matching the filter becomes a POST.</summary>
/// <remarks>每条规则一条独立队列，互不阻塞。</remarks>
public sealed class WebhookOptions
{
    /// <summary>Name used in logs and in the event body.</summary>
    /// <remarks>只做标识，不参与匹配。</remarks>
    public string Name { get; set; } = "webhook";

    /// <summary>Topic filter that selects messages; invalid filters disable the rule.</summary>
    /// <remarks>写错过滤器不会让 Broker 启动失败，只是这条规则不生效。</remarks>
    public string Filter { get; set; } = "#";

    /// <summary>Absolute HTTP or HTTPS endpoint.</summary>
    /// <remarks>必须是绝对地址，且只接受 http/https。</remarks>
    public string Url { get; set; } = string.Empty;

    /// <summary>Per-request timeout in milliseconds.</summary>
    /// <remarks>超时按单次尝试计算，重试会重新计时。</remarks>
    public int TimeoutMs { get; set; } = 5000;

    /// <summary>Events buffered for this webhook before new ones are dropped.</summary>
    /// <remarks>队列满时丢新消息并计数，避免拖慢发布路径。</remarks>
    public int MaxQueue { get; set; } = 1000;

    /// <summary>Extra request headers, for example an authorization token.</summary>
    /// <remarks>用于鉴权或路由标记，原样发送。</remarks>
    public Dictionary<string, string> Headers { get; set; } = [];

    /// <summary>Include the base64 payload in the event body.</summary>
    /// <remarks>关闭后只发主题与元数据，适合只做审计计数。</remarks>
    public bool IncludePayload { get; set; } = true;
}
