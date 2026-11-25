namespace NextMqtt.Broker.Storage;

/// <summary>Portable snapshot of the state the broker can export and import.</summary>
/// <remarks>JSON 结构可通过 /api/v1/backup 导出；跨版本导入时以 FormatVersion 判断兼容性。</remarks>
public sealed class BrokerBackup
{
    /// <summary>Backup format version; currently 1.</summary>
    public int FormatVersion { get; set; } = 1;

    /// <summary>When the backup was taken.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Retained messages at export time.</summary>
    public List<PersistedRetained> Retained { get; set; } = [];

    /// <summary>Persistent sessions at export time.</summary>
    public List<PersistedSession> Sessions { get; set; } = [];
}

/// <summary>One retained message inside a backup.</summary>
public sealed class PersistedRetained
{
    /// <summary>Topic name.</summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>Base64 payload.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>QoS stored with the message.</summary>
    public int QoS { get; set; }

    /// <summary>When the message was retained.</summary>
    public DateTimeOffset StoredAt { get; set; }

    /// <summary>Absolute expiry, or null when it never expires.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>MQTT 5 properties to restore with the message.</summary>
    public List<PropertyRecord>? Properties { get; set; }
}

/// <summary>Result of importing a backup.</summary>
/// <param name="Retained">Retained messages written.</param>
/// <param name="Sessions">Persistent sessions created; online sessions are skipped.</param>
public sealed record RestoreResult(int Retained, int Sessions);
