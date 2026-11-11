using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using NativeExtractCallback = SevenZip.Interop.Archives.IArchiveExtractCallback;
using NativeInArchive = SevenZip.Interop.Archives.IInArchive;
using NativeOutArchive = SevenZip.Interop.Archives.IOutArchive;
using SevenZipSharper.Interop.Runtime;
using SevenZipSharper.Native.Callbacks;
using SevenZipSharper.Native.Streams;
using SevenZipSharper.Operations;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com.StructuredStorage;

namespace SevenZipSharper.Native;

internal sealed unsafe class NativeArchiveSession : IDisposable
{
    private readonly ComObject archiveObject;
    private readonly NativeInArchive archive;
    private readonly NativeInputStream input;
    private readonly OpenCallback openCallback;
    private bool disposed;

    internal NativeFormat Format { get; }
    internal int ExtractionInvocations { get; private set; }

    private NativeArchiveSession(ComObject archiveObject, NativeInArchive archive, NativeFormat format,
        NativeInputStream input, OpenCallback openCallback)
    {
        this.archiveObject = archiveObject;
        this.archive = archive;
        Format = format;
        this.input = input;
        this.openCallback = openCallback;
    }

    internal static NativeArchiveSession Open(Stream source, ArchiveOpenOptions options, OperationContext context)
    {
        long origin = source.Position;
        long length = checked(source.Length - origin);
        if (length < 0)
        {
            throw new ArgumentException("The source position is beyond its end.", nameof(source));
        }

        var runtime = NativeRuntime.Instance;
        byte[] prefix = new byte[(int)Math.Min(length, 8192)];
        source.ReadExactly(prefix);
        source.Position = origin;
        bool Matches(NativeFormat format) =>
            format.Signature.Length > 0 && format.SignatureOffset >= 0 &&
            format.SignatureOffset <= prefix.Length - format.Signature.Length &&
            prefix.AsSpan((int)format.SignatureOffset, format.Signature.Length).SequenceEqual(format.Signature);
        var candidates = runtime.Formats.Where(f => options.FormatHint is null || f.Format == options.FormatHint)
            .OrderByDescending(Matches).ToArray();
        foreach (var format in candidates)
        {
            context.Check();
            source.Position = origin;
            var instance = runtime.TryCreateReader(format);
            if (instance is null)
            {
                continue;
            }

            var input = new NativeInputStream(source, origin, length, context);
            var callback = new OpenCallback(options, context);
            NativeInArchive? archive = null;
            ComObject? archiveObject = null;
            NativeArchiveSession? session = null;
            bool transferred = false;
            try
            {
                archive = UniqueComInterfaceMarshaller<NativeInArchive>.ConvertToManaged((void*)instance.DangerousGetHandle())
                    ?? throw new InvalidOperationException("The 7-Zip factory returned a null archive interface.");
                archiveObject = (ComObject)(object)archive;
                instance.Dispose();
                session = new(archiveObject, archive, format, input, callback);
                ulong search = (ulong)options.MaximumSearchBytes;
                HRESULT result = archive.Open(input, search, callback);
                if (context.HasFailure)
                {
                    context.Complete(result);
                }

                if (result == HRESULT.S_OK)
                {
                    session.Attach(null);
                    transferred = true;

                    return session;
                }
                if (result.Failed)
                {
                    result.ThrowOnFailure();
                }
            }
            finally
            {
                if (session is not null && !transferred)
                {
                    session.Dispose();
                }
                else if (session is null)
                {
                    archiveObject?.FinalRelease();
                    instance.Dispose();
                    callback.Dispose();
                }
            }
        }

        throw new InvalidDataException("The stream is not a supported archive, or its headers are invalid.");
    }

    internal uint Count
    {
        get
        {
            uint count = 0;
            archive.GetNumberOfItems(ref count).ThrowOnFailure();

            return count;
        }
    }

    internal PROPVARIANT Property(uint index, NativePropertyId id)
    {
        PROPVARIANT value = default;
        try
        {
            archive.GetProperty(index, (uint)id, ref value).ThrowOnFailure();

            return value;
        }
        catch
        {
            InteropHelpers.Clear(ref value);

            throw;
        }
    }

    internal PROPVARIANT ArchiveProperty(NativePropertyId id)
    {
        PROPVARIANT value = default;
        try
        {
            archive.GetArchiveProperty((uint)id, ref value).ThrowOnFailure();

            return value;
        }
        catch
        {
            InteropHelpers.Clear(ref value);

            throw;
        }
    }

    internal NativeOutArchive? QueryWriter()
    {
        return archive as NativeOutArchive;
    }

    internal HRESULT Extract(uint[] indexes, bool test, ExtractCallback callback, OperationContext context)
    {
        ExtractionInvocations++;
        Attach(context);
        void* archivePointer = null;
        void* callbackPointer = null;
        try
        {
            archivePointer = ComInterfaceMarshaller<NativeInArchive>.ConvertToUnmanaged(archive);
            callbackPointer = ComInterfaceMarshaller<NativeExtractCallback>.ConvertToUnmanaged(callback);
            fixed (uint* selected = indexes)
            {
                uint empty = 0;
                uint* indices = selected;
                if (indices == null)
                {
                    indices = &empty;
                }

                // WinMD cannot express this pointer's element count, so pass the full array at its ABI slot.
                return ((delegate* unmanaged[Stdcall]<void*, uint*, uint, int, void*, HRESULT>)(*(void***)archivePointer)[7])(
                    archivePointer, indices, (uint)indexes.Length, test ? 1 : 0, callbackPointer);
            }
        }
        finally
        {
            ComInterfaceMarshaller<NativeExtractCallback>.Free(callbackPointer);
            ComInterfaceMarshaller<NativeInArchive>.Free(archivePointer);
            Attach(null);
        }
    }

    internal void Attach(OperationContext? context)
    {
        input.Context = context;
        openCallback.Attach(context);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try { _ = archive.Close(); }
        catch (COMException) { }
        finally
        {
            openCallback.Dispose();
            archiveObject.FinalRelease();
        }
    }

}
