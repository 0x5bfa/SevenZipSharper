using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using SevenZipSharp.Interop;
using SevenZipSharp.Native;
using SevenZipSharp.Native.Callbacks;
using SevenZipSharp.Native.Streams;

namespace SevenZipSharp.Operations;

internal static unsafe class WriteOperation
{
    internal static void Validate(Stream destination, IReadOnlyList<WriteItem> items, WriteConfiguration options)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite || !destination.CanSeek)
            throw new ArgumentException("Archive writing currently requires a writable, seekable destination.", nameof(destination));
        if (options.Format is ArchiveFormat.GZip or ArchiveFormat.BZip2 or ArchiveFormat.Xz &&
            (items.Count != 1 || items[0].IsDirectory))
            throw new ArgumentException("A single-payload compression format requires exactly one regular file.");
    }

    internal static void Write(IReadOnlyList<WriteItem> items, Stream destination, WriteConfiguration options,
        OperationContext context, NativeArchiveSession? session = null, string? sourcePassword = null, Stream? archiveSource = null)
    {
        Validate(destination, items, options);
        context.Check();
        using var created = session is null ? NativeRuntime.Instance.Create(NativeRuntime.Instance.Writable(options.Format), NativeMethods.OutArchiveId) : null;
        nint writer = session is null ? created!.DangerousGetHandle() : session.QueryWriter();
        if (writer == 0) throw new NotSupportedException("The archive handler does not support editing.");
        var callback = new UpdateCallback(items, context, options.Password, sourcePassword ?? options.Password, destination, archiveSource);
        var output = new NativeOutputStream(destination, context);
        void* outputPointer = null;
        void* callbackPointer = null;
        int result = NativeMethods.Fail;
        try
        {
            SetProperties(writer, options);
            session?.Attach(context);
            outputPointer = ComInterfaceMarshaller<ISequentialOutStream>.ConvertToUnmanaged(output);
            callbackPointer = ComInterfaceMarshaller<IArchiveUpdateCallback>.ConvertToUnmanaged(callback);
            result = ((delegate* unmanaged[Stdcall]<nint, void*, uint, void*, int>)(*(nint**)writer)[3])
                (writer, outputPointer, checked((uint)items.Count), callbackPointer);
        }
        catch (Exception error) { context.Fail(error); }
        finally
        {
            ComInterfaceMarshaller<IArchiveUpdateCallback>.Free(callbackPointer);
            ComInterfaceMarshaller<ISequentialOutStream>.Free(outputPointer);
            callback.Dispose();
            session?.Attach(null);
            if (session is not null) Marshal.Release(writer);
            GC.KeepAlive(output);
        }
        context.Complete(result);
        context.Report(null, true);
    }

    private static void SetProperties(nint writer, WriteConfiguration configuration)
    {
        if (configuration.Properties.Count == 0) return;
        NativeMethods.Check(Marshal.QueryInterface(writer, in NativeMethods.SetPropertiesId, out nint setter));
        nint[] names = new nint[configuration.Properties.Count];
        var values = new NativeVariant[names.Length];
        try
        {
            for (int i = 0; i < names.Length; i++)
            {
                var property = configuration.Properties[i];
                names[i] = Marshal.StringToCoTaskMemUni(property.Name);
                values[i] = property.Value switch
                {
                    string text => NativeVariant.FromString(text),
                    bool flag => NativeVariant.FromBoolean(flag),
                    uint number => NativeVariant.FromUInt32(number),
                    _ => throw new InvalidOperationException("Unsupported native property value.")
                };
            }
            fixed (nint* nativeNames = names)
            fixed (NativeVariant* nativeValues = values)
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, NativeVariant*, uint, int>)(*(nint**)setter)[3])
                    (setter, nativeNames, nativeValues, (uint)names.Length));
        }
        finally
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] != 0) Marshal.FreeCoTaskMem(names[i]);
                values[i].Clear();
            }
            Marshal.Release(setter);
        }
    }
}
