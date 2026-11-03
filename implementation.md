# Implementation

## Products

| Product | Location | Purpose |
|---|---|---|
| SML specification | `Documents/SML-Language-Specification.md` | The language: SML 1.0-SVG-1 |
| `smlxml` | `Source/SmlXml/` (command line), `Source/Sml/` (library) | Two-way SML ↔ XML/SVG converter in C# |
| Python prototype and test harness | `Tests/svg-roundtrip/` | Reference implementation, fixture corpus, fuzz and differential tests |
| SVG corpus | `Tests/svg-corpus/` | 976 third-party real-world SVG files, kept as examples |

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

The publish profile writes two framework-dependent single-file executables, each with `LICENSE.md` and `svg.smlprofile` beside it: `publish/smlxml/win-x64/smlxml.exe` (64-bit, needs the x64 .NET 10 runtime) and `publish/smlxml/win-x86/smlxml.exe` (32-bit, needs the x86 .NET 10 runtime). `Source/SmlXml/Properties/PublishProfiles/windows.pubxml` is the only publish profile; Visual Studio's Publish command uses it too. On Windows, `pull-and-publish.bat` runs `git pull --ff-only origin main` and then that publish; it stops if `main` is not checked out or the pull cannot fast-forward. `.gitattributes` gives `*.bat` files CRLF line endings, which `cmd.exe` needs. `crosscheck_csharp.py` needs a Release build of `Source/SmlXml` first.

## Conventions

- **Two implementations, one behavior.** The Python prototype and the C# library implement the same specification. Their canonical SML must be byte-identical: `CorpusTests` compares C# output with `Tests/svg-roundtrip/results/*.sml`, and `crosscheck_csharp.py` compares both on random documents and mutants. A change to the specification's syntax or canonical form must be made in both, followed by both test suites and the cross-check. Writer options (`SmlWriterOptions`: cleaning with a profile, `SmlProfile` and `ValueLayout`, as in §3.2, and vertical attributes) exist only in C#. They change neither syntax nor the canonical form, and their output is ordinary SML that the Python reader must read the same way.
- **Code points, not UTF-16 units.** The canonical form measures lines in code points and orders names by code point. In C#, use `XmlRules.CodePointLength` and `XmlRules.CompareCodePoints`, never `string.Length` or ordinal comparison, for those rules.
- **Warnings are errors** in all C# projects.
- **Calculation separate from presentation.** `Source/Sml` has no console or file-system code; `Source/SmlXml/CommandLine.cs` owns arguments, files, and messages.

## Decisions

- **The library is split from the command line** so that the format logic can be tested directly and reused by future tools such as an editor.
- **The XML importer turns off System.Xml namespace processing.** It uses `XmlTextReader` with `Namespaces = false`, wrapped with character checking, and validates names and namespaces itself with `XmlRules`, as the SML reader does. With namespace processing on, System.Xml rejects `xml:space` values other than `default` and `preserve`, which are well-formed opaque values without a DTD.
- **Blocks are delimited by `"""`**, with a longer run of quotation marks when a payload line is `"""`, and `end` closes only elements.
  - Braces were rejected: CSS and scripts, the usual block payloads, put `}` alone on a line, so a brace-delimited block would close early and the rest of the CSS would parse as SML.
  - A backtick was rejected: it is hard to see, a dead key on many keyboard layouts, and PowerShell's escape character.
- **Blocks are verbatim.** Only the closing marker's indentation comes off each payload line; trailing whitespace, extra indentation, and whitespace-only lines are data. The user chose this over trimming, so that CSS and scripts from editors such as Inkscape stay readable blocks instead of quoted strings. The cost: an editor that strips trailing whitespace changes a block's data. The canonical writer therefore quotes values of whitespace only, and puts a block's closing marker level with its payload.
- **`standalone="no"` is no flag.** The user chose to treat `standalone="no"` like a missing flag, as XML 1.0 §2.9 does: both importers and both SML readers read it, and `@standalone no`, as none, and the writers write only `@standalone yes` and `standalone="yes"`. Most SVG files have no standalone flag at all (964 of the 976 corpus files), so making `no` the SML default would have added a line to nearly every file.
- **Comments are trimmed.** The user decided that a comment's data has no whitespace at its ends: both importers and both SML readers remove spaces, tabs, and line breaks there, and XML output writes `<!-- data -->` (`<!---->` when empty). So `<!-- x -->`, `<!--x-->`, and `@rem " x "` are the same comment, written `@rem x`. The cost is that XML → SML → XML no longer keeps the whitespace at a comment's ends, nor the line breaks around a pretty-printed multi-line comment; the indentation of its later lines stays. Comment blocks are always literal; §6.4 predicts edges only for text blocks.
- **Two-space indentation is the default.** Without an `@indent` header, the reader predicts indentation by two spaces (§6.4), so SML written by hand gives indented XML, and the writer never writes `@indent 2`. `@indent none` turns the prediction off; the writer writes it only when reading without it would give a different document, such as minified XML.
- **Profiles are only for cleaning.**
  - A profile (`SmlProfile`) gives SML output aliases and layouts; `smlxml --clean <profile>` (alias `--tidy`) applies it, and there is no exact profile mode.
  - Cleaning is the specification's lossy readable import (§§3.1–3.2). Formatting whitespace is replaced by the prediction of `@indent 2` whatever the input's indentation, so cleaned SML needs no indentation statements and the XML written back is indented by two spaces; values with a layout go one item per line; multi-line text and comments are tidied. Whitespace that can change rendering (SVG text content where SVG 1.1 would differ, `xml:space="preserve"`, `foreignObject`) stays. A value that cannot be divided cleanly keeps its whitespace and is quoted with a continuation line per item.
  - The exact (canonical) writer is not affected; it still reproduces the input's whitespace, choosing `@indent` as §14.2 rule 9 says.
  - The format is smlxml configuration, not SML. SML written with a profile is self-contained (§8.5): `SmlWriter` declares the aliases it uses on the root element.
- **External DTDs are never loaded** (`XmlResolver = null`). DTD internal subsets are rejected, including an empty `[]`, which System.Xml cannot distinguish from no subset; `XmlImporter.HasInternalSubset` scans the prolog for it.
- **One publish profile, both bitnesses.**
  - `windows.pubxml` serves both the command line and Visual Studio. `PublishProtocol=FileSystem` and `_TargetId=Folder` make Visual Studio list it as a folder profile.
  - Visual Studio's Publish page shows "Target runtime: win-x64" for it, because a profile can name only one runtime. Each Publish still produces both builds. This display was accepted in preference to two profiles or a "Portable" profile.
  - It publishes `win-x64`. The `PublishAdditionalRuntimes` target in `SmlXml.csproj` runs after `Publish` and publishes each runtime in `AdditionalPublishRuntimes` (`win-x86`) the same way, so both executables come from one publish, in Visual Studio too.
  - `RuntimeIdentifiers` restores both runtimes up front.
  - `PublishDir` (`..\..\publish\smlxml\$(RuntimeIdentifier)\`) is relative to the project folder, so the output is the same wherever publishing starts.
- **Any CPU.** The assemblies are CPU-neutral (`PlatformTarget=AnyCPU`), also inside the 32- and 64-bit publishes; only the native `smlxml.exe` launcher is per-architecture. The solution has only the default Any CPU platform.
- **Licensing.**
  - The repository, `smlxml` included, is licensed under the MIT license in `LICENSE.md` at the root, Copyright (c) 2026 Optimax Technologies, Inc. The only exception is the third-party files in `Tests/svg-corpus`. The same copyright is in `<Company>` and `<Copyright>` in the C# project files, in the banner, and in `README.md`.
  - `SmlXml.csproj` copies `LICENSE.md` to the build and publish output, beside `smlxml.exe`.
  - The shipped `smlxml` uses no third-party packages; xUnit and the test SDK are test-only and not redistributed.
  - `Tests/svg-corpus` holds third-party files under MIT and ISC licenses, with their exact license texts in its `THIRD-PARTY-NOTICES.md`. It is distributed with the source only, never with `smlxml`. `Tests/svg-corpus/gather_corpus.py` lists the sources at pinned versions, and writes both the files and the notices. A new source must be under an MIT-like license, and no file may carry an "All Rights Reserved" notice, which the script checks; the W3C SVG 1.1 test suite was removed for that reason. Brand logos are kept out because they are trademarks.
  - `Tests/svg-corpus/other` is the exception: 79 files the user added by hand, outside `gather_corpus.py` and the notices. According to the user, its logos come from Wikimedia Commons under Creative Commons licenses or in the public domain.

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
