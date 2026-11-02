using System.Text;
using SevenZipSharp.Tests.Reading;

namespace SevenZipSharp.Tests.Updating;

[TestFixture]
public sealed class ArchiveEditingTests
{
    [TestCase(ArchiveFormat.Zip)]
    [TestCase(ArchiveFormat.SevenZip)]
    [TestCase(ArchiveFormat.Tar)]
    public void EditsProduceReplacementAndKeepSourceContentsIntact(ArchiveFormat format)
    {
        var builder = new ArchiveBuilder();
        builder.AddEntry("rename.txt", 3, () => new MemoryStream("old"u8.ToArray()));
        builder.AddEntry("delete.txt", 6, () => new MemoryStream("delete"u8.ToArray()));
        builder.AddEntry("replace.txt", 3, () => new MemoryStream("old"u8.ToArray()));
        builder.AddEntry("keep.txt", 4, () => new MemoryStream("keep"u8.ToArray()));
        using var source = new MemoryStream();
        ArchiveWriteOptions options = format switch
        {
            ArchiveFormat.Zip => new ZipWriteOptions(),
            ArchiveFormat.Tar => new TarWriteOptions(),
            _ => new SevenZipWriteOptions()
        };
        builder.WriteTo(source, options);
        byte[] original = source.ToArray();
        source.Position = 0;
        using var archive = Archive.Open(source, leaveOpen: true);
        var editor = archive.CreateEditor();
        editor.Rename(archive.GetEntry("rename.txt")!, "renamed.txt");
        editor.Delete(archive.GetEntry("delete.txt")!);
        editor.Replace(archive.GetEntry("replace.txt")!, 3, () => new MemoryStream("new"u8.ToArray()));
        editor.AddEntry("added.txt", 3, () => new MemoryStream("add"u8.ToArray()));
        using var destination = new MemoryStream();
        editor.WriteTo(destination);
        Assert.That(source.ToArray(), Is.EqualTo(original));
        destination.Position = 0;
        using var replacement = Archive.Open(destination);
        Assert.That(replacement.Entries.Select(entry => entry.FullName), Is.EquivalentTo(new[] { "renamed.txt", "replace.txt", "keep.txt", "added.txt" }));
        foreach (var (name, content) in new[] { ("renamed.txt", "old"), ("replace.txt", "new"), ("keep.txt", "keep"), ("added.txt", "add") })
        {
            using var result = new MemoryStream();
            replacement.GetEntry(name)!.CopyTo(result);
            Assert.That(Encoding.UTF8.GetString(result.ToArray()), Is.EqualTo(content));
        }
        replacement.Test();
    }

    [Test]
    public void DuplicateNamesAreEditedByEntryIdentity()
    {
        using var source = new MemoryStream(ArchiveReadingTests.CreateZip(("a", "first"), ("a", "second")));
        using var archive = Archive.Open(source);
        var editor = archive.CreateEditor();
        editor.Delete(archive.Entries[0]);
        editor.Rename(archive.Entries[1], "remaining");
        using var output = new MemoryStream();
        editor.WriteTo(output);
        output.Position = 0;
        using var replacement = Archive.Open(output);
        Assert.That(replacement.Entries.Single().FullName, Is.EqualTo("remaining"));
        using var result = new MemoryStream();
        replacement.Entries[0].CopyTo(result);
        Assert.That(Encoding.UTF8.GetString(result.ToArray()), Is.EqualTo("second"));
    }

    [Test]
    public void ConflictingAndForeignEditsAreRejected()
    {
        using var source = new MemoryStream(ArchiveReadingTests.CreateZip(("a", "a")));
        using var otherSource = new MemoryStream(source.ToArray());
        using var archive = Archive.Open(source);
        using var other = Archive.Open(otherSource);
        var editor = archive.CreateEditor();
        Assert.Throws<ArgumentException>(() => editor.Delete(other.Entries[0]));
        editor.Rename(archive.Entries[0], "b");
        Assert.Throws<InvalidOperationException>(() => editor.Delete(archive.Entries[0]));
        using var output = new MemoryStream();
        Assert.Throws<ArgumentException>(() => editor.WriteTo(output, new() { WriteOptions = new SevenZipWriteOptions() }));
        Assert.Throws<ArgumentException>(() => editor.WriteTo(source));
    }
}
