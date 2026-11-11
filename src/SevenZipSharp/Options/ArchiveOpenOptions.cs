namespace SevenZipSharp;

/// <summary>Controls opening and metadata enumeration.</summary>
public sealed record ArchiveOpenOptions
{
    internal string? SourceName { get; init; }
    /// <summary>Gets an optional format hint; null enables native format detection.</summary>
    public ArchiveFormat? FormatHint { get; init; }
    /// <summary>Gets the password, or null if no password has been supplied.</summary>
    public string? Password { get; init; }
    /// <summary>Gets the maximum number of metadata entries to materialize.</summary>
    public int MaximumEntries { get; init; } = 1_000_000;
    /// <summary>Gets the maximum embedded-archive search distance in bytes.</summary>
    public long MaximumSearchBytes { get; init; } = 1_048_576;
    /// <summary>Gets a named-volume resolver. Returned streams transfer ownership to the session.</summary>
    public Func<string, Stream?>? VolumeResolver { get; init; }

    /// <summary>Returns a description without exposing the password.</summary>
    public override string ToString()
    {
        return $"{nameof(ArchiveOpenOptions)} {{ FormatHint = {FormatHint}, MaximumEntries = {MaximumEntries}, Password = <redacted> }}";
    }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumSearchBytes);
        if (FormatHint is { } format && (!Enum.IsDefined(format) || format == ArchiveFormat.Other))
        {
            throw new ArgumentException("A format hint must identify a named format.", nameof(FormatHint));
        }
    }
}
