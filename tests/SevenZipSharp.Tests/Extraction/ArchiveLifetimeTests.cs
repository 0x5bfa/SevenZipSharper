using SevenZipSharp.Tests.Reading;

namespace SevenZipSharp.Tests.Extraction;

[TestFixture]
public sealed class ArchiveLifetimeTests
{
    [Test]
    public async Task ActiveOperationsRejectConcurrentUseAndSynchronousDisposal()
    {
        using var source = new MemoryStream(ArchiveReadingTests.CreateZip(("a", new string('x', 1000))));
        using var archive = Archive.Open(source);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var output = new GatedOutput(entered, release);
        Task operation = archive.Entries[0].CopyToAsync(output);
        try
        {
            Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True);
            Assert.Throws<InvalidOperationException>(() => archive.Test());
            Assert.Throws<InvalidOperationException>(() => archive.Dispose());
            Assert.That(archive.Entries[0].Length, Is.EqualTo(1000));
        }
        finally { release.Set(); }
        await operation;
        archive.Test();
    }

    [Test]
    public async Task AsyncDisposalWaitsForNativeWorkAndClosesOwnedInput()
    {
        var source = new MemoryStream(ArchiveReadingTests.CreateZip(("a", new string('x', 1000))));
        var archive = Archive.Open(source);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var output = new GatedOutput(entered, release);
        Task operation = archive.Entries[0].CopyToAsync(output);
        try
        {
            Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True);
            Task disposal = archive.DisposeAsync().AsTask();
            Assert.That(disposal.IsCompleted, Is.False);
            Assert.Throws<ObjectDisposedException>(() => archive.Test());
            release.Set();
            try { await operation; } catch (OperationCanceledException) { }
            await disposal;
            Assert.That(source.CanRead, Is.False);
        }
        finally { release.Set(); await archive.DisposeAsync(); }
    }

    [Test]
    public void CallbackReentryAndDisposalAreRejectedWithoutDeadlocking()
    {
        using var source = new MemoryStream(ArchiveReadingTests.CreateZip(("a", "abc")));
        using var archive = Archive.Open(source);
        var progress = new InlineProgress(_ =>
        {
            Assert.Throws<InvalidOperationException>(() => archive.Test());
            Assert.Throws<InvalidOperationException>(() => archive.DisposeAsync());
        });
        using var output = new MemoryStream();
        archive.Entries[0].CopyTo(output, progress);
        Assert.That(output.Length, Is.EqualTo(3));
    }

    [Test]
    public void OutputFailureIdentitySurvivesCleanupAndDoesNotCloseBorrowedOutput()
    {
        using var source = new MemoryStream(ArchiveReadingTests.CreateZip(("a", "abc")));
        using var archive = Archive.Open(source);
        var expected = new IOException("destination failed");
        using var output = new FailingOutput(expected);
        Assert.That(Assert.Throws<IOException>(() => archive.Entries[0].CopyTo(output)), Is.SameAs(expected));
        Assert.That(output.CanWrite, Is.True);
    }

    [Test]
    public void BatchOutputsAreOwnedAndSkippedEntriesDoNotOpenStreams()
    {
        using var source = new MemoryStream(ArchiveReadingTests.CreateZip(("a", "a"), ("b", "b")));
        using var archive = Archive.Open(source);
        var outputs = new List<MemoryStream>();
        archive.Extract(archive.Entries, entry =>
        {
            if (entry.FullName == "b") return null;
            var output = new MemoryStream();
            outputs.Add(output);
            return output;
        });
        Assert.That(outputs, Has.Count.EqualTo(1));
        Assert.That(outputs[0].CanWrite, Is.False);
        Assert.That(outputs[0].ToArray(), Is.EqualTo("a"u8.ToArray()));
    }

    [Test]
    public void CancellationBeforeExecutionLeavesTheSessionUsable()
    {
        using var source = new MemoryStream(ArchiveReadingTests.CreateZip(("a", "abc")));
        using var archive = Archive.Open(source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var output = new MemoryStream();
        var error = Assert.Throws<OperationCanceledException>(() => archive.Entries[0].CopyTo(output, cancellationToken: cancellation.Token));
        Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
        archive.Test();
    }

    private sealed class InlineProgress(Action<ArchiveProgress> report) : IProgress<ArchiveProgress>
    {
        public void Report(ArchiveProgress value) => report(value);
    }

    private sealed class GatedOutput(ManualResetEventSlim entered, ManualResetEventSlim release) : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("The test did not release the destination.");
            base.Write(buffer);
        }
    }

    private sealed class FailingOutput(Exception error) : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer) => throw error;
    }
}
