namespace SevenZipSharp;

/// <summary>Describes the kind of an archive entry.</summary>
public enum ArchiveEntryType
{
    /// <summary>A regular file.</summary>
    File,
    /// <summary>A directory.</summary>
    Directory,
    /// <summary>A symbolic link.</summary>
    SymbolicLink,
    /// <summary>A hard link.</summary>
    HardLink,
    /// <summary>An unsupported special entry.</summary>
    Other
}
