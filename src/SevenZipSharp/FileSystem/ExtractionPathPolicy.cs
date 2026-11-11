namespace SevenZipSharp.FileSystem;

internal static class ExtractionPathPolicy
{
    private static readonly HashSet<string> devices = new(StringComparer.OrdinalIgnoreCase)
        { "CON", "PRN", "AUX", "NUL", "CLOCK$", "CONIN$", "CONOUT$" };

    internal static string Resolve(string root, string name)
    {
        string relative = name.Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith('/'))
        {
            throw new InvalidDataException("An entry must have a nonempty relative path.");
        }

        foreach (string segment in relative.Split('/'))
        {
            ValidateSegment(segment);
        }

        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("An entry escapes the destination directory.");
        }

        return path;
    }

    internal static void ValidateSegment(string segment)
    {
        if (segment is "" or "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') ||
            segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || segment.Contains(':'))
        {
            throw new InvalidDataException($"Unsupported filesystem path component '{segment}'.");
        }

        string stem = segment.Split('.')[0];
        if (devices.Contains(stem) ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                                stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
             "123456789¹²³".Contains(stem[3])))
        {
            throw new InvalidDataException($"Reserved Windows device name '{segment}'.");
        }
    }

    internal static void EnsureNoReparsePoints(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"Reparse points are not permitted in an extraction or publication path: '{current}'.");
            }
        }
    }
}
