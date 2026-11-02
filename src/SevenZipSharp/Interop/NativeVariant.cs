using System.Runtime.InteropServices;

namespace SevenZipSharp.Interop;

// ABI pinned to 7-Zip 26.05, b88c6bc09339de32f7d18bbab900686b244f42ba.
// The pointer-bearing union is 16 bytes on x64/ARM64 and 8 bytes on x86.
[StructLayout(LayoutKind.Sequential)]
internal struct NativeVariant
{
    internal ushort Type;
    private ushort reserved1, reserved2, reserved3;
    internal VariantValue Value;

    internal readonly string? String => Type == 8 ? Marshal.PtrToStringBSTR(Value.Pointer) : null;
    internal readonly bool? Boolean => Type == 11 ? Value.Int16 != 0 : null;
    internal readonly ulong? Unsigned => Type switch
    {
        17 => Value.Byte,
        18 => Value.UInt16,
        19 => Value.UInt32,
        21 => Value.UInt64,
        _ => null
    };
    internal readonly long? Length => Unsigned is { } size
        ? size <= long.MaxValue ? (long)size : throw new NotSupportedException("Lengths above Int64.MaxValue are unsupported.")
        : null;
    internal readonly DateTimeOffset? Timestamp => Type == 64
        ? DateTimeOffset.FromFileTime(Value.Int64) : null;

    internal static NativeVariant FromString(string value) => new() { Type = 8, Value = new() { Pointer = Marshal.StringToBSTR(value) } };
    internal static NativeVariant FromBoolean(bool value) => new() { Type = 11, Value = new() { Int16 = value ? (short)-1 : (short)0 } };
    internal static NativeVariant FromUInt64(ulong value) => new() { Type = 21, Value = new() { UInt64 = value } };
    internal static NativeVariant FromUInt32(uint value) => new() { Type = 19, Value = new() { UInt32 = value } };
    internal static NativeVariant FromTimestamp(DateTimeOffset value) => new() { Type = 64, Value = new() { Int64 = value.ToFileTime() } };
    internal void Clear() => NativeMethods.PropVariantClear(ref this);
}

[StructLayout(LayoutKind.Explicit)]
internal struct VariantValue
{
    [FieldOffset(0)] internal byte Byte;
    [FieldOffset(0)] internal short Int16;
    [FieldOffset(0)] internal ushort UInt16;
    [FieldOffset(0)] internal uint UInt32;
    [FieldOffset(0)] internal ulong UInt64;
    [FieldOffset(0)] internal long Int64;
    [FieldOffset(0)] internal nint Pointer;
    [FieldOffset(0)] private VariantArray array;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VariantArray
{
    private uint count;
    private nint pointer;
}
