using SevenZip.Interop;

namespace SevenZipSharp.Native;

internal enum NativePropertyId : uint
{
    Path = PInvoke.kpidPath,
    Name = PInvoke.kpidName,
    IsDirectory = PInvoke.kpidIsDir,
    Size = PInvoke.kpidSize,
    PackedSize = PInvoke.kpidPackSize,
    Attributes = PInvoke.kpidAttrib,
    ModifiedTime = PInvoke.kpidMTime,
    Solid = PInvoke.kpidSolid,
    Encrypted = PInvoke.kpidEncrypted,
    Crc32 = PInvoke.kpidCRC,
    IsAnti = PInvoke.kpidIsAnti,
    PhysicalSize = PInvoke.kpidPhySize,
    PosixAttributes = PInvoke.kpidPosixAttrib,
    SymbolicLink = PInvoke.kpidSymLink,
    IsAlternateStream = PInvoke.kpidIsAltStream,
    HardLink = PInvoke.kpidHardLink
}
