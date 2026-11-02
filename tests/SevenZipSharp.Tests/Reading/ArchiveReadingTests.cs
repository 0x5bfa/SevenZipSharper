using System.IO.Compression;
using System.Text;

namespace SevenZipSharp.Tests.Reading;

[TestFixture]
public sealed class ArchiveReadingTests
{
    internal static byte[] CreateZip(params (string Name, string Content)[] files)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var file in files)
            {
                var entry = zip.CreateEntry(file.Name, CompressionLevel.NoCompression);
                using var stream = entry.Open();
                stream.Write(Encoding.UTF8.GetBytes(file.Content));
            }
        }
        return output.ToArray();
    }

    [Test]
    public void MetadataAndSingleEntryContentMatchBcl()
    {
        using var source = new MemoryStream(CreateZip(("folder/", ""), ("folder/readme.txt", "native archive contents")));
        using var archive = Archive.Open(source, leaveOpen: true);
        Assert.Multiple(() =>
        {
            Assert.That(archive.Format, Is.EqualTo(ArchiveFormat.Zip));
            Assert.That(archive.Entries, Has.Count.EqualTo(2));
            Assert.That(archive.GetEntry("folder/")!.EntryType, Is.EqualTo(ArchiveEntryType.Directory));
            Assert.That(archive.GetEntry("folder/readme.txt")!.Length, Is.EqualTo(23));
            Assert.That(archive.Capabilities.CanUpdate, Is.True);
        });
        using var output = new MemoryStream();
        archive.GetEntry("folder/readme.txt")!.CopyTo(output);
        Assert.That(Encoding.UTF8.GetString(output.ToArray()), Is.EqualTo("native archive contents"));
        archive.Test();
        Assert.That(source.CanRead, Is.True);
        Assert.That(output.CanWrite, Is.True);
    }

    [Test]
    public void DuplicateAndCaseDistinctNamesRemainAddressable()
    {
        using var source = new MemoryStream(CreateZip(("a.txt", "first"), ("a.txt", "second"), ("A.txt", "third")));
        using var archive = Archive.Open(source);
        Assert.That(archive.GetEntries("a.txt").Select(e => e.Index), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(archive.GetEntry("A.txt")!.Index, Is.EqualTo(2));
        using var result = new MemoryStream();
        archive.Entries[1].CopyTo(result);
        Assert.That(Encoding.UTF8.GetString(result.ToArray()), Is.EqualTo("second"));
    }

    [Test]
    public void InputOriginAndOwnershipAreExplicit()
    {
        byte[] zip = CreateZip(("a", "content"));
        using var source = new MemoryStream();
        source.Write(new byte[37]);
        source.Write(zip);
        source.Position = 37;
        var archive = Archive.Open(source);
        ArchiveEntry entry = archive.Entries.Single();
        using var result = new MemoryStream();
        entry.CopyTo(result);
        archive.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(source.CanRead, Is.False);
            Assert.That(entry.FullName, Is.EqualTo("a"));
            Assert.That(Encoding.UTF8.GetString(result.ToArray()), Is.EqualTo("content"));
            Assert.Throws<ObjectDisposedException>(() => entry.CopyTo(result));
        });
    }

    [Test]
    public void FailedOpenLeavesBorrowedInputOpen()
    {
        using var source = new MemoryStream([1, 2, 3, 4]);
        Assert.Throws<InvalidDataException>(() => Archive.Open(source));
        Assert.That(source.CanRead, Is.True);
    }

    [Test]
    public void EntryLimitFailsBeforeMaterializationAndKeepsInputOpen()
    {
        using var source = new MemoryStream(CreateZip(("a", "a"), ("b", "b")));
        Assert.Throws<InvalidDataException>(() => Archive.Open(source, new() { MaximumEntries = 1 }));
        Assert.That(source.CanRead, Is.True);
    }

    [Test]
    public async Task AsyncOpenAndCopyUseTheSameContract()
    {
        using var source = new MemoryStream(CreateZip(("a", "async")));
        await using var archive = await Archive.OpenAsync(source, leaveOpen: true);
        using var output = new MemoryStream();
        await archive.Entries[0].CopyToAsync(output);
        Assert.That(Encoding.UTF8.GetString(output.ToArray()), Is.EqualTo("async"));
    }

    [Test]
    public void CorruptionIsReportedEvenWhenTheOuterExtractionCompletes()
    {
        const string content = "unique uncompressed content for corruption";
        byte[] zip = CreateZip(("a", content));
        int offset = zip.AsSpan().IndexOf(Encoding.UTF8.GetBytes(content));
        Assert.That(offset, Is.GreaterThanOrEqualTo(0));
        zip[offset] ^= 0x40;
        using var source = new MemoryStream(zip);
        using var archive = Archive.Open(source);
        Assert.Throws<InvalidDataException>(() => archive.Test());
    }

    [Test]
    public void EmptyZipCanBeOpenedAndTested()
    {
        using var source = new MemoryStream(CreateZip());
        using var archive = Archive.Open(source);
        Assert.That(archive.Entries, Is.Empty);
        archive.Test();
    }
}
