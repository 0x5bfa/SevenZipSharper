namespace SevenZipSharper;

public static partial class ArchiveFile
{
    /// <summary>Opens and extracts an archive using validated filesystem paths and per-file publication.</summary>
    public static void ExtractToDirectory(string sourceArchive, string destinationDirectory, ArchiveOpenOptions? openOptions = null,
        ArchiveExtractionOptions? extractionOptions = null, CancellationToken cancellationToken = default)
    {
        using var archive = OpenRead(sourceArchive, openOptions, cancellationToken);
        archive.ExtractToDirectory(destinationDirectory, extractionOptions, cancellationToken: cancellationToken);
    }

    /// <summary>Opens, plans, and extracts an archive on a worker.</summary>
    public static Task ExtractToDirectoryAsync(string sourceArchive, string destinationDirectory, ArchiveOpenOptions? openOptions = null,
        ArchiveExtractionOptions? extractionOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceArchive);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        var open = openOptions is null ? new ArchiveOpenOptions() : openOptions with { };
        var extraction = (extractionOptions ?? new()).Capture();
        return Task.Run(() => ExtractToDirectory(sourceArchive, destinationDirectory, open, extraction, cancellationToken));
    }
}
