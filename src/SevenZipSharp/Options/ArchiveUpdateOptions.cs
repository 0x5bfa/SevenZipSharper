namespace SevenZipSharp;

/// <summary>Controls replacement output in an existing archive's original format.</summary>
public sealed record ArchiveUpdateOptions
{
    /// <summary>Gets optional format-specific repacking settings. Required when editing encrypted archives.</summary>
    public ArchiveWriteOptions? WriteOptions { get; init; }
}
