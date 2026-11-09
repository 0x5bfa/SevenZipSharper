# Implementation status

Updated: 2026-11-02. The repository now contains a working first implementation,
rather than only the scaffold. Public namespace and assembly: `SevenZipSharp`.

## Implemented

| Area | Current behavior |
| --- | --- |
| Read sessions | `Archive.Open/OpenAsync`, `ArchiveFile.OpenRead/OpenReadAsync`, eager immutable metadata, exact name lookup, duplicate entry identity |
| Content | Borrowed-stream `CopyTo/CopyToAsync`, one-call batch extraction for solid and non-solid selections, explicit integrity testing |
| Creation | Single-use `ArchiveBuilder`, deferred factories, directories, exact input lengths, regular source files |
| Write formats | 7z, ZIP, TAR, GZip, XZ, BZip2; single-payload restrictions enforced before writing |
| Encryption | 7z passwords/header encryption, ZIP AES-256 and explicit legacy ZipCrypto, password-specific error categories |
| Editing | Single-use `ArchiveEditor`, rename/delete/replace/add file/add directory, distinct replacement output |
| Filesystem | Directory planning, collision/path checks, actual-byte limits, per-file publication, source-preserving archive replacement |
| Lifetime | Operation exclusion, direct callback reentry checks, cooperative cancellation, synchronous and asynchronous disposal |
| Native integration | Pinned package, native format catalog, session-owned interfaces, managed callback source generation, named multipart RAR resolution |
| Sample/CI | CLI sample, native integration tests, x64 NativeAOT publish/run smoke checks |

## Dependency and ABI boundary

`packages/SevenZip.Interop.26.5.1.nupkg` is a pinned local feed dependency.
A clean-package-cache restore has been exercised. See `packages/README.md` for
its checksum, upstream source revision, and included licenses.

The WinMD groups types by function under `SevenZip.Interop`: `Archives`,
`Compression`, `Cryptography`, `Hashing`, `Progress`, `Streams`, and `FileManager`.
The 26.5.1 metadata update preserves the native contracts and native DLL bytes.

ABI definitions are internal and manually pinned to the matching 7-Zip headers.
.NET's COM source generator generates the managed callback implementations;
native archive calls use typed vtable adapters with explicit pointer ownership.
The bundled WinMD is not yet used to generate those ABI declarations. Do not
describe this as a completed WinMD-to-managed projection pipeline.

Native property IDs and variant interpretation live below the public API:
`Interop/NativePropertyId.cs` and `Native/Properties/NativeMetadataReader.cs`.
The process-wide runtime shares a module and immutable format metadata, not
archive instances. The code has no STA/message-pump or UI framework dependency.

## Validation performed

- Windows x64: **77 tests passed**, no skipped tests; process architecture
  explicitly logged as X64.
- Windows x86: **77 tests passed**, no skipped tests; process architecture
  explicitly logged as X86. The ABI test verifies the 16-byte x86 / 24-byte
  x64 native variant layout and union offset.
- Independent inputs: BCL-created ZIP and TAR, plus existing 7z, RAR 4/5,
  multipart RAR, WIM, and ARJ fixtures. Fixture provenance is recorded under
  `tests/SevenZipSharp.Tests/TestData/README.md`.
- ZIP output was also read through the BCL. All six write formats round-trip
  through the pinned native engine.
- Solid selection tests count exactly one native extraction invocation.
- Failure cases include CRC corruption, wrong/missing passwords, missing volumes,
  source length changes, callback/cleanup failures, cancellation, concurrent
  operations, callback reentry, and disposal during native execution.
- Filesystem cases include failed publication, original-byte preservation,
  temporary-file cleanup, traversal/device/ADS/collision rejection, existing
  directory symlinks, and actual-byte limits.
- Windows x64 NativeAOT sample was published and executed. Its self-test exercises
  all six write formats, 7z encrypted headers, editing, and filesystem
  create/update/extract without the JIT.

CI is configured to run the build, native tests, and x64 NativeAOT smoke program.
The hosted workflow itself has not been dispatched in this session. ARM64 assets
are included by the dependency, but ARM64 execution has not been validated.

## Deliberate limits and deferred work

- Indexed input and archive output require seeking. Entry extraction destinations
  and known-length entry source factories may be sequential.
- Empty unnamed TAR streams need a TAR format hint; named `.tar` files supply
  the equivalent evidence to the native handler.
- Basic names use `/` and directory suffixes. They are normalized logical paths,
  not an attempt to preserve the original archive's exact filename byte encoding.
  Timestamp conversion follows the native engine; original time zone fidelity
  and explicit legacy filename encoding overrides are not currently provided.
- Failed native sessions are not reused. Metadata remains available, but content
  operations require reopening.
- Editing cannot convert formats; encrypted input requires explicit matching
  write options. Unchanged data reuse is handler-dependent and solid changes may
  require repacking.
- Supported metadata is limited to the surfaced timestamps and Windows attributes.
  Full ACL/owner/extended-attribute/alternate-stream fidelity is not promised.
- Filesystem path checks require an application-controlled directory tree and do
  not prevent every concurrent path substitution race. Extraction is not a
  directory-wide transaction; already published files may remain after failure.
- Entry/byte limits do not bound native parser allocations or all decoder CPU and
  memory use. Entry-count limits apply after native header opening.
- Pull entry streams, explicit spooling adapters, non-seekable archive output,
  custom DLL runtimes, expert native properties, multi-volume writing, SFX
  construction, raw codecs, and automatic WinMD projection remain separate work.

The [original architecture proposal](architecture-and-api-redesign.md) remains a
design reference; its historical namespace and signature sketches are not the
authoritative API inventory. Current usage examples are in the root README.
