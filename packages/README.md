# Pinned native dependency

`SevenZipSharper.Interop.26.6.0-local.4.nupkg` is produced by the in-repository
metadata project. It is kept in this repository
as a local NuGet feed because this version is not published on NuGet.org.

- SHA-256: `B6E69578630BE3E3E3AF54E0549BAAB13169422452BEFE0BD0B50133AE0D06A4`
- Native source: 7-Zip 26.06, commit
  `d3296e39e1f1f92c7b41c0c3848e65640428d42d`.
- Windows runtime assets: x86, x64, ARM64.
- Metadata: `metadata/SevenZip.winmd`.
- Metadata namespaces: `SevenZip.Interop.Archives`, `.Compression`, `.Cryptography`,
  `.Hashing`, `.Progress`, `.Streams`, and `.FileManager`.
- Included licenses: `LICENSES/PackageLicenses.txt` inside the package.

The managed ABI adapters are pinned to that header revision. The archive API
does not expose those native types. Updating the package requires checking ABI
layouts and running the native integration tests.

The native source is pinned to 7-Zip 26.06. Metadata namespaces follow the
7-Zip ABI under `SevenZip.Interop`; the managed loader is in
`SevenZipSharper.Interop.Runtime`.
