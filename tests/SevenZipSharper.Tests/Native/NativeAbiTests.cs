using System.Runtime.InteropServices;
using Windows.Win32.System.Com.StructuredStorage;

namespace SevenZipSharper.Tests.Native;

[TestFixture]
public sealed class NativeAbiTests
{
    [Test]
    public void GeneratedPropVariantLayoutMatchesWindowsAbi()
    {
        TestContext.Progress.WriteLine($"Native integration process architecture: {RuntimeInformation.ProcessArchitecture}");
        Assert.That(Marshal.SizeOf<PROPVARIANT>(), Is.EqualTo(IntPtr.Size == 8 ? 24 : 16));
    }
}
