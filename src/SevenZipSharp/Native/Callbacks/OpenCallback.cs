using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using SevenZipSharp.Interop;
using SevenZipSharp.Native.Streams;
using SevenZipSharp.Operations;

namespace SevenZipSharp.Native.Callbacks;

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
        foreach (var volume in volumes) volume.Adapter.Context = operation;
    }

    public int SetTotal(ulong* files, ulong* bytes) => Check();
    public int SetCompleted(ulong* files, ulong* bytes) => Check();

    private int Check()
    {
        using var callback = Context?.EnterCallback();
        try { Context?.Check(); return 0; }
        catch (Exception error) { return Context?.Fail(error) ?? NativeMethods.Fail; }
    }

    public int CryptoGetTextPassword(nint* password)
    {
        *password = 0;
        using var callback = Context?.EnterCallback();
        try
        {
            Context?.Check();
            if (options.Password is null)
                throw new ArchiveException(ArchiveErrorCode.PasswordRequired, "This archive requires a password.");
            *password = Marshal.StringToBSTR(options.Password);
            return 0;
        }
        catch (Exception error) { return Context?.Fail(error) ?? NativeMethods.Fail; }
    }

    public int GetProperty(uint id, NativeVariant* value)
    {
        *value = default;
        using var callback = Context?.EnterCallback();
        try
        {
            Context?.Check();
            // The TAR handler accepts an empty archive only with a .tar name.
            // An explicit format hint provides that evidence for unnamed streams.
            string? name = options.FormatHint == ArchiveFormat.Tar ? "archive.tar" : options.SourceName;
            if (id == (uint)NativePropertyId.Name && name is not null) *value = NativeVariant.FromString(name);
            return 0;
        }
        catch (Exception error) { return Context?.Fail(error) ?? NativeMethods.Fail; }
    }

    public int GetStream(char* name, nint* result)
    {
        *result = 0;
        using var callback = Context?.EnterCallback();
        Stream? stream = null;
        try
        {
            Context?.Check();
            string volumeName = new(name);
            stream = options.VolumeResolver?.Invoke(volumeName)
                ?? throw new ArchiveException(ArchiveErrorCode.MissingVolume, $"Required archive volume '{volumeName}' is unavailable.");
            if (!issuedVolumes.Add(stream))
            {
                stream = null;
                throw new ArgumentException("A volume resolver must return a fresh stream for each request.");
            }
            if (!stream.CanRead || !stream.CanSeek)
                throw new ArgumentException("Archive volumes must be readable and seekable.");
            var adapter = new NativeInputStream(stream, stream.Position, checked(stream.Length - stream.Position), Context);
            volumes.Add((stream, adapter));
            stream = null;
            *result = (nint)ComInterfaceMarshaller<IInStream>.ConvertToUnmanaged(adapter);
            return 0;
        }
        catch (Exception error)
        {
            Context?.Fail(error);
            try { stream?.Dispose(); } catch (Exception cleanup) { Context?.Fail(cleanup); }
            return Context?.Fail(error) ?? NativeMethods.Fail;
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
