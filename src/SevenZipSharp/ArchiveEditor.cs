using SevenZipSharp.Native;
using SevenZipSharp.Operations;

namespace SevenZipSharp;

/// <summary>Plans changes to an opened archive and writes a replacement once.</summary>
public sealed class ArchiveEditor
{
    private readonly Archive owner;
    private readonly object gate = new();
    private readonly Dictionary<int, WriteItem?> changes = [];
    private readonly List<WriteItem> additions = [];
    private bool consumed;
    internal ArchiveEditor(Archive owner) => this.owner = owner;

    /// <summary>Gets the unchanged source entries used to identify edit targets.</summary>
    public IReadOnlyList<ArchiveEntry> Entries => owner.Entries;

    /// <summary>Removes a source entry from the replacement output.</summary>
    public void Delete(ArchiveEntry entry) => Change(entry, null);

    /// <summary>Changes a source entry's logical name while retaining its content where supported.</summary>
    public void Rename(ArchiveEntry entry, string newFullName)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var item = WriteItem.FromEntry(entry);
        Change(entry, item with { FullName = WriteItem.ValidateName(newFullName, item.IsDirectory), NewProperties = true });
    }

    /// <summary>Replaces a regular file's content with an owned factory result.</summary>
    public void Replace(ArchiveEntry entry, long length, Func<Stream> openRead, ArchiveEntryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentNullException.ThrowIfNull(openRead);
        if (entry.EntryType != ArchiveEntryType.File) throw new NotSupportedException("Only regular files can have replacement content.");
        var metadata = options is null ? WriteItem.FromEntry(entry).Options : WriteItem.CaptureOptions(options);
        Change(entry, new(entry.FullName, false, length, openRead, metadata, (uint)entry.Index, true, true));
    }

    /// <summary>Adds a new regular file to the replacement output.</summary>
    public void AddEntry(string fullName, long length, Func<Stream> openRead, ArchiveEntryOptions? options = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentNullException.ThrowIfNull(openRead);
        var item = new WriteItem(WriteItem.ValidateName(fullName, false), false, length, openRead, WriteItem.CaptureOptions(options));
        lock (gate) { Check(); additions.Add(item); }
    }

    private void Change(ArchiveEntry entry, WriteItem? item)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!ReferenceEquals(entry.Owner, owner)) throw new ArgumentException("The entry belongs to another archive.", nameof(entry));
        if (entry.EntryType is not (ArchiveEntryType.File or ArchiveEntryType.Directory))
            throw new NotSupportedException("Editing links and special entries is not supported.");
        lock (gate)
        {
            Check();
            if (!changes.TryAdd(entry.Index, item)) throw new InvalidOperationException("Conflicting edits target the same source entry.");
        }
    }

    /// <summary>Adds an explicit directory, including an empty directory, to the replacement output.</summary>
    public void AddDirectory(string fullName, ArchiveEntryOptions? options = null)
    {
        var item = new WriteItem(WriteItem.ValidateName(fullName, true), true, 0, null, WriteItem.CaptureOptions(options));
        lock (gate) { Check(); additions.Add(item); }
    }

    private void Check()
    {
        if (consumed) throw new InvalidOperationException("This editor has already been consumed.");
    }

    /// <summary>Writes a replacement archive to a distinct borrowed destination.</summary>
    public void WriteTo(Stream destination, ArchiveUpdateOptions? options = null, IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var plan = Reserve(destination, options, cancellationToken);
        using var lease = plan.Lease;
        Execute(plan.Items, destination, plan.Options, lease, progress);
    }

    /// <summary>Reserves the source session and writes replacement output on a worker.</summary>
    public Task WriteToAsync(Stream destination, ArchiveUpdateOptions? options = null, IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var plan = Reserve(destination, options, cancellationToken);
        return Task.Run(() =>
        {
            using var lease = plan.Lease;
            Execute(plan.Items, destination, plan.Options, lease, progress);
        });
    }

    private (WriteItem[] Items, WriteConfiguration Options, Archive.OperationLease Lease) Reserve(
        Stream destination, ArchiveUpdateOptions? options, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (ReferenceEquals(destination, owner.Source)) throw new ArgumentException("Replacement output must not alias the source.", nameof(destination));
        WriteConfiguration config;
        if (options?.WriteOptions is { } writeOptions) config = writeOptions.Capture();
        else
        {
            if (Entries.Any(entry => entry.IsEncrypted)) throw new ArgumentException("Editing encrypted archives requires explicit write options.", nameof(options));
            config = new(owner.Format, null, []);
        }
        if (config.Format != owner.Format) throw new ArgumentException("An editor cannot change the archive format.", nameof(options));
        lock (gate)
        {
            Check();
            var items = Entries.Select(entry => changes.TryGetValue(entry.Index, out var changed) ? changed : WriteItem.FromEntry(entry))
                .Where(item => item is not null).Cast<WriteItem>().Concat(additions).ToArray();
            WriteOperation.Validate(destination, items, config);
            var lease = owner.Enter(token);
            consumed = true;
            return (items, config, lease);
        }
    }

    private void Execute(WriteItem[] items, Stream destination, WriteConfiguration options,
        Archive.OperationLease lease, IProgress<ArchiveProgress>? progress)
    {
        var context = new OperationContext(lease.Token, ArchiveOperationPhase.Writing, progress, owner);
        try { WriteOperation.Write(items, destination, options, context, owner.Session, owner.Password, owner.Source); }
        catch (Exception error)
        {
            owner.MarkFaulted();
            if (error is OperationCanceledException && lease.CallerToken.IsCancellationRequested)
                throw new OperationCanceledException("The archive edit was canceled.", error, lease.CallerToken);
            throw;
        }
    }
}
