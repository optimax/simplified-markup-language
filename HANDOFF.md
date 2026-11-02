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
- **`Source/SmlXml`** (`smlxml`) is the command line; behavior and requirements are in `Source/SmlXml/implementation.md`, usage in `Source/SmlXml/README.md`.
- **`Source/Sml.Tests`** has 152 xUnit tests:
  - the corpus, compared byte for byte with the Python reference output;
  - both round trips;
  - the negative corpus;
  - C# ports of the CP and key SV tests;
  - command-line tests.
- **`Tests/svg-roundtrip/crosscheck_csharp.py`** compares the C# and Python implementations on random documents, non-canonical SML spellings, and SML mutants.
- Solution-wide environment, conventions, decisions, and traps are in `implementation.md`.

## Files changed in this task

- New:
  - `SimplifiedMarkupLanguage.slnx`
  - `implementation.md`
  - `Source/Sml/*`, `Source/SmlXml/*` (including `Properties/PublishProfiles/win-x64.pubxml`), `Source/Sml.Tests/*`
  - `Tests/svg-roundtrip/crosscheck_csharp.py`
- Changed:
  - `Tests/svg-roundtrip/sml_prototype.py`: `@use` versions take ASCII digits only, and `@block` operands split on spaces and tabs only, matching the specification and the C# reader;
  - `Tests/svg-roundtrip/README.md`;
  - `Documents/SML-Language-Specification.md` (§15).

## Build and test results

- `dotnet build SimplifiedMarkupLanguage.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test`: 152/152 passed.
- `dotnet publish Source/SmlXml/SmlXml.csproj -p:PublishProfile=win-x64` produced `publish/smlxml/smlxml.exe` (252 KB, PE32+ x86-64 console). It was built on Linux and **not run on Windows**.
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
