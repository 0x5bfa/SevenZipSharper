namespace SevenZipSharp.FileSystem;

internal static class TemporaryArchivePublisher
{
    internal static void Write(string destination, bool overwrite, Action<Stream> write, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        token.ThrowIfCancellationRequested();
        string target = Path.GetFullPath(destination);
        string directory = Path.GetDirectoryName(target)!;
        ExtractionPathPolicy.ValidateSegment(Path.GetFileName(target));
        ExtractionPathPolicy.EnsureNoReparsePoints(target);
        if (!overwrite && File.Exists(target))
        {
            throw new IOException("The destination archive already exists.");
        }

        if (Directory.Exists(target))
        {
            throw new IOException("The destination archive is a directory.");
        }

        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".sevenzipsharp-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                write(stream);
                token.ThrowIfCancellationRequested();
                stream.Flush(flushToDisk: true);
            }
            ExtractionPathPolicy.EnsureNoReparsePoints(target);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite);
        }
        catch
        {
            try { File.Delete(temporary); } catch { /* Preserve the operation/publication failure. */ }
            throw;
        }
    }
}
