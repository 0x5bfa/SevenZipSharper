using System.IO.Compression;

namespace SevenZipSharper.Native;

internal sealed record WriteConfiguration(ArchiveFormat Format, string? Password, IReadOnlyList<(string Name, object Value)> Properties)
{
    public override string ToString()
    {
        return $"{nameof(WriteConfiguration)} {{ Format = {Format}, Password = <redacted> }}";
    }

    internal static uint Level(CompressionLevel level)
    {
        return level switch
        {
            CompressionLevel.NoCompression => 0,
            CompressionLevel.Fastest => 1,
            CompressionLevel.Optimal => 5,
            CompressionLevel.SmallestSize => 9,
            _ => throw new ArgumentOutOfRangeException(nameof(level))
        };
    }
}
