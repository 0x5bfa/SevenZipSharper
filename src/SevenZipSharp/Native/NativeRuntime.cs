using System.Runtime.InteropServices;
using NativeInArchive = SevenZip.Interop.Archives.IInArchive;
using SevenZip.Interop.Archives;
using SevenZip.Interop.Runtime;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;

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
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The bundled 7-Zip engine requires Windows.");
        }

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

    internal SevenZipObject Create(NativeFormat format, Guid interfaceId) =>
        SevenZipLibrary.Instance.CreateObject(format.ClassId, interfaceId);

    internal SevenZipObject? TryCreateReader(NativeFormat format)
    {
        try { return SevenZipLibrary.Instance.CreateObject(format.ClassId, typeof(NativeInArchive).GUID); }
        catch (COMException error) when (error.HResult == HRESULT.CLASS_E_CLASSNOTAVAILABLE.Value) { return null; }
    }

    internal NativeFormat Writable(ArchiveFormat format)
    {
        return Formats.FirstOrDefault(f => f.Format == format && f.CanWrite)
        ?? throw new NotSupportedException($"The native engine cannot write {format}.");
    }

    private IReadOnlyList<NativeFormat> ReadFormats()
    {
        // Use the WinMD function type for the count export.
        var countFunction = (Func_GetNumberOfFormats)(delegate* unmanaged[Stdcall]<uint*, HRESULT>)NativeLibrary.GetExport(module, "GetNumberOfFormats");
        // Keep the property export typed with the projected PROPVARIANT layout.
        delegate* unmanaged[Stdcall]<uint, uint, PROPVARIANT*, HRESULT> propertyFunction =
            (delegate* unmanaged[Stdcall]<uint, uint, PROPVARIANT*, HRESULT>)NativeLibrary.GetExport(module, "GetHandlerProperty2");
        uint count = 0;
        countFunction.Value(&count).ThrowOnFailure();
        if (count > 10_000)
        {
            throw new InvalidDataException("The native format catalog is too large.");
        }

        var formats = new List<NativeFormat>();
        for (uint index = 0; index < count; index++)
        {
            PROPVARIANT value = default;
            try
            {
                propertyFunction(index, 0, &value).ThrowOnFailure();
                string name = InteropHelpers.GetString(ref value) ?? "";
                InteropHelpers.Clear(ref value);
                propertyFunction(index, 1, &value).ThrowOnFailure();
                if (value.vt != VARENUM.VT_BSTR)
                {
                    continue;
                }

                ReadOnlySpan<byte> classId = MemoryMarshal.AsBytes((ReadOnlySpan<char>)value.bstrVal);
                if (classId.Length != 16)
                {
                    continue;
                }

                var id = new Guid(classId);
                InteropHelpers.Clear(ref value);
                propertyFunction(index, 4, &value).ThrowOnFailure();
                bool canWrite = InteropHelpers.GetBoolean(ref value) ?? false;
                InteropHelpers.Clear(ref value);
                propertyFunction(index, 6, &value).ThrowOnFailure();
                byte[] signature = value.vt == VARENUM.VT_BSTR && value.bstrVal.Value != null
                    ? MemoryMarshal.AsBytes((ReadOnlySpan<char>)value.bstrVal).ToArray() : [];
                InteropHelpers.Clear(ref value);
                propertyFunction(index, 8, &value).ThrowOnFailure();
                long offset = InteropHelpers.GetLength(ref value) ?? 0;
                ArchiveFormat format = name.ToLowerInvariant() switch
                {
                    "7z" => ArchiveFormat.SevenZip, "zip" => ArchiveFormat.Zip, "tar" => ArchiveFormat.Tar,
                    "gzip" => ArchiveFormat.GZip, "bzip2" => ArchiveFormat.BZip2, "xz" => ArchiveFormat.Xz,
                    "rar" => ArchiveFormat.Rar, "rar5" => ArchiveFormat.Rar5, "cab" => ArchiveFormat.Cab,
                    "iso" => ArchiveFormat.Iso, "wim" => ArchiveFormat.Wim, _ => ArchiveFormat.Other
                };
                formats.Add(new(format, name, id, canWrite, signature, offset));
            }
            finally { InteropHelpers.Clear(ref value); }
        }
        return formats.AsReadOnly();
    }
}
