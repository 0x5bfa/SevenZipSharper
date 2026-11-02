using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using SevenZipSharp.Interop;
using SevenZipSharp.Native.Streams;
using SevenZipSharp.Operations;

namespace SevenZipSharp.Native.Callbacks;

internal interface IExtractionSink
{
    Stream? Open(ArchiveEntry entry);
    void Complete(ArchiveEntry entry, bool success);
    void Finish() { }
}

[GeneratedComClass]
internal sealed unsafe partial class ExtractCallback(
    IReadOnlyList<ArchiveEntry> entries, HashSet<int> selected, IExtractionSink sink,
    OperationContext context, string? password, bool test)
    : IArchiveExtractCallback, IArchiveExtractCallbackMessage2, ICryptoGetTextPassword, IDisposable
{
    private ArchiveEntry? current;
    private readonly List<NativeOutputStream> adapters = [];

    public int SetTotal(ulong total) => Check();
    public int SetCompleted(ulong* completed) => Check();
    public int PrepareOperation(int askMode) => Check();

    private int Check()
    {
        using var callback = context.EnterCallback();
        try { context.Check(); return 0; }
        catch (Exception error) { return context.Fail(error); }
    }

    public int GetStream(uint index, nint* result, int askMode)
    {
        *result = 0;
        using var callback = context.EnterCallback();
        try
        {
            context.Check();
            current = index < entries.Count ? entries[(int)index] : throw new InvalidDataException("The engine requested an invalid entry.");
            if (test || askMode != 0 || !selected.Contains(current.Index) || current.EntryType == ArchiveEntryType.Directory)
                return 0;
            Stream? destination = sink.Open(current);
            if (destination is null) return 0;
            if (!destination.CanWrite) throw new ArgumentException("The entry destination is not writable.");
            var adapter = new NativeOutputStream(destination, context, current);
            adapters.Add(adapter);
            *result = (nint)ComInterfaceMarshaller<ISequentialOutStream>.ConvertToUnmanaged(adapter);
            return 0;
        }
        catch (Exception error) { return context.Fail(error); }
    }

    public int SetOperationResult(int result)
    {
        using var callback = context.EnterCallback();
        try
        {
            context.Result(result, current);
            if (current is not null && selected.Contains(current.Index))
            {
                sink.Complete(current, result == 0 && !context.HasFailure);
                if (result == 0) context.EntryCompleted(current);
            }
            context.Check();
            return context.HasFailure ? NativeMethods.Fail : 0;
        }
        catch (Exception error) { return context.Fail(error); }
    }

    public int ReportExtractResult(uint indexType, uint index, int result)
    {
        using var callback = context.EnterCallback();
        try
        {
            context.Result(result, indexType == 1 && index < entries.Count ? entries[(int)index] : null);
            context.Check();
            return context.HasFailure ? NativeMethods.Fail : 0;
        }
        catch (Exception error) { return context.Fail(error); }
    }

    public int CryptoGetTextPassword(nint* result)
    {
        *result = 0;
        using var callback = context.EnterCallback();
        try
        {
            context.Check();
            if (password is null) throw new ArchiveException(ArchiveErrorCode.PasswordRequired, "This entry requires a password.");
            *result = Marshal.StringToBSTR(password);
            return 0;
        }
        catch (Exception error) { return context.Fail(error); }
    }

    public void Dispose() => GC.KeepAlive(adapters);
}
