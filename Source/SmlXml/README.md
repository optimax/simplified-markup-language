# smlxml

Converts between SML 1.0-SVG-4 and XML/SVG, in both directions, without losing information.

```text
smlxml [options] <file>...
```

Each file is converted beside the original:

| Input | Output |
|---|---|
| `drawing.svg`, `data.xml`, or any other non-`.sml` file | `drawing.sml`, `data.sml` |
| `drawing.sml` whose root is an SVG `<svg>` element | `drawing.svg` |
| any other `.sml` file | `name.xml` |

File names may contain wildcards, which `smlxml` expands itself because Windows shells do not:

| Pattern | Matches |
|---|---|
| `*` | Any run of characters within one file or folder name |
| `?` | Exactly one character |
| `**` (a whole path part) | Any number of folders, including none |

For example, `smlxml *.svg`, `smlxml drawings\*.sml`, or `smlxml art\**\*.svg`.
- Matching is case-insensitive on Windows and macOS, and case-sensitive on Linux.
- Hidden files and folders, and names starting with `.`, match only patterns that start with `.`.
- A pattern that matches no file is reported as an error.
- A file matched by several arguments is converted once.
- If a file's output is itself an input of the same run (for example with `smlxml *`), both files are refused.

Run `smlxml` without arguments to see the usage. Every run starts with a banner showing the version, copyright, and license; `-v` prints only the banner.

### Output patterns

When `--output` contains `*`, each input gets its own output, and `*` stands for the input's file name without its extension. Without a folder, each output goes beside its input; with a folder, into that folder, which is created if needed. For example, this converts every SVG to SML and back, leaving `name.sml` and `name.out.svg` beside each `name.svg`:

```text
smlxml *.svg
smlxml -o *.out.svg *.sml
```

`smlxml -o converted\*.sml *.svg` writes the SML files into `converted`. An output pattern can use only `*`, and only in the file name. A literal `--output` (without `*`) still takes a single input file. Two inputs that would produce the same output, such as `a.svg` and `a.xml`, are refused. On Linux and macOS, quote patterns (`'*.out.svg'`) so that the shell passes them to `smlxml` unexpanded.

| Option | Meaning |
|---|---|
| `-o`, `--output <path>` | Write to this file instead (one input file only), or with `*`, one file per input (see below) |
| `-f`, `--force` | Overwrite an existing output file |
| `-p`, `--profile <name>` | Write SML with a profile's aliases and layouts (see [Profiles](#profiles)) |
| `-r`, `--reflow` | With `--profile`: one item per line in blocks; readable, but not lossless |
| `--verbose` | Write every attribute and namespace declaration on its own line, never in a `name(...)` list |
| `-h`, `--help` | Show help |
| `-v`, `--version` | Show the version (the banner) |

An existing output file is never overwritten without `--force`. Errors appear as `file(line): error: message`. The exit code is 0 on success, 1 if a conversion failed or a pattern matched no file, and 2 for an invalid command line or profile, including no arguments.

SML output is in the canonical form of the specification (§14.2), unless a profile or `--verbose` is used.

The canonical form writes short attribute lists inline: `path(d=M0 0 L1 1, fill=#fff)/`. With `--verbose`, every attribute and namespace declaration gets its own line instead:

```text
path
  .d M0 0 L1 1
  .fill #fff
end
```

Elements without attributes keep their compact forms (`g/`, `title = Text`), and values are written as in the canonical form. The SML reads back as the same document. `--verbose` has no short form, because `-v` is `--version`; it can be combined with `--profile`. XML input must be namespace-well-formed XML 1.0 without a DTD internal subset (§3); a DOCTYPE with only a public or system identifier is kept, and its DTD is never loaded.

## Profiles

A profile makes the SML easier to read by giving long, structured attribute values a layout: one path command, coordinate pair, CSS declaration, or transform function per line. `smlxml` comes with the `svg` profile, `svg.smlprofile` beside `smlxml.exe`.

**Exact.** `smlxml --profile svg drawing.svg` writes SML that converts back to exactly the same SVG. A value with a layout and at least two items is quoted, with a continuation line per item:

```text
path
  .d "M 20 60 "
    "L 60 20 "
    "Z"
end
```

**Reflowed.** `smlxml --profile svg --reflow drawing.svg` puts each item on its own line in a block, using the profile's aliases, which it declares on the root element:

```text
# Readable conversion (SML §3.2), not strict: one item per line in d
@sml 1.0-SVG-4
svg(xmlns=http://www.w3.org/2000/svg)
  @block path .d data
  path
    data """
      M 20 60
      L 60 20
      Z
    """
  end
end
```

This is easier to read and edit, but it is a readable conversion, not a strict one (§3.2 of the specification). The values now contain line breaks where they had spaces, so converting the SML back gives SVG that renders the same but is not identical, and the first line says so.
- In path data, each command letter and each comma is followed by a space, so `M10,20L30,40` becomes the lines `M 10, 20` and `L 30, 40`. Parameters written together are separated too, so `a.75.75 0 01-.69-.35` becomes `a .75 .75 0 0 1 -.69 -.35`. Parameters are read as path data reads them: numbers each as long as possible, and an arc's two flags as single characters `0` or `1`.
- Point lists and other number lists are spaced the same way: `10,20` becomes `10, 20`, and `1-2` becomes `1 -2`.
- A value is reflowed only if it has its layout's form and at least two items; any other value is written exactly.
- A single path command is never split, so a long run of coordinates after one command stays on one line.

The SML written with a profile is self-contained: it declares every alias it uses, attribute and element aliases alike, and reads without the profile. Profiles apply only to XML-to-SML conversion; SML input converts as usual.

### The `svg` profile

| Attributes | Items, one per line | Alias |
|---|---|---|
| `d` of `path`, `glyph`, and `missing-glyph`; `path` of `animateMotion` and `textPath` | Path commands, spaced as in `M 10, 20` | `data`, `path` |
| `points` of `polyline` and `polygon` | x, y pairs, spaced as in `10, 20` | `points` |
| `values` of `feColorMatrix` | Rows of five numbers | |
| `values` of `animate`, `animateColor`, `animateTransform`, and `animateMotion`; `keyPoints` of `animateMotion`; `keyTimes` and `keySplines` | Entries | |
| `style` of any element | CSS declarations | |
| `transform`, `gradientTransform`, and `patternTransform` of any element | Functions | |

An alias appears only where the value is written as a block: with `--reflow`, or for a value that already contains line breaks.

### Writing a profile

A profile is a UTF-8 text file with one statement per line; blank lines and lines starting with `#` are ignored. Pass its path to `--profile`, or put `name.smlprofile` beside `smlxml` and pass `name`. The built-in [`svg.smlprofile`](svg.smlprofile) is an example.

| Statement | Meaning |
|---|---|
| `@ns [prefix] URI` | Binds a prefix, or the default namespace, for the names that follow. All `@ns` lines come first. |
| `@block element .attribute alias` | An alias, as in SML (§8): a block for this attribute of this element is written `alias """` instead of `.attribute """`. The aliases used are declared on the root element, so an alias is used only where the root's namespace declarations can name its element. |
| `@node element alias` | An element alias, as in SML (§8.7): elements with this name are opened as `alias`, as in `group(id=a)` for `<g id="a">`. The aliases used are declared before the root, for the name of the first such element; elements with another prefix keep their names. An alias is not used at all when an element of the document already has that name. |
| `@layout element .attribute layout` | How the attribute's values divide into items (§3.2): `path`, `numbers N` (N numbers per item), `declarations`, `functions`, or `list` (`;`-separated). The element `*` means any element without a layout of its own for that attribute. |

A mistake in a profile is reported as `file(line): error: message` before any file is converted.

## Build

```bash
dotnet publish Source/SmlXml/SmlXml.csproj -p:PublishProfile=windows
```

This writes both Windows executables, each with `LICENSE.md` (MIT) and `svg.smlprofile` beside it; distribute each `smlxml.exe` together with both files:

| Folder | Executable | Needs |
|---|---|---|
| `publish/smlxml/win-x64/` | 64-bit `smlxml.exe` | the x64 .NET 10 runtime |
| `publish/smlxml/win-x86/` | 32-bit `smlxml.exe` | the x86 .NET 10 runtime, which 64-bit Windows does not include by default |

In Visual Studio, **Publish** on the `SmlXml` project uses the same profile (`windows`) and writes the same files. Visual Studio's Publish page shows **Target runtime: win-x64**, because a profile can name only one runtime. That is expected: each Publish still writes the 32-bit build to `publish/smlxml/win-x86/` as well.
