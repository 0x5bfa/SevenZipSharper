using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;

namespace SevenZipSharp.Native;

internal static unsafe class InteropHelpers
{
    internal static PROPVARIANT CreateString(string value)
    {
        PROPVARIANT result = default;
        result.vt = VARENUM.VT_BSTR;
        result.bstrVal = (BSTR)Marshal.StringToBSTR(value);

        return result;
    }

    internal static PROPVARIANT CreateBoolean(bool value)
    {
        PROPVARIANT result = default;
        result.vt = VARENUM.VT_BOOL;
        result.boolVal = (VARIANT_BOOL)(value ? (short)-1 : (short)0);

        return result;
    }

    internal static PROPVARIANT CreateUInt64(ulong value)
    {
        PROPVARIANT result = default;
        result.vt = VARENUM.VT_UI8;
        result.uhVal = value;

        return result;
    }

    internal static PROPVARIANT CreateUInt32(uint value)
    {
        PROPVARIANT result = default;
        result.vt = VARENUM.VT_UI4;
        result.ulVal = value;

        return result;
    }

    internal static PROPVARIANT CreateTimestamp(DateTimeOffset value)
    {
        PROPVARIANT result = default;
        result.vt = VARENUM.VT_FILETIME;
        result.hVal = value.ToFileTime();

        return result;
    }

    internal static string? GetString(ref PROPVARIANT value)
    {
        return value.vt == VARENUM.VT_BSTR ? value.bstrVal.ToString() : null;
    }

    internal static bool? GetBoolean(ref PROPVARIANT value)
    {
        return value.vt == VARENUM.VT_BOOL ? (short)value.boolVal != 0 : null;
    }

    internal static ulong? GetUnsigned(ref PROPVARIANT value)
    {
        return value.vt switch
        {
            VARENUM.VT_UI1 => value.bVal,
            VARENUM.VT_UI2 => value.uiVal,
            VARENUM.VT_UI4 => value.ulVal,
            VARENUM.VT_UI8 => value.uhVal,
            _ => null
        };
    }

    internal static long? GetLength(ref PROPVARIANT value)
    {
        return GetUnsigned(ref value) is { } size
            ? size <= long.MaxValue ? (long)size : throw new NotSupportedException("Lengths above Int64.MaxValue are unsupported.")
            : null;
    }

    internal static DateTimeOffset? GetTimestamp(ref PROPVARIANT value)
    {
        return value.vt == VARENUM.VT_FILETIME ? DateTimeOffset.FromFileTime(value.hVal) : null;
    }

    internal static void Clear(ref PROPVARIANT value)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(5))
        {
            throw new PlatformNotSupportedException();
        }

        PInvoke.PropVariantClear(ref value).ThrowOnFailure();
        value = default;
    }
}
