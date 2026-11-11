namespace SevenZipSharper;

/// <summary>Controls how unsupported source entries are handled.</summary>
public enum UnsupportedEntryPolicy
{
    /// <summary>Reject the operation before creating output.</summary>
    Reject,
    /// <summary>Explicitly skip unsupported entries.</summary>
    Skip
}

/// <summary>Controls filesystem extraction and actual-byte limits.</summary>
public sealed record ArchiveExtractionOptions
{
    /// <summary>Gets whether successfully extracted files may replace existing files.</summary>
    public bool Overwrite { get; init; }
    /// <summary>Gets whether modification times are restored.</summary>
    public bool PreserveLastWriteTime { get; init; } = true;
    /// <summary>Gets whether supported regular filesystem attributes are restored.</summary>
    public bool PreserveAttributes { get; init; }
    /// <summary>Gets the maximum number of selected entries.</summary>
    public int MaximumEntries { get; init; } = 1_000_000;
    /// <summary>Gets an optional actual-byte limit for each regular file.</summary>
    public long? MaximumEntryBytes { get; init; }
    /// <summary>Gets an optional actual-byte limit for all extracted regular files.</summary>
    public long? MaximumTotalBytes { get; init; }
    /// <summary>Gets the policy for links and special entries.</summary>
    public UnsupportedEntryPolicy UnsupportedEntries { get; init; }

    internal ArchiveExtractionOptions Capture()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumEntries);
        if (MaximumEntryBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumEntryBytes));
        }

        if (MaximumTotalBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumTotalBytes));
        }

        if (!Enum.IsDefined(UnsupportedEntries))
        {
            throw new ArgumentOutOfRangeException(nameof(UnsupportedEntries));
        }

        return this with { };
    }
}
