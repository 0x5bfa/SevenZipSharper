using SevenZipSharp.Native.Callbacks;

namespace SevenZipSharp.FileSystem;

internal sealed class DirectoryExtractionSink : IExtractionSink, IDisposable
{
    private readonly ArchiveExtractionOptions options;
    private readonly Dictionary<int, (ArchiveEntry Entry, string Path)> targets;
    private readonly Dictionary<int, PendingFile> pending = [];
    private readonly HashSet<int> completed = [];
    private long totalBytes;

    private DirectoryExtractionSink(Dictionary<int, (ArchiveEntry Entry, string Path)> targets, ArchiveExtractionOptions options)
    {
        this.targets = targets;
        this.options = options;
    }

    internal static (DirectoryExtractionSink Sink, ArchiveEntry[] Entries) Create(
        IReadOnlyList<ArchiveEntry> entries, string destination, ArchiveExtractionOptions options, CancellationToken token)
    {
        string root = Path.GetFullPath(destination);
        if (entries.Count > options.MaximumEntries)
        {
            throw new InvalidDataException("The extraction entry limit was exceeded.");
        }

        var targets = new Dictionary<int, (ArchiveEntry Entry, string Path)>();
        var unique = new Dictionary<string, ArchiveEntryType>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            if (entry.EntryType is not (ArchiveEntryType.File or ArchiveEntryType.Directory))
            {
                if (options.UnsupportedEntries == UnsupportedEntryPolicy.Skip)
                {
                    continue;
                }

                throw new NotSupportedException("Links and special entries cannot be restored by this extraction policy.");
            }
            string path = ExtractionPathPolicy.Resolve(root, entry.FullName);
            if (!unique.TryAdd(path, entry.EntryType))
            {
                throw new InvalidDataException("Multiple entries map to the same filesystem path.");
            }

            targets.Add(entry.Index, (entry, path));
        }
        foreach (string path in unique.Keys)
        {
            token.ThrowIfCancellationRequested();
            for (string? parent = Path.GetDirectoryName(path); parent is not null && parent.Length >= root.Length; parent = Path.GetDirectoryName(parent))
            {
                if (unique.TryGetValue(parent, out var type) && type != ArchiveEntryType.Directory)
                {
                    throw new InvalidDataException("An entry is both a file and a parent directory.");
                }
            }
        }
        var sink = new DirectoryExtractionSink(targets, options);
        sink.Prepare(root, token);
        return (sink, targets.Values.Select(target => target.Entry).OrderBy(entry => entry.Index).ToArray());
    }

    internal static DirectoryExtractionSink CreateSingle(ArchiveEntry entry, string path, ArchiveExtractionOptions options, CancellationToken token)
    {
        if (entry.EntryType != ArchiveEntryType.File)
        {
            throw new NotSupportedException("Only regular files can be extracted to a file.");
        }

        if (options.MaximumEntries < 1)
        {
            throw new InvalidDataException("The extraction entry limit was exceeded.");
        }

        string target = Path.GetFullPath(path);
        ExtractionPathPolicy.ValidateSegment(Path.GetFileName(target));
        var sink = new DirectoryExtractionSink(new() { [entry.Index] = (entry, target) }, options);
        sink.Prepare(Path.GetDirectoryName(target)!, token);
        return sink;
    }

    private void Prepare(string root, CancellationToken token)
    {
        ExtractionPathPolicy.EnsureNoReparsePoints(root);
        foreach (var target in targets.Values)
        {
            token.ThrowIfCancellationRequested();
            ExtractionPathPolicy.EnsureNoReparsePoints(target.Path);
            if (target.Entry.EntryType == ArchiveEntryType.File)
            {
                if (Directory.Exists(target.Path))
                {
                    throw new IOException("A file destination is an existing directory.");
                }

                if (!options.Overwrite && File.Exists(target.Path))
                {
                    throw new IOException("A file destination already exists.");
                }
            }
            else if (File.Exists(target.Path))
            {
                throw new IOException("A directory destination is an existing file.");
            }
        }
        Directory.CreateDirectory(root);
        foreach (var target in targets.Values.Where(target => target.Entry.EntryType == ArchiveEntryType.Directory)
                     .OrderBy(target => target.Path.Length))
        {
            token.ThrowIfCancellationRequested();
            ExtractionPathPolicy.EnsureNoReparsePoints(target.Path);
            Directory.CreateDirectory(target.Path);
        }
    }

    public Stream? Open(ArchiveEntry entry)
    {
        var target = targets[entry.Index];
        string parent = Path.GetDirectoryName(target.Path)!;
        ExtractionPathPolicy.EnsureNoReparsePoints(parent);
        Directory.CreateDirectory(parent);
        ExtractionPathPolicy.EnsureNoReparsePoints(target.Path);
        string temporary = Path.Combine(parent, $".sevenzipsharp-{Guid.NewGuid():N}.tmp");
        var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var limited = new LimitedOutput(file, count =>
        {
            long entryBytes = checked((pending.TryGetValue(entry.Index, out var state) ? state.Stream.Bytes : 0) + count);
            long sum = checked(totalBytes + count);
            if (options.MaximumEntryBytes is { } entryLimit && entryBytes > entryLimit ||
                options.MaximumTotalBytes is { } totalLimit && sum > totalLimit)
            {
                throw new InvalidDataException("The extraction actual-byte limit was exceeded.");
            }

            totalBytes = sum;
        });
        pending.Add(entry.Index, new(target.Path, temporary, limited));
        return limited;
    }

    public void Complete(ArchiveEntry entry, bool success)
    {
        if (entry.EntryType == ArchiveEntryType.Directory) { completed.Add(entry.Index); return; }
        if (!pending.Remove(entry.Index, out var file))
        {
            return;
        }

        try
        {
            file.Stream.Dispose();
            if (!success)
            {
                return;
            }

            if (entry.Length is { } length && file.Stream.Bytes != length)
            {
                throw new InvalidDataException("The extracted file does not match its declared length.");
            }

            if (options.PreserveLastWriteTime && entry.LastWriteTime is { } time)
            {
                File.SetLastWriteTimeUtc(file.Temporary, time.UtcDateTime);
            }

            if (options.PreserveAttributes && entry.NativeAttributes is { } attributes)
            {
                const FileAttributes allowed = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive | FileAttributes.NotContentIndexed;
                File.SetAttributes(file.Temporary, (FileAttributes)attributes & allowed);
            }
            ExtractionPathPolicy.EnsureNoReparsePoints(file.Target);
            File.Move(file.Temporary, file.Target, options.Overwrite);
            completed.Add(entry.Index);
        }
        finally
        {
            if (File.Exists(file.Temporary))
            {
                File.SetAttributes(file.Temporary, FileAttributes.Normal);
                File.Delete(file.Temporary);
            }
        }
    }

    public void Finish()
    {
        if (pending.Count != 0 || targets.Values.Any(target => target.Entry.EntryType == ArchiveEntryType.File && !completed.Contains(target.Entry.Index)))
        {
            throw new InvalidDataException("The native engine did not complete every selected file.");
        }

        if (!options.PreserveLastWriteTime)
        {
            return;
        }

        foreach (var target in targets.Values.Where(target => target.Entry.EntryType == ArchiveEntryType.Directory)
                     .OrderByDescending(target => target.Path.Length))
        {
            if (target.Entry.LastWriteTime is { } time)
            {
                Directory.SetLastWriteTimeUtc(target.Path, time.UtcDateTime);
            }
        }
    }

    public void Dispose()
    {
        Exception? first = null;
        foreach (var file in pending.Values)
        {
            try { file.Stream.Dispose(); } catch (Exception error) { first ??= error; }
            try { File.Delete(file.Temporary); } catch (Exception error) { first ??= error; }
        }
        pending.Clear();
        if (first is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
        }
    }

    private sealed record PendingFile(string Target, string Temporary, LimitedOutput Stream);

    private sealed class LimitedOutput(Stream destination, Action<int> reserve) : Stream
    {
        private bool disposed;
        internal long Bytes { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !disposed;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            reserve(buffer.Length);
            destination.Write(buffer);
            Bytes += buffer.Length;
        }
        public override void Flush()
        {
            destination.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposed && disposing) { disposed = true; destination.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
