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
dotnet publish Source/SmlXml/SmlXml.csproj -p:PublishProfile=win-x64
python Tests/svg-roundtrip/build_and_test.py
python Tests/svg-roundtrip/crosscheck_csharp.py --docs 1000
```

The publish profile writes one framework-dependent `publish/smlxml/smlxml.exe` (requires the .NET 10 runtime). `crosscheck_csharp.py` needs a Release build of `Source/SmlXml` first.

## Conventions

- **Two implementations, one behavior.** The Python prototype and the C# library implement the same specification. Their canonical SML must be byte-identical: `CorpusTests` compares C# output with `Tests/svg-roundtrip/results/*.sml`, and `crosscheck_csharp.py` compares both on random documents and mutants. A change to the specification's syntax or canonical form must be made in both, followed by both test suites and the cross-check.
- **Code points, not UTF-16 units.** The canonical form measures lines in code points and orders names by code point. In C#, use `XmlRules.CodePointLength` and `XmlRules.CompareCodePoints`, never `string.Length` or ordinal comparison, for those rules.
- **Warnings are errors** in all C# projects.
- **Calculation separate from presentation.** `Source/Sml` has no console or file-system code; `Source/SmlXml/CommandLine.cs` owns arguments, files, and messages.

## Decisions

- **The library is split from the command line** so that the format logic can be tested directly and reused by future tools such as an editor.
- **The XML importer turns off System.Xml namespace processing.** It uses `XmlTextReader` with `Namespaces = false`, wrapped with character checking, and validates names and namespaces itself with `XmlRules`, as the SML reader does. With namespace processing on, System.Xml rejects `xml:space` values other than `default` and `preserve`, which are well-formed opaque values without a DTD.
- **External DTDs are never loaded** (`XmlResolver = null`). DTD internal subsets are rejected, including an empty `[]`, which System.Xml cannot distinguish from no subset; `XmlImporter.HasInternalSubset` scans the prolog for it.
- **Licensing.** `smlxml` is distributed under the MIT license, as its banner states, and is Copyright (c) 2026 Optimax Technologies, Inc. (`<Company>` and `<Copyright>` in the C# project files). The repository has no `LICENSE` file yet. The shipped `smlxml` uses no third-party packages; xUnit and the test SDK are test-only and not redistributed.

## Known traps

- **`--self-contained false` is not honored.** With SDK 10.0.114, `dotnet publish -r win-x64 --self-contained false` produced a 73 MB self-contained executable. `--no-self-contained`, `-p:SelfContained=false`, or the `win-x64` publish profile produce the 250 KB framework-dependent `smlxml.exe`.
- **System.Xml reports an empty internal subset (`[]`) exactly like no subset.** See `HasInternalSubset`.
- **XML parsers disagree at the edges.** Recorded by the Python fuzzer as known divergences:
  - libxml2 rejects namespace names that are not URI references;
  - expat uses XML 1.0 4th-edition name characters, so 5th-edition names such as one containing U+FEFF fail in expat;
  - libxml2 accepts a malformed `version="1."` with only a warning;
  - lxml raises only when the last logged message is an error.
- **Expat decodes undeclared encodings one byte at a time.** The Python importer transcodes registered multi-byte encodings itself (`_transcode`).
