using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using NativeOutArchive = SevenZip.Interop.Archives.IOutArchive;
using NativeSetProperties = SevenZip.Interop.Archives.ISetProperties;
using SevenZip.Interop.Runtime;
using SevenZipSharp.Native;
using SevenZipSharp.Native.Callbacks;
using SevenZipSharp.Native.Streams;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com.StructuredStorage;

namespace SevenZipSharp.Operations;

internal static unsafe class WriteOperation
{
    internal static void Validate(Stream destination, IReadOnlyList<WriteItem> items, WriteConfiguration options)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite || !destination.CanSeek)
        {
            throw new ArgumentException("Archive writing currently requires a writable, seekable destination.", nameof(destination));
        }

        if (options.Format is ArchiveFormat.GZip or ArchiveFormat.BZip2 or ArchiveFormat.Xz &&
            (items.Count != 1 || items[0].IsDirectory))
        {
            throw new ArgumentException("A single-payload compression format requires exactly one regular file.");
        }
    }

    internal static void Write(IReadOnlyList<WriteItem> items, Stream destination, WriteConfiguration options,
        OperationContext context, NativeArchiveSession? session = null, string? sourcePassword = null, Stream? archiveSource = null)
    {
        Validate(destination, items, options);
        context.Check();
        NativeOutArchive? writer = session is null
            ? CreateWriter(NativeRuntime.Instance.Writable(options.Format))
            : session.QueryWriter();
        if (writer is null)
        {
            throw new NotSupportedException("The archive handler does not support editing.");
        }

        ComObject? uniqueWriter = session is null ? (ComObject)(object)writer : null;
        var callback = new UpdateCallback(items, context, options.Password, sourcePassword ?? options.Password, destination, archiveSource);
        var output = new NativeOutputStream(destination, context);
        HRESULT result = HRESULT.E_FAIL;
        try
        {
            SetProperties(writer, options);
            session?.Attach(context);
            HRESULT updateResult = writer.UpdateItems(output, checked((uint)items.Count), callback);
            result = updateResult;
        }
        catch (Exception error)
        {
            context.Fail(error);
        }
        finally
        {
            callback.Dispose();
            session?.Attach(null);
            uniqueWriter?.FinalRelease();
        }

        context.Complete(result);
        context.Report(null, true);
    }

    private static NativeOutArchive CreateWriter(NativeFormat format)
    {
        using var instance = NativeRuntime.Instance.Create(format, typeof(NativeOutArchive).GUID);

        return UniqueComInterfaceMarshaller<NativeOutArchive>.ConvertToManaged((void*)instance.DangerousGetHandle())
            ?? throw new InvalidOperationException("The 7-Zip factory returned a null writer interface.");
    }

    private static void SetProperties(NativeOutArchive writer, WriteConfiguration configuration)
    {
        if (configuration.Properties.Count == 0)
        {
            return;
        }

        nint[] names = new nint[configuration.Properties.Count];
        var values = new PROPVARIANT[names.Length];
        NativeSetProperties? setter = writer as NativeSetProperties;
        if (setter is null)
        {
            throw new NotSupportedException("The archive handler does not support compression properties.");
        }

        try
        {
            for (int i = 0; i < names.Length; i++)
            {
                var property = configuration.Properties[i];
                names[i] = Marshal.StringToCoTaskMemUni(property.Name);
                values[i] = (property.Value switch
                {
                    string text => InteropHelpers.CreateString(text),
                    bool flag => InteropHelpers.CreateBoolean(flag),
                    uint number => InteropHelpers.CreateUInt32(number),
                    _ => throw new InvalidOperationException("Unsupported native property value.")
                });
            }
            fixed (nint* nativeNames = names)
            fixed (PROPVARIANT* nativeValues = values)
            {
                // The WinMD projection does not preserve the count of this PROPVARIANT array.
                setter.SetProperties((ushort**)nativeNames, in *nativeValues, (uint)names.Length).ThrowOnFailure();
            }
        }
        finally
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] != 0)
                {
                    Marshal.FreeCoTaskMem(names[i]);
                }

                InteropHelpers.Clear(ref values[i]);
            }
        }
    }
}
