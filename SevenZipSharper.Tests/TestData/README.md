# Native interoperability fixtures

These binary fixtures predate the new API and were copied from this repository
at commit `272df303e836f171ea9a0a07b93c1a4572c3732a`, under
`SevenZip.Tests/TestData/`. They are deliberately independent of the new
`ArchiveBuilder` implementation.

Included cases: LZMA2 7z, RAR 4, RAR 5, multipart RAR, WIM, and ARJ.
Tests generate ZIP and TAR inputs independently using the .NET BCL as well.
