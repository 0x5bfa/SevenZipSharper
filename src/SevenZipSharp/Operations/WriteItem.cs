namespace SevenZipSharp.Operations;

internal sealed record WriteItem(string FullName, bool IsDirectory, long Length, Func<Stream>? OpenRead,
    ArchiveEntryOptions Options, uint ArchiveIndex = uint.MaxValue, bool NewData = true, bool NewProperties = true)
{
    internal static string ValidateName(string fullName, bool directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        string name = fullName.Replace('\\', '/');
        if (name[0] == '/' || name.Contains(':') || name.Contains('\0') ||
            name.TrimEnd('/').Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new ArgumentException("An entry name must be a relative logical path without empty, dot, or parent segments.", nameof(fullName));
        }

        if (!directory && name.EndsWith('/'))
        {
            throw new ArgumentException("A regular file name cannot end in '/'.", nameof(fullName));
        }

        return directory ? name.TrimEnd('/') + "/" : name;
    }

    internal static ArchiveEntryOptions CaptureOptions(ArchiveEntryOptions? options)
    {
        var snapshot = options is null ? new ArchiveEntryOptions() : options with { };
        snapshot.Validate();
        return snapshot;
    }

    internal static WriteItem FromEntry(ArchiveEntry entry)
    {
        return new(entry.FullName,
        entry.EntryType == ArchiveEntryType.Directory, entry.Length ?? 0, null,
        new() { LastWriteTime = entry.LastWriteTime, Attributes = entry.NativeAttributes is { } attributes ? (FileAttributes)attributes : null },
        (uint)entry.Index, false, false);
    }
}
