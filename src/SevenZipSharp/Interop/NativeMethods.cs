using System.Runtime.InteropServices;

namespace SevenZipSharp.Interop;

internal static partial class NativeMethods
{
    [LibraryImport("ole32.dll")]
    internal static partial int PropVariantClear(ref NativeVariant value);

    internal const int Abort = unchecked((int)0x80004004);
    internal const int Fail = unchecked((int)0x80004005);
    internal static readonly Guid InArchiveId = new("23170F69-40C1-278A-0000-000600600000");
    internal static readonly Guid OutArchiveId = new("23170F69-40C1-278A-0000-000600A00000");
    internal static readonly Guid SetPropertiesId = new("23170F69-40C1-278A-0000-000600030000");

    internal static void Check(int result)
    {
        if (result == 0) return;
        throw result switch
        {
            1 => new InvalidDataException("The native engine rejected the archive data."),
            unchecked((int)0x80004001) or unchecked((int)0x80040111) =>
                new NotSupportedException("The native engine does not implement the requested operation."),
            unchecked((int)0x8007000E) => new OutOfMemoryException(),
            unchecked((int)0x80070005) => new UnauthorizedAccessException("The native archive operation was denied access."),
            unchecked((int)0x80070057) => new NotSupportedException("The native engine rejected this operation or option combination."),
            _ => new IOException($"The native archive operation failed (0x{result:X8}).", result)
        };
    }
}
