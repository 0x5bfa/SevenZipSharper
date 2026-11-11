namespace SevenZipSharp.Tests.Native;

[TestFixture]
public sealed class NativeFixtureTests
{
    private static string Fixture(string name)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", name);
    }

    [TestCase("7z_LZMA2.7z", ArchiveFormat.SevenZip)]
    [TestCase("rar4.rar", ArchiveFormat.Rar)]
    [TestCase("rar5.rar", ArchiveFormat.Rar5)]
    [TestCase("wim.wim", ArchiveFormat.Wim)]
    [TestCase("arj.arj", ArchiveFormat.Other)]
    public void IndependentlyCreatedFixturesCanBeReadAndVerified(string name, ArchiveFormat format)
    {
        using var archive = ArchiveFile.OpenRead(Fixture(name));
        Assert.That(archive.Format, Is.EqualTo(format));
        Assert.That(archive.Entries, Is.Not.Empty);
        foreach (var entry in archive.Entries.Where(entry => entry.EntryType == ArchiveEntryType.File))
        {
            using var output = new MemoryStream();
            entry.CopyTo(output);
            if (entry.Length is { } length)
            {
                Assert.That(output.Length, Is.EqualTo(length));
            }
        }
        archive.Test();
    }

    [Test]
    public void NamedVolumeResolutionReadsARealMultipartRar()
    {
        using var archive = ArchiveFile.OpenRead(Fixture("multivolume.part0001.rar"));
        using var output = new MemoryStream();
        archive.Entries.Single(entry => entry.EntryType == ArchiveEntryType.File).CopyTo(output);
        Assert.That(System.Text.Encoding.UTF8.GetString(output.ToArray()), Does.StartWith("Lorem ipsum dolor sit amet"));
        archive.Test();
    }

    [Test]
    public void MissingVolumeIsActionableAndDoesNotLeakTheOwnedSource()
    {
        using var temp = new TestDirectory();
        string path = temp.File("multivolume.part0001.rar");
        File.Copy(Fixture("multivolume.part0001.rar"), path);
        ArchiveException? missing = null;
        try
        {
            using var archive = ArchiveFile.OpenRead(path);
            using var output = new MemoryStream();
            archive.Entries.Single(entry => entry.EntryType == ArchiveEntryType.File).CopyTo(output);
        }
        catch (ArchiveException error) { missing = error; }
        Assert.That(missing?.ErrorCode, Is.EqualTo(ArchiveErrorCode.MissingVolume));
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }
}
