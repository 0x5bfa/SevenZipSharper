namespace SevenZipSharper;

/// <summary>Identifies an actionable archive-specific failure.</summary>
public enum ArchiveErrorCode
{
    /// <summary>A password must be supplied.</summary>
    PasswordRequired,
    /// <summary>The native engine reported an incorrect password.</summary>
    WrongPassword,
    /// <summary>A required volume could not be resolved.</summary>
    MissingVolume,
    /// <summary>The native engine could not be loaded.</summary>
    NativeLibraryUnavailable
}

/// <summary>Represents an archive-specific I/O failure.</summary>
public sealed class ArchiveException : IOException
{
    /// <summary>Initializes an archive-specific failure.</summary>
    public ArchiveException(ArchiveErrorCode errorCode, string message, Exception? innerException = null)
        : base(message, innerException) => ErrorCode = errorCode;

    /// <summary>Gets the actionable failure category.</summary>
    public ArchiveErrorCode ErrorCode { get; }
}
