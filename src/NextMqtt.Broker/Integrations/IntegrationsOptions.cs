namespace NextMqtt.Broker.Integrations;

/// <summary>Outbound integration settings.</summary>
public sealed class IntegrationsOptions
{
    /// <summary>Configuration section name (<c>Integrations</c>).</summary>
    public const string SectionName = "Integrations";

    /// <summary>HTTP webhooks fed by published messages.</summary>
    public List<WebhookOptions> Webhooks { get; set; } = [];

    /// <summary>Routing rules evaluated before storage and delivery.</summary>
    public List<RuleOptions> Rules { get; set; } = [];

    /// <summary>Optional NATS forwarding.</summary>
    public NatsOptions Nats { get; set; } = new();

    /// <summary>Optional Kafka forwarding (ADR 0007).</summary>
    public KafkaOptions Kafka { get; set; } = new();

    /// <summary>Optional RabbitMQ forwarding (ADR 0008).</summary>
    public RabbitOptions Rabbit { get; set; } = new();
}

/// <summary>One routing rule, evaluated on every inbound publish.</summary>
public sealed class RuleOptions
{
    /// <summary>Topic filter that selects messages.</summary>
    public string Filter { get; set; } = string.Empty;

    /// <summary>Action to take: <c>drop</c> or <c>rewrite</c>.</summary>
    public string Action { get; set; } = "drop";

    /// <summary>Target prefix or full topic for <c>rewrite</c> rules.</summary>
    public string? Target { get; set; }
}

/// <summary>One webhook rule: every publish matching the filter becomes a POST.</summary>
public sealed class WebhookOptions
{
    /// <summary>Name used in logs and in the event body.</summary>
    public string Name { get; set; } = "webhook";

    /// <summary>Topic filter that selects messages; invalid filters disable the rule.</summary>
    public string Filter { get; set; } = "#";

    /// <summary>Absolute HTTP or HTTPS endpoint.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Per-request timeout in milliseconds.</summary>
    public int TimeoutMs { get; set; } = 5000;

    /// <summary>Events buffered for this webhook before new ones are dropped.</summary>
    public int MaxQueue { get; set; } = 1000;

    /// <summary>Extra request headers, for example an authorization token.</summary>
    public Dictionary<string, string> Headers { get; set; } = [];

    /// <summary>Include the base64 payload in the event body.</summary>
    public bool IncludePayload { get; set; } = true;
}
