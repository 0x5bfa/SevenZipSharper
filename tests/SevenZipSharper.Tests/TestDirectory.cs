namespace SevenZipSharper.Tests;

internal sealed class TestDirectory : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SevenZipSharper.Tests-" + Guid.NewGuid().ToString("N"));
    internal TestDirectory() => Directory.CreateDirectory(Path);
    internal string File(string name)
    {
        return System.IO.Path.Combine(Path, name);
    }

    public void Dispose()
    {
        string root = System.IO.Path.GetFullPath(Path);
        string expected = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SevenZipSharper.Tests-");
        if (!root.StartsWith(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unexpected test directory.");
        }

        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }
}
