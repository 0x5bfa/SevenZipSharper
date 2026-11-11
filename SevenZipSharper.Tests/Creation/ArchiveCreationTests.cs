using System.IO.Compression;
using System.Text;
using SevenZipSharper.Tests.Reading;

namespace SevenZipSharper.Tests.Creation;

[TestFixture]
public sealed class ArchiveCreationTests
{
    private static IEnumerable<TestCaseData> Formats()
    {
        yield return new TestCaseData(new SevenZipWriteOptions(), ArchiveFormat.SevenZip);
        yield return new TestCaseData(new ZipWriteOptions(), ArchiveFormat.Zip);
        yield return new TestCaseData(new TarWriteOptions(), ArchiveFormat.Tar);
        yield return new TestCaseData(new GZipWriteOptions(), ArchiveFormat.GZip);
        yield return new TestCaseData(new XzWriteOptions(), ArchiveFormat.Xz);
        yield return new TestCaseData(new BZip2WriteOptions(), ArchiveFormat.BZip2);
    }

    [TestCaseSource(nameof(Formats))]
    public void NativeWriterRoundTrips(ArchiveWriteOptions options, ArchiveFormat format)
    {
        byte[] bytes = Encoding.UTF8.GetBytes("native writer content");
        var builder = new ArchiveBuilder();
        builder.AddEntry("readme.txt", bytes.Length, () => new MemoryStream(bytes));
        using var output = new MemoryStream();
        builder.WriteTo(output, options);
        Assert.That(output.CanWrite, Is.True);
        output.Position = 0;
        using var archive = Archive.Open(output, new() { FormatHint = format }, true);
        Assert.That(archive.Format, Is.EqualTo(format));
        using var decoded = new MemoryStream();
        archive.Entries.Single().CopyTo(decoded);
        Assert.That(decoded.ToArray(), Is.EqualTo(bytes));
        archive.Test();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SolidSelectionUsesOneOperationAndPreservesSelectedContents(bool solid)
    {
        var builder = new ArchiveBuilder();
        for (int i = 0; i < 6; i++)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(new string((char)('a' + i), 3000));
            builder.AddEntry($"file{i}.txt", bytes.Length, () => new MemoryStream(bytes));
        }
        using var encoded = new MemoryStream();
        builder.WriteTo(encoded, new SevenZipWriteOptions { Solid = solid });
        encoded.Position = 0;
        using var archive = Archive.Open(encoded);
        var outputs = new Dictionary<int, MemoryStream>();
        archive.Extract([archive.Entries[2], archive.Entries[5]], entry =>
        {
            var stream = new MemoryStream();
            outputs.Add(entry.Index, stream);
            return stream;
        });
        Assert.Multiple(() =>
        {
            Assert.That(archive.Info.IsSolid, Is.EqualTo(solid));
            Assert.That(outputs.Keys, Is.EquivalentTo(new[] { 2, 5 }));
            Assert.That(outputs[2].ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes(new string('c', 3000))));
            Assert.That(outputs[5].ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes(new string('f', 3000))));
            Assert.That(outputs.Values.All(stream => !stream.CanWrite), Is.True);
            Assert.That(archive.Session.ExtractionInvocations, Is.EqualTo(1));
        });
    }

    [Test]
    public void ZipOutputCanBeReadByTheBcl()
    {
        var builder = new ArchiveBuilder();
        builder.AddDirectory("folder");
        builder.AddEntry("folder/a.txt", 3, () => new MemoryStream("abc"u8.ToArray()));
        using var output = new MemoryStream();
        builder.WriteTo(output, new ZipWriteOptions());
        output.Position = 0;
        using var zip = new ZipArchive(output, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("folder/a.txt")!.Open());
        Assert.That(reader.ReadToEnd(), Is.EqualTo("abc"));
    }

    [Test]
    public void SourcesAreDeferredOwnedAndBuildersAreSingleUse()
    {
        int calls = 0;
        var source = new TrackingStream("abc"u8.ToArray());
        var builder = new ArchiveBuilder();
        builder.AddEntry("a", 3, () => { calls++; return source; });
        Assert.That(calls, Is.Zero);
        using var output = new MemoryStream();
        builder.WriteTo(output, new ZipWriteOptions());
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(source.DisposeCount, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => builder.AddDirectory("later"));
        Assert.Throws<InvalidOperationException>(() => builder.WriteTo(output, new ZipWriteOptions()));
    }

    [Test]
    public void CallbackFailurePreservesOriginalExceptionAndClosesSources()
    {
        var expected = new IOException("factory failure identity");
        var builder = new ArchiveBuilder();
        builder.AddEntry("a", 3, () => throw expected);
        using var output = new MemoryStream();
        Assert.That(Assert.Throws<IOException>(() => builder.WriteTo(output, new ZipWriteOptions())), Is.SameAs(expected));
        Assert.That(output.CanWrite, Is.True);
        Assert.Throws<InvalidOperationException>(() => builder.WriteTo(output, new ZipWriteOptions()));
    }

    [Test]
    public void SourceLengthChangesAreRejected()
    {
        var source = new TrackingStream("changed"u8.ToArray());
        var builder = new ArchiveBuilder();
        builder.AddEntry("a", 3, () => source);
        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => builder.WriteTo(output, new ZipWriteOptions()));
        Assert.That(source.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void SinglePayloadFormatsRejectMultipleEntriesBeforeWriting()
    {
        var builder = new ArchiveBuilder();
        builder.AddEntry("a", 0, () => new MemoryStream());
        builder.AddEntry("b", 0, () => new MemoryStream());
        using var output = new MemoryStream();
        Assert.Throws<ArgumentException>(() => builder.WriteTo(output, new GZipWriteOptions()));
        Assert.That(output.Length, Is.Zero);
    }

    [Test]
    public void EncryptedHeadersRequirePasswordAndCorrectPasswordOpensContent()
    {
        var builder = new ArchiveBuilder();
        builder.AddEntry("secret.txt", 6, () => new MemoryStream("secret"u8.ToArray()));
        using var output = new MemoryStream();
        builder.WriteTo(output, new SevenZipWriteOptions { Password = "test-password", EncryptHeaders = true });
        output.Position = 0;
        var required = Assert.Throws<ArchiveException>(() => Archive.Open(output, leaveOpen: true));
        Assert.That(required!.ErrorCode, Is.EqualTo(ArchiveErrorCode.PasswordRequired));
        output.Position = 0;
        using var archive = Archive.Open(output, new() { Password = "test-password" }, true);
        using var decoded = new MemoryStream();
        archive.Entries[0].CopyTo(decoded);
        Assert.That(Encoding.UTF8.GetString(decoded.ToArray()), Is.EqualTo("secret"));
    }

    [Test]
    public void WrongZipPasswordHasAnActionableError()
    {
        var builder = new ArchiveBuilder();
        builder.AddEntry("secret", 6, () => new MemoryStream("secret"u8.ToArray()));
        using var output = new MemoryStream();
        builder.WriteTo(output, new ZipWriteOptions { Password = "correct" });
        output.Position = 0;
        using var archive = Archive.Open(output, new() { Password = "wrong" });
        using var decoded = new MemoryStream();
        var error = Assert.Throws<ArchiveException>(() => archive.Entries[0].CopyTo(decoded));
        Assert.That(error!.ErrorCode, Is.EqualTo(ArchiveErrorCode.WrongPassword));
    }

    [Test]
    public async Task AsyncWriteFinalizesBeforeTaskCompletion()
    {
        var builder = new ArchiveBuilder();
        builder.AddEntry("a", 3, () => new MemoryStream("abc"u8.ToArray()));
        using var output = new MemoryStream();
        await builder.WriteToAsync(output, new ZipWriteOptions());
        output.Position = 0;
        using var archive = Archive.Open(output);
        Assert.That(archive.Entries.Single().Length, Is.EqualTo(3));
    }

    private sealed class TrackingStream(byte[] content) : MemoryStream(content)
    {
        internal int DisposeCount { get; private set; }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
        }
    }
}
