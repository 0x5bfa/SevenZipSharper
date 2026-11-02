namespace SevenZipSharp.Interop;

// Values from the pinned 7-Zip 26.05 PropID.h.
internal enum NativePropertyId : uint
{
    Path = 3,
    Name = 4,
    IsDirectory = 6,
    Size = 7,
    PackedSize = 8,
    Attributes = 9,
    ModifiedTime = 12,
    Solid = 13,
    Encrypted = 15,
    Crc32 = 19,
    IsAnti = 21,
    PhysicalSize = 44,
    PosixAttributes = 53,
    SymbolicLink = 54,
    IsAlternateStream = 63,
    HardLink = 90
}
