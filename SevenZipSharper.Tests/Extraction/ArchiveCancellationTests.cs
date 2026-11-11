using SevenZipSharper.Tests.Reading;

namespace SevenZipSharper.Tests.Extraction;

[TestFixture]
public sealed class ArchiveCancellationTests
{
    [Test]
    public async Task CancellationDuringNativeExecutionCarriesTheCallersToken()
    {
        using var source = new MemoryStream(ArchiveReadingTests.CreateZip(("a", new string('x', 2000))));
        using var archive = Archive.Open(source);
        using var cancellation = new CancellationTokenSource();
        using var output = new MemoryStream();
        var progress = new InlineProgress(_ => cancellation.Cancel());
        try
        {
            await archive.Entries[0].CopyToAsync(output, progress, cancellation.Token);
            Assert.Fail("Cancellation should have been observed.");
        }
        catch (OperationCanceledException error)
        {
            Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
        }
        Assert.That(output.CanWrite, Is.True);
    }

    [Test]
    public void SequentialDestinationsAreSupportedForEntryExtraction()
    {
        using var source = new MemoryStream(ArchiveReadingTests.CreateZip(("a", "abc")));
        using var archive = Archive.Open(source);
        using var output = new SequentialOutput();
        archive.Entries[0].CopyTo(output);
        Assert.That(output.ToArray(), Is.EqualTo("abc"u8.ToArray()));
    }

    private sealed class InlineProgress(Action<ArchiveProgress> report) : IProgress<ArchiveProgress>
    {
        public void Report(ArchiveProgress value)
        {
            report(value);
        }
    }

    private sealed class SequentialOutput : Stream
    {
        private readonly MemoryStream buffer = new();
        internal byte[] ToArray()
        {
            return buffer.ToArray();
        }

        public override bool CanRead => false;
        public override bool CanWrite => buffer.CanWrite;
        public override bool CanSeek => false;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Length => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin loc)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override int Read(byte[] value, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] value, int offset, int count)
        {
            buffer.Write(value, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> value)
        {
            buffer.Write(value);
        }

        public override void Flush()
        {
            buffer.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                buffer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
