<h1 align="center">SevenZipSharper</h1>
<p align="center">archive library built around the 7-Zip engine, with a stream-based API and separate filesystem conveniences. Windows is currently required.</p>

## Usage

| Type | Responsibility |
| --- | --- |
| `Archive` | Read-only archive session, cached entries, batch extraction, integrity tests |
| `ArchiveEntry` | Immutable metadata and content copying |
| `ArchiveBuilder` | Deferred input registration and single-use archive creation |
| `ArchiveEditor` | Single-use replacement output for additions, deletions, renames, and replacements |
| `ArchiveFile` | Opening files, directory creation/extraction, replacement publication |
| `ArchiveFileExtensions` | Adding source files and extracting an existing entry/session |

The namespace, assembly, and project name are `SevenZipSharper`. Native ABI
interfaces, HRESULTs, callbacks, and engine objects remain internal.

### Read

```csharp
using SevenZipSharper;

await using var archive = await ArchiveFile.OpenReadAsync("input.7z");
foreach (var entry in archive.Entries)
    Console.WriteLine($"{entry.FullName}: {entry.Length}");

var readme = archive.GetEntry("documents/readme.txt")
    ?? throw new FileNotFoundException("Entry not found.");
using var output = File.Create("readme.txt");
await readme.CopyToAsync(output);
```

`FullName` uses `/` separators; directory names end with `/`. Name lookup is
ordinal and case-sensitive. Duplicate names remain separately addressable through
entry identity and index; `GetEntry` returns the first match and `GetEntries`
returns all matches. Unknown metadata is nullable.
Timestamps follow the native engine's FILETIME interpretation; legacy archives
may not preserve their original time zone.

Use `Archive.Extract` / `ExtractAsync` for multiple selected files. A selection is
sorted once and extracted in a single native call, including solid archives.
The destination factory transfers ownership of each returned stream; return
`null` to skip a file. For filesystem output, use `ExtractToDirectory` or
`ExtractToFile` with `ArchiveExtractionOptions`.

### Create

```csharp
var builder = new ArchiveBuilder();
builder.AddFile("C:/input/readme.txt", "documents/readme.txt");
builder.AddDirectory("empty");

using var output = File.Create("output.7z");
await builder.WriteToAsync(output, new SevenZipWriteOptions
{
    Solid = true,
    CompressionLevel = System.IO.Compression.CompressionLevel.Optimal
});
```

Writing is implemented for 7z, ZIP, TAR, GZip, XZ, and BZip2. The last three
require exactly one regular file. Format-specific options separate compression,
encryption, and solid settings. ZIP password encryption defaults to AES-256;
legacy ZipCrypto requires an explicit choice.

`AddEntry(name, length, openRead)` defers opening content until writing. Each
factory result is owned by the operation, must be fresh, and must contain exactly
the declared remaining length. Known-length sequential sources are supported.
Builders are consumed when execution starts, including failed/canceled execution.

### Edit

```csharp
ArchiveFile.Update("existing.zip", editor =>
{
    var entry = editor.Entries.Single(e => e.FullName == "old-name.txt");
    editor.Rename(entry, "new-name.txt");
    editor.AddDirectory("empty");
});
```

An editor borrows its source session and writes a distinct replacement through
`WriteTo` / `WriteToAsync`. `ArchiveFile.Update` writes to a sibling temporary
file, closes the source, and publishes the replacement only after success.
Editing does not convert formats; encrypted input requires explicit matching
`ArchiveUpdateOptions.WriteOptions`. Native capabilities are guidance, not a
promise that every archive can be updated.

## Ownership and execution

- Successful `Archive.Open` owns its source unless `leaveOpen: true`; failed
  opening leaves a caller-supplied source open. The source's current position is
  its logical origin.
- `CopyTo` and `WriteTo` borrow destinations and leave them open. A failed write
  can leave partial data in a caller-supplied stream.
- Input/destination factory results and resolved volume streams transfer ownership.
- One session permits one active operation. Cached metadata remains readable.
  Concurrent operations and direct callback reentry are rejected.
- `Dispose` rejects active work. `DisposeAsync` requests cancellation and awaits
  that work before releasing the source and native interfaces.
- Task APIs offload synchronous native work. Cancellation is cooperative at
  callbacks and I/O boundaries; arbitrary blocking user I/O cannot be interrupted.
- Progress uses `IProgress<ArchiveProgress>`; UI dispatch is the caller's choice.
  Progress byte counters describe actual managed I/O, not a native percentage.
- CRC/data failures are checked independently of the outer HRESULT. Causal
  callback exceptions survive cleanup. A failed native session must be reopened.

## Filesystem behavior and current limits

Filesystem helpers reject traversal, rooted/device/alternate-stream paths,
case collisions, conflicting file/directory targets, and existing reparse points.
They default to no overwrite, can enforce actual-byte limits, and publish each
file after extraction and integrity checks succeed. Links and special entries
are rejected unless an explicit skip policy is selected.

Use application-controlled source/destination directories. Path checks are
best-effort and do not make concurrently writable directory trees race-free.
Completed extracted files can remain after a later failure. A whole directory is
not a transaction, and file name replacement is not a power-loss durability claim.

Indexed inputs must be seekable. Archive output currently requires a seekable
destination; entry extraction supports sequential destinations. Empty unnamed
TAR streams require `FormatHint = ArchiveFormat.Tar`; opening a `.tar` file also
supplies the needed name. Named multipart RAR reading is tested.

The current implementation does not provide pull entry streams, automatic
spooling, custom DLL runtimes, raw native properties, multi-volume output, SFX
creation, managed codecs, or full filesystem metadata restoration. Only surfaced
timestamps and supported Windows attributes are restoration/editing contracts;
do not assume ACL, owner, extended-attribute, or alternate-stream preservation.
