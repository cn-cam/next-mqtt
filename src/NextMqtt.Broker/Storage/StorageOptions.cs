namespace NextMqtt.Broker.Storage;

/// <summary>Where broker state lives between restarts.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public RetainedStorageOptions Retained { get; set; } = new();

    public SessionStorageOptions Session { get; set; } = new();
}

public enum StorageMode
{
    /// <summary>State lives only in process memory (default).</summary>
    Memory,

    /// <summary>State is written to disk and replayed on startup.</summary>
    File,
}

public sealed class RetainedStorageOptions
{
    public StorageMode Mode { get; set; } = StorageMode.Memory;

    /// <summary>Journal location; relative paths resolve against the working directory.</summary>
    public string Path { get; set; } = "data/retained.log";

    /// <summary>Rewrite the journal once it grows beyond this size.</summary>
    public long CompactThresholdBytes { get; set; } = 4 * 1024 * 1024;
}

public sealed class SessionStorageOptions
{
    public StorageMode Mode { get; set; } = StorageMode.Memory;

    /// <summary>Snapshot location; relative paths resolve against the working directory.</summary>
    public string Path { get; set; } = "data/sessions.json";

    /// <summary>How often a changed session set is written.</summary>
    public int SnapshotSeconds { get; set; } = 30;
}
