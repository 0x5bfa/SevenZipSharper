namespace SevenZipSharper;

/// <summary>Describes metadata assigned to a new or replaced entry.</summary>
public sealed record ArchiveEntryOptions
{
    /// <summary>Gets the optional last modification time.</summary>
    public DateTimeOffset? LastWriteTime { get; init; }
    /// <summary>Gets optional filesystem attributes. Reparse points are unsupported.</summary>
    public FileAttributes? Attributes { get; init; }

    internal void Validate()
    {
        if ((Attributes.GetValueOrDefault() & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
        {
            throw new NotSupportedException("Links and device entries cannot be created as regular files.");
        }

        if (LastWriteTime is { } time)
        {
            _ = time.ToFileTime();
        }
    }
}
