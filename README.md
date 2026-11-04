# Simplified Markup Language (SML)

SML is a plain-text notation for SVG and other XML that is easy to read and edit by hand, and converts to XML and back without losing information.

This repository contains the SML specification, `smlxml` (a two-way command-line converter in C#), and a Python reference prototype with conformance, fuzz, and differential tests.

> **Status:** SML 1.0-SVG-1 is a proposed specification, not yet final. The name and the `.sml` extension are provisional, because both are already used by Standard ML and by W3C's Service Modeling Language.

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
@sml 1.0-SVG-1
svg(xmlns=http://www.w3.org/2000/svg, viewBox=0 0 120 80)
  @rem A rounded badge with a label
  rect(x=5, y=5, width=110, height=70, rx=8, fill=#4477cc)/
  path(d=M20 60 L60 20 L100 60 Z, fill=white, opacity=0.5)/
  text(x=60, y=45, text-anchor=middle, font-size=14) = Hello, SML
end
```

- An element is its name followed by its attributes.
- `/` ends an element with no content, and `end` closes one that has content.
- `= text` gives an element's text.
- `@rem` is an XML comment. Its text is trimmed, and XML output separates it from `<!--` and `-->` by one space. A multi-line comment is a `@rem """` block, written back pretty-printed.
- SML assumes indentation by two spaces, as the original has. The SML therefore leaves out the indentation whitespace, and converting back restores it exactly. Other indentation is declared with `@indent`, such as `@indent 4` or `@indent tab`.

Converting this SML to XML gives the original document again. Attributes keep their order; `smlxml --sort-attributes` writes them alphabetically instead.

SML also has vertical attributes, multiline literal blocks for long paths, CSS, and scripts, reusable attribute blocks, and element aliases such as `@node g group`. See the [specification](Documents/SML-Language-Specification.md) for the full language.

## Readable, vertical SML

For editing by hand, `smlxml --tidy svg --verbose` writes SML that is cleaned with the built-in `svg` profile and has every attribute on its own line. Take an SVG file as a drawing program might export it, with a path and a text label:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="120" height="80" viewBox="0 0 120 80">
<style>.label{font-family:sans-serif;font-size:12px;fill:#333}</style>
<g transform="translate(10,10) scale(0.9)">
<path d="M10,50C10,20 50,20 50,50S90,80 90,50" fill="none" stroke="#4477cc" stroke-width="3"/>
<text class="label" x="50" y="70" text-anchor="middle">A smooth curve</text>
</g>
</svg>
```

`smlxml --tidy svg --verbose curve.svg` writes `curve.sml`:

```text
@sml 1.0-SVG-1 tidy
@node g group
@block path .d data
svg
  @ns http://www.w3.org/2000/svg
  .width 120
  .height 80
  .viewBox 0 0 120 80
  style = """
    .label {
      font-family:sans-serif;
      font-size:12px;
      fill:#333
    }
    """
  group
    .transform """
      translate(10,10)
      scale(0.9)
      """
    path
      data """
        M 10, 50
        C 10, 20 50, 20 50, 50
        S 90, 80 90, 50
        """
      .fill none
      .stroke #4477cc
      .stroke-width 3
    end
    text
      .class label
      .x 50
      .y 70
      .text-anchor middle
      = A smooth curve
    end
  end
end
```

- `@sml 1.0-SVG-1 tidy` says that the file was cleaned, so converting it back gives SVG that renders the same but is not byte for byte the original.
- The lines after it declare aliases from the profile: `g` elements are written `group`, and a `path`'s `d` attribute is written `data`. The file reads without the profile.
- `--verbose` puts each namespace declaration (`@ns`) and attribute (`.name value`) on its own line, and `end` closes each element.
- The path data has one command per line, with a space after each command letter and comma. The transform has one function per line, and the style sheet one declaration per line.
- The text is the `text` element's content, `= A smooth curve`.
- The input had no indentation; the SML uses two spaces per level, as always when cleaning.

Converting `curve.sml` back with `smlxml curve.sml` gives indented SVG. In the path data and the transform, the line breaks become `&#10;`, which SVG reads as spaces:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="120" height="80" viewBox="0 0 120 80">
  <style>
    .label {
      font-family:sans-serif;
      font-size:12px;
      fill:#333
    }
  </style>
  <g transform="translate(10,10)&#10;scale(0.9)">
    <path d="M 10, 50&#10;C 10, 20 50, 20 50, 50&#10;S 90, 80 90, 50" fill="none" stroke="#4477cc" stroke-width="3"/>
    <text class="label" x="50" y="70" text-anchor="middle">A smooth curve</text>
  </g>
</svg>
```

## Design

- **Lossless within a defined subset.** For every document in the supported SVG/XML subset (§3 of the specification), XML → SML → XML and SML → XML → SML give the same document. The comparison covers elements, attributes, exact text including whitespace, comments (without the whitespace at their ends), processing instructions, namespace prefixes and declarations, and DOCTYPE identifiers.
- **Strict.** Input outside the subset is rejected with an error, never silently changed. Examples are DTD internal subsets, declared entities, and XML 1.1.
- **Values are opaque.** Path data, transforms, styles, and scripts are kept exactly as written; the converter never reformats numbers or geometry.
- **Canonical form.** XML → SML output is deterministic, so converted files diff cleanly.
- **Verbatim blocks.** A multiline block keeps its text exactly, trailing whitespace and indentation included; only the closing `"""`'s indentation comes off each line. CSS and scripts copied from an editor stay readable text.
- **Readable on request.** `--clean` (or `--tidy`) with a profile ignores the input's indentation, puts path data, point lists, styles, and transforms one item per line in blocks, and tidies multi-line text and comments (§§3.1–3.2). It is explicit and lossy; the XML written back is indented by two spaces.
- **Safe.** External DTDs and entities are never loaded.

## Repository contents

| Path | Contents |
|---|---|
| [`Documents/SML-Language-Specification.md`](Documents/SML-Language-Specification.md) | The specification, revision 1.0-SVG-1 |
| [`Documents/sml-vs-kdl-vs-ndl.md`](Documents/sml-vs-kdl-vs-ndl.md) | Design study comparing SML with KDL and NDL for hand-editable SVG |
| [`Source/OptimaxTools.SMLReadWrite/`](Source/OptimaxTools.SMLReadWrite/) | C# library `OptimaxTools.SMLReadWrite`: SML reader and writer, XML importer and exporter, cleaning with profiles; usable by any .NET application |
| [`Source/SmlXml/`](Source/SmlXml/) | `smlxml`, the command-line converter |
| [`Source/Sml.Tests/`](Source/Sml.Tests/) | xUnit tests for the library and the command line |
| [`Tests/svg-roundtrip/`](Tests/svg-roundtrip/) | Python prototype, conformance fixtures, and fuzz and differential tests |
| [`Tests/svg-corpus/`](Tests/svg-corpus/) | 985 real-world SVG files from open-source projects around the world, for testing |

## smlxml

`smlxml` converts each file it is given to the other format, beside the original. XML or SVG becomes `name.sml`; SML becomes `name.svg` when its root is an SVG element, otherwise `name.xml`.

| Command | Result |
|---|---|
| `smlxml drawing.svg` | Writes `drawing.sml` |
| `smlxml drawing.sml` | Writes `drawing.svg` |
| `smlxml art\**\*.svg` | Converts every SVG below `art`; `smlxml` expands wildcards itself, so this works in Windows shells too |
| `smlxml -o *.out.svg *.sml` | Writes `name.out.svg` for each `name.sml` |
| `smlxml --clean svg drawing.svg` | Writes readable SML: indentation tidied whatever the input's was, path data, point lists, styles, and transforms one item per line, and multi-line text and comments tidied. More readable, but no longer lossless; `--tidy` is the same option |
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
