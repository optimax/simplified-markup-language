# Handoff

## Current objective

Provide `smlxml`, a two-way SML ↔ XML/SVG command-line converter in C# on .NET 10, alongside the SML 1.0-SVG-1 specification and its Python prototype and tests.

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
  - A profile can also declare element aliases (`@node`, specification §8.7), which that output then uses; `svg.smlprofile` declares `g group` and `p para`.
  - With `--verbose`, it writes every attribute and namespace declaration on its own line.

  Behavior and requirements are in `Source/SmlXml/implementation.md`, usage and the profile format in `Source/SmlXml/README.md`.
- **`Source/Sml.Tests`** has 2,036 xUnit tests:
  - the corpus, compared byte for byte with the Python reference output;
  - both round trips;
  - the negative corpus;
  - C# ports of the CP and key SV tests;
  - command-line tests, including wildcards, output patterns, profiles, and the matcher (`FileExpanderTests`);
  - `ProfileTests`: layouts, the profile format, and the profile writer in both modes, over every fixture and every file of `Tests/svg-corpus`;
  - `WriterOptionsTests`: vertical attributes, over the same files.
- **`Tests/svg-roundtrip/crosscheck_csharp.py`** compares the C# and Python implementations on random documents, non-canonical SML spellings, and SML mutants.
- **`Tests/svg-corpus`** holds 897 real-world SVG files from 15 MIT- and ISC-licensed projects: icon sets, emoji, every flag of flag-icons, animated icons, and the resvg test suite. The W3C SVG 1.1 test suite was removed, because its files say "All Rights Reserved"; `gather_corpus.py` now refuses such files. `ProfileTests` reads them; the Python harness does not.
  - `gather_corpus.py` downloads the sources at pinned versions, selects the files, and writes them and `THIRD-PARTY-NOTICES.md`.
  - Its `README.md` describes the selection and the expected results.
- **`README.md`** at the root introduces the project on GitHub. It assumes a public repository under the MIT license.
- Solution-wide environment, conventions, decisions, and traps are in `implementation.md`.

## Files changed in the last task (revision reset to 1.0-SVG-1)

The user reset the specification's revision to 1.0-SVG-1. The content is unchanged; only the identifier is. The syntax is defined as it goes, with no compatibility with earlier forms, and nothing refers to earlier revisions.

- Readers accept `@sml 1.0-SVG-1` (and `@sml 1.0` or no header) and reject every other draft identifier; writers emit `@sml 1.0-SVG-1`. CP-20 uses `1.0-SVG-2` as the other identifier.
- `smlxml --version` prints `smlxml 1.0.0 (SML 1.0-SVG-1)`.
- Changed: the specification (header, dated 2026-11-03; §11; §14.2 rule 2; CP-20; the closing status), `SmlReader.Version`, `SML_VERSION` in `sml_prototype.py`, the docstrings of the Python scripts, every test that writes or checks the header, the 53 reference outputs in `Tests/svg-roundtrip/results/` (their first line only), `SmlXml.csproj` (`Description`), and the READMEs, `implementation.md` files, and this file.
- Also current: the corpus holds every flag of flag-icons (897 files), `|` is an ordinary character, both alias declarations put the alias last (`@node g group`, `@block path .d data`), and `svg.smlprofile` declares `@node g group` and `@node p para` (the latter names an SVG-namespace `p`, which the user chose to leave as is).

## Build and test results

- `dotnet build SimplifiedMarkupLanguage.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test`: 4,084/4,084 passed, including the byte-for-byte comparison with the regenerated Python reference SML.
- `fuzz_roundtrip.py --docs 2000 --seed 7`: 0 findings (only the known parser divergences).
- `crosscheck_csharp.py` seeds 1–3 with `@node` variants and the new `@block` order (3,000 documents, 12,000 SML mutants): 0 differences.
- `smlxml --verbose` over a copy of `Tests/svg-corpus`: the Python prototype read all 893 accepted files back equal to their originals.
- Profiles over a copy of `Tests/svg-corpus` with the real executable, checked by the Python prototype (script not in the repository), with all flags and the profile's `@node g group` (used in 830 files):
  - `smlxml -p svg` output read back equal to the original for all 893 accepted files;
  - `smlxml -p svg -r` reflowed values in 823 files;
  - the C# and Python readers agreed on every reflowed file.
- The real executable was also exercised on Linux with quoted relative patterns (`*.svg`, `art/**/*.svg`, an unmatched pattern, no arguments), simulating how Windows shells pass them.
- `smlxml *.svg` then `smlxml -o *.out.svg *.sml` over all 47 corpus fixtures produced 47 `.out.svg` files; the Python importer found each equal in information to its original.
- `dotnet publish Source/SmlXml/SmlXml.csproj -p:PublishProfile=windows` produced `publish/smlxml/win-x64/smlxml.exe` (PE32+ x86-64) and `publish/smlxml/win-x86/smlxml.exe` (PE32 Intel 80386), each with `LICENSE.md` and `svg.smlprofile`. This held both from the repository root and from the project folder. The assemblies' CLR headers confirm Any CPU. `windows.pubxml` is the only publish profile and also serves Visual Studio; Visual Studio itself was not run, and neither executable was run on Windows. Visual Studio's Publish page shows "Target runtime: win-x64" for the profile; that is expected and documented, and each Publish writes both builds.
- `python build_and_test.py`: 53/53 fixtures, 9/9 rejections, 61/61 unit tests, 0 fuzz findings.
- `fuzz_roundtrip.py` seed 23 (1,500 documents) after the `@block` order change and the removal of the old-form checks: 0 findings. Seed 21 (1,500 documents) with `@node` variants: 0 findings (only the known parser divergences). 400 variants had 393 `@node` declarations, 171 of them in declaration sections, and 236 `@element` openers; all read back equal.
- `smlxml -p` with `svg.smlprofile` plus six `@node` aliases (one colliding with real `desc` elements) over a copy of `Tests/svg-corpus` and the fixtures: the Python prototype read all 434 outputs back equal to their SVG files; 433 declared aliases, used 5,213 times, and no colliding alias was used.
- `Tests/svg-corpus` was checked by hand, with a script that is not in the repository:
  - Python and C# both accept 893 of the 897 files, including all 542 flags. Each round-trips XML → SML → XML to the same document, and the canonical SML is byte-identical between the implementations.
  - Both reject the same 4 files (resvg), which have DTD internal subsets.
  - `smlxml "svg-corpus/**/*.svg"` followed by `smlxml -o "*.out.svg" "svg-corpus/**/*.sml"` on a copy gave the same result.
  - `gather_corpus.py --dest <empty folder>` downloaded all 15 sources again. Every folder but `flag-icons` came out byte for byte as committed, the 30 flags already in the corpus were among the 542, and the notices differed only in the flag count.

## Unresolved

- Spec decisions still open:
  - should namespace names be required to be URI references (libxml2 enforces this)?
  - should the spec carry a portability note on XML 1.0 5th-edition name characters?
- Encoding names follow each importer's XML library. The Python importer accepts `UTF8`, while System.Xml may reject it. The cross-check only feeds both importers UTF-8.
- SV-49 (writer-generated aliases) is covered only in C#, by `ProfileTests`. The writer side of element aliases is likewise C#-only; the Python writer never emits `@node`. SV-65 (readable whitespace normalization, §3.1) is implemented in neither implementation. Profiles and the §3.2 reflow exist only in C#.
- Text whose first line follows its start tag directly, such as the second `p` of the W3C example (`<p>The diagram…` with the next line indented eight spaces), has no laid-out layout. It stays a literal `= """` block in the normal form, with the continuation line's extra indentation visible, because removing it would lose information.
- Reflow never splits a single path command, so a long implicit coordinate run after one command stays on one line. One line in the Spanish flag is over 150 characters.
- The language name still collides with Standard ML and W3C SML.
- The SVG corpus has no Inkscape or Illustrator files with editor metadata. None were found under an MIT-like license on hosts reachable from Claude Cloud.

## Recommended next step

- Run `smlxml` on real Inkscape and Illustrator SVG files on Windows, with and without `--profile svg`.
- Decide the open specification questions.
- Consider running `Tests/svg-corpus` in `build_and_test.py` too.
