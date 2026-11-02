# Handoff

## Current objective

Provide `smlxml`, a two-way SML ↔ XML/SVG command-line converter in C# on .NET 10, alongside the SML 1.0-SVG-4 specification and its Python prototype and tests.

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
  - With `--verbose`, it writes every attribute and namespace declaration on its own line.

  Behavior and requirements are in `Source/SmlXml/implementation.md`, usage and the profile format in `Source/SmlXml/README.md`.
- **`Source/Sml.Tests`** has 1,587 xUnit tests:
  - the corpus, compared byte for byte with the Python reference output;
  - both round trips;
  - the negative corpus;
  - C# ports of the CP and key SV tests;
  - command-line tests, including wildcards, output patterns, profiles, and the matcher (`FileExpanderTests`);
  - `ProfileTests`: layouts, the profile format, and the profile writer in both modes, over every fixture and every file of `Tests/svg-corpus`;
  - `WriterOptionsTests`: vertical attributes, over the same files.
- **`Tests/svg-roundtrip/crosscheck_csharp.py`** compares the C# and Python implementations on random documents, non-canonical SML spellings, and SML mutants.
- **`Tests/svg-corpus`** holds 385 real-world SVG files from 15 MIT- and ISC-licensed projects: icon sets, emoji, flags, animated icons, and the resvg test suite. The W3C SVG 1.1 test suite was removed, because its files say "All Rights Reserved"; `gather_corpus.py` now refuses such files. `ProfileTests` reads them; the Python harness does not.
  - `gather_corpus.py` downloads the sources at pinned versions, selects the files, and writes them and `THIRD-PARTY-NOTICES.md`.
  - Its `README.md` describes the selection and the expected results.
- **`README.md`** at the root introduces the project on GitHub. It assumes a public repository under the MIT license.
- Solution-wide environment, conventions, decisions, and traps are in `implementation.md`.

## Files changed in the last task (per-element steps and laid-out text)

The user's W3C example, a `d:testDescription` whose children are indented by four spaces and whose `p` text is pretty-printed, now converts to readable SML: `@indent 4` in its declaration section and `p = """` blocks instead of `= "\n        "` statements and quoted text. The language stays 1.0-SVG-4 (change-list items 6 and 7).

- **Relative indentation.** An element's indentation is its parent's indentation plus its parent's step. The step is the `@indent` unit, or a per-element `@indent N` / `@indent tab` in the declaration section. A per-element step needs the header, may appear once, and is not inherited. Documents without per-element steps get the same whitespace as before.
- **Laid-out text blocks.** A compact `name = """` in a laid-out position gets predicted edges, like a laid-out comment block: LF, the lines indented one unit deeper than the element, LF and the element's indentation. The normal form's `= """` stays literal. The canonical writer uses the normal form for text that needs a literal block but lacks the layout, and quotes other multi-line text there. This changed `results/04-whitespace.sml`: its `desc` now uses the normal form.
- **Canonical steps.** The unit is still chosen with every step equal to the unit (rule 9). An element that does not match the unit but matches the step its first whitespace suggests (0–16 spaces or a tab) gets `@indent` as the last statement of its declaration section.
- Files:
  - `Documents/SML-Language-Specification.md`: change list, §§5, 5.1, 6.4, 7, 11, 12.2, 12.3, 14.1, 14.2 (rules 6, 7, 9), and §15 rows SV-81 and SV-82.
  - `Tests/svg-roundtrip/sml_prototype.py`: reader frames carry indentation and step; `lay_out_block`, `laid_out_value`, `matches_layout`, `step_from`; the writer passes indentation down.
  - `Tests/svg-roundtrip/build_and_test.py`: fixtures `81-local-indent.svg` and `82-laid-out-text.svg`.
  - `Tests/svg-roundtrip/test_sml_core.py`: SV-81 and SV-82.
  - `Tests/svg-roundtrip/fuzz_roundtrip.py`: the generator makes per-element steps and laid-out text and comments.
  - `Tests/svg-roundtrip/results/`: regenerated.
  - `Source/Sml/SmlReader.cs`, `Source/Sml/SmlWriter.cs`: the same changes in C#.
  - `Source/Sml.Tests/ReaderTests.cs`: `SV81_LocalIndent` and `SV82_LaidOutText`.

## Build and test results

- `dotnet build SimplifiedMarkupLanguage.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test`: 1,587/1,587 passed, including the byte-for-byte comparison with the regenerated Python reference SML.
- `fuzz_roundtrip.py --docs 2000 --seed 7`: 0 findings (only the known parser divergences).
- `crosscheck_csharp.py` seeds 1–3 (3,000 documents, 12,000 SML mutants): 0 differences.
- `smlxml --verbose` over a copy of `Tests/svg-corpus`: the Python prototype read all 381 accepted files back equal to their originals.
- Profiles over a copy of `Tests/svg-corpus` with the real executable, checked by the Python prototype (script not in the repository):
  - `smlxml -p svg` output read back equal to the original for all 381 accepted files;
  - `smlxml -p svg -r` reflowed values in 311 files;
  - the C# and Python readers agreed on every reflowed file.
- The real executable was also exercised on Linux with quoted relative patterns (`*.svg`, `art/**/*.svg`, an unmatched pattern, no arguments), simulating how Windows shells pass them.
- `smlxml *.svg` then `smlxml -o *.out.svg *.sml` over all 47 corpus fixtures produced 47 `.out.svg` files; the Python importer found each equal in information to its original.
- `dotnet publish Source/SmlXml/SmlXml.csproj -p:PublishProfile=windows` produced `publish/smlxml/win-x64/smlxml.exe` (PE32+ x86-64) and `publish/smlxml/win-x86/smlxml.exe` (PE32 Intel 80386), each with `LICENSE.md` and `svg.smlprofile`. This held both from the repository root and from the project folder. The assemblies' CLR headers confirm Any CPU. `windows.pubxml` is the only publish profile and also serves Visual Studio; Visual Studio itself was not run, and neither executable was run on Windows. Visual Studio's Publish page shows "Target runtime: win-x64" for the profile; that is expected and documented, and each Publish writes both builds.
- `python build_and_test.py`: 53/53 fixtures, 9/9 rejections, 60/60 unit tests, 0 fuzz findings.
- `fuzz_roundtrip.py` seeds 13 (1,500 documents) and 14 (1,000 documents): 0 findings (only the known parser divergences). 400 generated documents had 18 per-element steps and 15 laid-out text blocks.
- `Tests/svg-corpus` was checked by hand, with a script that is not in the repository:
  - Python and C# both accept 381 of the 385 files. Each round-trips XML → SML → XML to the same document, and the canonical SML is byte-identical between the implementations. Three files use the new forms (8 per-element steps, 1 laid-out text block).
  - Both reject the same 4 files (resvg), which have DTD internal subsets.
  - `smlxml "svg-corpus/**/*.svg"` followed by `smlxml -o "*.out.svg" "svg-corpus/**/*.sml"` on a copy gave the same result.
  - `gather_corpus.py --dest <empty folder>` downloaded all 15 sources again and reproduced the 385 committed files and the notices byte for byte.

## Unresolved

- Spec decisions still open:
  - should namespace names be required to be URI references (libxml2 enforces this)?
  - should the spec carry a portability note on XML 1.0 5th-edition name characters?
- Encoding names follow each importer's XML library. The Python importer accepts `UTF8`, while System.Xml may reject it. The cross-check only feeds both importers UTF-8.
- SV-49 (writer-generated aliases) is covered only in C#, by `ProfileTests`. SV-65 (readable whitespace normalization, §3.1) is implemented in neither implementation. Profiles and the §3.2 reflow exist only in C#.
- Text whose first line follows its start tag directly, such as the second `p` of the W3C example (`<p>The diagram…` with the next line indented eight spaces), has no laid-out layout. It stays a literal `= """` block in the normal form, with the continuation line's extra indentation visible, because removing it would lose information.
- Reflow never splits a single path command, so a long implicit coordinate run after one command stays on one line. One line in the Spanish flag is over 150 characters.
- The language name still collides with Standard ML and W3C SML.
- The SVG corpus has no Inkscape or Illustrator files with editor metadata. None were found under an MIT-like license on hosts reachable from Claude Cloud.

## Recommended next step

- Run `smlxml` on real Inkscape and Illustrator SVG files on Windows, with and without `--profile svg`.
- Decide the open specification questions.
- Consider running `Tests/svg-corpus` in `build_and_test.py` too.
