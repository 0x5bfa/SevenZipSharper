using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using SevenZip.Interop.Runtime;
using SevenZipSharp.Interop;
using SevenZipSharp.Native.Callbacks;
using SevenZipSharp.Native.Streams;
using SevenZipSharp.Operations;

namespace SevenZipSharp.Native;

internal sealed unsafe class NativeArchiveSession : IDisposable
{
    private readonly SevenZipObject instance;
    private readonly NativeInputStream input;
    private readonly OpenCallback openCallback;
    private nint inputPointer;
    private nint openPointer;
    private bool disposed;
    internal NativeFormat Format { get; }
    internal int ExtractionInvocations { get; private set; }
    internal nint Handle => instance.DangerousGetHandle();
    private nint* Vtable => *(nint**)Handle;

    private NativeArchiveSession(SevenZipObject instance, NativeFormat format, NativeInputStream input,
        OpenCallback openCallback, nint inputPointer, nint openPointer)
    {
        this.instance = instance;
        Format = format;
        this.input = input;
        this.openCallback = openCallback;
        this.inputPointer = inputPointer;
        this.openPointer = openPointer;
    }

    internal static NativeArchiveSession Open(Stream source, ArchiveOpenOptions options, OperationContext context)
    {
        long origin = source.Position;
        long length = checked(source.Length - origin);
        if (length < 0) throw new ArgumentException("The source position is beyond its end.", nameof(source));
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
            if (instance is null) continue;
            var input = new NativeInputStream(source, origin, length, context);
            var callback = new OpenCallback(options, context);
            nint inputPointer = 0, callbackPointer = 0;
            NativeArchiveSession? session = null;
            bool transferred = false;
            try
            {
                inputPointer = (nint)ComInterfaceMarshaller<IInStream>.ConvertToUnmanaged(input);
                callbackPointer = (nint)ComInterfaceMarshaller<IArchiveOpenCallback>.ConvertToUnmanaged(callback);
                session = new(instance, format, input, callback, inputPointer, callbackPointer);
                ulong search = (ulong)options.MaximumSearchBytes;
                int result = ((delegate* unmanaged[Stdcall]<nint, nint, ulong*, nint, int>)session.Vtable[3])
                    (session.Handle, inputPointer, &search, callbackPointer);
                if (context.HasFailure) context.Complete(result);
                if (result == 0)
                {
                    session.Attach(null);
                    transferred = true;
                    return session;
                }
                if (result < 0) NativeMethods.Check(result);
            }
            finally
            {
                if (session is not null && !transferred) session.Dispose();
                else if (session is null)
                {
                    if (callbackPointer != 0) ComInterfaceMarshaller<IArchiveOpenCallback>.Free((void*)callbackPointer);
                    if (inputPointer != 0) ComInterfaceMarshaller<IInStream>.Free((void*)inputPointer);
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
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Vtable[5])(Handle, &count));
            return count;
        }
    }

    internal NativeVariant Property(uint index, NativePropertyId id)
    {
        NativeVariant value = default;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint, uint, NativeVariant*, int>)Vtable[6])(Handle, index, (uint)id, &value));
            return value;
        }
        catch { value.Clear(); throw; }
    }

    internal NativeVariant ArchiveProperty(NativePropertyId id)
    {
        NativeVariant value = default;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint, NativeVariant*, int>)Vtable[8])(Handle, (uint)id, &value));
            return value;
        }
        catch { value.Clear(); throw; }
    }

    internal nint QueryWriter()
    {
        int result = Marshal.QueryInterface(Handle, in NativeMethods.OutArchiveId, out nint writer);
        if (result == unchecked((int)0x80004002)) return 0;
        NativeMethods.Check(result);
        return writer;
    }

    internal int Extract(uint[] indexes, bool test, ExtractCallback callback, OperationContext context)
    {
        ExtractionInvocations++;
        Attach(context);
        void* callbackPointer = null;
        try
        {
            callbackPointer = ComInterfaceMarshaller<IArchiveExtractCallback>.ConvertToUnmanaged(callback);
            fixed (uint* selected = indexes)
                return ((delegate* unmanaged[Stdcall]<nint, uint*, uint, int, void*, int>)Vtable[7])
                    (Handle, selected, (uint)indexes.Length, test ? 1 : 0, callbackPointer);
        }
        finally
        {
            ComInterfaceMarshaller<IArchiveExtractCallback>.Free(callbackPointer);
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
        if (disposed) return;
        disposed = true;
        try { _ = ((delegate* unmanaged[Stdcall]<nint, int>)Vtable[4])(Handle); }
        finally
        {
            instance.Dispose();
            ComInterfaceMarshaller<IArchiveOpenCallback>.Free((void*)openPointer);
            ComInterfaceMarshaller<IInStream>.Free((void*)inputPointer);
            openPointer = inputPointer = 0;
            openCallback.Dispose();
        }
    }
}
