using System.Runtime.InteropServices.Marshalling;
using SevenZip.Interop.Streams;
using SevenZipSharp.Interop;
using SevenZipSharp.Operations;
using Windows.Win32.Foundation;

namespace SevenZipSharp.Native.Streams;

[GeneratedComClass]
internal sealed unsafe partial class NativeInputStream(
    Stream stream, long origin, long length, OperationContext? context) : IInStream
{
    internal OperationContext? Context { get; set; } = context;

    public HRESULT Read(void* data, uint size, ref uint processed)
    {
        processed = 0;
        using var callback = Context?.EnterCallback();
        try
        {
            Context?.Check();
            long remaining = Math.Max(0, length - (stream.Position - origin));
            int count = (int)Math.Min(Math.Min(size, int.MaxValue), remaining);
            int read = stream.Read(new Span<byte>(data, count));
            processed = (uint)read;

            return HRESULT.S_OK;
        }
        catch (Exception error)
        {
            return Context?.Fail(error) ?? HRESULT.E_FAIL;
        }
    }

    public HRESULT Seek(long offset, uint seekOrigin, ref ulong position)
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
            if (target < 0)
            {
                throw new IOException("Cannot seek before the beginning of an archive.");
            }

            long actual = stream.Seek(checked(origin + target), SeekOrigin.Begin) - origin;
            position = checked((ulong)actual);

            return HRESULT.S_OK;
        }
        catch (Exception error)
        {
            return Context?.Fail(error) ?? HRESULT.E_FAIL;
        }
    }
}
