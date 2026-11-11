using System.Formats.Tar;
using System.Text;
using SevenZipSharper.Tests.Reading;

namespace SevenZipSharper.Tests.FileSystem;

[TestFixture]
public sealed class ArchiveFileSystemTests
{
    [Test]
    public async Task DirectoryCreationAndExtractionPreserveContentAndEmptyDirectories()
    {
        using var temp = new TestDirectory();
        string source = Directory.CreateDirectory(temp.File("source")).FullName;
        Directory.CreateDirectory(System.IO.Path.Combine(source, "empty"));
        Directory.CreateDirectory(System.IO.Path.Combine(source, "nested"));
        File.WriteAllText(System.IO.Path.Combine(source, "nested", "readme.txt"), "filesystem content");
        string encoded = temp.File("archive.7z");
        await ArchiveFile.CreateFromDirectoryAsync(source, encoded, new SevenZipWriteOptions());
        string output = temp.File("output");
        await ArchiveFile.ExtractToDirectoryAsync(encoded, output);
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(System.IO.Path.Combine(output, "nested", "readme.txt")), Is.EqualTo("filesystem content"));
            Assert.That(Directory.Exists(System.IO.Path.Combine(output, "empty")), Is.True);
            Assert.That(Directory.GetFiles(temp.Path, ".sevenzipsharp-*.tmp", SearchOption.AllDirectories), Is.Empty);
        });
    }

    [Test]
    public void FailedUpdatePreservesOriginalBytesAndCleansTemporaryOutput()
    {
        using var temp = new TestDirectory();
        string path = temp.File("archive.zip");
        byte[] original = ArchiveReadingTests.CreateZip(("a", "original"));
        File.WriteAllBytes(path, original);
        var expected = new IOException("source factory failed");
        var actual = Assert.Throws<IOException>(() => ArchiveFile.Update(path, editor =>
            editor.Replace(editor.Entries[0], 3, () => throw expected)));
        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(expected));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
            Assert.That(Directory.GetFiles(temp.Path, ".sevenzipsharp-*.tmp"), Is.Empty);
        });
    }

    [Test]
    public void FailedPublicationPreservesTheOriginal()
    {
        using var temp = new TestDirectory();
        string path = temp.File("archive.zip");
        byte[] original = ArchiveReadingTests.CreateZip(("a", "original"));
        File.WriteAllBytes(path, original);
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Exception? error = Assert.Catch(() => ArchiveFile.Update(path, editor => editor.Rename(editor.Entries[0], "renamed")));
        Assert.That(error, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
        using var check = new MemoryStream();
        locked.CopyTo(check);
        Assert.That(check.ToArray(), Is.EqualTo(original));
        Assert.That(Directory.GetFiles(temp.Path, ".sevenzipsharp-*.tmp"), Is.Empty);
    }

    [Test]
    public void SuccessfulPathUpdateClosesSourceBeforeReplacement()
    {
        using var temp = new TestDirectory();
        string path = temp.File("archive.zip");
        File.WriteAllBytes(path, ArchiveReadingTests.CreateZip(("a", "original")));
        ArchiveFile.Update(path, editor => editor.Rename(editor.Entries[0], "renamed"));
        using var archive = ArchiveFile.OpenRead(path);
        Assert.That(archive.Entries.Single().FullName, Is.EqualTo("renamed"));
    }

    [TestCase("../escape.txt")]
    [TestCase("/rooted.txt")]
    [TestCase("C:/drive.txt")]
    [TestCase("folder/file:stream")]
    [TestCase("folder/NUL.txt")]
    [TestCase("folder/COM1")]
    [TestCase("folder/trailing.")]
    [TestCase("folder/trailing ")]
    public void UnsupportedPathsFailBeforeCreatingTheDestination(string name)
    {
        using var temp = new TestDirectory();
        using var source = new MemoryStream(ArchiveReadingTests.CreateZip((name, "payload")));
        using var archive = Archive.Open(source);
        string destination = temp.File("output");
        Assert.Throws<InvalidDataException>(() => archive.ExtractToDirectory(destination));
        Assert.That(Directory.Exists(destination), Is.False);
    }

    [Test]
    public void CaseCollisionsFailDuringPlanning()
    {
        using var temp = new TestDirectory();
        using var source = new MemoryStream(ArchiveReadingTests.CreateZip(("a", "first"), ("A", "second")));
        using var archive = Archive.Open(source);
        string output = temp.File("output");
        Assert.Throws<InvalidDataException>(() => archive.ExtractToDirectory(output));
        Assert.That(Directory.Exists(output), Is.False);
    }

    [Test]
    public void FileAndParentDirectoryCollisionsFailDuringPlanning()
    {
        using var temp = new TestDirectory();
        using var source = new MemoryStream(ArchiveReadingTests.CreateZip(("a", "file"), ("a/b", "child")));
        using var archive = Archive.Open(source);
        Assert.Throws<InvalidDataException>(() => archive.ExtractToDirectory(temp.File("output")));
    }

    [Test]
    public void ActualByteLimitDoesNotPublishPartialFiles()
    {
        using var temp = new TestDirectory();
        using var source = new MemoryStream(ArchiveReadingTests.CreateZip(("a", "1234567890")));
        using var archive = Archive.Open(source);
        string destination = temp.File("output");
        Assert.Throws<InvalidDataException>(() => archive.ExtractToDirectory(destination, new() { MaximumEntryBytes = 5 }));
        Assert.That(Directory.GetFiles(destination, "*", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public void TotalByteLimitKeepsEarlierCompletedFilesButNotTheFailingFile()
    {
        using var temp = new TestDirectory();
        using var source = new MemoryStream(ArchiveReadingTests.CreateZip(("a", "12345"), ("b", "67890")));
        using var archive = Archive.Open(source);
        string destination = temp.File("output");
        Assert.Throws<InvalidDataException>(() => archive.ExtractToDirectory(destination, new() { MaximumTotalBytes = 7 }));
        Assert.That(File.ReadAllText(System.IO.Path.Combine(destination, "a")), Is.EqualTo("12345"));
        Assert.That(File.Exists(System.IO.Path.Combine(destination, "b")), Is.False);
        Assert.That(Directory.GetFiles(destination, ".sevenzipsharp-*.tmp"), Is.Empty);
    }

    [Test]
    public void CorruptExtractionPreservesAnExistingFile()
    {
        using var temp = new TestDirectory();
        const string content = "unique payload with a bad crc";
        byte[] bytes = ArchiveReadingTests.CreateZip(("a", content));
        int index = bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(content));
        bytes[index] ^= 0x40;
        using var source = new MemoryStream(bytes);
        using var archive = Archive.Open(source);
        string path = temp.File("existing.txt");
        File.WriteAllText(path, "original");
        Assert.Throws<InvalidDataException>(() => archive.Entries[0].ExtractToFile(path, new() { Overwrite = true }));
        Assert.That(File.ReadAllText(path), Is.EqualTo("original"));
        Assert.That(Directory.GetFiles(temp.Path, ".sevenzipsharp-*.tmp"), Is.Empty);
    }

    [Test]
    public void ExistingFilesAreNotOverwrittenByDefault()
    {
        using var temp = new TestDirectory();
        using var source = new MemoryStream(ArchiveReadingTests.CreateZip(("a", "new")));
        using var archive = Archive.Open(source);
        string path = temp.File("a");
        File.WriteAllText(path, "original");
        Assert.Throws<IOException>(() => archive.Entries[0].ExtractToFile(path));
        Assert.That(File.ReadAllText(path), Is.EqualTo("original"));
        archive.Entries[0].ExtractToFile(path, new() { Overwrite = true });
        Assert.That(File.ReadAllText(path), Is.EqualTo("new"));
    }

    [Test]
    public void TarLinksRequireAnExplicitSkipPolicy()
    {
        using var temp = new TestDirectory();
        using var source = new MemoryStream();
        using (var writer = new TarWriter(source, true))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "link") { LinkName = "../outside" });
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "a") { DataStream = new MemoryStream("abc"u8.ToArray()) });
        }
        source.Position = 0;
        using var archive = Archive.Open(source);
        Assert.That(archive.Entries[0].EntryType, Is.EqualTo(ArchiveEntryType.SymbolicLink));
        Assert.Throws<NotSupportedException>(() => archive.ExtractToDirectory(temp.File("rejected")));
        archive.ExtractToDirectory(temp.File("output"), new() { UnsupportedEntries = UnsupportedEntryPolicy.Skip });
        Assert.That(File.Exists(temp.File("output/link")), Is.False);
        Assert.That(File.ReadAllText(temp.File("output/a")), Is.EqualTo("abc"));
    }

    [Test]
    public void ExistingDirectorySymlinkIsRejected()
    {
        using var temp = new TestDirectory();
        string output = Directory.CreateDirectory(temp.File("output")).FullName;
        string outside = Directory.CreateDirectory(temp.File("outside")).FullName;
        try { Directory.CreateSymbolicLink(System.IO.Path.Combine(output, "link"), outside); }
        catch (UnauthorizedAccessException) { Assert.Ignore("Creating directory symlinks requires Developer Mode or the symlink privilege."); }
        using var source = new MemoryStream(ArchiveReadingTests.CreateZip(("link/escape", "payload")));
        using var archive = Archive.Open(source);
        Assert.Throws<IOException>(() => archive.ExtractToDirectory(output));
        Assert.That(Directory.GetFiles(outside), Is.Empty);
    }
}
