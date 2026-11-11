namespace SevenZipSharper;

/// <summary>Identifies an archive container or single-payload compression format.</summary>
public enum ArchiveFormat
{
    /// <summary>A format identified by the native engine without a named managed identifier.</summary>
    Other,
    /// <summary>The 7z container.</summary>
    SevenZip,
    /// <summary>The ZIP container.</summary>
    Zip,
    /// <summary>The TAR container.</summary>
    Tar,
    /// <summary>A GZip payload.</summary>
    GZip,
    /// <summary>A BZip2 payload.</summary>
    BZip2,
    /// <summary>An XZ payload.</summary>
    Xz,
    /// <summary>A RAR archive before version 5.</summary>
    Rar,
    /// <summary>A RAR version 5 archive.</summary>
    Rar5,
    /// <summary>A cabinet archive.</summary>
    Cab,
    /// <summary>An ISO image.</summary>
    Iso,
    /// <summary>A Windows imaging archive.</summary>
    Wim
}
