using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using SevenZipSharp.Interop;
using SevenZipSharp.Native.Streams;
using SevenZipSharp.Operations;

namespace SevenZipSharp.Native.Callbacks;

[GeneratedComClass]
internal sealed unsafe partial class UpdateCallback(
    IReadOnlyList<WriteItem> items, OperationContext context, string? newPassword, string? sourcePassword,
    Stream destination, Stream? archiveSource)
    : IArchiveUpdateCallback, ICryptoGetTextPassword2, ICryptoGetTextPassword, IArchiveExtractCallbackMessage2, IDisposable
{
    private readonly List<NativeSourceStream> sources = [];
    private readonly HashSet<Stream> issued = new(ReferenceEqualityComparer.Instance);

    public int SetTotal(ulong total) => Check();
    public int SetCompleted(ulong* completed) => Check();

    private int Check()
    {
        using var callback = context.EnterCallback();
        try { context.Check(); return 0; }
        catch (Exception error) { return context.Fail(error); }
    }

    public int GetUpdateItemInfo(uint index, int* newData, int* newProperties, uint* archiveIndex)
    {
        using var callback = context.EnterCallback();
        try
        {
            context.Check();
            var item = items[checked((int)index)];
            *newData = item.NewData ? 1 : 0;
            *newProperties = item.NewProperties ? 1 : 0;
            *archiveIndex = item.ArchiveIndex;
            return 0;
        }
        catch (Exception error) { return context.Fail(error); }
    }

    public int GetProperty(uint index, uint id, NativeVariant* value)
    {
        *value = default;
        using var callback = context.EnterCallback();
        try
        {
            context.Check();
            var item = items[checked((int)index)];
            *value = (NativePropertyId)id switch
            {
                NativePropertyId.Path => NativeVariant.FromString(item.FullName.TrimEnd('/')),
                NativePropertyId.IsDirectory => NativeVariant.FromBoolean(item.IsDirectory),
                NativePropertyId.Size => NativeVariant.FromUInt64((ulong)item.Length),
                NativePropertyId.Attributes when item.Options.Attributes is { } attributes => NativeVariant.FromUInt32((uint)attributes),
                NativePropertyId.ModifiedTime when item.Options.LastWriteTime is { } time => NativeVariant.FromTimestamp(time),
                _ => default
            };
            return 0;
        }
        catch (Exception error) { return context.Fail(error); }
    }

    public int GetStream(uint index, nint* result)
    {
        *result = 0;
        using var callback = context.EnterCallback();
        Stream? source = null;
        try
        {
            context.Check();
            var item = items[checked((int)index)];
            if (item.IsDirectory) return 0;
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
            if (!source.CanRead) throw new ArgumentException("An entry source is not readable.");
            if (source.CanSeek && source.Length - source.Position != item.Length)
                throw new InvalidDataException("An entry source no longer has its declared length.");
            var adapter = new NativeSourceStream(source, item.Length, context);
            sources.Add(adapter);
            source = null;
            adapter.ValidateEmpty();
            *result = (nint)ComInterfaceMarshaller<ISequentialInStream>.ConvertToUnmanaged(adapter);
            return 0;
        }
        catch (Exception error)
        {
            context.Fail(error);
            try { source?.Dispose(); } catch (Exception cleanup) { context.Fail(cleanup); }
            return NativeMethods.Fail;
        }
    }

    public int SetOperationResult(int result)
    {
        using var callback = context.EnterCallback();
        try
        {
            context.Result(result);
            if (result == 0) context.EntryCompleted(null);
            context.Check();
            return context.HasFailure ? NativeMethods.Fail : 0;
        }
        catch (Exception error) { return context.Fail(error); }
    }

    public int ReportExtractResult(uint indexType, uint index, int result)
    {
        using var callback = context.EnterCallback();
        try { context.Result(result); context.Check(); return context.HasFailure ? NativeMethods.Fail : 0; }
        catch (Exception error) { return context.Fail(error); }
    }

    public int CryptoGetTextPassword2(int* defined, nint* password)
    {
        *defined = 0; *password = 0;
        using var callback = context.EnterCallback();
        try
        {
            context.Check();
            *defined = newPassword is null ? 0 : 1;
            if (newPassword is not null) *password = Marshal.StringToBSTR(newPassword);
            return 0;
        }
        catch (Exception error) { return context.Fail(error); }
    }

    public int CryptoGetTextPassword(nint* password)
    {
        *password = 0;
        using var callback = context.EnterCallback();
        try
        {
            context.Check();
            if (sourcePassword is null) throw new ArchiveException(ArchiveErrorCode.PasswordRequired, "Repacking this archive requires its source password.");
            *password = Marshal.StringToBSTR(sourcePassword);
            return 0;
        }
        catch (Exception error) { return context.Fail(error); }
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
