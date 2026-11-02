# smlxml

## Requirements

- A command-line program named `smlxml.exe`, in C# on .NET 10.
- Given an XML file, it writes SML with the same name; given an SML file, it writes XML beside the original.
- Input file names may contain wildcards.
- Run without arguments, it shows the usage.
- `--output` accepts a `*` pattern, so that `smlxml *.svg` followed by `smlxml -o *.out.svg *.sml` leaves `name.sml` and `name.out.svg` beside each `name.svg`.

## Behavior

- **Direction by extension.** A `.sml` input (case-insensitive) is read as SML and written as XML. Any other input (`.xml`, `.svg`, …) is read as XML and written as SML.
- **Output name.** XML becomes `name.sml`. SML becomes `name.svg` when the root is the SVG `svg` element (`{http://www.w3.org/2000/svg}svg`), otherwise `name.xml`. SML does not record the original extension, so this is the closest match. `-o` / `--output` overrides the name: a literal path for one input file only, or a pattern (see below).
- **No silent overwrite.** An existing output file is replaced only with `-f` / `--force`, because a hand-edited `.sml` (with `#` comments and aliases) would otherwise be lost by re-converting the XML. An output path equal to the input is always refused.
- **Atomic writes.** Output goes to a temporary file in the target folder, which is then renamed, so a failure never leaves a partial file.
- **Output encoding.** UTF-8 without BOM, LF line endings. SML is in the canonical form (§14.2); XML starts with an XML declaration.
- **Wildcards.** `FileExpander` expands `*`, `?`, and `**` (a whole path part, any number of folders) itself, because `cmd.exe` and PowerShell pass patterns to programs unexpanded.
  - It uses its own matcher rather than .NET's legacy Win32 match rules.
  - Matching is case-insensitive on Windows and macOS, case-sensitive elsewhere.
  - Hidden files and folders, and dot-names, match only a pattern part that starts with `.`. This also keeps the converter's own `.name.guid.tmp` files out of matches.
  - An argument that names an existing file literally is not expanded; on Linux, `*` and `?` are legal in file names.
  - All patterns are expanded before any conversion, so files written by the run never become inputs. A file matched more than once is converted once.
  - A pattern that matches nothing is an error for that argument.
  - `--output` with a pattern is a usage error unless it matches exactly one file.
- **Output patterns.** When `--output` contains `*`, it is applied to each input (`ApplyOutputPattern`): every `*` becomes the input's file name without its extension (`x.y.sml` with `*.out.svg` gives `x.y.out.svg`).
  - This simple rule was chosen over binding `*` to what the input pattern matched, which is ambiguous for literal names, several patterns, and `**`.
  - Without a folder, the output goes beside the input; with a folder, into that folder, which is created if missing.
  - `?`, or `*` in the folder part, is a usage error.
- **One output per run.** An output already written by this run is refused for a later input, so `a.svg` and `a.xml` cannot both write `a.sml`, even with `--force`.
- **Inputs are never outputs.** A file whose output path is also an input of the same run is refused, so `smlxml -f *` cannot convert `a.svg` to `a.sml` and `a.sml` back to `a.svg`.
- **No arguments** prints the usage text on standard output and exits with 2, because nothing was converted. `--help` exits with 0.
- **Errors** are reported on standard error as `file(line): error: message`, a format Visual Studio can jump to, and conversion continues with the next file.
- **Exit codes:** 0 when every file converted, 1 when at least one conversion failed or a pattern matched no file, 2 for an invalid command line or no arguments.

## Structure

- `Program.cs` calls `CommandLine.Run`, which is public so that `Source/Sml.Tests/CommandLineTests.cs` can test the command line without starting a process.
- `FileExpander` (wildcards) is public for the same reason; `FileExpanderTests` covers its matcher.
- All format logic is in `Source/Sml` (`SmlReader`, `SmlWriter`, `XmlImporter`, `XmlExporter`).

## Build

`dotnet publish Source/SmlXml/SmlXml.csproj -p:PublishProfile=win-x64` writes `publish/smlxml/smlxml.exe` as one framework-dependent file of about 250 KB. On Windows, `dotnet build` also produces `smlxml.exe` in `bin/`.
