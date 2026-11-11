namespace SevenZipSharper;

/// <summary>Provides filesystem conveniences over the stream-based archive API.</summary>
public static partial class ArchiveFile
{
    /// <summary>Opens a file for reading and owns its stream on both success and failure.</summary>
    public static Archive OpenRead(string path, ArchiveOpenOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        string fullPath = Path.GetFullPath(path);
        options = options is null ? new() { SourceName = Path.GetFileName(fullPath) } : options with { SourceName = Path.GetFileName(fullPath) };
        if (options.VolumeResolver is null)
        {
            string directory = Path.GetDirectoryName(fullPath)!;
            options = options with { VolumeResolver = name =>
            {
                if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', ':']) >= 0 || name is "." or "..")
                {
                    throw new InvalidDataException("A volume name must identify a file in the source directory.");
                }

                string volume = Path.Combine(directory, name);
                return File.Exists(volume) ? new FileStream(volume, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
            }};
        }
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try { return Archive.Open(stream, options, leaveOpen: false, cancellationToken); }
        catch { stream.Dispose(); throw; }
    }

    /// <summary>Opens a file and reads archive metadata on a worker.</summary>
    public static Task<Archive> OpenReadAsync(string path, ArchiveOpenOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var snapshot = options is null ? new ArchiveOpenOptions() : options with { };
        snapshot.Validate();
        return Task.Run(() => OpenRead(path, snapshot, cancellationToken));
    }
}
