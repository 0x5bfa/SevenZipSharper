namespace SevenZipSharper;

/// <summary>Controls filesystem traversal when building an archive.</summary>
public sealed record ArchiveCreationOptions
{
    /// <summary>Gets whether the source directory's own name is included.</summary>
    public bool IncludeBaseDirectory { get; init; }
    /// <summary>Gets whether directory entries, including empty directories, are included.</summary>
    public bool IncludeEmptyDirectories { get; init; } = true;
    /// <summary>Gets the maximum number of planned filesystem entries.</summary>
    public int MaximumEntries { get; init; } = 1_000_000;
    /// <summary>Gets whether an existing destination archive may be replaced after success.</summary>
    public bool Overwrite { get; init; }
    /// <summary>Gets the policy for source reparse points and special files.</summary>
    public UnsupportedEntryPolicy UnsupportedEntries { get; init; }

    internal ArchiveCreationOptions Capture()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumEntries);
        if (!Enum.IsDefined(UnsupportedEntries))
        {
            throw new ArgumentOutOfRangeException(nameof(UnsupportedEntries));
        }

        return this with { };
    }
}
