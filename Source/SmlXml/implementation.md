# smlxml

## Requirements

- A command-line program named `smlxml.exe`, in C# on .NET 10.
- Given an XML file, it writes SML with the same name; given an SML file, it writes XML beside the original.

## Behavior

- **Direction by extension.** A `.sml` input (case-insensitive) is read as SML and written as XML. Any other input (`.xml`, `.svg`, …) is read as XML and written as SML.
- **Output name.** XML becomes `name.sml`. SML becomes `name.svg` when the root is the SVG `svg` element (`{http://www.w3.org/2000/svg}svg`), otherwise `name.xml`. SML does not record the original extension, so this is the closest match. `-o` / `--output` overrides the name, for one input file only.
- **No silent overwrite.** An existing output file is replaced only with `-f` / `--force`, because a hand-edited `.sml` (with `#` comments and aliases) would otherwise be lost by re-converting the XML. An output path equal to the input is always refused.
- **Atomic writes.** Output goes to a temporary file in the target folder, which is then renamed, so a failure never leaves a partial file.
- **Output encoding.** UTF-8 without BOM, LF line endings. SML is in the canonical form (§14.2); XML starts with an XML declaration.
- **Errors** are reported on standard error as `file(line): error: message`, a format Visual Studio can jump to, and conversion continues with the next file.
- **Exit codes:** 0 when every file converted, 1 when at least one conversion failed, 2 for an invalid command line.

## Structure

- `Program.cs` calls `CommandLine.Run`, which is public so that `Source/Sml.Tests/CommandLineTests.cs` can test the command line without starting a process.
- All format logic is in `Source/Sml` (`SmlReader`, `SmlWriter`, `XmlImporter`, `XmlExporter`).

## Build

`dotnet publish Source/SmlXml/SmlXml.csproj -p:PublishProfile=win-x64` writes `publish/smlxml/smlxml.exe` as one framework-dependent file of about 250 KB. On Windows, `dotnet build` also produces `smlxml.exe` in `bin/`.
