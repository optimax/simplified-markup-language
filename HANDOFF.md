# Handoff

## Current objective

Provide `smlxml`, a two-way SML ↔ XML/SVG command-line converter in C# on .NET 10, alongside the SML 1.0-SVG-3 specification and its Python prototype and tests.

## Current state

- **`Source/Sml`** (library) implements:
  - the SML reader;
  - the §14.2 canonical writer;
  - an XML importer on System.Xml, with namespace processing off and validation done by `XmlRules`;
  - the XML exporter and document comparison.

  It is a port of `Tests/svg-roundtrip/sml_prototype.py`.
- **`Source/SmlXml`** (`smlxml`) is the command line. It expands wildcards in input names itself (`*`, `?`, `**`; `FileExpander`), accepts `*` patterns in `--output` (one output per input, `ApplyOutputPattern`), and shows the usage when run without arguments. Behavior and requirements are in `Source/SmlXml/implementation.md`, usage in `Source/SmlXml/README.md`.
- **`Source/Sml.Tests`** has 182 xUnit tests:
  - the corpus, compared byte for byte with the Python reference output;
  - both round trips;
  - the negative corpus;
  - C# ports of the CP and key SV tests;
  - command-line tests, including wildcards, output patterns, and the matcher (`FileExpanderTests`).
- **`Tests/svg-roundtrip/crosscheck_csharp.py`** compares the C# and Python implementations on random documents, non-canonical SML spellings, and SML mutants.
- Solution-wide environment, conventions, decisions, and traps are in `implementation.md`.

## Files changed in recent tasks (output patterns; `-v`; banner; license; publish profile; 32/64-bit)

- 32- and 64-bit executables, Any CPU assemblies: `win-x64.pubxml` renamed to `windows.pubxml`; `SmlXml.csproj` gains `PlatformTarget`, `RuntimeIdentifiers`, and the `PublishAdditionalRuntimes` target; docs in `implementation.md`, `Source/SmlXml/README.md`, `Source/SmlXml/implementation.md`.
- One publish profile: `Source/SmlXml/Properties/PublishProfiles/FolderProfile.pubxml` removed; the remaining profile gains Visual Studio's folder-profile properties and a project-relative `PublishDir`. Docs: `implementation.md`, `Source/SmlXml/README.md`, `Source/SmlXml/implementation.md`.
- MIT license: new `LICENSE.md` at the root, published beside `smlxml.exe` (`Source/SmlXml/SmlXml.csproj`); `implementation.md`, `Source/SmlXml/README.md`, `Source/SmlXml/implementation.md`.
- Banner at the start of every run (`-v` prints only the banner): `Source/SmlXml/CommandLine.cs` (`Run`, new `WriteBanner`), `Source/SmlXml/SmlXml.csproj` and `Source/Sml/Sml.csproj` (`Company`, `Copyright`), `Source/Sml.Tests/CommandLineTests.cs`, `Source/SmlXml/README.md`, `Source/SmlXml/implementation.md`, `implementation.md` (licensing).
- `-v` is the short form of `--version`: `Source/SmlXml/CommandLine.cs` (`Run`, usage text), `Source/SmlXml/README.md`, `Source/Sml.Tests/CommandLineTests.cs`.
- Changed: `Source/SmlXml/CommandLine.cs` (`Run`, `Convert`, new `RunContext` and `ApplyOutputPattern`, `WriteAtomically` creates a missing output folder, usage text), `Source/Sml.Tests/CommandLineTests.cs`, `Source/SmlXml/README.md`, `Source/SmlXml/implementation.md`.

## Build and test results

- `dotnet build SimplifiedMarkupLanguage.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test`: 182/182 passed.
- The real executable was also exercised on Linux with quoted relative patterns (`*.svg`, `art/**/*.svg`, an unmatched pattern, no arguments), simulating how Windows shells pass them.
- `smlxml *.svg` then `smlxml -o *.out.svg *.sml` over all 47 corpus fixtures produced 47 `.out.svg` files; the Python importer found each equal in information to its original.
- `dotnet publish Source/SmlXml/SmlXml.csproj -p:PublishProfile=windows` produced `publish/smlxml/win-x64/smlxml.exe` (PE32+ x86-64) and `publish/smlxml/win-x86/smlxml.exe` (PE32 Intel 80386), each with `LICENSE.md`. This held both from the repository root and from the project folder. The assemblies' CLR headers confirm Any CPU. `windows.pubxml` is the only publish profile and also serves Visual Studio; Visual Studio itself was not run, and neither executable was run on Windows. It was built on Linux and **not run on Windows**.
- `python build_and_test.py`: 47/47 fixtures, 9/9 rejections, 54/54 unit tests, 0 fuzz findings.
- `crosscheck_csharp.py` seeds 1–10 (8,500 documents, 34,000 SML mutants): 0 differences between the implementations.

## Unresolved

- Spec decisions still open:
  - should namespace names be required to be URI references (libxml2 enforces this)?
  - should the spec carry a portability note on XML 1.0 5th-edition name characters?
- Encoding names follow each importer's XML library. The Python importer accepts `UTF8`, while System.Xml may reject it. The cross-check only feeds both importers UTF-8.
- SV-49 (writer-generated aliases) and SV-65 (readable import) are not implemented in either implementation.
- The language name still collides with Standard ML and W3C SML.

## Recommended next step

Run `smlxml` on real Inkscape and Illustrator SVG files on Windows, and decide the open specification questions.
