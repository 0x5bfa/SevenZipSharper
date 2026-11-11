using System.Runtime.InteropServices.Marshalling;
using SevenZip.Interop.Streams;
using SevenZipSharper.Interop;
using SevenZipSharper.Operations;
using Windows.Win32.Foundation;

namespace SevenZipSharper.Native.Streams;

// Content factories expose sequential data. Advertising IInStream for a
// non-seekable source would mislead handlers that probe it with QueryInterface.
[GeneratedComClass]
internal sealed unsafe partial class NativeSourceStream(Stream source, long length, OperationContext context)
    : ISequentialInStream, IDisposable
{
    private long consumed;
    private bool disposed;

    internal void ValidateEmpty()
    {
        if (length == 0)
        {
            VerifyEnd();
        }
    }

    public HRESULT Read(void* data, uint size, ref uint processed)
    {
        processed = 0;
        using var callback = context.EnterCallback();
        try
        {
            context.Check();
            if (size == 0)
            {
                return HRESULT.S_OK;
            }

            if (consumed == length)
            {
                VerifyEnd();

                return HRESULT.S_OK;
            }
            int count = (int)Math.Min(Math.Min(size, int.MaxValue), length - consumed);
            int read = source.Read(new Span<byte>(data, count));
            if (read == 0)
            {
                throw new EndOfStreamException("An entry source ended before its declared length.");
            }

            consumed += read;
            processed = (uint)read;
            if (consumed == length)
            {
                VerifyEnd();
            }

            return HRESULT.S_OK;
        }
        catch (Exception error)
        {
            return context.Fail(error);
        }
    }

    private void VerifyEnd()
    {
        if (disposed)
        {
            return;
        }

        if (source.ReadByte() != -1)
        {
            throw new InvalidDataException("An entry source exceeds its declared length.");
        }

        Dispose();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        source.Dispose();
    }
}
