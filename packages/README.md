# Pinned native dependency

`SevenZip.Interop.26.5.0.nupkg` is the existing package produced by
[7z-metadata](https://github.com/0x5bfa/7z-metadata). It is kept in this repository
as a local NuGet feed because this version is not published on NuGet.org.

- SHA-256: `14E543A1E6066AAC52439121580017AD1E2582A562CA410A2F8AE17CB2D1E468`
- Native source: 7-Zip 26.05, commit
  `b88c6bc09339de32f7d18bbab900686b244f42ba`.
- Windows runtime assets: x86, x64, ARM64.
- Metadata: `metadata/SevenZip.winmd`.
- Included licenses: `LICENSES/PackageLicenses.txt` inside the package.

The managed ABI adapters are pinned to that header revision. The archive API
does not expose those native types. Updating the package requires checking ABI
layouts and running the native integration tests.
