using System.Runtime.InteropServices.Marshalling;
using SevenZip.Interop.Streams;
using SevenZipSharp.Interop;
using SevenZipSharp.Operations;
using Windows.Win32.Foundation;

namespace SevenZipSharp.Native.Streams;

[GeneratedComClass]
internal sealed unsafe partial class NativeOutputStream(
    Stream stream, OperationContext context, ArchiveEntry? entry = null,
    Action<int>? countBytes = null) : IOutStream
{
    private readonly long origin = stream.CanSeek ? stream.Position : 0;

    public HRESULT Write(void* data, uint size, ref uint processed)
    {
        processed = 0;
        using var callback = context.EnterCallback();
        try
        {
            context.Check();
            int count = checked((int)size);
            countBytes?.Invoke(count);
            stream.Write(new ReadOnlySpan<byte>(data, count));
            processed = size;
            context.AddBytes(count, entry);

            return HRESULT.S_OK;
        }
        catch (Exception error)
        {
            return context.Fail(error);
        }
    }

    public HRESULT Seek(long offset, uint seekOrigin, ref ulong position)
    {
        using var callback = context.EnterCallback();
        try
        {
            context.Check();
            long actual = stream.Seek(seekOrigin == 0 ? checked(origin + offset) : offset, (SeekOrigin)seekOrigin) - origin;
            position = checked((ulong)actual);

            return HRESULT.S_OK;
        }
        catch (Exception error)
        {
            return context.Fail(error);
        }
    }

    public HRESULT SetSize(ulong size)
    {
        using var callback = context.EnterCallback();
        try
        {
            context.Check();
            stream.SetLength(checked(origin + (long)size));

            return HRESULT.S_OK;
        }
        catch (Exception error)
        {
            return context.Fail(error);
        }
    }
}
