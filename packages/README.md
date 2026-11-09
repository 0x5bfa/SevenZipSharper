# Pinned native dependency

`SevenZip.Interop.26.5.1.nupkg` is produced by
[7z-metadata](https://github.com/0x5bfa/7z-metadata). It is kept in this repository
as a local NuGet feed because this version is not published on NuGet.org.

- SHA-256: `95B64BEEBF90296FB56060D7F7DEC098C617C235DFA18AAE546F07F370F84C2A`
- Native source: 7-Zip 26.05, commit
  `b88c6bc09339de32f7d18bbab900686b244f42ba`.
- Windows runtime assets: x86, x64, ARM64.
- Metadata: `metadata/SevenZip.winmd`.
- Metadata namespaces: `SevenZip.Interop.Archives`, `.Compression`, `.Cryptography`,
  `.Hashing`, `.Progress`, `.Streams`, and `.FileManager`.
- Included licenses: `LICENSES/PackageLicenses.txt` inside the package.

The managed ABI adapters are pinned to that header revision. The archive API
does not expose those native types. Updating the package requires checking ABI
layouts and running the native integration tests.

Version 26.5.1 reorganizes metadata namespaces without changing the native DLLs.
The 7-Zip source version remains 26.05, and the loader remains in
`SevenZip.Interop.Runtime`. The renamed metadata was compared with 26.5.0:
433 type/member contracts, including interface identifiers, inheritance,
layouts, signatures, and constants, are unchanged after namespace normalization.
