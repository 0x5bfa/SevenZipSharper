using SevenZipSharper.FileSystem;

namespace SevenZipSharper;

public static partial class ArchiveFile
{
    /// <summary>Creates and publishes a new archive from a directory after successful finalization.</summary>
    public static void CreateFromDirectory(string sourceDirectory, string destinationArchive, ArchiveWriteOptions options,
        ArchiveCreationOptions? creationOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationArchive);
        ArgumentNullException.ThrowIfNull(options);
        _ = options.Capture();
        var snapshot = (creationOptions ?? new()).Capture();
        var builder = DirectoryInputPlanner.Create(sourceDirectory, destinationArchive, snapshot, cancellationToken);
        TemporaryArchivePublisher.Write(destinationArchive, snapshot.Overwrite,
            output => builder.WriteTo(output, options, cancellationToken: cancellationToken), cancellationToken);
    }

    /// <summary>Plans, creates, and publishes a directory archive on a worker.</summary>
    public static Task CreateFromDirectoryAsync(string sourceDirectory, string destinationArchive, ArchiveWriteOptions options,
        ArchiveCreationOptions? creationOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationArchive);
        ArgumentNullException.ThrowIfNull(options);
        _ = options.Capture();
        var snapshot = (creationOptions ?? new()).Capture();
        return Task.Run(() => CreateFromDirectory(sourceDirectory, destinationArchive, options, snapshot, cancellationToken));
    }

    /// <summary>Writes edits to a sibling temporary archive, closes the source, and then replaces the original.</summary>
    public static void Update(string path, Action<ArchiveEditor> configure, ArchiveOpenOptions? openOptions = null,
        ArchiveUpdateOptions? updateOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(configure);
        string target = Path.GetFullPath(path);
        TemporaryArchivePublisher.Write(target, true, output =>
        {
            using var archive = OpenRead(target, openOptions, cancellationToken);
            var editor = archive.CreateEditor();
            configure(editor);
            editor.WriteTo(output, updateOptions, cancellationToken: cancellationToken);
        }, cancellationToken);
    }

    /// <summary>Plans edits and publishes a replacement on a worker.</summary>
    public static Task UpdateAsync(string path, Action<ArchiveEditor> configure, ArchiveOpenOptions? openOptions = null,
        ArchiveUpdateOptions? updateOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(configure);
        var open = openOptions is null ? new ArchiveOpenOptions() : openOptions with { };
        var update = updateOptions is null ? new ArchiveUpdateOptions() : updateOptions with { };
        return Task.Run(() => Update(path, configure, open, update, cancellationToken));
    }
}
