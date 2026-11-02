using System.IO.Compression;
using SevenZipSharp.Native;

namespace SevenZipSharp;

/// <summary>The base of the library-provided, format-specific write options.</summary>
public abstract record ArchiveWriteOptions
{
    private protected ArchiveWriteOptions() { }
    internal abstract WriteConfiguration Capture();
}

/// <summary>Chooses a 7z compression method.</summary>
public enum SevenZipCompressionMethod
{
    /// <summary>LZMA2 compression.</summary>
    Lzma2,
    /// <summary>LZMA compression.</summary>
    Lzma,
    /// <summary>PPMd compression.</summary>
    Ppmd,
    /// <summary>Uncompressed storage.</summary>
    Copy
}

/// <summary>Chooses a ZIP compression method.</summary>
public enum ZipCompressionMethod
{
    /// <summary>Deflate compression.</summary>
    Deflate,
    /// <summary>Uncompressed storage.</summary>
    Stored
}

/// <summary>Chooses an explicit ZIP encryption method when a password is supplied.</summary>
public enum ZipEncryptionMethod
{
    /// <summary>AES-256 encryption.</summary>
    Aes256,
    /// <summary>Legacy ZipCrypto encryption.</summary>
    ZipCrypto
}

/// <summary>Controls writing a 7z archive.</summary>
public sealed record SevenZipWriteOptions : ArchiveWriteOptions
{
    /// <summary>Gets the compression preset.</summary>
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.Optimal;
    /// <summary>Gets the compression method.</summary>
    public SevenZipCompressionMethod Method { get; init; } = SevenZipCompressionMethod.Lzma2;
    /// <summary>Gets whether entries may share solid compression blocks.</summary>
    public bool Solid { get; init; } = true;
    /// <summary>Gets an optional content password.</summary>
    public string? Password { get; init; }
    /// <summary>Gets whether metadata headers are encrypted; requires a password.</summary>
    public bool EncryptHeaders { get; init; }
    /// <summary>Gets an optional native thread count.</summary>
    public int? ThreadCount { get; init; }

    /// <summary>Returns a description without exposing the password.</summary>
    public override string ToString() => $"{nameof(SevenZipWriteOptions)} {{ CompressionLevel = {CompressionLevel}, Method = {Method}, Solid = {Solid}, EncryptHeaders = {EncryptHeaders}, Password = <redacted> }}";

    internal override WriteConfiguration Capture()
    {
        if (!Enum.IsDefined(Method)) throw new ArgumentOutOfRangeException(nameof(Method));
        if (EncryptHeaders && Password is null) throw new ArgumentException("Header encryption requires a password.");
        if (ThreadCount is <= 0) throw new ArgumentOutOfRangeException(nameof(ThreadCount));
        string method = Method switch
        {
            SevenZipCompressionMethod.Lzma2 => "LZMA2", SevenZipCompressionMethod.Lzma => "LZMA",
            SevenZipCompressionMethod.Ppmd => "PPMd", _ => "Copy"
        };
        if (CompressionLevel == CompressionLevel.NoCompression) method = "Copy";
        var properties = new List<(string, object)> { ("x", WriteConfiguration.Level(CompressionLevel)), ("0", method), ("s", Solid), ("he", EncryptHeaders) };
        if (ThreadCount is { } threads) properties.Add(("mt", checked((uint)threads)));
        return new(ArchiveFormat.SevenZip, Password, properties);
    }
}

/// <summary>Controls writing a ZIP archive.</summary>
public sealed record ZipWriteOptions : ArchiveWriteOptions
{
    /// <summary>Gets the compression preset.</summary>
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.Optimal;
    /// <summary>Gets the compression method.</summary>
    public ZipCompressionMethod Method { get; init; } = ZipCompressionMethod.Deflate;
    /// <summary>Gets an optional content password.</summary>
    public string? Password { get; init; }
    /// <summary>Gets the encryption method used when a password is supplied.</summary>
    public ZipEncryptionMethod EncryptionMethod { get; init; } = ZipEncryptionMethod.Aes256;

    /// <summary>Returns a description without exposing the password.</summary>
    public override string ToString() => $"{nameof(ZipWriteOptions)} {{ CompressionLevel = {CompressionLevel}, Method = {Method}, EncryptionMethod = {EncryptionMethod}, Password = <redacted> }}";

    internal override WriteConfiguration Capture()
    {
        if (!Enum.IsDefined(Method)) throw new ArgumentOutOfRangeException(nameof(Method));
        if (!Enum.IsDefined(EncryptionMethod)) throw new ArgumentOutOfRangeException(nameof(EncryptionMethod));
        return new(ArchiveFormat.Zip, Password,
        [
            ("x", WriteConfiguration.Level(CompressionLevel)),
            ("m", Method == ZipCompressionMethod.Stored || CompressionLevel == CompressionLevel.NoCompression ? "Copy" : "Deflate"),
            ("em", EncryptionMethod == ZipEncryptionMethod.Aes256 ? "AES256" : "ZipCrypto")
        ]);
    }
}

/// <summary>Controls writing an uncompressed TAR archive.</summary>
public sealed record TarWriteOptions : ArchiveWriteOptions
{
    internal override WriteConfiguration Capture() => new(ArchiveFormat.Tar, null, []);
}

/// <summary>Controls writing a single GZip payload.</summary>
public sealed record GZipWriteOptions : ArchiveWriteOptions
{
    /// <summary>Gets the compression preset.</summary>
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.Optimal;
    internal override WriteConfiguration Capture() => new(ArchiveFormat.GZip, null, [("x", WriteConfiguration.Level(CompressionLevel))]);
}

/// <summary>Controls writing a single XZ payload.</summary>
public sealed record XzWriteOptions : ArchiveWriteOptions
{
    /// <summary>Gets the compression preset.</summary>
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.Optimal;
    internal override WriteConfiguration Capture() => new(ArchiveFormat.Xz, null, [("x", WriteConfiguration.Level(CompressionLevel))]);
}

/// <summary>Controls writing a single BZip2 payload.</summary>
public sealed record BZip2WriteOptions : ArchiveWriteOptions
{
    /// <summary>Gets the compression preset.</summary>
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.Optimal;
    internal override WriteConfiguration Capture() => new(ArchiveFormat.BZip2, null, [("x", WriteConfiguration.Level(CompressionLevel))]);
}
