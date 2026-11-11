using System.Diagnostics;
using System.Runtime.ExceptionServices;
using SevenZipSharp.Interop;
using Windows.Win32.Foundation;

namespace SevenZipSharp.Operations;

internal sealed class OperationContext(
    CancellationToken cancellationToken,
    ArchiveOperationPhase phase,
    IProgress<ArchiveProgress>? progress = null,
    Archive? owner = null,
    long? totalBytes = null)
{
    [ThreadStatic] private static Archive? callbackOwner;
    private ExceptionDispatchInfo? failure;
    private long bytes;
    private int entries;
    private long lastReport;
    private readonly object progressGate = new();

    internal static Archive? CallbackOwner => callbackOwner;
    internal CancellationToken CancellationToken => cancellationToken;
    internal bool HasFailure => Volatile.Read(ref failure) is not null;
    internal CallbackScope EnterCallback()
    {
        return new(owner);
    }

    internal void Check()
    {
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal HRESULT Fail(Exception exception)
    {
        Interlocked.CompareExchange(ref failure, ExceptionDispatchInfo.Capture(exception), null);
        return exception is OperationCanceledException ? HRESULT.E_ABORT : HRESULT.E_FAIL;
    }

    internal void Complete(HRESULT result)
    {
        Volatile.Read(ref failure)?.Throw();
        Check();
        result.ThrowOnFailure();
    }

    internal void Result(int result, ArchiveEntry? entry = null)
    {
        if (result == 0)
        {
            return;
        }

        Exception error = result switch
        {
            1 => new NotSupportedException("The entry uses an unsupported compression method."),
            9 => new ArchiveException(ArchiveErrorCode.WrongPassword, "The archive password is incorrect."),
            _ => new InvalidDataException($"Entry '{entry?.FullName ?? "(unknown)"}' failed integrity verification (native result {result}).")
        };
        Fail(error);
    }

    internal void AddBytes(int count, ArchiveEntry? entry = null)
    {
        Interlocked.Add(ref bytes, count);
        Report(entry, false);
    }

    internal void EntryCompleted(ArchiveEntry? entry)
    {
        Interlocked.Increment(ref entries);
        Report(entry, true);
    }

    internal void Report(ArchiveEntry? entry, bool force)
    {
        if (progress is null)
        {
            return;
        }

        using var callback = EnterCallback();
        lock (progressGate)
        {
            long now = Stopwatch.GetTimestamp();
            if (!force && Stopwatch.GetElapsedTime(lastReport, now) < TimeSpan.FromMilliseconds(50))
            {
                return;
            }

            lastReport = now;
            progress.Report(new(phase, entry, Interlocked.Read(ref bytes), totalBytes, Volatile.Read(ref entries)));
        }
    }

    internal readonly struct CallbackScope : IDisposable
    {
        private readonly Archive? previous;
        internal CallbackScope(Archive? owner)
        {
            previous = callbackOwner;
            callbackOwner = owner;
        }
        public void Dispose()
        {
            callbackOwner = previous;
        }
    }
}
