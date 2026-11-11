using System.IO.Compression;

namespace SevenZipSharp.Tests.Creation;

[TestFixture]
public sealed class ArchiveFormatContractTests
{
    [Test]
    public void PasswordOptionsAreRedactedFromDiagnosticDescriptions()
    {
        const string password = "unit-test-password-that-must-not-be-logged";
        object[] options =
        [
            new ArchiveOpenOptions { Password = password },
            new SevenZipWriteOptions { Password = password },
            new ZipWriteOptions { Password = password },
            new ArchiveUpdateOptions { WriteOptions = new ZipWriteOptions { Password = password } }
        ];
        foreach (var option in options)
        {
            Assert.That(option.ToString(), Does.Not.Contain(password));
        }
    }

    [TestCase(ArchiveFormat.SevenZip)]
    [TestCase(ArchiveFormat.Zip)]
    public void NoCompressionPresetStoresContentWithoutACompressionCodec(ArchiveFormat format)
    {
        var builder = new ArchiveBuilder();
        byte[] content = new byte[4096];
        builder.AddEntry("a", content.Length, () => new MemoryStream(content));
        using var output = new MemoryStream();
        ArchiveWriteOptions options = format == ArchiveFormat.SevenZip
            ? new SevenZipWriteOptions { CompressionLevel = CompressionLevel.NoCompression, Solid = false }
            : new ZipWriteOptions { CompressionLevel = CompressionLevel.NoCompression };
        builder.WriteTo(output, options);
        output.Position = 0;
        using var archive = Archive.Open(output);
        Assert.That(archive.Entries[0].CompressedLength, Is.EqualTo(content.Length));
        archive.Test();
    }

    [TestCase(ArchiveFormat.SevenZip)]
    [TestCase(ArchiveFormat.Zip)]
    [TestCase(ArchiveFormat.Tar)]
    public void EmptyMultiEntryArchivesAreValid(ArchiveFormat format)
    {
        var builder = new ArchiveBuilder();
        using var output = new MemoryStream();
        ArchiveWriteOptions options = format switch
        {
            ArchiveFormat.Zip => new ZipWriteOptions(), ArchiveFormat.Tar => new TarWriteOptions(), _ => new SevenZipWriteOptions()
        };
        builder.WriteTo(output, options);
        output.Position = 0;
        using var archive = Archive.Open(output, new() { FormatHint = format });
        Assert.That(archive.Entries, Is.Empty);
        archive.Test();
    }

    [Test]
    public void EmptyDirectoriesCanBeAddedByAnEditor()
    {
        var builder = new ArchiveBuilder();
        using var source = new MemoryStream();
        builder.WriteTo(source, new ZipWriteOptions());
        source.Position = 0;
        using var archive = Archive.Open(source);
        var editor = archive.CreateEditor();
        editor.AddDirectory("empty");
        using var output = new MemoryStream();
        editor.WriteTo(output);
        output.Position = 0;
        using var updated = Archive.Open(output);
        Assert.That(updated.Entries.Single().EntryType, Is.EqualTo(ArchiveEntryType.Directory));
        Assert.That(updated.Entries.Single().FullName, Is.EqualTo("empty/"));
    }
}
