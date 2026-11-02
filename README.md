# Simplified Markup Language (SML)

SML is a plain-text notation for SVG and other XML that is easy to read and edit by hand, and converts to XML and back without losing information.

This repository contains the SML specification, `smlxml` (a two-way command-line converter in C#), and a Python reference prototype with conformance, fuzz, and differential tests.

> **Status:** SML 1.0-SVG-4 is a proposed specification, not yet final. The name and the `.sml` extension are provisional, because both are already used by Standard ML and by W3C's Service Modeling Language.

## Example

An SVG file:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 80">
  <!-- A rounded badge with a label -->
  <rect x="5" y="5" width="110" height="70" rx="8" fill="#4477cc"/>
  <path d="M20 60 L60 20 L100 60 Z" fill="white" opacity="0.5"/>
  <text x="60" y="45" text-anchor="middle" font-size="14">Hello, SML</text>
</svg>
```

The same document in SML, as `smlxml` writes it:

```text
@sml 1.0-SVG-4
@indent 2
svg(xmlns=http://www.w3.org/2000/svg, viewBox=0 0 120 80)
  @comment " A rounded badge with a label "
  rect(fill=#4477cc, height=70, rx=8, width=110, x=5, y=5)/
  path(d=M20 60 L60 20 L100 60 Z, fill=white, opacity=0.5)/
  text(font-size=14, text-anchor=middle, x=60, y=45) = Hello, SML
end
```

- An element is its name followed by its attributes.
- `/` ends an element with no content, and `end` closes one that has content.
- `= text` gives an element's text.
- `@indent 2` records that the original was indented with two spaces. The SML therefore leaves out the indentation whitespace, and converting back restores it exactly.

Converting this SML to XML gives the original document again. Only attribute order changes, which XML does not treat as information.

SML also has vertical attributes, multiline literal blocks for long paths, CSS, and scripts, and reusable attribute blocks. See the [specification](Documents/SML-Language-Specification.md) for the full language.

## Design

- **Lossless within a defined subset.** For every document in the supported SVG/XML subset (§3 of the specification), XML → SML → XML and SML → XML → SML give the same document. The comparison covers elements, attributes, exact text including whitespace, comments, processing instructions, namespace prefixes and declarations, and DOCTYPE identifiers.
- **Strict.** Input outside the subset is rejected with an error, never silently changed. Examples are DTD internal subsets, declared entities, and XML 1.1.
- **Values are opaque.** Path data, transforms, styles, and scripts are kept exactly as written; the converter never reformats numbers or geometry.
- **Canonical form.** XML → SML output is deterministic, so converted files diff cleanly.
- **Readable on request.** A writer profile can break long path data and point lists at item boundaries. An explicit, lossy *reflow* goes further and puts each item on its own line in a block (§3.2).
- **Safe.** External DTDs and entities are never loaded.

## Repository contents

| Path | Contents |
|---|---|
| [`Documents/SML-Language-Specification.md`](Documents/SML-Language-Specification.md) | The specification, revision 1.0-SVG-4 |
| [`Documents/sml-vs-kdl-vs-ndl.md`](Documents/sml-vs-kdl-vs-ndl.md) | Design study comparing SML with KDL and NDL for hand-editable SVG |
| [`Documents/SML-Spec-Review-Claude.md`](Documents/SML-Spec-Review-Claude.md) | Review of the previous revision, 1.0-SVG-2 |
| [`Source/Sml/`](Source/Sml/) | C# library: SML reader, canonical writer, XML importer and exporter |
| [`Source/SmlXml/`](Source/SmlXml/) | `smlxml`, the command-line converter |
| [`Source/Sml.Tests/`](Source/Sml.Tests/) | xUnit tests for the library and the command line |
| [`Tests/svg-roundtrip/`](Tests/svg-roundtrip/) | Python prototype, conformance fixtures, and fuzz and differential tests |
| [`Tests/svg-corpus/`](Tests/svg-corpus/) | 385 real-world SVG files from open-source projects, for testing |

## smlxml

`smlxml` converts each file it is given to the other format, beside the original. XML or SVG becomes `name.sml`; SML becomes `name.svg` when its root is an SVG element, otherwise `name.xml`.

| Command | Result |
|---|---|
| `smlxml drawing.svg` | Writes `drawing.sml` |
| `smlxml drawing.sml` | Writes `drawing.svg` |
| `smlxml art\**\*.svg` | Converts every SVG below `art`; `smlxml` expands wildcards itself, so this works in Windows shells too |
| `smlxml -o *.out.svg *.sml` | Writes `name.out.svg` for each `name.sml` |
| `smlxml --profile svg drawing.svg` | Writes `drawing.sml` with path data, point lists, styles, and transforms broken at items, exactly |
| `smlxml --profile svg --reflow drawing.svg` | Writes them one item per line in blocks: more readable, but no longer lossless |
| `smlxml --verbose drawing.svg` | Writes every attribute on its own line instead of in `name(...)` lists |

An existing file is never overwritten without `-f`. Errors are reported as `file(line): error: message`. See [`Source/SmlXml/README.md`](Source/SmlXml/README.md) for all options.

### Building

`smlxml` needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet build SimplifiedMarkupLanguage.slnx -c Release
```

```bash
dotnet test SimplifiedMarkupLanguage.slnx -c Release
```

The build writes `Source/SmlXml/bin/Release/net10.0/smlxml` (`smlxml.exe` on Windows). To produce the Windows executables:

```bash
dotnet publish Source/SmlXml/SmlXml.csproj -p:PublishProfile=windows
```

This writes `publish/smlxml/win-x64/smlxml.exe` (64-bit) and `publish/smlxml/win-x86/smlxml.exe` (32-bit), each beside its `LICENSE.md` and `svg.smlprofile`. Both are single-file executables that need the .NET 10 runtime of the same bitness.

On Windows, `pull-and-publish.bat` at the root does both steps for `main`: it pulls the latest commits and then publishes. It only fast-forwards, so it stops, without publishing, if your `main` has local commits or differs from GitHub's.

## Python prototype and tests

The prototype in `Tests/svg-roundtrip` is the reference implementation of the specification. It needs Python 3.10 or later and `lxml`.

```bash
python -m pip install lxml
```

```bash
python Tests/svg-roundtrip/build_and_test.py
```

`build_and_test.py` runs the conformance fixtures, the core unit tests, and a fixed-seed fuzz pass. Each fixture is also cross-checked against lxml.

The C# library and the Python prototype must produce byte-identical canonical SML. `crosscheck_csharp.py` compares them on random documents and mutants; it needs a Release build of `smlxml` first:

```bash
python Tests/svg-roundtrip/crosscheck_csharp.py --docs 1000
```

The [SVG corpus](Tests/svg-corpus/README.md) adds real-world files from icon sets, emoji, flags, animated icons, and the resvg test suite. Both implementations agree on every file.

## Development notes

- [`implementation.md`](implementation.md) records the development environment, conventions, design decisions, and known traps.
- [`HANDOFF.md`](HANDOFF.md) describes the current state of the work and its open questions.
- [`AGENTS.md`](AGENTS.md) holds the rules for coding agents working in this repository.

A change to the syntax or the canonical form must be made in the specification and in both implementations. Both test suites and the cross-check must pass afterwards.

## License

Copyright (c) 2026 Optimax Technologies, Inc. Licensed under the [MIT License](LICENSE.md).

The SVG files in `Tests/svg-corpus/` are third-party files under their own licenses (MIT and ISC), listed with their license texts in [`Tests/svg-corpus/THIRD-PARTY-NOTICES.md`](Tests/svg-corpus/THIRD-PARTY-NOTICES.md).
