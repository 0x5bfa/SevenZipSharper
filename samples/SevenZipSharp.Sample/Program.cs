using System.Runtime.Versioning;
using System.Text;
using SevenZipSharp;

[assembly: SupportedOSPlatform("windows")]

try
{
    if (args is ["self-test"])
    {
        RunSelfTest();
        return 0;
    }
    if (args is ["list", var input])
    {
        using var archive = await ArchiveFile.OpenReadAsync(input);
        Console.WriteLine($"{archive.Info.FormatName}: {archive.Entries.Count} entries");
        foreach (var entry in archive.Entries)
            Console.WriteLine($"{entry.EntryType,-14} {entry.Length,12} {entry.FullName}");
        return 0;
    }
    if (args is ["verify", var verifyInput])
    {
        using var archive = await ArchiveFile.OpenReadAsync(verifyInput);
        await archive.TestAsync();
        Console.WriteLine("Archive integrity verified.");
        return 0;
    }
    if (args is ["extract", var archivePath, var outputDirectory])
    {
        await ArchiveFile.ExtractToDirectoryAsync(archivePath, outputDirectory);
        Console.WriteLine("Extraction completed.");
        return 0;
    }
    if (args is ["create", var sourceDirectory, var destination])
    {
        ArchiveWriteOptions options = Path.GetExtension(destination).ToLowerInvariant() switch
        {
            ".zip" => new ZipWriteOptions(), ".7z" => new SevenZipWriteOptions(),
            ".tar" => new TarWriteOptions(), _ => throw new ArgumentException("Choose a .zip, .7z, or .tar destination.")
        };
        await ArchiveFile.CreateFromDirectoryAsync(sourceDirectory, destination, options);
        Console.WriteLine("Archive created.");
        return 0;
    }
    Console.WriteLine("SevenZipSharp sample");
    Console.WriteLine("  list <archive>");
    Console.WriteLine("  verify <archive>");
    Console.WriteLine("  extract <archive> <directory>");
    Console.WriteLine("  create <directory> <archive.zip|archive.7z|archive.tar>");
    Console.WriteLine("  self-test");
    return args.Length == 0 ? 0 : 2;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static void RunSelfTest()
{
    byte[] content = Encoding.UTF8.GetBytes("NativeAOT archive callback verification");
    (ArchiveWriteOptions Options, string? Password)[] cases =
    [
        (new ZipWriteOptions(), null), (new SevenZipWriteOptions(), null), (new TarWriteOptions(), null),
        (new GZipWriteOptions(), null), (new XzWriteOptions(), null), (new BZip2WriteOptions(), null),
        (new SevenZipWriteOptions { Password = "sample-password", EncryptHeaders = true }, "sample-password")
    ];
    foreach (var test in cases)
    {
        var builder = new ArchiveBuilder();
        builder.AddEntry("readme.txt", content.Length, () => new MemoryStream(content));
        using var encoded = new MemoryStream();
        builder.WriteTo(encoded, test.Options);
        encoded.Position = 0;
        using var archive = Archive.Open(encoded, new() { Password = test.Password }, true);
        using var decoded = new MemoryStream();
        archive.Entries.Single().CopyTo(decoded);
        if (!decoded.ToArray().AsSpan().SequenceEqual(content)) throw new InvalidDataException("Round-trip content mismatch.");
        archive.Test();
        if (archive.Format is ArchiveFormat.Zip or ArchiveFormat.SevenZip or ArchiveFormat.Tar)
        {
            var editor = archive.CreateEditor();
            editor.Rename(archive.Entries[0], "renamed.txt");
            using var replacement = new MemoryStream();
            editor.WriteTo(replacement, new() { WriteOptions = test.Options });
            replacement.Position = 0;
            using var updated = Archive.Open(replacement, new() { Password = test.Password });
            if (updated.GetEntry("renamed.txt") is null) throw new InvalidDataException("Editor output mismatch.");
            updated.Test();
        }
        Console.WriteLine($"Verified {archive.Info.FormatName}{(test.Password is null ? "" : " with encrypted headers")}.");
    }
    string root = Path.Combine(Path.GetTempPath(), "SevenZipSharp.Sample-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "input"));
    try
    {
        File.WriteAllBytes(Path.Combine(root, "input", "readme.txt"), content);
        string path = Path.Combine(root, "archive.zip");
        ArchiveFile.CreateFromDirectory(Path.Combine(root, "input"), path, new ZipWriteOptions());
        ArchiveFile.Update(path, editor => editor.Rename(editor.Entries.Single(), "renamed.txt"));
        ArchiveFile.ExtractToDirectory(path, Path.Combine(root, "output"));
        if (!File.ReadAllBytes(Path.Combine(root, "output", "renamed.txt")).AsSpan().SequenceEqual(content))
            throw new InvalidDataException("Filesystem output mismatch.");
        Console.WriteLine("Verified filesystem creation, update, and extraction.");
    }
    finally
    {
        string expected = Path.Combine(Path.GetTempPath(), "SevenZipSharp.Sample-");
        if (!Path.GetFullPath(root).StartsWith(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected sample cleanup directory.");
        Directory.Delete(root, true);
    }
    Console.WriteLine("All native smoke checks passed.");
}
