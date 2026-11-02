# Implementation

## Products

| Product | Location | Purpose |
|---|---|---|
| SML specification | `Documents/SML-Language-Specification.md` | The language: SML 1.0-SVG-3 |
| `smlxml` | `Source/SmlXml/` (command line), `Source/Sml/` (library) | Two-way SML ↔ XML/SVG converter in C# |
| Python prototype and test harness | `Tests/svg-roundtrip/` | Reference implementation, fixture corpus, fuzz and differential tests |

## Development environment

- .NET 10 SDK (developed with 10.0.114), `net10.0`, the default C# version for that target (C# 14).
- Tests: xUnit 2.9.3, xunit.runner.visualstudio 3.1.4, Microsoft.NET.Test.Sdk 17.14.1.
- Python 3.10 or later with `lxml`, for `Tests/svg-roundtrip/`.
- Solution: `SimplifiedMarkupLanguage.slnx` at the root.

## Build and test

```bash
dotnet build SimplifiedMarkupLanguage.slnx -c Release
dotnet test SimplifiedMarkupLanguage.slnx -c Release
dotnet publish Source/SmlXml/SmlXml.csproj -p:PublishProfile=windows
python Tests/svg-roundtrip/build_and_test.py
python Tests/svg-roundtrip/crosscheck_csharp.py --docs 1000
```

The publish profile writes two framework-dependent single-file executables, each with `LICENSE.md` beside it: `publish/smlxml/win-x64/smlxml.exe` (64-bit, needs the x64 .NET 10 runtime) and `publish/smlxml/win-x86/smlxml.exe` (32-bit, needs the x86 .NET 10 runtime). `Source/SmlXml/Properties/PublishProfiles/windows.pubxml` is the only publish profile; Visual Studio's Publish command uses it too. `crosscheck_csharp.py` needs a Release build of `Source/SmlXml` first.

## Conventions

- **Two implementations, one behavior.** The Python prototype and the C# library implement the same specification. Their canonical SML must be byte-identical: `CorpusTests` compares C# output with `Tests/svg-roundtrip/results/*.sml`, and `crosscheck_csharp.py` compares both on random documents and mutants. A change to the specification's syntax or canonical form must be made in both, followed by both test suites and the cross-check.
- **Code points, not UTF-16 units.** The canonical form measures lines in code points and orders names by code point. In C#, use `XmlRules.CodePointLength` and `XmlRules.CompareCodePoints`, never `string.Length` or ordinal comparison, for those rules.
- **Warnings are errors** in all C# projects.
- **Calculation separate from presentation.** `Source/Sml` has no console or file-system code; `Source/SmlXml/CommandLine.cs` owns arguments, files, and messages.

## Decisions

- **The library is split from the command line** so that the format logic can be tested directly and reused by future tools such as an editor.
- **The XML importer turns off System.Xml namespace processing.** It uses `XmlTextReader` with `Namespaces = false`, wrapped with character checking, and validates names and namespaces itself with `XmlRules`, as the SML reader does. With namespace processing on, System.Xml rejects `xml:space` values other than `default` and `preserve`, which are well-formed opaque values without a DTD.
- **External DTDs are never loaded** (`XmlResolver = null`). DTD internal subsets are rejected, including an empty `[]`, which System.Xml cannot distinguish from no subset; `XmlImporter.HasInternalSubset` scans the prolog for it.
- **One publish profile, both bitnesses.**
  - `windows.pubxml` serves both the command line and Visual Studio. `PublishProtocol=FileSystem` and `_TargetId=Folder` make Visual Studio list it as a folder profile.
  - Visual Studio's Publish page shows "Target runtime: win-x64" for it, because a profile can name only one runtime. Each Publish still produces both builds. This display was accepted in preference to two profiles or a "Portable" profile.
  - It publishes `win-x64`. The `PublishAdditionalRuntimes` target in `SmlXml.csproj` runs after `Publish` and publishes each runtime in `AdditionalPublishRuntimes` (`win-x86`) the same way, so both executables come from one publish, in Visual Studio too.
  - `RuntimeIdentifiers` restores both runtimes up front.
  - `PublishDir` (`..\..\publish\smlxml\$(RuntimeIdentifier)\`) is relative to the project folder, so the output is the same wherever publishing starts.
- **Any CPU.** The assemblies are CPU-neutral (`PlatformTarget=AnyCPU`), also inside the 32- and 64-bit publishes; only the native `smlxml.exe` launcher is per-architecture. The solution has only the default Any CPU platform.
- **Licensing.**
  - `smlxml` is distributed under the MIT license in `LICENSE.md` at the root, Copyright (c) 2026 Optimax Technologies, Inc. The same copyright is in `<Company>` and `<Copyright>` in the C# project files and in the banner.
  - `SmlXml.csproj` copies `LICENSE.md` to the build and publish output, beside `smlxml.exe`.
  - The shipped `smlxml` uses no third-party packages; xUnit and the test SDK are test-only and not redistributed.

## Known traps

- **`--self-contained false` is not honored.** With SDK 10.0.114, `dotnet publish -r win-x64 --self-contained false` produced a 73 MB self-contained executable. `--no-self-contained`, `-p:SelfContained=false`, or the `windows` publish profile produce the 250 KB framework-dependent `smlxml.exe`.
- **Single-file publishing bundles content files.** With `PublishSingleFile`, a file marked `CopyToPublishDirectory` is embedded in the executable unless it has `ExcludeFromSingleFile="true"`, as `LICENSE.md` does in `SmlXml.csproj`.
- **System.Xml reports an empty internal subset (`[]`) exactly like no subset.** See `HasInternalSubset`.
- **XML parsers disagree at the edges.** Recorded by the Python fuzzer as known divergences:
  - libxml2 rejects namespace names that are not URI references;
  - expat uses XML 1.0 4th-edition name characters, so 5th-edition names such as one containing U+FEFF fail in expat;
  - libxml2 accepts a malformed `version="1."` with only a warning;
  - lxml raises only when the last logged message is an error.
- **Expat decodes undeclared encodings one byte at a time.** The Python importer transcodes registered multi-byte encodings itself (`_transcode`).
