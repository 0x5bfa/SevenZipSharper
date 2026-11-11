namespace SevenZipSharper;

/// <summary>Describes immutable archive metadata.</summary>
/// <param name="FormatName">The native handler's format name.</param>
/// <param name="IsSolid">Whether entries share solid compression blocks.</param>
/// <param name="Length">The total uncompressed length, or null if unavailable.</param>
/// <param name="CompressedLength">The physical archive length, or null if unavailable.</param>
public sealed record ArchiveInfo(string FormatName, bool IsSolid, long? Length, long? CompressedLength);

/// <summary>Describes operations advertised by an opened archive's handler.</summary>
/// <param name="CanUpdate">Whether the handler exposes an archive writer.</param>
public sealed record ArchiveCapabilities(bool CanUpdate);
