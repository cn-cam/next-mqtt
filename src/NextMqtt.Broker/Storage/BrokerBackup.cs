namespace NextMqtt.Broker.Storage;

/// <summary>Portable snapshot of the state the broker can export and import.</summary>
public sealed class BrokerBackup
{
    public int FormatVersion { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }

    public List<PersistedRetained> Retained { get; set; } = [];

    public List<PersistedSession> Sessions { get; set; } = [];
}

public sealed class PersistedRetained
{
    public string Topic { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public int QoS { get; set; }

    public DateTimeOffset StoredAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public List<PropertyRecord>? Properties { get; set; }
}

/// <summary>Result of importing a backup.</summary>
public sealed record RestoreResult(int Retained, int Sessions);
