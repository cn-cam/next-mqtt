namespace NextMqtt.Broker.Storage;

/// <summary>Where broker state lives between restarts.</summary>
public sealed class StorageOptions
{
    /// <summary>Configuration section name (<c>Storage</c> / <c>NEXTMQTT_STORAGE__</c>).</summary>
    public const string SectionName = "Storage";

    /// <summary>Retained message persistence.</summary>
    public RetainedStorageOptions Retained { get; set; } = new();

    /// <summary>Persistent session snapshots.</summary>
    public SessionStorageOptions Session { get; set; } = new();
}

/// <summary>Selects between an in-memory store and a durable one.</summary>
public enum StorageMode
{
    /// <summary>State lives only in process memory (default).</summary>
    Memory,

    /// <summary>State is written to disk and replayed on startup.</summary>
    File,
}

/// <summary>Retained message journal settings.</summary>
public sealed class RetainedStorageOptions
{
    /// <summary>Memory or file-backed retained storage.</summary>
    public StorageMode Mode { get; set; } = StorageMode.Memory;

    /// <summary>Journal location; relative paths resolve against the working directory.</summary>
    public string Path { get; set; } = "data/retained.log";

    /// <summary>Rewrite the journal once it grows beyond this size, in bytes.</summary>
    public long CompactThresholdBytes { get; set; } = 4 * 1024 * 1024;
}

/// <summary>Persistent session snapshot settings.</summary>
public sealed class SessionStorageOptions
{
    /// <summary>Memory or file-backed session storage.</summary>
    public StorageMode Mode { get; set; } = StorageMode.Memory;

    /// <summary>Snapshot location; relative paths resolve against the working directory.</summary>
    public string Path { get; set; } = "data/sessions.json";

    /// <summary>How often a changed session set is written, in seconds.</summary>
    public int SnapshotSeconds { get; set; } = 30;
}
