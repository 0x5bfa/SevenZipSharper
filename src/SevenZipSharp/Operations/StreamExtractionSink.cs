using SevenZipSharp.Native.Callbacks;

namespace SevenZipSharp.Operations;

internal sealed class StreamExtractionSink(Func<ArchiveEntry, Stream?> factory, bool ownsStreams) : IExtractionSink, IDisposable
{
    private readonly Dictionary<int, Stream> streams = [];
    private readonly HashSet<Stream> issued = new(ReferenceEqualityComparer.Instance);
    internal int Outstanding => streams.Count;

    public Stream? Open(ArchiveEntry entry)
    {
        Stream? stream = factory(entry);
        if (stream is null)
        {
            return null;
        }

        if (!issued.Add(stream) && ownsStreams)
        {
            throw new ArgumentException("Each entry destination must be a distinct stream.");
        }

        streams.Add(entry.Index, stream);
        return stream;
    }

    public void Complete(ArchiveEntry entry, bool success)
    {
        if (streams.Remove(entry.Index, out var stream) && ownsStreams)
        {
            stream.Dispose();
        }
    }

    public void Dispose()
    {
        Exception? first = null;
        foreach (var stream in streams.Values)
        {
            if (!ownsStreams)
            {
                continue;
            }

            try { stream.Dispose(); } catch (Exception error) { first ??= error; }
        }
        streams.Clear();
        if (first is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
        }
    }
}
