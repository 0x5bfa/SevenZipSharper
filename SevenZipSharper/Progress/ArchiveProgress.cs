namespace SevenZipSharper;

/// <summary>Identifies the current operation phase.</summary>
public enum ArchiveOperationPhase
{
    /// <summary>Opening and enumerating metadata.</summary>
    Opening,
    /// <summary>Extracting entry contents.</summary>
    Extracting,
    /// <summary>Testing entry integrity.</summary>
    Testing,
    /// <summary>Writing an archive.</summary>
    Writing
}

/// <summary>Contains a progress snapshot. Byte counters describe actual managed I/O, not native codec percentages.</summary>
/// <param name="Phase">The current phase.</param>
/// <param name="Entry">The current entry, if available.</param>
/// <param name="CompletedBytes">Bytes transferred through the operation's managed content adapters.</param>
/// <param name="TotalBytes">The expected byte count, if known.</param>
/// <param name="CompletedEntries">The number of completed entries.</param>
public sealed record ArchiveProgress(
    ArchiveOperationPhase Phase, ArchiveEntry? Entry, long CompletedBytes,
    long? TotalBytes, int CompletedEntries);
