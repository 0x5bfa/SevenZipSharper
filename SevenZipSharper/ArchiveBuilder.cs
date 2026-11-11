using SevenZipSharper.Native;
using SevenZipSharper.Operations;

namespace SevenZipSharper;

/// <summary>Collects entry descriptors and writes a new archive once.</summary>
public sealed class ArchiveBuilder
{
    private readonly object gate = new();
    private readonly List<WriteItem> items = [];
    private bool consumed;

    /// <summary>Registers a named file. Each factory result transfers ownership and must contain exactly the declared remaining length.</summary>
    public void AddEntry(string fullName, long length, Func<Stream> openRead, ArchiveEntryOptions? options = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentNullException.ThrowIfNull(openRead);
        Add(new(WriteItem.ValidateName(fullName, false), false, length, openRead, WriteItem.CaptureOptions(options)));
    }

    /// <summary>Registers a directory, including an empty directory.</summary>
    public void AddDirectory(string fullName, ArchiveEntryOptions? options = null)
    {
        Add(new(WriteItem.ValidateName(fullName, true), true, 0, null, WriteItem.CaptureOptions(options)));
    }

    private void Add(WriteItem item)
    {
        lock (gate)
        {
            if (consumed)
            {
                throw new InvalidOperationException("This builder has already been consumed.");
            }

            items.Add(item);
        }
    }

    /// <summary>Writes and finalizes the archive, leaving the borrowed destination open.</summary>
    public void WriteTo(Stream destination, ArchiveWriteOptions options, IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var plan = Reserve(destination, options, cancellationToken);
        WriteOperation.Write(plan.Items, destination, plan.Options, new(cancellationToken, ArchiveOperationPhase.Writing, progress));
    }

    /// <summary>Reserves the builder and writes on a worker. Failures and cancellation consume it.</summary>
    public Task WriteToAsync(Stream destination, ArchiveWriteOptions options, IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var plan = Reserve(destination, options, cancellationToken);
        return Task.Run(() => WriteOperation.Write(plan.Items, destination, plan.Options,
            new(cancellationToken, ArchiveOperationPhase.Writing, progress)));
    }

    private (WriteItem[] Items, WriteConfiguration Options) Reserve(Stream destination, ArchiveWriteOptions options, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(options);
        WriteConfiguration snapshot = options.Capture();
        lock (gate)
        {
            if (consumed)
            {
                throw new InvalidOperationException("This builder has already been consumed.");
            }

            var plan = items.ToArray();
            WriteOperation.Validate(destination, plan, snapshot);
            token.ThrowIfCancellationRequested();
            consumed = true;
            return (plan, snapshot);
        }
    }
}
