using SevenZipSharper.FileSystem;

namespace SevenZipSharper;

/// <summary>Connects archive and entry operations to filesystem conveniences.</summary>
public static class ArchiveFileExtensions
{
    /// <summary>Captures a file descriptor and defers opening its contents until archive writing.</summary>
    public static void AddFile(this ArchiveBuilder builder, string path, string fullName, ArchiveEntryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists)
        {
            throw new FileNotFoundException("The source file does not exist.", file.FullName);
        }

        if ((file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Device)) != 0)
        {
            throw new NotSupportedException("The source must be a regular file.");
        }

        options ??= new() { LastWriteTime = new DateTimeOffset(file.LastWriteTimeUtc), Attributes = file.Attributes };
        string source = file.FullName;
        builder.AddEntry(fullName, file.Length, () => new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read), options);
    }

    /// <summary>Extracts using validated paths and per-file publication. Use an application-controlled destination to avoid path races.</summary>
    public static void ExtractToDirectory(this Archive archive, string destinationDirectory, ArchiveExtractionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        var snapshot = (options ?? new()).Capture();
        using var lease = archive.Enter(cancellationToken);
        var plan = DirectoryExtractionSink.Create(archive.Entries, destinationDirectory, snapshot, lease.Token);
        archive.RunExtraction(lease, plan.Entries, plan.Sink, false, progress);
    }

    /// <summary>Reserves the archive, then plans and extracts filesystem output on a worker.</summary>
    public static Task ExtractToDirectoryAsync(this Archive archive, string destinationDirectory, ArchiveExtractionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        var snapshot = (options ?? new()).Capture();
        var lease = archive.Enter(cancellationToken);
        return Task.Run(() =>
        {
            using (lease)
            {
                var plan = DirectoryExtractionSink.Create(archive.Entries, destinationDirectory, snapshot, lease.Token);
                archive.RunExtraction(lease, plan.Entries, plan.Sink, false, progress);
            }
        });
    }

    /// <summary>Extracts a regular entry to an explicit path, publishing only after verification succeeds.</summary>
    public static void ExtractToFile(this ArchiveEntry entry, string path, ArchiveExtractionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var snapshot = (options ?? new()).Capture();
        using var lease = entry.Owner.Enter(cancellationToken);
        var sink = DirectoryExtractionSink.CreateSingle(entry, path, snapshot, lease.Token);
        entry.Owner.RunExtraction(lease, [entry], sink, false, progress);
    }

    /// <summary>Extracts a regular entry to an explicit path on a worker.</summary>
    public static Task ExtractToFileAsync(this ArchiveEntry entry, string path, ArchiveExtractionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var snapshot = (options ?? new()).Capture();
        var lease = entry.Owner.Enter(cancellationToken);
        return Task.Run(() =>
        {
            using (lease)
            {
                var sink = DirectoryExtractionSink.CreateSingle(entry, path, snapshot, lease.Token);
                entry.Owner.RunExtraction(lease, [entry], sink, false, progress);
            }
        });
    }
}
