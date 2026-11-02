# smlxml

Converts between SML 1.0-SVG-3 and XML/SVG, in both directions, without losing information.

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

Run `smlxml` without arguments to see the usage.

| Option | Meaning |
|---|---|
| `-o`, `--output <path>` | Write to this file instead (one input file only) |
| `-f`, `--force` | Overwrite an existing output file |
| `-h`, `--help` | Show help |
| `--version` | Show the version |

An existing output file is never overwritten without `--force`. Errors appear as `file(line): error: message`. The exit code is 0 on success, 1 if a conversion failed or a pattern matched no file, and 2 for an invalid command line, including no arguments.

SML output is in the canonical form of the specification (§14.2). XML input must be namespace-well-formed XML 1.0 without a DTD internal subset (§3); a DOCTYPE with only a public or system identifier is kept, and its DTD is never loaded.

## Build

```bash
dotnet publish Source/SmlXml/SmlXml.csproj -p:PublishProfile=win-x64
```

This writes `publish/smlxml/smlxml.exe`, which requires the .NET 10 runtime.
