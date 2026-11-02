using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace SevenZipSharp.Interop;

// PreserveSig keeps every native HRESULT under operation-local error control.
[GeneratedComInterface, Guid("23170F69-40C1-278A-0000-000300010000")]
internal unsafe partial interface ISequentialInStream
{
    [PreserveSig] int Read(void* data, uint size, uint* processed);
}

[GeneratedComInterface, Guid("23170F69-40C1-278A-0000-000300030000")]
internal unsafe partial interface IInStream : ISequentialInStream
{
    [PreserveSig] int Seek(long offset, uint origin, ulong* position);
}

[GeneratedComInterface, Guid("23170F69-40C1-278A-0000-000300020000")]
internal unsafe partial interface ISequentialOutStream
{
    [PreserveSig] int Write(void* data, uint size, uint* processed);
}

[GeneratedComInterface, Guid("23170F69-40C1-278A-0000-000300040000")]
internal unsafe partial interface IOutStream : ISequentialOutStream
{
    [PreserveSig] int Seek(long offset, uint origin, ulong* position);
    [PreserveSig] int SetSize(ulong size);
}

[GeneratedComInterface, Guid("23170F69-40C1-278A-0000-000000050000")]
internal unsafe partial interface INativeProgress
{
    [PreserveSig] int SetTotal(ulong total);
    [PreserveSig] int SetCompleted(ulong* completed);
}

[GeneratedComInterface, Guid("23170F69-40C1-278A-0000-000600100000")]
internal unsafe partial interface IArchiveOpenCallback
{
    [PreserveSig] int SetTotal(ulong* files, ulong* bytes);
    [PreserveSig] int SetCompleted(ulong* files, ulong* bytes);
}

[GeneratedComInterface, Guid("23170F69-40C1-278A-0000-000600300000")]
internal unsafe partial interface IArchiveOpenVolumeCallback
{
    [PreserveSig] int GetProperty(uint id, NativeVariant* value);
    [PreserveSig] int GetStream(char* name, nint* stream);
}

[GeneratedComInterface, Guid("23170F69-40C1-278A-0000-000500100000")]
internal unsafe partial interface ICryptoGetTextPassword
{
    [PreserveSig] int CryptoGetTextPassword(nint* password);
}

[GeneratedComInterface, Guid("23170F69-40C1-278A-0000-000500110000")]
internal unsafe partial interface ICryptoGetTextPassword2
{
    [PreserveSig] int CryptoGetTextPassword2(int* defined, nint* password);
}

[GeneratedComInterface, Guid("23170F69-40C1-278A-0000-000600200000")]
internal unsafe partial interface IArchiveExtractCallback : INativeProgress
{
    [PreserveSig] int GetStream(uint index, nint* stream, int askMode);
    [PreserveSig] int PrepareOperation(int askMode);
    [PreserveSig] int SetOperationResult(int result);
}

[GeneratedComInterface, Guid("23170F69-40C1-278A-0000-000600220000")]
internal partial interface IArchiveExtractCallbackMessage2
{
    [PreserveSig] int ReportExtractResult(uint indexType, uint index, int result);
}

[GeneratedComInterface, Guid("23170F69-40C1-278A-0000-000600800000")]
internal unsafe partial interface IArchiveUpdateCallback : INativeProgress
{
    [PreserveSig] int GetUpdateItemInfo(uint index, int* newData, int* newProperties, uint* archiveIndex);
    [PreserveSig] int GetProperty(uint index, uint id, NativeVariant* value);
    [PreserveSig] int GetStream(uint index, nint* stream);
    [PreserveSig] int SetOperationResult(int result);
}
