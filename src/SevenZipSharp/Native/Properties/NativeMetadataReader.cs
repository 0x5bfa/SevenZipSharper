using System.Runtime.InteropServices;
using SevenZipSharp.Interop;
using SevenZipSharp.Operations;

namespace SevenZipSharp.Native.Properties;

internal sealed record EntryMetadata(int Index, string Name, ArchiveEntryType Type, long? Length,
    long? PackedLength, DateTimeOffset? ModifiedTime, bool Encrypted, uint? Crc32, string? LinkTarget, uint? Attributes);

internal sealed record ArchiveMetadata(EntryMetadata[] Entries, ArchiveInfo Info, ArchiveCapabilities Capabilities);

internal sealed class NativeMetadataReader(NativeArchiveSession session)
{
    internal ArchiveMetadata Read(ArchiveOpenOptions options, OperationContext context)
    {
        uint count = session.Count;
        if (count > options.MaximumEntries || count > int.MaxValue)
            throw new InvalidDataException("The archive exceeds the configured entry count limit.");
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
            if (type == ArchiveEntryType.Directory && path.Length > 0 && !path.EndsWith('/')) path += '/';
            if (symlink is not null || (mode & 0xF000) == 0xA000) type = ArchiveEntryType.SymbolicLink;
            else if (hardlink is not null) type = ArchiveEntryType.HardLink;
            else if ((mode & 0xF000) is { } bits && bits is not (0 or 0x8000 or 0x4000) ||
                     Boolean(i, NativePropertyId.IsAlternateStream) || Boolean(i, NativePropertyId.IsAnti) ||
                     (attributes.GetValueOrDefault() & 0x400) != 0)
                type = ArchiveEntryType.Other;
            entries[index] = new(index, path, type, Length(i, NativePropertyId.Size), Length(i, NativePropertyId.PackedSize),
                Time(i, NativePropertyId.ModifiedTime), Boolean(i, NativePropertyId.Encrypted),
                UInt32(i, NativePropertyId.Crc32), symlink ?? hardlink, attributes);
        }

        NativeVariant solid = session.ArchiveProperty(NativePropertyId.Solid);
        NativeVariant physical = default;
        ArchiveInfo info;
        try
        {
            physical = session.ArchiveProperty(NativePropertyId.PhysicalSize);
            long? total = 0;
            foreach (var entry in entries)
                total = total is { } sum && entry.Length is { } size && size <= long.MaxValue - sum ? sum + size : null;
            info = new(session.Format.Name, solid.Boolean ?? false, total, physical.Length);
        }
        finally { solid.Clear(); physical.Clear(); }
        nint writer = session.QueryWriter();
        var capabilities = new ArchiveCapabilities(writer != 0);
        if (writer != 0) Marshal.Release(writer);
        return new(entries, info, capabilities);
    }

    private string? String(uint index, NativePropertyId id)
    {
        var value = session.Property(index, id);
        try { return value.String; } finally { value.Clear(); }
    }
    private bool Boolean(uint index, NativePropertyId id)
    {
        var value = session.Property(index, id);
        try { return value.Boolean ?? false; } finally { value.Clear(); }
    }
    private long? Length(uint index, NativePropertyId id)
    {
        var value = session.Property(index, id);
        try { return value.Length; } finally { value.Clear(); }
    }
    private uint? UInt32(uint index, NativePropertyId id)
    {
        var value = session.Property(index, id);
        try { return value.Unsigned is { } number ? checked((uint)number) : null; } finally { value.Clear(); }
    }
    private DateTimeOffset? Time(uint index, NativePropertyId id)
    {
        var value = session.Property(index, id);
        try { return value.Timestamp; }
        catch (ArgumentOutOfRangeException error) { throw new InvalidDataException("The archive contains an invalid native timestamp.", error); }
        finally { value.Clear(); }
    }
}
