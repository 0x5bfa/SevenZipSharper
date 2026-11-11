using System.Text;

namespace SevenZipSharp.Tests.Creation;

[TestFixture]
public sealed class ArchiveStreamContractTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void SequentialSourcesWithKnownLengthsAreSupported(bool sevenZip)
    {
        var builder = new ArchiveBuilder();
        builder.AddEntry("a", 10, () => new SequentialInput("0123456789"u8.ToArray()));
        using var output = new MemoryStream();
        builder.WriteTo(output, sevenZip ? new SevenZipWriteOptions() : new ZipWriteOptions());
        output.Position = 0;
        using var archive = Archive.Open(output);
        using var decoded = new MemoryStream();
        archive.Entries[0].CopyTo(decoded);
        Assert.That(Encoding.UTF8.GetString(decoded.ToArray()), Is.EqualTo("0123456789"));
    }

    [Test]
    public void OverlongSequentialInputIsRejected()
    {
        var builder = new ArchiveBuilder();
        builder.AddEntry("a", 3, () => new SequentialInput("too long"u8.ToArray()));
        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => builder.WriteTo(output, new ZipWriteOptions()));
    }

    [Test]
    public void EmptySourceDescriptorsCannotHideAdditionalSequentialData()
    {
        var builder = new ArchiveBuilder();
        builder.AddEntry("a", 0, () => new SequentialInput("unexpected"u8.ToArray()));
        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => builder.WriteTo(output, new ZipWriteOptions()));
    }

    [Test]
    public void CleanupFailureDoesNotReplaceTheSourceReadFailure()
    {
        var expected = new IOException("read failure");
        var source = new ReadAndDisposeFailure(expected);
        var builder = new ArchiveBuilder();
        builder.AddEntry("a", 3, () => source);
        using var output = new MemoryStream();
        Assert.That(Assert.Throws<IOException>(() => builder.WriteTo(output, new ZipWriteOptions())), Is.SameAs(expected));
        Assert.That(source.DisposeCount, Is.EqualTo(1));
    }

    private sealed class SequentialInput(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Length => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin loc)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ReadAndDisposeFailure(Exception error) : MemoryStream("abc"u8.ToArray())
    {
        internal int DisposeCount { get; private set; }
        public override int Read(Span<byte> buffer)
        {
            throw error;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
            throw new IOException("secondary cleanup failure");
        }
    }
}
