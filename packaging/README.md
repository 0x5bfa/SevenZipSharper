# SevenZipSharper.Interop

Low-level interop support for SevenZipSharper. This package contains generated
7-Zip WinMD metadata, native Windows runtimes for x86, x64, and ARM64, and the
managed `CreateObject` loader.

Most consumers should install `SevenZipSharper`, which depends on this package.
Install this package directly only when building a custom 7-Zip interop layer
with CsWin32.
