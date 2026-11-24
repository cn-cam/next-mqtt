namespace NextMqtt.Broker.Storage;

/// <summary>Where broker state lives between restarts.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public RetainedStorageOptions Retained { get; set; } = new();
}

public enum RetainedStoreMode
{
    /// <summary>Retained messages live only in process memory (default).</summary>
    Memory,

    /// <summary>Retained messages are journaled to an append-only file.</summary>
    File,
}

public sealed class RetainedStorageOptions
{
    public RetainedStoreMode Mode { get; set; } = RetainedStoreMode.Memory;

    /// <summary>Journal location; relative paths resolve against the working directory.</summary>
    public string Path { get; set; } = "data/retained.log";

    /// <summary>Rewrite the journal once it grows beyond this size.</summary>
    public long CompactThresholdBytes { get; set; } = 4 * 1024 * 1024;
}
