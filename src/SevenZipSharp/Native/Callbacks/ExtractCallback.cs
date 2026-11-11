using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using SevenZip.Interop.Archives;
using SevenZip.Interop.Cryptography;
using SevenZip.Interop.Progress;
using SevenZip.Interop.Streams;
using SevenZipSharp.Interop;
using SevenZipSharp.Native.Streams;
using SevenZipSharp.Operations;
using Windows.Win32.Foundation;

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

    public HRESULT SetTotal(ulong total)
    {
        return Check();
    }

    public HRESULT SetCompleted(in ulong completed)
    {
        return Check();
    }

    public HRESULT PrepareOperation(int askMode)
    {
        return Check();
    }

    private HRESULT Check()
    {
        using var callback = context.EnterCallback();
        try
        {
            context.Check();

            return HRESULT.S_OK;
        }
        catch (Exception error)
        {
            return (HRESULT)context.Fail(error);
        }
    }

    public HRESULT GetStream(uint index, out ISequentialOutStream outStream, int askMode)
    {
        outStream = null!;
        using var callback = context.EnterCallback();
        try
        {
            context.Check();
            current = index < entries.Count ? entries[(int)index] : throw new InvalidDataException("The engine requested an invalid entry.");
            if (test || askMode != 0 || !selected.Contains(current.Index) || current.EntryType == ArchiveEntryType.Directory)
            {
                return HRESULT.S_OK;
            }

            Stream? destination = sink.Open(current);
            if (destination is null)
            {
                return HRESULT.S_OK;
            }

            if (!destination.CanWrite)
            {
                throw new ArgumentException("The entry destination is not writable.");
            }

            var adapter = new NativeOutputStream(destination, context, current);
            adapters.Add(adapter);
            outStream = adapter;

            return HRESULT.S_OK;
        }
        catch (Exception error)
        {
            return context.Fail(error);
        }
    }

    public HRESULT SetOperationResult(int result)
    {
        using var callback = context.EnterCallback();
        try
        {
            context.Result(result, current);
            if (current is not null && selected.Contains(current.Index))
            {
                sink.Complete(current, result == 0 && !context.HasFailure);
                if (result == 0)
                {
                    context.EntryCompleted(current);
                }
            }
            context.Check();

            return context.HasFailure ? HRESULT.E_FAIL : HRESULT.S_OK;
        }
        catch (Exception error)
        {
            return context.Fail(error);
        }
    }

    public HRESULT ReportExtractResult(uint indexType, uint index, int result)
    {
        using var callback = context.EnterCallback();
        try
        {
            context.Result(result, indexType == 1 && index < entries.Count ? entries[(int)index] : null);
            context.Check();

            return context.HasFailure ? HRESULT.E_FAIL : HRESULT.S_OK;
        }
        catch (Exception error)
        {
            return context.Fail(error);
        }
    }

    public HRESULT CryptoGetTextPassword(BSTR* result)
    {
        *result = default;
        using var callback = context.EnterCallback();
        try
        {
            context.Check();
            if (password is null)
            {
                throw new ArchiveException(ArchiveErrorCode.PasswordRequired, "This entry requires a password.");
            }

            *result = (BSTR)Marshal.StringToBSTR(password);

            return HRESULT.S_OK;
        }
        catch (Exception error)
        {
            return context.Fail(error);
        }
    }

    public void Dispose()
    {
        GC.KeepAlive(adapters);
    }
}
