using System.Runtime.InteropServices;
using SevenZipSharp.Interop;

namespace SevenZipSharp.Tests.Native;

[TestFixture]
public sealed class NativeAbiTests
{
    [Test]
    public void VariantLayoutMatchesThePinnedWindowsAbi()
    {
        TestContext.Progress.WriteLine($"Native integration process architecture: {RuntimeInformation.ProcessArchitecture}");
        Assert.That(Marshal.SizeOf<NativeVariant>(), Is.EqualTo(IntPtr.Size == 8 ? 24 : 16));
        Assert.That(Marshal.OffsetOf<NativeVariant>(nameof(NativeVariant.Value)).ToInt32(), Is.EqualTo(8));
    }
}
