using System.Runtime.InteropServices;
using SevenZip.Interop.Runtime;
using SevenZipSharp.Interop;

namespace SevenZipSharp.Native;

internal sealed record NativeFormat(ArchiveFormat Format, string Name, Guid ClassId, bool CanWrite, byte[] Signature, long SignatureOffset);

internal sealed unsafe class NativeRuntime
{
    private static readonly Lazy<NativeRuntime> instance = new(() => new());
    private readonly nint module;
    internal static NativeRuntime Instance => instance.Value;
    internal IReadOnlyList<NativeFormat> Formats { get; }

    private NativeRuntime()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The bundled 7-Zip engine requires Windows.");
        try
        {
            _ = SevenZipLibrary.Instance;
            // The package loader owns the process-lifetime module; this reference
            // provides read-only handler exports that its public API does not expose.
            module = NativeLibrary.Load("7z.dll", typeof(SevenZipLibrary).Assembly, DllImportSearchPath.SafeDirectories);
            Formats = ReadFormats();
        }
        catch (Exception error) when (error is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            throw new ArchiveException(ArchiveErrorCode.NativeLibraryUnavailable, "Could not load the bundled 7-Zip engine.", error);
        }
    }

    internal SevenZipObject Create(NativeFormat format, Guid interfaceId)
    {
        try { return SevenZipLibrary.Instance.CreateObject(format.ClassId, interfaceId); }
        catch (COMException error) { NativeMethods.Check(error.HResult); throw; }
    }

    internal SevenZipObject? TryCreateReader(NativeFormat format)
    {
        try { return SevenZipLibrary.Instance.CreateObject(format.ClassId, NativeMethods.InArchiveId); }
        catch (COMException error) when (error.HResult == unchecked((int)0x80040111)) { return null; }
        catch (COMException error) { NativeMethods.Check(error.HResult); throw; }
    }

    internal NativeFormat Writable(ArchiveFormat format) => Formats.FirstOrDefault(f => f.Format == format && f.CanWrite)
        ?? throw new NotSupportedException($"The native engine cannot write {format}.");

    private IReadOnlyList<NativeFormat> ReadFormats()
    {
        var countFunction = (delegate* unmanaged[Stdcall]<uint*, int>)NativeLibrary.GetExport(module, "GetNumberOfFormats");
        var propertyFunction = (delegate* unmanaged[Stdcall]<uint, uint, NativeVariant*, int>)NativeLibrary.GetExport(module, "GetHandlerProperty2");
        uint count = 0;
        NativeMethods.Check(countFunction(&count));
        if (count > 10_000) throw new InvalidDataException("The native format catalog is too large.");
        var formats = new List<NativeFormat>();
        for (uint index = 0; index < count; index++)
        {
            NativeVariant value = default;
            try
            {
                NativeMethods.Check(propertyFunction(index, 0, &value));
                string name = value.String ?? "";
                value.Clear();
                NativeMethods.Check(propertyFunction(index, 1, &value));
                if (value.Type != 8 || value.Value.Pointer == 0 || Marshal.ReadInt32(value.Value.Pointer, -4) != 16)
                    continue;
                var id = new Guid(new ReadOnlySpan<byte>((void*)value.Value.Pointer, 16));
                value.Clear();
                NativeMethods.Check(propertyFunction(index, 4, &value));
                bool canWrite = value.Boolean ?? false;
                value.Clear();
                NativeMethods.Check(propertyFunction(index, 6, &value));
                byte[] signature = value.Type == 8 && value.Value.Pointer != 0
                    ? new ReadOnlySpan<byte>((void*)value.Value.Pointer, Marshal.ReadInt32(value.Value.Pointer, -4)).ToArray() : [];
                value.Clear();
                NativeMethods.Check(propertyFunction(index, 8, &value));
                long offset = value.Length ?? 0;
                ArchiveFormat format = name.ToLowerInvariant() switch
                {
                    "7z" => ArchiveFormat.SevenZip, "zip" => ArchiveFormat.Zip, "tar" => ArchiveFormat.Tar,
                    "gzip" => ArchiveFormat.GZip, "bzip2" => ArchiveFormat.BZip2, "xz" => ArchiveFormat.Xz,
                    "rar" => ArchiveFormat.Rar, "rar5" => ArchiveFormat.Rar5, "cab" => ArchiveFormat.Cab,
                    "iso" => ArchiveFormat.Iso, "wim" => ArchiveFormat.Wim, _ => ArchiveFormat.Other
                };
                formats.Add(new(format, name, id, canWrite, signature, offset));
            }
            finally { value.Clear(); }
        }
        return formats.AsReadOnly();
    }
}
