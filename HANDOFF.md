# Handoff

## Current objective

Provide `smlxml`, a two-way SML ↔ XML/SVG command-line converter in C# on .NET 10, alongside the SML 1.0-SVG-3 specification and its Python prototype and tests.

## Current state

- **`Source/Sml`** (library) implements:
  - the SML reader;
  - the §14.2 canonical writer;
  - an XML importer on System.Xml, with namespace processing off and validation done by `XmlRules`;
  - the XML exporter and document comparison;
  - writer profiles (`SmlProfile`, `ValueLayout`), which only C# has.

  Apart from the profiles, it is a port of `Tests/svg-roundtrip/sml_prototype.py`.
- **`Source/SmlXml`** (`smlxml`) is the command line.
  - It expands wildcards in input names itself (`*`, `?`, `**`; `FileExpander`).
  - It accepts `*` patterns in `--output` (one output per input, `ApplyOutputPattern`).
  - It shows the usage when run without arguments.
  - It writes SML with a profile (`--profile svg`, `svg.smlprofile` beside the executable). That output is exact by default, or lossy with `--reflow` (specification §3.2).

  Behavior and requirements are in `Source/SmlXml/implementation.md`, usage and the profile format in `Source/SmlXml/README.md`.
- **`Source/Sml.Tests`** has 1,337 xUnit tests:
  - the corpus, compared byte for byte with the Python reference output;
  - both round trips;
  - the negative corpus;
  - C# ports of the CP and key SV tests;
  - command-line tests, including wildcards, output patterns, profiles, and the matcher (`FileExpanderTests`);
  - `ProfileTests`: layouts, the profile format, and the profile writer in both modes, over every fixture and every file of `Tests/svg-corpus`.
- **`Tests/svg-roundtrip/crosscheck_csharp.py`** compares the C# and Python implementations on random documents, non-canonical SML spellings, and SML mutants.
- **`Tests/svg-corpus`** holds 505 real-world SVG files from 16 MIT-, ISC-, and BSD-licensed projects: icon sets, emoji, flags, animated icons, and the W3C SVG 1.1 and resvg test suites. `ProfileTests` reads them; the Python harness does not.
  - `gather_corpus.py` downloads the sources at pinned versions, selects the files, and writes them and `THIRD-PARTY-NOTICES.md`.
  - Its `README.md` describes the selection and the expected results.
- **`README.md`** at the root introduces the project on GitHub. It assumes a public repository under the MIT license.
- Solution-wide environment, conventions, decisions, and traps are in `implementation.md`.

## Files changed in the last task (smlxml profiles)

- New: `Source/Sml/SmlProfile.cs`, `Source/Sml/ValueLayout.cs`, `Source/SmlXml/svg.smlprofile`, `Source/Sml.Tests/ProfileTests.cs`.
- `Source/Sml/SmlWriter.cs`:
  - new overload `Write(doc, profile, reflow)`;
  - new `Effective`, `Segmented`, `PlanAliases`, `QNameOnRoot`, `AttributeResolvesOnRoot`, `Attribute`, `Block`;
  - `Write`, `Value`, and `Element` adjusted.

  Without a profile the canonical output is unchanged.
- `Source/SmlXml/CommandLine.cs`: `-p`/`--profile`, `-r`/`--reflow`, `FindProfile`, `RunContext`, `Convert`, usage text.
- `Source/SmlXml/SmlXml.csproj`: copies `*.smlprofile` to build and publish output.
- `Source/Sml.Tests/CommandLineTests.cs`: profile tests.
- `Documents/SML-Language-Specification.md`: new §3.2 (readable attribute reflow), with §1.2, §3.1, §8.5, the change list, §15, and §17 adjusted.
- Docs: `Source/SmlXml/README.md` (Profiles section), `Source/SmlXml/implementation.md`, `README.md`, `implementation.md`.

## Build and test results

- `dotnet build SimplifiedMarkupLanguage.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test`: 1,337/1,337 passed.
- Profiles over a copy of `Tests/svg-corpus` with the real executable, checked by the Python prototype (script not in the repository):
  - `smlxml -p svg` output read back equal to the original for all 495 accepted files;
  - `smlxml -p svg -r` reflowed values in 365 files;
  - the C# and Python readers agreed on every reflowed file.
- The real executable was also exercised on Linux with quoted relative patterns (`*.svg`, `art/**/*.svg`, an unmatched pattern, no arguments), simulating how Windows shells pass them.
- `smlxml *.svg` then `smlxml -o *.out.svg *.sml` over all 47 corpus fixtures produced 47 `.out.svg` files; the Python importer found each equal in information to its original.
- `dotnet publish Source/SmlXml/SmlXml.csproj -p:PublishProfile=windows` produced `publish/smlxml/win-x64/smlxml.exe` (PE32+ x86-64) and `publish/smlxml/win-x86/smlxml.exe` (PE32 Intel 80386), each with `LICENSE.md` and `svg.smlprofile`. This held both from the repository root and from the project folder. The assemblies' CLR headers confirm Any CPU. `windows.pubxml` is the only publish profile and also serves Visual Studio; Visual Studio itself was not run, and neither executable was run on Windows. Visual Studio's Publish page shows "Target runtime: win-x64" for the profile; that is expected and documented, and each Publish writes both builds.
- `python build_and_test.py`: 47/47 fixtures, 9/9 rejections, 54/54 unit tests, 0 fuzz findings.
- `crosscheck_csharp.py` seeds 1–10 (8,500 documents, 34,000 SML mutants): 0 differences between the implementations.
- `Tests/svg-corpus` was checked by hand, with a script that is not in the repository:
  - Python and C# both accept 495 of the 505 files. Each round-trips XML → SML → XML to the same document, and the canonical SML is byte-identical between the implementations.
  - Both reject the same 10 files, which have DTD internal subsets.
  - `smlxml "svg-corpus/**/*.svg"` followed by `smlxml -o "*.out.svg" "svg-corpus/**/*.sml"` on a copy gave the same result.
  - `gather_corpus.py --dest <empty folder>` downloaded all 16 sources again and reproduced the 505 committed files byte for byte.

## Unresolved

- Spec decisions still open:
  - should namespace names be required to be URI references (libxml2 enforces this)?
  - should the spec carry a portability note on XML 1.0 5th-edition name characters?
- Encoding names follow each importer's XML library. The Python importer accepts `UTF8`, while System.Xml may reject it. The cross-check only feeds both importers UTF-8.
- SV-49 (writer-generated aliases) is covered only in C#, by `ProfileTests`. SV-65 (readable whitespace normalization, §3.1) is implemented in neither implementation. Profiles and the §3.2 reflow exist only in C#.
- Reflow never splits a single path command, so a long implicit coordinate run after one command stays on one line. One line in the Spanish flag is over 150 characters.
- The language name still collides with Standard ML and W3C SML.
- The SVG corpus has no Inkscape or Illustrator files with editor metadata. None were found under an MIT-like license on hosts reachable from Claude Cloud.

## Recommended next step

- Run `smlxml` on real Inkscape and Illustrator SVG files on Windows, with and without `--profile svg`.
- Decide the open specification questions.
- Consider running `Tests/svg-corpus` in `build_and_test.py` too.
