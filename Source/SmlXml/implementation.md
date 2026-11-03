# smlxml

## Requirements

- A command-line program named `smlxml.exe`, in C# on .NET 10, built for Any CPU and published as both a 32-bit and a 64-bit executable.
- Given an XML file, it writes SML with the same name; given an SML file, it writes XML beside the original.
- Input file names may contain wildcards.
- Run without arguments, it shows the usage.
- Every run starts with this banner on standard output, followed by a blank line:
  `smlxml 1.0.0 (SML 1.0-SVG-1)`, `Copyright (c) 2026 Optimax Technologies, Inc.`, `Freely distributed under MIT license.`
  The SML version in it is `SmlReader.Version`, the revision the reader implements.
- `--output` accepts a `*` pattern, so that `smlxml *.svg` followed by `smlxml -o *.out.svg *.sml` leaves `name.sml` and `name.out.svg` beside each `name.svg`.
- `--clean <profile>` (alias `--tidy`) converts XML to readable SML with an SML profile, which defines blocks for path `d`, polyline `points`, and other SVG values that are more readable one item per line, and supplies aliases. Cleaning is immune to the input's indentation: the SML always uses the default two-space indentation with no indentation statements, and the XML written back from it is indented by two spaces. It tidies multi-line text and comments, so no whitespace-only lines or redundant blank lines remain except where whitespace can change rendering. The `svg` profile ships with `smlxml`. A profile applies only with `--clean`: there is no exact profile mode and no separate `--profile` option.
- `--verbose` writes every attribute and namespace declaration on its own line, never in a `name(...)` list.

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
- **Banner.** `WriteBanner` runs first in every run, including `--help`, usage errors, and failed conversions; errors still go to standard error.
  - The version comes from `<Version>` and the copyright from `<Copyright>` in `SmlXml.csproj`. `<Company>` and `<Copyright>` also appear in the `.exe`'s file properties on Windows.
  - `-v` / `--version` prints only the banner, because its first line is the version.
- **Cleaning.** `-c` / `--clean` takes a profile: a file, or a plain name (no folder or extension) for `name.smlprofile` beside the executable (`AppContext.BaseDirectory`).
  - The profile is parsed once, before any conversion. A missing or invalid profile is a usage error (exit 2), reported as `file(line): error: message`.
  - It applies to XML input only; SML input converts as usual.
  - `SmlWriter.Write(doc, SmlWriterOptions)` does the work; the format and the writer rules are in `SmlProfile` and the README.
  - `SmlWriterOptions.Clean` holds the profile. The writer finds the elements whose formatting whitespace it replaces with the prediction of `@indent 2` (`FindNormalized`, specification §3.1), reflows values with a layout one item per line (§3.2), tidies multi-line text outside SVG text content (`TidyText`) and multi-line comments (`TidyComment`, which dedents the lines after the first, since comment data is always trimmed), and marks the output `@sml 1.0-SVG-1 tidy` (specification §11). A value with a layout that cannot be divided cleanly is quoted with a continuation line per item; values written as blocks use the profile's alias.
  - Tidied text in a compact element in a laid-out position is written as a laid-out block (§6.4), so pretty-printed `<style>` content becomes a plain block; in the normal form (with `--verbose`) it is a literal block without edges. A tidied comment is a literal block.
  - In SVG text content, whitespace between children is normalized only where every gap already holds a space or tab (and, inside text, the edges too), because SVG 1.1 renders a bare line break as nothing; text there is never tidied.
  - Aliases are declared only on the root, only for aliases actually used, and only where the root's namespace declarations name the element and attribute. One declaration serves the whole document, and the output never depends on the profile.
  - Element aliases (`@node`, specification §8.7) are declared before the root, only for aliases actually used. An SML element alias stands for a lexical name, while a profile names an expanded name, so the writer declares the alias for the lexical name of the first element with that expanded name and opens only the elements with both. An alias that an element of the document is named like is not used at all, so the writer never needs `@element` for it. The shipped `svg.smlprofile` declares `@node g group` and `@node p para`.
- **Verbose.** `--verbose` sets `SmlWriterOptions.VerticalAttributes`. It replaces canonical rule 5 (inline or vertical) with "always vertical" and changes nothing else.
  - Value forms and the compact forms of elements without declarations (`g/`, `title = Text`) are as in the canonical form.
  - It combines with `--clean` and applies to XML input only.
  - It has no short form, because `-v` is `--version` and `-V` would be easy to confuse with it.
- **No arguments** prints the usage text on standard output and exits with 2, because nothing was converted. `--help` exits with 0.
- **Errors** are reported on standard error as `file(line): error: message`, a format Visual Studio can jump to, and conversion continues with the next file.
- **Exit codes:** 0 when every file converted, 1 when at least one conversion failed or a pattern matched no file, 2 for an invalid command line or no arguments.

## Structure

- `Program.cs` calls `CommandLine.Run`, which is public so that `Source/Sml.Tests/CommandLineTests.cs` can test the command line without starting a process.
- `FileExpander` (wildcards) is public for the same reason; `FileExpanderTests` covers its matcher.
- All format logic is in `Source/Sml` (`SmlReader`, `SmlWriter` with `SmlWriterOptions`, `XmlImporter`, `XmlExporter`, `SmlProfile`, `ValueLayout`).
- `svg.smlprofile` is the built-in profile. `SmlXml.csproj` copies every `*.smlprofile` to the build and publish output; `ExcludeFromSingleFile` keeps the profile beside the executable rather than inside the single-file bundle.
- `Source/Sml.Tests/ProfileTests.cs` covers the layouts, the profile format, and the writer. It cleans every fixture in `Tests/svg-roundtrip/corpus` and every file in `Tests/svg-corpus`, and checks, against an independent statement of the rules, that only formatting whitespace, values with a layout, and multi-line text and comments change, and that cleaning twice gives the same SML. `WriterOptionsTests.cs` does the same for vertical attributes.

## Build

`dotnet publish Source/SmlXml/SmlXml.csproj -p:PublishProfile=windows` writes `publish/smlxml/win-x64/smlxml.exe` (64-bit) and `publish/smlxml/win-x86/smlxml.exe` (32-bit), each one framework-dependent file of about 250–300 KB with `LICENSE.md` and `svg.smlprofile` beside it.
- This is the only publish profile, and Visual Studio's Publish command uses it too. Visual Studio's Publish page shows "Target runtime: win-x64", because a profile can name only one runtime; each Publish still writes both builds. Keeping one profile with that display was chosen over two profiles (one per runtime) and over a profile that Visual Studio shows as Portable.
- The profile publishes win-x64; `PublishAdditionalRuntimes` in `SmlXml.csproj` then publishes win-x86.
- The assemblies are Any CPU (`PlatformTarget`); only the launcher differs between the two. The license is included in every build and publish output; `ExcludeFromSingleFile` keeps it out of the single-file bundle. On Windows, `dotnet build` also produces `smlxml.exe` in `bin/`.
