namespace SevenZipSharper.FileSystem;

internal static class DirectoryInputPlanner
{
    internal static ArchiveBuilder Create(string sourceDirectory, string destination, ArchiveCreationOptions options, CancellationToken token)
    {
        string root = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(root);
        }

        ExtractionPathPolicy.EnsureNoReparsePoints(root);
        string excluded = Path.GetFullPath(destination);
        string prefix = options.IncludeBaseDirectory ? new DirectoryInfo(root).Name + "/" : "";
        var builder = new ArchiveBuilder();
        var pending = new Stack<(string Directory, string Name)>();
        pending.Push((root, prefix));
        int count = 0;
        void Count()
        {
            token.ThrowIfCancellationRequested();
            if (++count > options.MaximumEntries)
            {
                throw new InvalidDataException("The creation entry limit was exceeded.");
            }
        }
        while (pending.TryPop(out var current))
        {
            token.ThrowIfCancellationRequested();
            if (options.IncludeEmptyDirectories && current.Name.Length > 0)
            {
                Count();
                builder.AddDirectory(current.Name, new() { LastWriteTime = new DateTimeOffset(Directory.GetLastWriteTimeUtc(current.Directory)) });
            }
            foreach (string path in Directory.EnumerateFileSystemEntries(current.Directory).Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                if (path.Equals(excluded, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                {
                    if (options.UnsupportedEntries == UnsupportedEntryPolicy.Skip)
                    {
                        continue;
                    }

                    throw new NotSupportedException("Directory creation does not follow reparse points or device entries.");
                }
                string name = current.Name + Path.GetFileName(path);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push((path, name + "/"));
                }
                else
                {
                    Count();
                    builder.AddFile(path, name);
                }
            }
        }
        return builder;
    }
}
