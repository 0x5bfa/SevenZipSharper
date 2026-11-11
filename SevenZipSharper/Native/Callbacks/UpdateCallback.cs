using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using SevenZip.Interop.Archives;
using SevenZip.Interop.Cryptography;
using SevenZip.Interop.Progress;
using SevenZip.Interop.Streams;
using SevenZipSharper.Native.Streams;
using SevenZipSharper.Operations;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com.StructuredStorage;

namespace SevenZipSharper.Native.Callbacks;

[GeneratedComClass]
internal sealed unsafe partial class UpdateCallback(
    IReadOnlyList<WriteItem> items, OperationContext context, string? newPassword, string? sourcePassword,
    Stream destination, Stream? archiveSource)
    : IArchiveUpdateCallback, ICryptoGetTextPassword2, ICryptoGetTextPassword, IArchiveExtractCallbackMessage2, IDisposable
{
    private readonly List<NativeSourceStream> sources = [];
    private readonly HashSet<Stream> issued = new(ReferenceEqualityComparer.Instance);

    public HRESULT SetTotal(ulong total)
    {
        return Check();
    }

    public HRESULT SetCompleted(in ulong completed)
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
            return context.Fail(error);
        }
    }

    public HRESULT GetUpdateItemInfo(uint index, ref int newData, ref int newProperties, ref uint archiveIndex)
    {
        using var callback = context.EnterCallback();
        try
        {
            context.Check();
            var item = items[checked((int)index)];
            newData = item.NewData ? 1 : 0;
            newProperties = item.NewProperties ? 1 : 0;
            archiveIndex = item.ArchiveIndex;

            return HRESULT.S_OK;
        }
        catch (Exception error)
        {
            return context.Fail(error);
        }
    }

    public HRESULT GetProperty(uint index, uint id, ref PROPVARIANT value)
    {
        value = default;
        using var callback = context.EnterCallback();
        try
        {
            context.Check();
            var item = items[checked((int)index)];
            value = ((NativePropertyId)id switch
            {
                NativePropertyId.Path => InteropHelpers.CreateString(item.FullName.TrimEnd('/')),
                NativePropertyId.IsDirectory => InteropHelpers.CreateBoolean(item.IsDirectory),
                NativePropertyId.Size => InteropHelpers.CreateUInt64((ulong)item.Length),
                NativePropertyId.Attributes when item.Options.Attributes is { } attributes => InteropHelpers.CreateUInt32((uint)attributes),
                NativePropertyId.ModifiedTime when item.Options.LastWriteTime is { } time => InteropHelpers.CreateTimestamp(time),
                _ => default
            });

            return HRESULT.S_OK;
        }
        catch (Exception error)
        {
            return context.Fail(error);
        }
    }

    public HRESULT GetStream(uint index, out ISequentialInStream inStream)
    {
        inStream = null!;
        using var callback = context.EnterCallback();
        Stream? source = null;
        try
        {
            context.Check();
            var item = items[checked((int)index)];
            if (item.IsDirectory)
            {
                return HRESULT.S_OK;
            }

            source = item.OpenRead?.Invoke() ?? throw new InvalidOperationException("An entry source factory returned null.");
            if (ReferenceEquals(source, destination) || ReferenceEquals(source, archiveSource))
            {
                source = null;
                throw new ArgumentException("An entry source must not alias an archive stream.");
            }
            if (!issued.Add(source))
            {
                source = null;
                throw new ArgumentException("An entry factory must return a fresh stream.");
            }
            if (!source.CanRead)
            {
                throw new ArgumentException("An entry source is not readable.");
            }

            if (source.CanSeek && source.Length - source.Position != item.Length)
            {
                throw new InvalidDataException("An entry source no longer has its declared length.");
            }

            var adapter = new NativeSourceStream(source, item.Length, context);
            sources.Add(adapter);
            source = null;
            adapter.ValidateEmpty();
            inStream = adapter;

            return HRESULT.S_OK;
        }
        catch (Exception error)
        {
            HRESULT result = context.Fail(error);
            try { source?.Dispose(); } catch (Exception cleanup) { context.Fail(cleanup); }

            return result;
        }
    }

    public HRESULT SetOperationResult(int result)
    {
        using var callback = context.EnterCallback();
        try
        {
            context.Result(result);
            if (result == 0)
            {
                context.EntryCompleted(null);
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
            context.Result(result);
            context.Check();

            return context.HasFailure ? HRESULT.E_FAIL : HRESULT.S_OK;
        }
        catch (Exception error)
        {
            return context.Fail(error);
        }
    }

    public HRESULT CryptoGetTextPassword2(ref int defined, BSTR* password)
    {
        defined = 0;
        *password = default;
        using var callback = context.EnterCallback();
        try
        {
            context.Check();
            defined = newPassword is null ? 0 : 1;
            if (newPassword is not null)
            {
                *password = (BSTR)Marshal.StringToBSTR(newPassword);
            }

            return HRESULT.S_OK;
        }
        catch (Exception error)
        {
            return context.Fail(error);
        }
    }

    public HRESULT CryptoGetTextPassword(BSTR* password)
    {
        *password = default;
        using var callback = context.EnterCallback();
        try
        {
            context.Check();
            if (sourcePassword is null)
            {
                throw new ArchiveException(ArchiveErrorCode.PasswordRequired, "Repacking this archive requires its source password.");
            }

            *password = (BSTR)Marshal.StringToBSTR(sourcePassword);

            return HRESULT.S_OK;
        }
        catch (Exception error)
        {
            return context.Fail(error);
        }
    }

    public void Dispose()
    {
        foreach (var source in sources)
        {
            try { source.Dispose(); } catch (Exception error) { context.Fail(error); }
        }
        sources.Clear();
    }
}
