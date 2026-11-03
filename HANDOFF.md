# Handoff

## Current objective

Provide `smlxml`, a two-way SML ↔ XML/SVG command-line converter in C# on .NET 10, alongside the SML 1.0-SVG-1 specification and its Python prototype and tests.

## Current state

- **`Source/Sml`** (library) implements:
  - the SML reader;
  - the §14.2 canonical writer;
  - an XML importer on System.Xml, with namespace processing off and validation done by `XmlRules`;
  - the XML exporter and document comparison;
  - cleaning with a profile (`SmlProfile`, `ValueLayout`), which only C# has.

  Apart from cleaning, it is a port of `Tests/svg-roundtrip/sml_prototype.py`.
- **`Source/SmlXml`** (`smlxml`) is the command line.
  - It expands wildcards in input names itself (`*`, `?`, `**`; `FileExpander`).
  - It accepts `*` patterns in `--output` (one output per input, `ApplyOutputPattern`).
  - It shows the usage when run without arguments.
  - `--clean <profile>` writes readable, lossy SML (specification §3.2): values with a layout one item per line, CSS and scripts tidied, and the profile's aliases. `svg.smlprofile` beside the executable is the built-in profile; it declares element aliases `g group` and `p para` and code layouts for `style` and `script`. A profile is used only for cleaning.
  - With `--verbose`, it writes every attribute and namespace declaration on its own line.

  Behavior and requirements are in `Source/SmlXml/implementation.md`, usage and the profile format in `Source/SmlXml/README.md`.
- **`Source/Sml.Tests`** has 3,140 xUnit tests:
  - the corpus, compared byte for byte with the Python reference output;
  - both round trips;
  - the negative corpus;
  - C# ports of the CP and key SV tests;
  - command-line tests, including wildcards, output patterns, profiles, and the matcher (`FileExpanderTests`);
  - `ProfileTests`: layouts, the profile format, and cleaning, over every fixture and every file of `Tests/svg-corpus`;
  - `WriterOptionsTests`: vertical attributes, over the same files.
- **`Tests/svg-roundtrip/crosscheck_csharp.py`** compares the C# and Python implementations on random documents, non-canonical SML spellings, and SML mutants.
- **`Tests/svg-corpus`** holds 897 real-world SVG files from 15 MIT- and ISC-licensed projects: icon sets, emoji, every flag of flag-icons, animated icons, and the resvg test suite. The W3C SVG 1.1 test suite was removed, because its files say "All Rights Reserved"; `gather_corpus.py` now refuses such files. `ProfileTests` reads them; the Python harness does not.
  - `gather_corpus.py` downloads the sources at pinned versions, selects the files, and writes them and `THIRD-PARTY-NOTICES.md`.
  - Its `README.md` describes the selection and the expected results.
- **`README.md`** at the root introduces the project on GitHub. It assumes a public repository under the MIT license.
- Solution-wide environment, conventions, decisions, and traps are in `implementation.md`.

## Files changed in the last task (verbatim blocks; `--clean`)

The user's Inkscape `<style>` (CSS indented with tabs, trailing whitespace on six lines, a last line of four spaces) converted to an unreadable quoted string. The user chose both remedies offered, and then replaced `--profile`/`--reflow` with `--clean`.

- **Verbatim blocks (language, both implementations).** Only the closing marker's indentation comes off each payload line; trailing whitespace, extra indentation, and whitespace-only lines are data. A whitespace-only line without that indentation is empty; any other line without it is an error. The canonical writer puts the closing marker level with the payload, writes every value with LF and a visible character (no CR or escaped character) as a block, and quotes values of whitespace only. Such CSS now converts to a verbatim block.
  - Specification: §7 rewritten; §§3.2, 4, 8.3, 8.5, 9, 12.2 notes, 12.3, 14.1, 14.2 rules 3 and 7; SV-45, SV-56, SV-80, CP-14; every block example's closing marker.
  - `sml_prototype.py` (`dedent_block`, `read_block`, `block_marker`, `visible`, `block_tag`, the writers), `fuzz_roundtrip.py` (`VariantWriter` blocks), `test_sml_core.py`, `build_and_test.py` (fixture 82's wording); the reference outputs in `Tests/svg-roundtrip/results/` (closing markers; values that were quoted only because of whitespace are now blocks).
  - `SmlReader.cs` (`ReadBlock`, `DedentBlock`), `SmlWriter.cs` (`Visible`, `BlockTag`, `BlockMarker`, `Block`, `@comments`), `ReaderTests.cs` (new `SV45_SV56_VerbatimBlocks`).
- **`--clean <profile>` (C# only).** Replaces `--profile` and `--reflow`; a profile is used only for cleaning, so there is no exact profile mode. `SmlWriterOptions.Clean` replaces `Profile` and `Reflow`, and `SmlWriter.Write(doc, profile)` cleans.
  - New profile statement `@layout element = code`: cleaning tidies the element's text (`SmlWriter.TidyCode`: trailing whitespace, empty first and last lines, shared indentation) and writes it as a block, laid out where the compact block gets predicted edges. `svg.smlprofile` marks `style` and `script`.
  - The output's first line is `# Cleaned (SML §3.2), not exact: one item per line in …; code tidied in …`.
  - `SmlProfile.cs`, `SmlWriterOptions.cs`, `SmlWriter.cs`, `CommandLine.cs`, `svg.smlprofile`; `ProfileTests.cs` (cleaning tests, `TidiesCode`, `CleansInkscapeStyleSheets`, corpus theories), `CommandLineTests.cs`, `WriterOptionsTests.cs`.
- Docs: `README.md`, `implementation.md` (decisions: verbatim blocks; profiles only for cleaning), `Source/SmlXml/README.md` (Cleaning with a profile), `Source/SmlXml/implementation.md`.

## Build and test results

- `dotnet build SimplifiedMarkupLanguage.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test`: 3,140/3,140 passed, including the byte-for-byte comparison with the regenerated Python reference SML.
- `fuzz_roundtrip.py --docs 2000 --seed 7`: 0 findings (only the known parser divergences).
- `crosscheck_csharp.py` seeds 1–3 with verbatim blocks (3,000 documents, 12,000 SML mutants): 0 differences.
- `smlxml --verbose` over a copy of `Tests/svg-corpus`: the Python prototype read all 893 accepted files back equal to their originals.
- Over a copy of `Tests/svg-corpus` with the real executable, checked by the Python prototype (script not in the repository):
  - canonical `smlxml` output read back equal to the original for all 893 accepted files;
  - `smlxml --clean svg` changed 826 files (code tidied in 8), and the C# and Python readers agreed on every cleaned file.
- The real executable was also exercised on Linux with quoted relative patterns (`*.svg`, `art/**/*.svg`, an unmatched pattern, no arguments), simulating how Windows shells pass them.
- `smlxml *.svg` then `smlxml -o *.out.svg *.sml` over all 47 corpus fixtures produced 47 `.out.svg` files; the Python importer found each equal in information to its original.
- `dotnet publish Source/SmlXml/SmlXml.csproj -p:PublishProfile=windows` produced `publish/smlxml/win-x64/smlxml.exe` (PE32+ x86-64) and `publish/smlxml/win-x86/smlxml.exe` (PE32 Intel 80386), each with `LICENSE.md` and `svg.smlprofile`. This held both from the repository root and from the project folder. The assemblies' CLR headers confirm Any CPU. `windows.pubxml` is the only publish profile and also serves Visual Studio; Visual Studio itself was not run, and neither executable was run on Windows. Visual Studio's Publish page shows "Target runtime: win-x64" for the profile; that is expected and documented, and each Publish writes both builds.
- `python build_and_test.py`: 53/53 fixtures, 9/9 rejections, 61/61 unit tests, 0 fuzz findings.
- `fuzz_roundtrip.py` seed 32 (1,500 documents) with verbatim blocks: 0 findings. Seed 23 (1,500 documents) after the `@block` order change: 0 findings. Seed 21 (1,500 documents) with `@node` variants: 0 findings (only the known parser divergences). 400 variants had 393 `@node` declarations, 171 of them in declaration sections, and 236 `@element` openers; all read back equal.
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

- Run `smlxml` on real Inkscape and Illustrator SVG files on Windows, with and without `--clean svg`.
- Decide the open specification questions.
- Consider running `Tests/svg-corpus` in `build_and_test.py` too.
