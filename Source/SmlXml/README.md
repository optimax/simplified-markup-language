# smlxml

Converts between SML 1.0-SVG-1 and XML/SVG, in both directions, without losing information.

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
| `-c`, `--clean <name>`, `--tidy <name>` | Write readable SML with a profile: whitespace and indentation tidied whatever the input's were, one item per line, multi-line text and comments tidied, and aliases; not exact (see [Cleaning with a profile](#cleaning-with-a-profile)) |
| `--verbose` | Write every attribute and namespace declaration on its own line, never in a `name(...)` list |
| `-h`, `--help` | Show help |
| `-v`, `--version` | Show the version (the banner) |

An existing output file is never overwritten without `--force`. Errors appear as `file(line): error: message`. The exit code is 0 on success, 1 if a conversion failed or a pattern matched no file, and 2 for an invalid command line or profile, including no arguments.

SML output is in the canonical form of the specification (§14.2), unless `--clean` or `--verbose` is used. It is exact: text such as a style sheet keeps every space and tab, in a block whose lines are copied verbatim (§7).

The canonical form writes short attribute lists inline: `path(d=M0 0 L1 1, fill=#fff)/`. With `--verbose`, every attribute and namespace declaration gets its own line instead:

```text
path
  .d M0 0 L1 1
  .fill #fff
end
```

Elements without attributes keep their compact forms (`g/`, `title = Text`), and values are written as in the canonical form. The SML reads back as the same document. `--verbose` has no short form, because `-v` is `--version`; it can be combined with `--clean`. XML input must be namespace-well-formed XML 1.0 without a DTD internal subset (§3); a DOCTYPE with only a public or system identifier is kept, and its DTD is never loaded.

## Cleaning with a profile

`smlxml --clean svg drawing.svg`, or `smlxml --tidy svg drawing.svg`, writes SML that is easier to read and edit, using a profile. `smlxml` comes with the `svg` profile, `svg.smlprofile` beside `smlxml.exe`. Cleaning ignores how the input was indented: the SML always uses `@indent 2` and needs no other indentation statements, and the XML written back from it is indented by two spaces. It tidies multi-line text and comments. The profile gives long, structured attribute values a layout, with one path command, coordinate pair, CSS declaration, or transform function per line, and it supplies aliases. For example:

```xml
<svg xmlns="http://www.w3.org/2000/svg">
  <style>
		.road {
			stroke: #123456;
		}
  </style>
  <g>
    <path d="M20,60L60,20Z"/>
  </g>
</svg>
```

becomes

```text
@sml 1.0-SVG-1 tidy
@indent 2
@node g group
svg(xmlns=http://www.w3.org/2000/svg)
  @block path .d data
  style = """
    .road {
    	stroke: #123456;
    }
    """
  group
    path
      data """
        M 20, 60
        L 60, 20
        Z
        """
    end
  end
end
```

This is a readable conversion, not a strict one (§§3.1–3.2 of the specification). Values and text now differ in whitespace, so converting the SML back gives SVG that renders the same but is not identical. The first line, `@sml 1.0-SVG-1 tidy`, says so.

- In path data, each command letter and each comma is followed by a space, so `M10,20L30,40` becomes the lines `M 10, 20` and `L 30, 40`. Parameters written together are separated too, so `a.75.75 0 01-.69-.35` becomes `a .75 .75 0 0 1 -.69 -.35`. Parameters are read as path data reads them: numbers each as long as possible, and an arc's two flags as single characters `0` or `1`.
- Point lists and other number lists are spaced the same way: `10,20` becomes `10, 20`, and `1-2` becomes `1 -2`.
- A value is reflowed only if it has its layout's form and at least two items. A value that has the form but cannot be divided cleanly, such as one with a TAB inside an item, keeps its whitespace and is quoted with a continuation line per item. Any other value is written exactly.
- A single path command is never split, so a long run of coordinates after one command stays on one line.
- The whitespace between elements is replaced by two-space indentation, however the input was indented or even if it was minified. Where whitespace can change rendering, it stays as it was: in mixed text, between `tspan`s where SVG 1.1 would render no space, under `xml:space="preserve"`, and in `foreignObject`.
- Multi-line text, such as a style sheet, a script, or a description, and multi-line comments lose the spaces and tabs at the ends of their lines, their empty first and last lines, extra empty lines, and the indentation all their lines share. Inner indentation, such as the tab before `stroke`, stays. In a script, whitespace inside a multi-line string, such as a JavaScript template literal, would change too. Text in SVG `text` elements is left alone, because there a line break counts.

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

An attribute alias appears wherever such a value is written as a block.

The profile also has element aliases (`@node`): `g` elements are opened as `group`, and `p` elements in its namespace, the SVG namespace, as `para`.

### Writing a profile

A profile is a UTF-8 text file with one statement per line; blank lines and lines starting with `#` are ignored. Pass its path to `--clean`, or put `name.smlprofile` beside `smlxml` and pass `name`. The built-in [`svg.smlprofile`](svg.smlprofile) is an example.

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
