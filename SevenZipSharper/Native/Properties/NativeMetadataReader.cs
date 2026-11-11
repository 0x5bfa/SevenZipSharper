using SevenZipSharper.Operations;
using Windows.Win32.System.Com.StructuredStorage;

namespace SevenZipSharper.Native.Properties;

internal sealed record EntryMetadata(int Index, string Name, ArchiveEntryType Type, long? Length,
    long? PackedLength, DateTimeOffset? ModifiedTime, bool Encrypted, uint? Crc32, string? LinkTarget, uint? Attributes);

internal sealed record ArchiveMetadata(EntryMetadata[] Entries, ArchiveInfo Info, ArchiveCapabilities Capabilities);

internal sealed class NativeMetadataReader(NativeArchiveSession session)
{
    internal ArchiveMetadata Read(ArchiveOpenOptions options, OperationContext context)
    {
        uint count = session.Count;
        if (count > options.MaximumEntries || count > int.MaxValue)
        {
            throw new InvalidDataException("The archive exceeds the configured entry count limit.");
        }

        var entries = new EntryMetadata[(int)count];
        for (int index = 0; index < entries.Length; index++)
        {
            context.Check();
            uint i = (uint)index;
            string path = (String(i, NativePropertyId.Path) ?? "").Replace('\\', '/');
            string? symlink = String(i, NativePropertyId.SymbolicLink), hardlink = String(i, NativePropertyId.HardLink);
            uint? attributes = UInt32(i, NativePropertyId.Attributes);
            uint? mode = UInt32(i, NativePropertyId.PosixAttributes);
            ArchiveEntryType type = Boolean(i, NativePropertyId.IsDirectory) ? ArchiveEntryType.Directory : ArchiveEntryType.File;
            if (type == ArchiveEntryType.Directory && path.Length > 0 && !path.EndsWith('/'))
            {
                path += '/';
            }

            if (symlink is not null || (mode & 0xF000) == 0xA000)
            {
                type = ArchiveEntryType.SymbolicLink;
            }
            else if (hardlink is not null)
            {
                type = ArchiveEntryType.HardLink;
            }
            else if ((mode & 0xF000) is { } bits && bits is not (0 or 0x8000 or 0x4000) ||
                     Boolean(i, NativePropertyId.IsAlternateStream) || Boolean(i, NativePropertyId.IsAnti) ||
                     (attributes.GetValueOrDefault() & 0x400) != 0)
            {
                type = ArchiveEntryType.Other;
            }

            entries[index] = new(index, path, type, Length(i, NativePropertyId.Size), Length(i, NativePropertyId.PackedSize),
                Time(i, NativePropertyId.ModifiedTime), Boolean(i, NativePropertyId.Encrypted),
                UInt32(i, NativePropertyId.Crc32), symlink ?? hardlink, attributes);
        }

        PROPVARIANT solid = session.ArchiveProperty(NativePropertyId.Solid);
        PROPVARIANT physical = default;
        ArchiveInfo info;
        try
        {
            physical = session.ArchiveProperty(NativePropertyId.PhysicalSize);
            long? total = 0;
            foreach (var entry in entries)
            {
                total = total is { } sum && entry.Length is { } size && size <= long.MaxValue - sum ? sum + size : null;
            }

            info = new(session.Format.Name, InteropHelpers.GetBoolean(ref solid) ?? false, total, InteropHelpers.GetLength(ref physical));
        }
        finally { InteropHelpers.Clear(ref solid); InteropHelpers.Clear(ref physical); }
        var capabilities = new ArchiveCapabilities(session.QueryWriter() is not null);
        return new(entries, info, capabilities);
    }

    private string? String(uint index, NativePropertyId id)
    {
        PROPVARIANT value = session.Property(index, id);
        try { return InteropHelpers.GetString(ref value); } finally { InteropHelpers.Clear(ref value); }
    }
    private bool Boolean(uint index, NativePropertyId id)
    {
        PROPVARIANT value = session.Property(index, id);
        try { return InteropHelpers.GetBoolean(ref value) ?? false; } finally { InteropHelpers.Clear(ref value); }
    }
    private long? Length(uint index, NativePropertyId id)
    {
        PROPVARIANT value = session.Property(index, id);
        try { return InteropHelpers.GetLength(ref value); } finally { InteropHelpers.Clear(ref value); }
    }
    private uint? UInt32(uint index, NativePropertyId id)
    {
        PROPVARIANT value = session.Property(index, id);
        try { return InteropHelpers.GetUnsigned(ref value) is { } number ? checked((uint)number) : null; }
        finally { InteropHelpers.Clear(ref value); }
    }
    private DateTimeOffset? Time(uint index, NativePropertyId id)
    {
        PROPVARIANT value = session.Property(index, id);
        try { return InteropHelpers.GetTimestamp(ref value); }
        catch (ArgumentOutOfRangeException error) { throw new InvalidDataException("The archive contains an invalid native timestamp.", error); }
        finally { InteropHelpers.Clear(ref value); }
    }
}
