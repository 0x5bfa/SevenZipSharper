namespace SevenZipSharp;

/// <summary>Represents immutable metadata and content belonging to an opened archive.</summary>
public sealed class ArchiveEntry
{
    internal Archive Owner { get; }
    internal uint? NativeAttributes { get; }
    internal ArchiveEntry(Archive owner, int index, string fullName, ArchiveEntryType type, long? length,
        long? compressedLength, DateTimeOffset? lastWriteTime, bool encrypted, uint? crc32, string? linkTarget, uint? attributes)
    {
        Owner = owner; Index = index; FullName = fullName; EntryType = type; Length = length;
        CompressedLength = compressedLength; LastWriteTime = lastWriteTime; IsEncrypted = encrypted;
        Crc32 = crc32; LinkTarget = linkTarget; NativeAttributes = attributes;
    }

    /// <summary>Gets the stable index within the source archive.</summary>
    public int Index { get; }
    /// <summary>Gets the decoded logical name with '/' separators and a trailing '/' for directories, without filesystem sanitization.</summary>
    public string FullName { get; }
    /// <summary>Gets the final component of the logical entry name.</summary>
    public string Name => FullName.TrimEnd('/', '\\').Split('/', '\\')[^1];
    /// <summary>Gets the entry kind.</summary>
    public ArchiveEntryType EntryType { get; }
    /// <summary>Gets the uncompressed length, or null if unknown.</summary>
    public long? Length { get; }
    /// <summary>Gets the compressed length, or null if unknown or shared.</summary>
    public long? CompressedLength { get; }
    /// <summary>Gets the last modification time, or null if unavailable.</summary>
    public DateTimeOffset? LastWriteTime { get; }
    /// <summary>Gets whether content encryption is reported.</summary>
    public bool IsEncrypted { get; }
    /// <summary>Gets the CRC32 checksum, or null if absent.</summary>
    public uint? Crc32 { get; }
    /// <summary>Gets the stored link target, if available.</summary>
    public string? LinkTarget { get; }

    /// <summary>Copies a regular file's contents to a borrowed stream, leaving it open.</summary>
    public void CopyTo(Stream destination, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
        => Owner.CopyEntry(this, destination, progress, cancellationToken);

    /// <summary>Copies content on a worker, leaving the borrowed destination open.</summary>
    public Task CopyToAsync(Stream destination, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
        => Owner.CopyEntryAsync(this, destination, progress, cancellationToken);
}
