namespace NextMqtt.Broker.Storage;

/// <summary>Portable snapshot of the state the broker can export and import.</summary>
/// <remarks>JSON 结构可通过 /api/v1/backup 导出；跨版本导入时以 FormatVersion 判断兼容性。</remarks>
public sealed class BrokerBackup
{
    /// <summary>Backup format version; currently 1.</summary>
    /// <remarks>导入时据此判断兼容性，目前只接受 1。</remarks>
    public int FormatVersion { get; set; } = 1;

    /// <summary>When the backup was taken.</summary>
    /// <remarks>仅用于审计展示，不影响恢复。</remarks>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Retained messages at export time.</summary>
    /// <remarks>包含过期但未清扫的条目，导入时会再过滤一遍。</remarks>
    public List<PersistedRetained> Retained { get; set; } = [];

    /// <summary>Persistent sessions at export time.</summary>
    /// <remarks>只含持久会话；在线会话导出后也不保证能恢复原连接。</remarks>
    public List<PersistedSession> Sessions { get; set; } = [];
}

/// <summary>One retained message inside a backup.</summary>
/// <remarks>字段与保留日志里的记录一一对应。</remarks>
public sealed class PersistedRetained
{
    /// <summary>Topic name.</summary>
    /// <remarks>导入时校验合法性，非法主题直接跳过。</remarks>
    public string Topic { get; set; } = string.Empty;

    /// <summary>Base64 payload.</summary>
    /// <remarks>用文本承载二进制，便于放进 JSON 与工单。</remarks>
    public string Payload { get; set; } = string.Empty;

    /// <summary>QoS stored with the message.</summary>
    /// <remarks>恢复后仍按这个级别下发。</remarks>
    public int QoS { get; set; }

    /// <summary>When the message was retained.</summary>
    /// <remarks>参与 last-write-wins 比较，不随恢复时间改变。</remarks>
    public DateTimeOffset StoredAt { get; set; }

    /// <summary>Absolute expiry, or null when it never expires.</summary>
    /// <remarks>过期消息在导入时被丢弃，而不是重新计时。</remarks>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>MQTT 5 properties to restore with the message.</summary>
    /// <remarks>与保留日志共用同一套属性编码。</remarks>
    public List<PropertyRecord>? Properties { get; set; }
}

/// <summary>Result of importing a backup.</summary>
/// <remarks>两个数字都只统计真正写入的条目。</remarks>
/// <param name="Retained">Retained messages written.</param>
/// <param name="Sessions">Persistent sessions created; online sessions are skipped.</param>
public sealed record RestoreResult(int Retained, int Sessions);
