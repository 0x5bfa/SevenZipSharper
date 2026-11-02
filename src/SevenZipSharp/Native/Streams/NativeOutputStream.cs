using System.Runtime.InteropServices.Marshalling;
using SevenZipSharp.Interop;
using SevenZipSharp.Operations;

namespace SevenZipSharp.Native.Streams;

[GeneratedComClass]
internal sealed unsafe partial class NativeOutputStream(
    Stream stream, OperationContext context, ArchiveEntry? entry = null,
    Action<int>? countBytes = null) : IOutStream
{
    private readonly long origin = stream.CanSeek ? stream.Position : 0;

    public int Write(void* data, uint size, uint* processed)
    {
        if (processed != null) *processed = 0;
        using var callback = context.EnterCallback();
        try
        {
            context.Check();
            int count = checked((int)size);
            countBytes?.Invoke(count);
            stream.Write(new ReadOnlySpan<byte>(data, count));
            if (processed != null) *processed = size;
            context.AddBytes(count, entry);
            return 0;
        }
        catch (Exception error) { return context.Fail(error); }
    }

    public int Seek(long offset, uint seekOrigin, ulong* position)
    {
        using var callback = context.EnterCallback();
        try
        {
            context.Check();
            long actual = stream.Seek(seekOrigin == 0 ? checked(origin + offset) : offset, (SeekOrigin)seekOrigin) - origin;
            if (position != null) *position = checked((ulong)actual);
            return 0;
        }
        catch (Exception error) { return context.Fail(error); }
    }

    public int SetSize(ulong size)
    {
        using var callback = context.EnterCallback();
        try
        {
            context.Check();
            stream.SetLength(checked(origin + (long)size));
            return 0;
        }
        catch (Exception error) { return context.Fail(error); }
    }
}
