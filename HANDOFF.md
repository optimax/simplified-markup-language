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
  - `--clean <profile>` (alias `--tidy`) writes readable, lossy SML (specification §§3.1–3.2): formatting whitespace replaced by the default two-space indentation whatever the input's, values with a layout one item per line, multi-line text and comments tidied, and the profile's aliases. `svg.smlprofile` beside the executable is the built-in profile; it declares element aliases `g group` and `p para`. A profile is used only for cleaning.
  - With `--verbose`, it writes every attribute and namespace declaration on its own line.

  Behavior and requirements are in `Source/SmlXml/implementation.md`, usage and the profile format in `Source/SmlXml/README.md`.
- **`Source/Sml.Tests`** has 3,414 xUnit tests:
  - the corpus, compared byte for byte with the Python reference output;
  - both round trips;
  - the negative corpus;
  - C# ports of the CP and key SV tests;
  - command-line tests, including wildcards, output patterns, profiles, and the matcher (`FileExpanderTests`);
  - `ProfileTests`: layouts, the profile format, and cleaning, over every fixture and every file of `Tests/svg-corpus`;
  - `WriterOptionsTests`: vertical attributes, over the same files.
- **`Tests/svg-roundtrip/crosscheck_csharp.py`** compares the C# and Python implementations on random documents, non-canonical SML spellings, and SML mutants.
- **`Tests/svg-corpus`** holds 985 real-world SVG files from 21 projects around the world, all gathered by `gather_corpus.py` under MIT, ISC, Apache-2.0, CC0, or public domain. The projects are icon sets, emoji, every flag of flag-icons, animated icons, the resvg test suite, and, added last at the user's request to replace the hand-added `other` folder (whose files had no known license): Apache Batik's samples (maps, international text, ISO-8859-1), Noto emoji (Adobe Illustrator exports), region flags (Inkscape files with editor metadata), VK icons (Sketch exports, Russia), Ant Design icons (China), OpenStreetMap Carto symbols, and two draw.io diagrams. The W3C SVG 1.1 test suite was removed, because its files say "All Rights Reserved"; `gather_corpus.py` now refuses such files. `ProfileTests` reads them; the Python harness does not.
  - `gather_corpus.py` downloads the sources at pinned versions, selects the files, and writes them and `THIRD-PARTY-NOTICES.md`.
  - Its `README.md` describes the selection and the expected results.
- **`README.md`** at the root introduces the project on GitHub. It assumes a public repository under the MIT license.
- Solution-wide environment, conventions, decisions, and traps are in `implementation.md`.

## Files changed in the last task (indentation: exact writer and `--clean`)

The user's fire SVG (copied out of HTML, so its root's children sit at 4 spaces and `</svg>` at 2) converted with every whitespace node spelled out.

- **Exact writer (both implementations; canonical form, §14.2 rule 9).** The user chose a writer-only fix over a `@margin` header. An exact element may now state its own step, taken from its first whitespace, when that lays out more of its child elements; and `@indent` is chosen by simulating the writer for each unit (most elements laid out, then fewest per-element `@indent`, then the usual tie order) instead of with the absolute depth × unit model. The fire SVG now needs `@indent 2`, `@indent 4` on the root, and three whitespace lines on the root only. Fixture `84-nested-indentation.svg`; SV-84 in both test suites, including an exact element with a zero step (a Python bug the cross-check found: `local or unit` treated a zero step as none).
- **`--clean` (C# only; §§3.1–3.2).** The user asked for cleaning that ignores the input's indentation, writes no `@margin`/`@indent`/whitespace lines, and gives pretty-printed XML back:
  - formatting whitespace in eligible elements is replaced by the prediction of `@indent 2` (`FindNormalized`); in SVG text content only where every gap already has a space or tab (and, nested in text, the edges), since SVG 1.1 renders a bare line break as nothing; under `xml:space="preserve"` only in SVG elements outside text content (SVG applies `xml:space` to text only; the user found Batik's `mapSpain.svg`, wholly under `preserve`, full of whitespace lines: 1,184 before, 36 after, all in `text` where they render); never in `foreignObject`. §3.1's eligibility now covers every namespace, not only SVG's;
  - multi-line text outside SVG text content and multi-line comments are tidied (`TidyText`: line-end whitespace, empty first and last lines, runs of empty lines, shared indentation) and written as laid-out blocks; this replaces the `@layout element = code` profile statement, which is removed;
  - `--tidy` is an alias for `--clean`;
  - Over the corpus, the only whitespace-only text statements left under `--clean` are 9 inside resvg's text-layout tests, where they can change rendering.
- Files: `Documents/SML-Language-Specification.md` (§§3.1, 3.2, 6.4, 14.2 rule 9; SV-65, SV-84), `sml_prototype.py` (`layout_step`, `laid_out_children`, `plan`, `choose_indent`), `build_and_test.py` (fixture 84), `test_sml_core.py`, `SmlWriter.cs` (`Plan`, `ChooseIndent`, `FindNormalized`, `TidyText`, comment tidying, header), `SmlProfile.cs` (code layout removed), `CommandLine.cs` (`--tidy`, usage), `svg.smlprofile`, `ReaderTests.cs`, `ProfileTests.cs` (cleaning tests, corpus theory with an independent `Normalize`), `CommandLineTests.cs`, `WriterOptionsTests.cs`, both READMEs, both `implementation.md`.

- **Follow-ups (same session).**
  - Cleaned output no longer starts with a `# Cleaned … not exact` comment. Instead its header is `@sml 1.0-SVG-1 tidy`: `@sml VERSION [tidy]` (§11, grammar, CP-20). Both readers accept `tidy` and ignore it; only cleaning writes it, always.
  - **Two-space indentation is the default** (§6.4): without an `@indent` header the reader predicts two spaces, so both writers omit `@indent 2`. `@indent none` (header only) turns the model off; the writer writes it only when the default would change the document (`default_matters`/`DefaultMatters`: an element with a child element, a multi-line prolog or epilog comment, or multi-line root text). §14.2 rules 2 and 9, SV-60, the grammar, and the examples changed; test inputs that need exact reading start with `@indent none`. Over the corpus, 634 of the 972 accepted files have no `@indent` line in their canonical SML, and 207 have `@indent none`.
  - **CDATA for style sheets and scripts** (§9; SV-73b in both suites; the user's request). XML output writes the text of `style` and `script` as `<![CDATA[…]]>` when it contains `<`, `&`, or `>` (`_text_xml` / `XmlExporter.Text`), splitting `]]>`; text with a CR stays escaped.
  - **Attributes keep their order** (§§2, 3, 14.2 rule 4; CP-23, CP-25; the user reported the sorted order as a bug and asked for a switch). Both writers keep namespace declarations, then attributes, in document order; `DocumentComparer` and `signature` compare the order; the fuzzer no longer shuffles attributes in its SML variants. `smlxml --sort-attributes` (`SmlWriterOptions.SortAttributes`) gives the old order of Canonical XML.
  - **`standalone="no"` is no flag** (§11, SV-30; the user chose this over making `no` the SML default, since 963 of the 974 corpus files then had no flag; 944 of 985 now). Importers and readers read `standalone="no"` and `@standalone no` as none; writers write only `@standalone yes` / `standalone="yes"`. SV-30 tests in both suites; the fuzzer writes `@standalone no` for documents without a flag.
  - **Comments are normalized** (§§2, 3, 9; the user's rules). Both importers and both SML readers trim a comment and remove the indentation its lines share (`comment_text` / `XmlRules.CommentText`; an XML comment starting on the `<!--` line has only its later lines dedented). XML output writes `<!-- data -->`, `<!---->` when empty, and multi-line comments pretty-printed one step deeper than the comment, the step taken from the document (`_comment_xml` / `XmlExporter.Comment`), so pretty-printed comments round-trip byte for byte. A comment may now end with `-`. Laid-out comment blocks are gone: a `@rem` block is always literal. `--clean` tidies comments with `TidyText`. `@comments` lines are trimmed too. Fixture 78 is now `78-multi-line-comment.svg`; SV-54, SV-77 to SV-80 changed in both test suites.
  - `@rem` is the comment directive; `@comment` is a synonym that both readers accept (§9). Both writers write only `@rem`, so every reference output with a comment changed. The run form stays `@comments """` (the user's choice). The fuzzer writes both spellings; `Tools/NodepadPlusPlus-SML-Syntax.xml` colors `@rem` too.

## Build and test results

- `dotnet build SimplifiedMarkupLanguage.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test`: 3,414/3,414 passed (with the 985 corpus files), including the byte-for-byte comparison with the regenerated Python reference SML.
- `fuzz_roundtrip.py --docs 2000 --seed 7`: 0 findings (only the known parser divergences).
- With normalized and pretty-printed comments: `crosscheck_csharp.py --seed 8` (1,000 documents, 4,000 mutants) and `fuzz_roundtrip.py --docs 1500 --seed 39`: 0 findings; the fuzzer also writes comments with blank lines around them and extra indentation on every line, which readers must remove. Earlier, with trimming only: seed 6 and seed 37, 0 findings. Python and C# byte-identical over the corpus; `--clean svg` output read back by Python for all 972 accepted files.
- With the default indentation: `crosscheck_csharp.py --seed 5` (1,000 documents, 4,000 mutants) and `fuzz_roundtrip.py --docs 1500 --seed 35`: 0 findings; Python and C# byte-identical over the corpus (972 accepted, the same 4 rejected); `--clean svg` output read back by Python for all 972.
- `crosscheck_csharp.py` seeds 1–4 with the new indentation choice (4,000 documents, 16,000 SML mutants): 0 differences (seed 2 first found the zero-step bug, fixed).
- `smlxml --verbose` over a copy of `Tests/svg-corpus`: the Python prototype read all 893 accepted files back equal to their originals.
- Over a copy of `Tests/svg-corpus` with the real executable, checked by the Python prototype (script not in the repository):
  - canonical `smlxml` output read back equal to the original for all 893 accepted files;
  - `smlxml --clean svg` changed all 893 files (formatting whitespace normalized in each), and the C# and Python readers agreed on every cleaned file.
  - Exact output changed for 2 corpus files with the new indentation choice.
- The real executable was also exercised on Linux with quoted relative patterns (`*.svg`, `art/**/*.svg`, an unmatched pattern, no arguments), simulating how Windows shells pass them.
- `smlxml *.svg` then `smlxml -o *.out.svg *.sml` over all 47 corpus fixtures produced 47 `.out.svg` files; the Python importer found each equal in information to its original.
- `dotnet publish Source/SmlXml/SmlXml.csproj -p:PublishProfile=windows` produced `publish/smlxml/win-x64/smlxml.exe` (PE32+ x86-64) and `publish/smlxml/win-x86/smlxml.exe` (PE32 Intel 80386), each with `LICENSE.md` and `svg.smlprofile`. This held both from the repository root and from the project folder. The assemblies' CLR headers confirm Any CPU. `windows.pubxml` is the only publish profile and also serves Visual Studio; Visual Studio itself was not run, and neither executable was run on Windows. Visual Studio's Publish page shows "Target runtime: win-x64" for the profile; that is expected and documented, and each Publish writes both builds.
- `python build_and_test.py`: 54/54 fixtures, 9/9 rejections, 62/62 unit tests, 0 fuzz findings.
- `fuzz_roundtrip.py` seeds 33 and 34 (1,500 documents each) with the new indentation choice: 0 findings. Seed 32 with verbatim blocks: 0 findings. Seed 23 (1,500 documents) after the `@block` order change: 0 findings. Seed 21 (1,500 documents) with `@node` variants: 0 findings (only the known parser divergences). 400 variants had 393 `@node` declarations, 171 of them in declaration sections, and 236 `@element` openers; all read back equal.
- `smlxml -p` with `svg.smlprofile` plus six `@node` aliases (one colliding with real `desc` elements) over a copy of `Tests/svg-corpus` and the fixtures: the Python prototype read all 434 outputs back equal to their SVG files; 433 declared aliases, used 5,213 times, and no colliding alias was used.
- With the new corpus sources: `gather_corpus.py --dest <scratch>` reproduced the 15 earlier folders byte for byte. Both implementations accept 981 of the 985 files (the same 4 resvg rejections); every accepted file round-trips XML → SML → XML, the C# and Python canonical SML is byte-identical, and `--clean svg` output reads back in Python for all 981.
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
