# Architecture and public API redesign

Status: design proposal, not an implemented API. Reviewed on 2026-11-02 against
the current `new2` working tree, including its uncommitted changes. Backward API
compatibility is deliberately not a constraint. Existing features still require
an explicit disposition; breaking compatibility does not justify losing them
accidentally.

Implementation began after the repository reset. The implemented namespace is
`SevenZipSharper`; names and signatures below describe the original proposal.
See [implementation-status.md](implementation-status.md) and the root README
for the current implementation, verified behavior, and deferred features.

## Recommendation

Use a .NET object model with four distinct responsibilities:

1. `Archive` and `ArchiveEntry` represent an opened, read-only archive.
2. `ArchiveBuilder` collects inputs and writes a new archive as one operation.
3. `ArchiveEditor` describes changes and writes a replacement archive.
4. `ArchiveFile` and `ArchiveFileExtensions` implement filesystem conveniences.

Use the `SevenZip` namespace and keep the package named `SevenZipSharper`.
`ArchiveFormat.SevenZip` denotes the 7z container; the `SevenZip` namespace denotes
the engine. An `Archive` can therefore represent ZIP, RAR, TAR, or other supported
formats without implying that every input is a 7z file.

The central boundary is **archive data and streams versus filesystem policy**.
Native callbacks, HRESULTs, COM interfaces, property IDs, and DLL ownership remain
below that boundary. A shared public base class, a public provider framework,
and a general virtual filesystem are unnecessary for this library.

Adopt the responsibility boundaries of the BCL, rather than promising that every
format behaves like ZIP. In particular, do not make independent, inexpensive
entry streams or in-place editing universal guarantees.

## Evidence from the current implementation

| Current location | Observation | Design consequence |
| --- | --- | --- |
| `SevenZip/SevenZipExtractor.cs` | Contains native opening, detection, metadata caching, stream ownership, extraction, filesystem overloads, and managed LZMA helpers. | Split the read session, operation engine, filesystem helpers, and codecs. |
| `SevenZip/SevenZipCompressor.cs` | Contains mutable format settings, directory scanning, source naming, create/append/modify, temporary storage, and LZMA helpers. | Separate creation from editing and traversal; validate format-specific settings before writing. |
| `SevenZip/SevenZipBase.cs` | Public `MarshalByRefObject` base mixes password state, identity, event dispatch, error translation, and global DLL configuration. | Remove this inheritance hierarchy; use composition and operation-local state. |
| `SevenZip/*Asynchronous.cs` | Task wrappers and `Begin*` methods maintain synchronization context and disposal flags; the shown task APIs have no cancellation token. | Expose task-returning operations with explicit cancellation and no UI dispatch policy. |
| `SevenZip/LibraryManager.cs` | Static dictionaries associate managed users and formats with native archive instances. Feature detection includes compression probes; the extraction benchmark currently returns `false`. | Share a runtime loader and format descriptions, not archive objects. Do not discover capabilities by performing user-visible compression work. |
| `SevenZip/ArchiveExtractCallback.cs` | Native callback code also chooses paths, creates directories/files, resolves overwrite behavior, and sanitizes names. | Callbacks adapt a prepared extraction plan; filesystem decisions belong to a separate layer. |
| `SevenZip/SevenZipExtractor.cs`, `SolidIndexes` and `ExtractFiles(ExtractFileCallback)` | Selection can expand to preceding indexes, and the managed callback overload loops through individual extraction calls. | Provide a real batch extraction path and avoid repeated decoding for a solid archive. |
| `SevenZip/SevenZipCompressor.cs`, `ModifyArchive(string, ...)` | Its `finally` block deletes the original and invokes finalization. Stream update paths contain `TODO: Copy tempStream to archiveStream?`. | Make successful replacement a separate, explicit step outside unconditional cleanup. This is static evidence of problematic control flow, not a runtime reproduction. |
| `SevenZip/ArchiveFileInfo.cs` | Public mutable struct uses non-nullable sizes, CRC, and timestamps. | Publish immutable entry metadata and preserve unknown values. |
| `SevenZip/COM.cs`, `SevenZip/sdk` | Native result values and managed codec implementation types contribute to the public surface. | Public types should describe archive operations, not their implementation. |
| `SevenZip/LZMA/LzmaEncodeStream.cs`, `LzmaDecodeStream.cs` | The stream classes encode/decode chunks with repeated properties and sizes. | Treat the existing representation separately until interoperability with standard LZMA framing is established. |

The project currently targets `net10.0`, declares `IsAotCompatible`, and references
`SevenZipSharper.Interop` 26.5.0. That package supplies Windows x86/x64/ARM64 native assets,
WinMD, and a loader. Its installed README and XML documentation do not constitute
a full managed archive API or callback projection. The local source in
`../SevenZipSharper.Interop/SevenZipLibrary.cs` confirms the loader's
process-wide default instance. An AOT compatibility property is not evidence of
a successful NativeAOT publish or execution.

## Comparison with established .NET libraries

| Reference | Boundary to adopt | Behavior not to generalize |
| --- | --- | --- |
| BCL `ZipArchive`, `ZipArchiveEntry`, `ZipFile` | Opened container, entry object, standard `Stream`, filesystem convenience methods, explicit ownership. | ZIP's create/update modes and entry stream behavior are not a universal implementation model. |
| BCL `TarReader`, `TarWriter`, `TarFile` | Sequential processing is a legitimate separate model; compression is separate from the container. | Do not add a forward-only reader merely as another name for an indexed archive session. |
| SharpCompress `IArchive`, `IArchiveEntry`, reader/writer APIs | Distinguish indexed access from sequential extraction and expose a batch path for solid archives. | Its provider framework and public interface hierarchy are not required by a single-engine wrapper. |
| SharpZipLib `ZipFile`, `ZipInputStream` | Separate archive access from sequential access; updates have an explicit operation boundary. | Its ZIP-specific concurrency guarantees should not be copied to 7-Zip sessions. |

These are comparisons, not promises of drop-in compatibility. The public BCL
surface also evolves; modern .NET archive APIs include asynchronous operations,
so the design should not be based on the old assumption that BCL archives are
entirely synchronous.

Sources: [ZipArchive][bcl-zip], [TarReader][bcl-tar-reader],
[TarWriter][bcl-tar-writer], [SharpCompress archive interface][sc-archive],
[SharpCompress entry interface][sc-entry], [SharpZipLib ZipFile][szl-zip].

## Public surface

The signatures below describe the intended shape, not a compilable reference
assembly. Namespace imports, constructors of data objects, synchronous/async
counterparts, and secondary overloads are abbreviated.

### Opened archive and entries

```csharp
namespace SevenZip;

public sealed class Archive : IDisposable, IAsyncDisposable
{
    public static Archive Open(
        Stream source,
        ArchiveOpenOptions? options = null,
        bool leaveOpen = false,
        CancellationToken cancellationToken = default);

    public static Task<Archive> OpenAsync(
        Stream source,
        ArchiveOpenOptions? options = null,
        bool leaveOpen = false,
        CancellationToken cancellationToken = default);

    public ArchiveFormat Format { get; }
    public ArchiveInfo Info { get; }
    public IReadOnlyList<ArchiveEntry> Entries { get; }
    public ArchiveCapabilities Capabilities { get; }

    public ArchiveEntry? GetEntry(string fullName);
    public IEnumerable<ArchiveEntry> GetEntries(string fullName);

    public void Extract(
        IEnumerable<ArchiveEntry> entries,
        Func<ArchiveEntry, Stream?> openDestination,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default);

    public Task ExtractAsync(
        IEnumerable<ArchiveEntry> entries,
        Func<ArchiveEntry, Stream?> openDestination,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default);

    public void Test(CancellationToken cancellationToken = default);
    public ArchiveEditor CreateEditor();

    public void Dispose();
    public ValueTask DisposeAsync();
}

public sealed class ArchiveEntry
{
    public int Index { get; }
    public string FullName { get; }
    public string Name { get; }
    public ArchiveEntryType EntryType { get; }
    public long? Length { get; }
    public long? CompressedLength { get; }
    public DateTimeOffset? LastWriteTime { get; }
    public bool IsEncrypted { get; }
    public uint? Crc32 { get; }

    public void CopyTo(
        Stream destination,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default);

    public Task CopyToAsync(
        Stream destination,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
```

Opening establishes the native session and eagerly materializes basic metadata.
`Entries` and ordinary entry getters perform no subsequent native I/O. Expensive
content verification is an explicit `Test` operation. Use an internal entry table
and create entry wrappers on demand if allocating one object per entry becomes
material; this does not require changing the public object model.

The input stream's position at open is the logical archive origin. Detection
operates within that bounded view, with explicit limits for embedded/SFX search.
Operations may reposition the stream; callers must not access or reposition it
while the session is alive. Do not silently interpret every input as starting at
physical offset zero.

`FullName` preserves the decoded logical entry name. Do not sanitize it while
reading metadata. Lookup uses ordinal, case-sensitive equality; `GetEntry`
returns the first match in archive order and `GetEntries` returns every match.
Duplicate names remain distinct through owner identity and entry index. An
editor targets entry objects, not ambiguous path strings.

An entry cannot be constructed or mutated by the caller. Detached metadata
remains readable after disposal; content operations require a live owner.
Passing an entry from another archive is an argument error. Directories, links,
and special entries have distinct types; byte-copy operations initially accept
regular files only. Do not silently follow a link or treat it as a normal file.
Batch planning can include directories without opening a byte destination for
them; filesystem sinks separately apply their metadata. Unsupported links and
special entries fail planning unless an explicit policy chooses to skip them.

Sizes use signed 64-bit values to fit `Stream`; native values above
`Int64.MaxValue` are explicitly unsupported, never wrapped or silently mapped to
unknown. Unknown metadata is `null`, not zero or `DateTime.MinValue`. Do not
invent a UTC offset for a format that supplies only an ambiguous local time;
retain that information in advanced metadata if needed.

`Test` throws on failed verification. A later detailed verification report can
be added if callers need to continue through multiple damaged entries; a boolean
must not conflate corruption, missing volumes, password failures, and I/O errors.

### Entry streams: an intentional limit

The initial universal content primitive is `CopyTo`, with `Extract` for a batch.
It writes directly through the native extraction operation and does not stage
the entire entry first. Stream input/output is still the interoperability
boundary used by callers.

Do not initially promise universal `ArchiveEntry.OpenRead()`. The 7-Zip ABI has
an optional `IInArchiveGetStream` interface, but its existence does not establish
support for all compressed entries and handlers. The pinned native source exposes
it in various image/container handlers and TAR. The checked 7z and ZIP handler
interface declarations do not expose it. It is not a universal substitute for
callback-based decompression. The local 7-Zip source is pinned to 26.05,
commit `b88c6bc09339de32f7d18bbab900686b244f42ba`.

If a pull-based API becomes necessary, distinguish three implementations:

| Strategy | Benefit | Cost and required contract |
| --- | --- | --- |
| Native entry input stream | Low overhead for handlers/entries that support it. | Discover support; preserve archive lifetime and operation exclusion; verify integrity semantics. |
| Bounded producer/consumer stream | Consumers can begin reading before extraction completes. | A worker, backpressure, early-close cancellation, exception delivery, and deterministic joining are mandatory. |
| `OpenBufferedRead` | Simple independent, seekable stream after extraction. | Explicit memory/disk staging, byte limits, cleanup, and potentially full-entry latency. |

Prefer an explicitly named `OpenBufferedRead` convenience for a first pull API
if consumers require one. Introduce universal lazy `OpenRead` only after the
bounded bridge has contract tests. Never hide an unlimited `MemoryStream` or a
full-entry temporary file behind an apparently cheap open operation.

For solid archives, callers should select all desired entries and make one
`Extract` call. Multiple `CopyTo` calls remain valid but can repeat decompression.
Plan selected indexes once; do not port the current expansion of all preceding
indexes without checking the pinned handler's requirements.

### Creating an archive

```csharp
public sealed class ArchiveBuilder
{
    public void AddEntry(
        string fullName,
        long length,
        Func<Stream> openRead,
        ArchiveEntryOptions? options = null);

    public void AddDirectory(
        string fullName,
        ArchiveEntryOptions? options = null);

    public void WriteTo(
        Stream destination,
        ArchiveWriteOptions options,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default);

    public Task WriteToAsync(
        Stream destination,
        ArchiveWriteOptions options,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
```

The builder collects descriptors, not all file contents and not open handles.
The native operation requests content when writing starts. The name communicates
this batch behavior more accurately than a writer that appears to emit each
entry immediately.

Factories supply fresh readable streams positioned at the beginning of the
declared content. Each returned stream is owned and disposed by the operation,
including failure paths. A factory may be requested again; it must be able to
reopen equivalent content. Do not silently retry a failed operation. The declared
length is required in the baseline API so metadata does not require opening all
sources in advance. Seek requirements, if any, are validated for the selected
handler. A one-shot, unknown-length source needs an explicit staging adapter,
not an undocumented full-content buffer.

Filesystem `AddFile` helpers capture metadata and create factories. Check source
length when opening and byte counts while consuming; report source changes or
length mismatches. A descriptor is not a snapshot of file contents, so this
cannot guarantee identical data if a file changes without changing its length.

The builder is single-use: `Collecting -> Writing -> Completed | Faulted`.
Registration validates names and snapshots entry options. `WriteTo` snapshots
write options and rejects unsupported combinations before modifying the output
where possible. Both failure and cancellation consume the builder. It owns no
resources between calls and does not need `IDisposable`.

`WriteTo` returning successfully is the completion boundary. Neither `Dispose`
nor a finalizer implicitly writes or commits an archive. Caller-supplied output
streams are borrowed and remain open; their position advances and their contents
may be partial on failure. The method does not rewind or truncate arbitrary
streams. Use an empty destination for a standalone archive.

Reject a public `CreateEntry(...).Open()` writing model for the first version:
with this backend it would require staging each entry or a more complicated
long-lived producer/consumer protocol. A native TAR or ZIP backend with genuine
incremental writing could justify that model later.

### Editing an existing archive

```csharp
public sealed class ArchiveEditor
{
    public IReadOnlyList<ArchiveEntry> Entries { get; }

    public void Delete(ArchiveEntry entry);
    public void Rename(ArchiveEntry entry, string newFullName);
    public void Replace(
        ArchiveEntry entry, long length, Func<Stream> openRead,
        ArchiveEntryOptions? options = null);
    public void AddEntry(
        string fullName, long length, Func<Stream> openRead,
        ArchiveEntryOptions? options = null);

    public void WriteTo(
        Stream destination,
        ArchiveUpdateOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
```

An editor borrows its source archive and has the same single-use execution
model as a builder. The source must remain alive until writing finishes. It
produces a replacement in the original format; format conversion is a separate
read-and-build operation. Conflicting edits to the same entry are rejected
during planning rather than resolved by an implicit last-write-wins rule.
`Entries` exposes the unchanged source entries so a path-based update callback
can select targets without separately opening another archive session.

Preserve existing compressed data where the handler supports it. Delete and
rename are not promises of zero decompression; solid blocks, encryption, and
format restrictions can require repacking. The editor never changes the source
archive's entry collection or source contents. Reading can advance its source
stream position.

`destination` must not alias the source, including wrappers around the same
underlying file/stream. Reject detectable aliasing; the contract forbids aliasing
that cannot be identified through arbitrary user stream wrappers. A stream
operation cannot offer transactional rollback or universally atomic replacement.

Do not overload `ArchiveMode.Update` with a mixture of implicit disposal-time
commit, stream copy-back, and filesystem replacement. Make the replacement step
the filesystem layer's responsibility.

### Filesystem conveniences

```csharp
public static class ArchiveFile
{
    public static Archive OpenRead(
        string path, ArchiveOpenOptions? options = null,
        CancellationToken cancellationToken = default);

    public static void CreateFromDirectory(
        string sourceDirectory, string destinationArchive,
        ArchiveWriteOptions options,
        ArchiveCreationOptions? creationOptions = null,
        CancellationToken cancellationToken = default);

    public static void ExtractToDirectory(
        string sourceArchive, string destinationDirectory,
        ArchiveOpenOptions? openOptions = null,
        ArchiveExtractionOptions? extractionOptions = null,
        CancellationToken cancellationToken = default);

    public static void Update(
        string path, Action<ArchiveEditor> configure,
        ArchiveOpenOptions? openOptions = null,
        ArchiveUpdateOptions? updateOptions = null,
        CancellationToken cancellationToken = default);
}
```

Provide filesystem extension methods for an existing `Archive`, `ArchiveEntry`,
and `ArchiveBuilder`: `ExtractToDirectory`, `ExtractToFile`, and `AddFile`.
Async counterparts must cover the whole workflow, including open and metadata
I/O; opening synchronously on a UI thread and then awaiting extraction is not
an asynchronous open workflow. These methods delegate to the same operation
engine, rather than implementing extraction again.

Creation options describe directory traversal, root inclusion, empty directories,
and source link handling. Extraction options describe destination mapping,
overwrite behavior, links, metadata restoration, and resource limits. Neither
set belongs in codec settings.

For path-based create/update, create a uniquely named temporary file beside the
destination, write and finalize it, close the relevant handles, then replace or
publish using the appropriate filesystem primitive. Cleanup removes only the
temporary output. Failure before publication preserves the original. Document
the actual filesystem guarantee; do not equate successful encoding, atomic name
replacement, and power-loss durability. Multi-volume publication requires its
own protocol and must not inherit a single-file atomicity claim.

### Expected calling code

```csharp
// Read: the entry owns no filesystem policy, and output remains caller-owned.
using var archive = await ArchiveFile.OpenReadAsync(
    "input.7z", cancellationToken: cancellationToken);
var entry = archive.GetEntry("documents/readme.txt")
    ?? throw new FileNotFoundException("Entry was not found.");
using var output = File.Create("readme.txt");
await entry.CopyToAsync(output, cancellationToken: cancellationToken);

// Create: filenames and archive entry names are distinct concepts.
var builder = new ArchiveBuilder();
builder.AddFile("C:/input/readme.txt", "documents/readme.txt");
using var destination = File.Create("output.7z");
await builder.WriteToAsync(destination, new SevenZipWriteOptions
{
    CompressionLevel = System.IO.Compression.CompressionLevel.Optimal,
    Solid = true
}, cancellationToken: cancellationToken);

// Edit: target identity comes from the editor's source session.
ArchiveFile.Update("existing.zip", editor =>
{
    var target = editor.Entries.Single(e => e.FullName == "old-name.txt");
    editor.Rename(target, "new-name.txt");
}, cancellationToken: cancellationToken);
```

The first two examples intentionally use caller-managed output files to show
stream ownership. Applications needing publication semantics use the filesystem
conveniences instead. The async and extension methods shown here are part of the
proposal, not APIs currently available in this repository.

## Options, formats, and metadata

Keep options operation-specific and copy their values at the relevant boundary.
Exposing init-only setters does not make nested lists and dictionaries immutable.
Never mutate a caller's options while applying defaults.

| Type | Responsibility |
| --- | --- |
| `ArchiveOpenOptions` | Optional format hint, password, legacy entry-name encoding where supported, volume resolution, detection/search limits. |
| `ArchiveWriteOptions` | Shared write settings, with concrete format-specific subclasses. |
| `SevenZipWriteOptions` | 7z method, solid settings, header encryption, dictionary/thread limits. |
| `ZipWriteOptions` | ZIP method and encryption settings. |
| `TarWriteOptions` | TAR-specific representation and metadata settings. |
| `ArchiveUpdateOptions` | Repacking/encryption decisions for the original format; no implicit conversion. |
| `ArchiveEntryOptions` | Per-entry timestamps and supported attributes. |
| `ArchiveCreationOptions` | Filesystem traversal and mapping. |
| `ArchiveExtractionOptions` | Filesystem publication and extraction policy. |

`ArchiveWriteOptions` has a `private protected` constructor and library-provided
sealed subclasses; it is an options union, not an engine plug-in interface.
Each subclass identifies its format, avoiding contradictory `Format = Zip` plus
7z-only options. Use `System.IO.Compression.CompressionLevel` for ordinary presets
where they apply and document their mappings. Do not claim bit-for-bit parity
with BCL output or expose a generic 0-9 level that has identical meaning for every
codec. Method choices belong to their applicable format options. TAR should not
inherit meaningless compression properties from the common base.

Encryption is explicit in applicable format options. Do not silently select
ZipCrypto because a password was supplied; expose supported choices and use a
documented modern default, with legacy encryption requiring an explicit choice.
Obtain interactive passwords before entering the operation, or retry opening
with a new password. A progress event is not a password prompt protocol.

Unify input/output format identity in `ArchiveFormat`; supported operations are
a separate concern. A format may be readable but not writable. A format-level
catalog, native interface availability, archive-specific restrictions, and
entry-specific restrictions are different facts. `CanUpdate` must not be inferred
only from DLL major version. A capability is preflight guidance, not a guarantee
that arbitrary input will succeed.

Use installed handler metadata where available and tested capability profiles
for properties the native ABI does not advertise. Do not probe capabilities by
creating test archives at startup. Baseline indexed reads require readable,
seekable input. Non-seekable indexed input requires explicit bounded spooling;
never make it an invisible fallback. Output seek requirements are independent:
enable sequential outputs for tested handler/configuration combinations, and
state the limitation for unverified combinations rather than applying the read
requirement to writes.

GZip, XZ, and BZip2 are single-payload compression formats. A multi-format read
session may represent one as a single synthetic entry, but its name/length may
be unavailable. Reject multiple-file creation for such a format. A `.tar.gz`
workflow is TAR plus GZip and should be explicit; do not recursively unpack or
infer multiple nested formats just from a filename extension.

Keep uncommon native properties in `SevenZip.Advanced`, using an immutable
managed value representation with no owning `PROPVARIANT` or unmanaged pointers.
If raw write properties are retained, isolate them as an expert option, validate
value kinds and snapshot them, and reject collisions with typed settings. They
must not silently override encryption, output format, or other typed settings.

## Ownership, concurrency, cancellation, and errors

| Resource | Owner and lifetime |
| --- | --- |
| Caller stream passed to `Archive.Open` | Archive owns it after successful open unless `leaveOpen`; failed open leaves it open. Document position changes on failure. |
| File stream opened by `ArchiveFile.OpenRead` | Helper/session owns it on every path, including failed open. |
| Stream passed to `CopyTo` or `WriteTo` | Borrowed; never disposed by that operation. |
| Stream returned by an extraction destination factory | Transferred to the extraction operation; disposed on entry completion/failure. `null` explicitly skips an entry. |
| Stream returned by a builder/editor source factory | Transferred to that operation; each returned instance disposed exactly once. |
| Native archive instance, callbacks, adapters | Session/operation owns them until native execution and callback activity end. |
| Default native module | Process-lifetime shared runtime. Never share archive instances through it. |
| Temporary buffered entry stream | Returned stream owns its backing file and deletes it on disposal. |

One native archive session permits one active operation. Concurrent operations
and callback reentry fail clearly with `InvalidOperationException`; do not use
an unbounded hidden queue or a global operation lock. Cached metadata reads are
safe and require no native call. Parallel extraction uses separate sessions and
independent source streams, not merely a second session over the same mutable
stream position.

Reserve the active operation before scheduling async work. The native calls are
synchronous; task APIs deliberately offload them and do not promise scalable
asynchronous network I/O. Sync methods use the synchronous engine directly,
without blocking on their async counterparts. Serialization does not imply a
dedicated STA thread or a Windows message loop; add thread affinity only when
required by actual backend evidence.

Use `CancellationToken` and `IProgress<ArchiveProgress>`. Progress contains a
phase, entry identity when known, completed/total bytes when known, and completed
entries. Native counters can mean different things for different handlers, so
normalize them by phase and do not assume one counter always counts extracted
bytes. Report progress with bounded frequency and thread-safe snapshots.

The caller decides how progress reaches a UI, for example with `Progress<T>`.
The library does not capture `SynchronizationContext` for event dispatch.
Progress notifications are observations; password entry and overwrite decisions
are separate policies. A dispatcher-backed `Progress<T>` can deliver its own
handler exceptions outside this operation; do not promise to capture those.

Cancellation is cooperative at callbacks and stream I/O boundaries. It cannot
immediately interrupt arbitrary blocked user I/O or every native codec phase.
Return a canceled task or throw `OperationCanceledException` carrying the token
only for cancellation requested by this operation. Preserve callback I/O failures
instead of misclassifying them as cancellation merely because native execution
returned an abort code.

All managed-to-native callbacks contain their exceptions. Capture the original
failure with `ExceptionDispatchInfo`, return the correct failure HRESULT, and
rethrow after the native call returns. Observe per-entry native operation results
as well as the outer HRESULT. Never let an outer `S_OK` erase a CRC or data error.
Preserve the first causal failure; retain cleanup failures as secondary details.

Prefer standard exceptions for ordinary contracts: `ArgumentException`,
`ObjectDisposedException`, `InvalidOperationException`, `NotSupportedException`,
`InvalidDataException`, `IOException`, and `OperationCanceledException`. Use one
small library exception family for actionable backend conditions such as
password-required/wrong-password or native-library load failures, with a managed
error code and optional entry identity. Do not create separate classes merely
for each operation name. Only report wrong-password when backend evidence supports
it; some damaged/encrypted data cannot be classified conclusively.

`Dispose` does not release a session while it is busy: it fails clearly, and the
caller can cancel and await the operation first. `DisposeAsync` marks the session
as closing, requests cooperative cancellation, awaits active work, and releases
resources. New work is rejected once closing begins. Disposing from a direct
callback is rejected rather than waiting on itself. Disposal does not commit
outputs; normal task completion remains the place to observe operation errors.

## Filesystem extraction policy

This is part of extraction semantics, not a reason to introduce a general
filesystem framework. The filesystem layer must validate the final mapped path,
including custom mappings, immediately before publication.

- Default to no overwrite and reject unsupported links/special files. Add link
  restoration only with a defined policy and platform tests.
- Reject rooted, traversal, drive-relative, and destination-escaping paths.
  Handle Windows device names, alternate data streams, trailing-dot/space aliases,
  case collisions, and duplicate target names explicitly.
- Do not silently strip invalid characters and merge two different entry names.
- A string prefix check does not prevent redirection through an existing junction
  or symbolic link. Use a controlled destination and a documented no-follow
  strategy; do not claim race-free containment from `GetFullPath` alone.
- Apply actual-byte entry/total limits in output adapters, plus metadata/entry
  count limits and configurable native memory/thread limits. Declared sizes alone
  do not enforce limits, and output limits do not bound solid decoding work.
- Publish a file only after successful content extraction and integrity checks.
  Set directory timestamps after children. Define which metadata failures fail
  the operation and which are explicitly ignored by policy.
- Cancellation or failure can leave already completed files. Per-file replacement
  is not a transaction covering the entire destination directory.

Microsoft documents similar limitations of path-based archive conveniences in
its [ZIP and TAR guidance][bcl-safety].

## Internal architecture

Keep one managed archive package initially. Organize it by responsibility;
separate NuGet packages only where there is an actual deployment or dependency
benefit.

```text
SevenZipSharper
  Public/
    Archive, ArchiveEntry, ArchiveBuilder, ArchiveEditor
    Options, metadata, progress, managed errors
  FileSystem/
    ArchiveFile, ArchiveFileExtensions
    DirectoryInputPlanner, ExtractionPathPolicy, DirectoryExtractionSink
    TemporaryArchivePublisher
  Operations/
    OpenOperation, ExtractOperation, TestOperation, WriteOperation
    CreationPlan, UpdatePlan, OperationContext, operation exclusion
  Native/
    NativeArchiveSession, NativeRuntime, FormatCatalog
    Callbacks/, Streams/, Properties/, error mapping
  Interop/
    Internal ABI projections and generated callback support
  Advanced/
    Expert properties and optional runtime configuration

SevenZipSharper.Interop (existing dependency)
  Native runtime assets, pinned metadata, loader
```

Dependency direction is `FileSystem -> Public/Operations -> Native -> Interop`.
`ArchiveEntry` delegates content work to its owning session. Native callbacks
adapt operation plans and stream sources/sinks; they do not depend on public
extractor/compressor classes or construct filesystem paths. Filesystem sinks
receive entry success/failure information internally so temporary files are not
published merely because a stream was disposed.

`OperationContext` holds only state belonging to one execution: cancellation,
progress, first failure, limits, and callback lifetimes. Avoid replacing the
existing base class with another all-purpose service object.

Keep narrow internal seams for native creation, source/sink I/O, and publication
where failure injection is valuable. Public `IArchive`, `IArchiveProvider`,
`IFileSystem`, a DI container, or a generalized scheduling framework are not
needed to test the design. Publish an extensibility interface only after concrete
implementations and real consumer requirements establish its contract.

### Interop and runtime boundary

Do not relocate native DLL packaging into the archive layer. Reuse the existing
`SevenZipSharper.Interop` boundary. Keep generated ABI types internal to this assembly
where the projection mechanism permits it, and never expose them in the public
signatures.

The current code retains handwritten/source-generated COM declarations alongside
the package WinMD. Establish one pinned source of truth for interface layouts,
GUIDs, property IDs, and ownership rules. Prove that the chosen projection can
generate both native calls and managed callbacks before deleting working
declarations. CsWin32's Windows API generation is not by itself proof of a usable
7-Zip projection. Check HRESULT preservation, interface inheritance/vtable slots,
variant layout, allocation/free pairs, and x86 calling conventions.

Default loading can remain process-wide, because the bundled package already
works that way. Remove the global mutable `SetLibraryPath` API. If custom DLL
loading is retained, provide an advanced `ArchiveRuntime.Load(absolutePath)`
instance with methods returning the same archive/builder objects and module
leases held by active sessions. Runtime disposal must wait for leases or reject
disposal while in use; it cannot unload code beneath live interfaces. This path
requires extending/wrapping the current loader, not pretending its singleton
already supports independent instances.

The first implementation remains Windows-specific because of its native assets.
Ordinary public archive types need no HWND, CsWin32 type, COM registration, STA
message pump, or UI framework dependency. Cross-platform support is a separate
native backend and packaging task.

## Managed codecs, compressed streams, SFX, and volumes

Move public managed SDK implementation types out of the archive object model.
If raw codec support is a product requirement, expose a separately specified
`SevenZip.Compression` surface, with standard framing, `Stream` semantics,
ownership, and independent interop tests. Do not rename the current chunked LZMA
stream to `LzmaStream` and assume it is a standard `.lzma` stream.

SFX construction is a separate assembly/stub composition feature. Reading an
archive embedded in an executable belongs to bounded detection/opening. Existing
`#if SFX` code does not require adding SFX creation to every archive options type.

Preserve multi-volume reading through a named-volume resolver in open options.
Its returned readable/seekable streams transfer ownership to the session;
unresolved names report a missing-volume condition. Default filesystem resolution
must validate archive-supplied volume names and not escape the source directory.
Never sort volume names lexicographically as a universal replacement for native
volume resolution. Multi-volume output needs an explicit destination factory and
publication policy; stage this advanced API separately rather than losing the
feature or hiding its multi-file behavior behind a single output path.

## Rejected alternatives

| Alternative | Reason |
| --- | --- |
| Keep extractor/compressor and just move methods to helper files | Does not change mutable state, ownership, operation boundaries, or coupled policies. |
| One `Archive` with Read/Create/Update modes | Compact naming, but many invalid states and implicit commit/ownership rules for this backend. Separate types make operations available only where meaningful. |
| Copy BCL `CreateEntry().Open()` immediately | Adds staging or bridge complexity without improving the native execution model. |
| Public provider/DI architecture | Multiple archive formats still use one native engine; there is no demonstrated second provider contract. |
| A dedicated STA worker per archive | Adds threads and shutdown complexity without an established ABI requirement. |
| Make every API async-only | Native operations are synchronous; callers and tools also need predictable synchronous execution. |
| Automatically buffer every non-seekable input or entry stream | Hides disk/memory use, latency, cleanup, and cancellation costs. |
| Make archive entries implement application storage abstractions | Couples an archive engine to a virtual filesystem, application navigation, or transfer policy. |

## Migration and acceptance criteria

Migrate by executable slices, replacing internals rather than putting a new facade
over the existing mutable compressor/extractor. Old and new implementations may
coexist temporarily during development; the shipped redesign does not need a
compatibility shim.

1. **Freeze the contracts.** Review signature sketches with examples for metadata,
   single-file content, solid batch extraction, creation, and editing. Specify
   ownership, unknown values, duplicate names, unsupported operations, and errors.
   Preserve current worktree changes while developing the redesign.
2. **Native session and read model.** Add explicit native ownership, basic metadata,
   format detection, `Archive`, and `ArchiveEntry.CopyTo`. Eliminate the global
   per-user archive-object cache from this path.
3. **Batch extraction and task APIs.** One native extraction for a selection,
   operation exclusion, callback error preservation, cooperative cancellation,
   and progress. Avoid per-entry reopen loops for solid archives.
4. **Creation.** Validate format-specific options; execute a builder plan through
   one native update/write operation. Add filesystem traversal as a caller of
   this engine.
5. **Editing and publication.** Add replacement-output editing, unchanged-item
   reuse where supported, failure injection, and path-based publication. Prove
   that encode/callback/publication failures preserve the original file.
6. **Advanced features and removal.** Integrate volume support and any required
   custom runtime/properties, decide codec/SFX scope, then remove the old API,
   duplicate exceptions, event infrastructure, and unused SDK surface.

Acceptance should emphasize observable contracts:

- Cross-read/write representative outputs with BCL ZIP/TAR and the pinned 7-Zip
  tools. Include ZIP/7z encrypted cases, solid/non-solid selections, empty archives,
  zero-byte files, nested directories, and supported single-payload formats.
- Count native extraction invocations for solid batch selection and measure a
  representative archive; do not infer performance from method names.
- Inject source/destination failures, cancellation, bad passwords, CRC/data errors,
  missing volumes, and replacement failures. Verify error identity, ownership,
  temporary-file cleanup, original preservation, and subsequent session state.
- Test duplicate names, unknown metadata, disallowed paths/links, collisions, byte
  limits, non-seekable streams, embedded offsets, and output seek requirements.
- Exercise concurrent calls, callback reentry, disposal during execution, early
  cancellation, and repeated disposal. Define whether a failed native session
  remains reusable; default to faulting it when backend state is uncertain.
- Run native integration tests on Windows x86, x64, and ARM64, and publish/run a
  trimmed NativeAOT smoke application covering extraction and managed callbacks.
  Do not report an x86-only test project or a build property as this evidence.
- Update README/package documentation and keep a reviewed public API baseline so
  internal interop/SDK types cannot accidentally become supported public APIs.

No implementation, build, native execution, or benchmark was performed for this
proposal. The unresolved empirical gates are handler-specific stream/seek support,
the chosen metadata-to-managed callback projection, NativeAOT execution, and exact
repacking behavior. They can change implementation details, but do not justify
mixing filesystem publication, native ownership, and UI events into one class.

[bcl-zip]: https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.ziparchive?view=net-10.0
[bcl-tar-reader]: https://learn.microsoft.com/en-us/dotnet/api/system.formats.tar.tarreader?view=net-10.0
[bcl-tar-writer]: https://learn.microsoft.com/en-us/dotnet/api/system.formats.tar.tarwriter?view=net-10.0
[bcl-safety]: https://learn.microsoft.com/en-us/dotnet/standard/io/zip-tar-best-practices
[sc-archive]: https://github.com/adamhathcock/sharpcompress/blob/master/src/SharpCompress/Archives/IArchive.cs
[sc-entry]: https://github.com/adamhathcock/sharpcompress/blob/master/src/SharpCompress/Archives/IArchiveEntry.cs
[szl-zip]: https://icsharpcode.github.io/SharpZipLib/help/api/ICSharpCode.SharpZipLib.Zip.ZipFile.html
[7z-abi]: https://github.com/ip7z/7zip/blob/b88c6bc09339de32f7d18bbab900686b244f42ba/CPP/7zip/Archive/IArchive.h

The [upstream 7-Zip ABI header][7z-abi] is pinned here to the locally inspected
26.05 revision and is the reference for native operation and ownership rules.
Implementation must keep that revision aligned with the packaged native binary.
