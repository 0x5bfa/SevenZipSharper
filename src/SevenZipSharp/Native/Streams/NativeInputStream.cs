using System.Runtime.InteropServices.Marshalling;
using SevenZipSharp.Interop;
using SevenZipSharp.Operations;

namespace SevenZipSharp.Native.Streams;

[GeneratedComClass]
internal sealed unsafe partial class NativeInputStream(
    Stream stream, long origin, long length, OperationContext? context) : IInStream
{
    internal OperationContext? Context { get; set; } = context;

    public int Read(void* data, uint size, uint* processed)
    {
        if (processed != null) *processed = 0;
        using var callback = Context?.EnterCallback();
        try
        {
            Context?.Check();
            long remaining = Math.Max(0, length - (stream.Position - origin));
            int count = (int)Math.Min(Math.Min(size, int.MaxValue), remaining);
            int read = stream.Read(new Span<byte>(data, count));
            if (processed != null) *processed = (uint)read;
            return 0;
        }
        catch (Exception error) { return Context?.Fail(error) ?? NativeMethods.Fail; }
    }

    public int Seek(long offset, uint seekOrigin, ulong* position)
    {
        using var callback = Context?.EnterCallback();
        try
        {
            Context?.Check();
            long target = seekOrigin switch
            {
                0 => offset,
                1 => checked(stream.Position - origin + offset),
                2 => checked(length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(seekOrigin))
            };
            if (target < 0) throw new IOException("Cannot seek before the beginning of an archive.");
            long actual = stream.Seek(checked(origin + target), SeekOrigin.Begin) - origin;
            if (position != null) *position = checked((ulong)actual);
            return 0;
        }
        catch (Exception error) { return Context?.Fail(error) ?? NativeMethods.Fail; }
    }
}
