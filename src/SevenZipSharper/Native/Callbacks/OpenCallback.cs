using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using SevenZip.Interop.Archives;
using SevenZip.Interop.Cryptography;
using SevenZip.Interop.Streams;
using SevenZipSharper.Native.Streams;
using SevenZipSharper.Operations;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com.StructuredStorage;

namespace SevenZipSharper.Native.Callbacks;

[GeneratedComClass]
internal sealed unsafe partial class OpenCallback(ArchiveOpenOptions options, OperationContext context)
    : IArchiveOpenCallback, ICryptoGetTextPassword, IArchiveOpenVolumeCallback, IDisposable
{
    private readonly List<(Stream Stream, NativeInputStream Adapter)> volumes = [];
    private readonly HashSet<Stream> issuedVolumes = new(ReferenceEqualityComparer.Instance);

    internal OperationContext? Context { get; private set; } = context;

    internal void Attach(OperationContext? operation)
    {
        Context = operation;
        foreach (var volume in volumes)
        {
            volume.Adapter.Context = operation;
        }
    }

    public HRESULT SetTotal(in ulong files, in ulong bytes)
    {
        return Check();
    }

    public HRESULT SetCompleted(in ulong files, in ulong bytes)
    {
        return Check();
    }

    private HRESULT Check()
    {
        using var callback = Context?.EnterCallback();
        try
        {
            Context?.Check();

            return HRESULT.S_OK;
        }
        catch (Exception error)
        {
            return Context?.Fail(error) ?? HRESULT.E_FAIL;
        }
    }

    public HRESULT CryptoGetTextPassword(BSTR* password)
    {
        *password = default;
        using var callback = Context?.EnterCallback();
        try
        {
            Context?.Check();
            if (options.Password is null)
            {
                throw new ArchiveException(ArchiveErrorCode.PasswordRequired, "This archive requires a password.");
            }

            *password = (BSTR)Marshal.StringToBSTR(options.Password);

            return HRESULT.S_OK;
        }
        catch (Exception error)
        {
            return Context?.Fail(error) ?? HRESULT.E_FAIL;
        }
    }

    public HRESULT GetProperty(uint id, ref PROPVARIANT value)
    {
        value = default;
        using var callback = Context?.EnterCallback();
        try
        {
            Context?.Check();
            // The TAR handler accepts an empty archive only with a .tar name.
            // An explicit format hint provides that evidence for unnamed streams.
            string? name = options.FormatHint == ArchiveFormat.Tar ? "archive.tar" : options.SourceName;
            if (id == (uint)NativePropertyId.Name && name is not null)
            {
                value = InteropHelpers.CreateString(name);
            }

            return HRESULT.S_OK;
        }
        catch (Exception error)
        {
            return Context?.Fail(error) ?? HRESULT.E_FAIL;
        }
    }

    public HRESULT GetStream(PCWSTR name, out IInStream inStream)
    {
        inStream = null!;
        using var callback = Context?.EnterCallback();
        Stream? stream = null;
        try
        {
            Context?.Check();
            string volumeName = new((char*)name);
            stream = options.VolumeResolver?.Invoke(volumeName)
                ?? throw new ArchiveException(ArchiveErrorCode.MissingVolume, $"Required archive volume '{volumeName}' is unavailable.");
            if (!issuedVolumes.Add(stream))
            {
                stream = null;
                throw new ArgumentException("A volume resolver must return a fresh stream for each request.");
            }
            if (!stream.CanRead || !stream.CanSeek)
            {
                throw new ArgumentException("Archive volumes must be readable and seekable.");
            }

            var adapter = new NativeInputStream(stream, stream.Position, checked(stream.Length - stream.Position), Context);
            volumes.Add((stream, adapter));
            stream = null;
            inStream = adapter;

            return HRESULT.S_OK;
        }
        catch (Exception error)
        {
            Context?.Fail(error);
            try { stream?.Dispose(); } catch (Exception cleanup) { Context?.Fail(cleanup); }

            return Context?.Fail(error) ?? HRESULT.E_FAIL;
        }
    }

    public void Dispose()
    {
        foreach (var volume in volumes)
        {
            try { volume.Stream.Dispose(); }
            catch (Exception error) { Context?.Fail(error); }
        }
        volumes.Clear();
    }
}
