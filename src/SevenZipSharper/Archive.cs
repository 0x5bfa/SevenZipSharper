using System.Collections.ObjectModel;
using SevenZipSharper.Native;
using SevenZipSharper.Native.Callbacks;
using SevenZipSharper.Native.Properties;
using SevenZipSharper.Operations;
using Windows.Win32.Foundation;

namespace SevenZipSharper;

/// <summary>An opened, read-only archive. One operation may execute at a time.</summary>
public sealed class Archive : IDisposable, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly NativeArchiveSession session;
    private readonly Stream source;
    private readonly bool leaveOpen;
    private readonly string? password;
    private OperationLease? active;
    private bool closing, disposed, faulted;

    private Archive(NativeArchiveSession session, Stream source, bool leaveOpen, ArchiveOpenOptions options, OperationContext context)
    {
        this.session = session; this.source = source; this.leaveOpen = leaveOpen; password = options.Password;
        Format = session.Format.Format;
        var metadata = new NativeMetadataReader(session).Read(options, context);
        Entries = new ReadOnlyCollection<ArchiveEntry>(metadata.Entries.Select(entry => new ArchiveEntry(
            this, entry.Index, entry.Name, entry.Type, entry.Length, entry.PackedLength,
            entry.ModifiedTime, entry.Encrypted, entry.Crc32, entry.LinkTarget, entry.Attributes)).ToArray());
        Info = metadata.Info;
        Capabilities = metadata.Capabilities;
    }

    /// <summary>Gets the recognized format.</summary>
    public ArchiveFormat Format { get; }
    /// <summary>Gets cached archive metadata.</summary>
    public ArchiveInfo Info { get; }
    /// <summary>Gets cached entries in archive order, preserving duplicate names.</summary>
    public IReadOnlyList<ArchiveEntry> Entries { get; }
    /// <summary>Gets capabilities advertised by this archive's handler.</summary>
    public ArchiveCapabilities Capabilities { get; }

    /// <summary>Opens a readable, seekable stream at its current position. Ownership transfers only on success.</summary>
    public static Archive Open(Stream source, ArchiveOpenOptions? options = null, bool leaveOpen = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead || !source.CanSeek)
        {
            throw new ArgumentException("An archive source must be readable and seekable.", nameof(source));
        }

        options = options is null ? new() : options with { };
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var context = new OperationContext(cancellationToken, ArchiveOperationPhase.Opening);
        var session = NativeArchiveSession.Open(source, options, context);
        try
        {
            session.Attach(context);
            var archive = new Archive(session, source, leaveOpen, options, context);
            context.Complete(HRESULT.S_OK);
            return archive;
        }
        catch { session.Dispose(); throw; }
        finally { session.Attach(null); }
    }

    /// <summary>Opens and materializes metadata on a worker; the native operation is synchronous.</summary>
    public static Task<Archive> OpenAsync(Stream source, ArchiveOpenOptions? options = null, bool leaveOpen = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var snapshot = options is null ? new ArchiveOpenOptions() : options with { };
        snapshot.Validate();
        return Task.Run(() => Open(source, snapshot, leaveOpen, cancellationToken));
    }

    /// <summary>Returns the first exact, ordinal name match, or null.</summary>
    public ArchiveEntry? GetEntry(string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        return Entries.FirstOrDefault(entry => entry.FullName.Equals(fullName, StringComparison.Ordinal));
    }

    /// <summary>Returns all exact, ordinal name matches in archive order.</summary>
    public IEnumerable<ArchiveEntry> GetEntries(string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        return Entries.Where(entry => entry.FullName.Equals(fullName, StringComparison.Ordinal));
    }

    /// <summary>Extracts a selection in one native call. Factory results are owned by this operation; null skips an entry.</summary>
    public void Extract(IEnumerable<ArchiveEntry> entries, Func<ArchiveEntry, Stream?> openDestination,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(openDestination);
        var selection = Select(entries);
        using var lease = Enter(cancellationToken);
        RunExtraction(lease, selection, new StreamExtractionSink(openDestination, true), false, progress);
    }

    /// <summary>Extracts a selection on a worker, reserving the archive before scheduling.</summary>
    public Task ExtractAsync(IEnumerable<ArchiveEntry> entries, Func<ArchiveEntry, Stream?> openDestination,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(openDestination);
        var selection = Select(entries);
        var lease = Enter(cancellationToken);
        return Task.Run(() =>
        {
            using (lease)
            {
                RunExtraction(lease, selection, new StreamExtractionSink(openDestination, true), false, progress);
            }
        });
    }

    /// <summary>Verifies entry integrity without producing output. Corruption throws.</summary>
    public void Test(CancellationToken cancellationToken = default)
    {
        using var lease = Enter(cancellationToken);
        RunExtraction(lease, [.. Entries], new StreamExtractionSink(_ => null, false), true, null);
    }

    /// <summary>Verifies integrity on a worker.</summary>
    public Task TestAsync(CancellationToken cancellationToken = default)
    {
        var lease = Enter(cancellationToken);
        return Task.Run(() =>
        {
            using (lease)
            {
                RunExtraction(lease, [.. Entries], new StreamExtractionSink(_ => null, false), true, null);
            }
        }, cancellationToken);
    }

    /// <summary>Creates an edit plan borrowing this archive until replacement writing completes.</summary>
    public ArchiveEditor CreateEditor()
    {
        using var lease = Enter(CancellationToken.None);
        if (!Capabilities.CanUpdate)
        {
            throw new NotSupportedException("The archive handler does not support editing.");
        }

        return new(this);
    }

    internal void CopyEntry(ArchiveEntry entry, Stream destination, IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        ValidateCopy(entry, destination);
        using var lease = Enter(token);
        RunExtraction(lease, [entry], new StreamExtractionSink(_ => destination, false), false, progress);
    }

    internal Task CopyEntryAsync(ArchiveEntry entry, Stream destination, IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        ValidateCopy(entry, destination);
        var lease = Enter(token);
        return Task.Run(() => { using (lease) { RunExtraction(lease, [entry], new StreamExtractionSink(_ => destination, false), false, progress); } });
    }

    private void ValidateCopy(ArchiveEntry entry, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination must be writable.", nameof(destination));
        }

        if (ReferenceEquals(source, destination))
        {
            throw new ArgumentException("Source and destination must not alias.", nameof(destination));
        }

        if (!ReferenceEquals(entry.Owner, this))
        {
            throw new ArgumentException("The entry belongs to another archive.", nameof(entry));
        }

        if (entry.EntryType != ArchiveEntryType.File)
        {
            throw new NotSupportedException("Only regular files have copyable content.");
        }
    }

    internal ArchiveEntry[] Select(IEnumerable<ArchiveEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var selection = entries.ToArray();
        if (selection.Any(entry => entry is null || !ReferenceEquals(entry.Owner, this)))
        {
            throw new ArgumentException("Every selected entry must belong to this archive.", nameof(entries));
        }

        if (selection.Any(entry => entry.EntryType is not (ArchiveEntryType.File or ArchiveEntryType.Directory)))
        {
            throw new NotSupportedException("Links and special entries require an explicit extraction policy.");
        }

        if (selection.Select(entry => entry.Index).Distinct().Count() != selection.Length)
        {
            throw new ArgumentException("An entry must not be selected more than once.", nameof(entries));
        }

        return [.. selection.OrderBy(entry => entry.Index)];
    }

    internal void RunExtraction(OperationLease lease, ArchiveEntry[] selection, IExtractionSink sink, bool test,
        IProgress<ArchiveProgress>? progress)
    {
        long? total = 0;
        foreach (var entry in selection)
        {
            total = total is { } sum && entry.Length is { } length && length <= long.MaxValue - sum ? sum + length : null;
        }

        var context = new OperationContext(lease.Token, test ? ArchiveOperationPhase.Testing : ArchiveOperationPhase.Extracting,
            progress, this, test ? null : total);
        bool started = false;
        try
        {
            context.Check();
            if (selection.Length == 0) { sink.Finish(); return; }
            var indexes = selection.Select(entry => (uint)entry.Index).ToArray();
            using var callback = new ExtractCallback(Entries, [.. indexes.Select(i => (int)i)], sink, context, password, test);
            started = true;
            HRESULT result = session.Extract(indexes, test, callback, context);
            context.Complete(result);
            sink.Finish();
            context.Report(null, true);
        }
        catch (Exception error)
        {
            context.Fail(error);
            if (started)
            {
                faulted = true;
            }

            if (error is OperationCanceledException && lease.CallerToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("The archive operation was canceled.", error, lease.CallerToken);
            }

            throw;
        }
        finally
        {
            if (sink is IDisposable disposable)
            {
                try { disposable.Dispose(); }
                catch when (context.HasFailure) { /* Preserve the causal operation failure. */ }
            }
        }
    }

    internal OperationLease Enter(CancellationToken token)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed || closing, this);
            if (faulted)
            {
                throw new InvalidOperationException("The native session failed; open a new archive before continuing.");
            }

            if (active is not null)
            {
                throw new InvalidOperationException("Another operation is already using this archive.");
            }

            token.ThrowIfCancellationRequested();
            return active = new(this, token);
        }
    }

    internal sealed class OperationLease : IDisposable
    {
        private readonly Archive owner;
        private readonly CancellationTokenSource cancellation;
        private bool released;
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken Token { get; }
        internal CancellationToken CallerToken { get; }
        internal OperationLease(Archive owner, CancellationToken token)
        {
            this.owner = owner;
            CallerToken = token;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            Token = cancellation.Token;
        }
        internal void Cancel() { lock (owner.gate) { if (!released) { cancellation.Cancel(); } } }
        public void Dispose()
        {
            lock (owner.gate)
            {
                if (released)
                {
                    return;
                }

                released = true;
                owner.active = null;
                cancellation.Dispose();
            }
            Completion.TrySetResult();
        }
    }

    /// <summary>Releases the session. Disposal while an operation is active is rejected.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (active is not null)
            {
                throw new InvalidOperationException("Cancel and await the active operation before disposing synchronously.");
            }

            disposed = closing = true;
        }
        try { session.Dispose(); } finally { if (!leaveOpen)
            {
                source.Dispose();
            }
        }
    }

    /// <summary>Cancels and awaits active work before releasing the session.</summary>
    public ValueTask DisposeAsync()
    {
        if (ReferenceEquals(OperationContext.CallbackOwner, this))
        {
            throw new InvalidOperationException("An archive cannot be disposed from its own native callback.");
        }

        OperationLease? operation;
        lock (gate)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }

            closing = true;
            operation = active;
        }
        return DisposeAfterAsync(operation);
    }

    private async ValueTask DisposeAfterAsync(OperationLease? operation)
    {
        operation?.Cancel();
        if (operation is not null)
        {
            await operation.Completion.Task.ConfigureAwait(false);
        }

        Dispose();
    }

    internal NativeArchiveSession Session => session;
    internal Stream Source => source;
    internal string? Password => password;
    internal void MarkFaulted()
    {
        faulted = true;
    }
}
