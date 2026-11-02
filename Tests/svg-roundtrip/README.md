# SVG round-trip experiment

Run `python build_and_test.py` from this folder. It requires Python 3.10 or later and `lxml`.

The script rewrites the fixture files, runs every check, and exits with status 1 if anything fails. The unit tests can also be run on their own with `python -m unittest test_sml_core`, and longer fuzz campaigns with `python fuzz_roundtrip.py --docs 2000 --seed 7`.

| Path | Contents |
|---|---|
| `sml_prototype.py` | Prototype for SML 1.0-SVG-4: SML reader, canonical writer (§14.2), expat-based XML importer, XML exporter, and the strict-profile comparison |
| `build_and_test.py` | The harness and the fixture definitions it writes to `corpus/` and `corpus-negative/` |
| `test_sml_core.py` | CP-1–CP-24 and the SML-only SV fixtures of the specification's §15 |
| `fuzz_roundtrip.py` | Seeded fuzz and differential tests (see below); findings are shrunk and saved to `results/fuzz-findings/` |
| `crosscheck_csharp.py` | Differential test between this prototype and the C# converter `smlxml` (`Source/`); needs a Release build of `Source/SmlXml` |
| `corpus/` | Positive SVG fixtures. The numeric prefix is the SV number in the specification (`01`–`18` are the original baseline); `X` fixtures are extra coverage |
| `corpus-negative/` | Inputs a strict importer must reject |
| `results/*.sml` | Canonical SML generated for each positive fixture |
| `results/results.json`, `results/REPORT.md` | Per-fixture results |

Each positive fixture goes through these checks:

1. **XML baseline:** lxml parse, serialize, and reparse agree.
2. **lxml cross-check:** the prototype's importer and lxml agree on the element tree, prolog and epilog, standalone flag, DOCTYPE identifiers, and namespace scopes.
3. **SML round trip:** XML → model → canonical SML → model is unchanged. The comparison covers the prolog and epilog, the DOCTYPE, the standalone flag, namespace prefixes and declaration locations, attributes, and exact text.
4. **XML re-import:** model → XML → model is unchanged, and lxml still agrees.
5. **Stable:** a second cycle produces byte-identical canonical SML.

Every harness run also includes a fixed-seed pass of `fuzz_roundtrip.py`:

- **Generated documents.** Random valid documents, with tricky names, namespaces, characters, long values, and pretty-printed whitespace, must survive SML and XML round trips and give stable canonical SML. Each is also written in randomly chosen, equivalent non-canonical SML (aliases, quoting, continuation lines, longer block markers such as `""""`, `end QName`, comments), which must read back to the same document. lxml must agree with the model on the exported XML.
- **SML mutants.** Random edits to valid SML must be rejected with `SmlError`, or accepted as a document that round-trips and exports XML that lxml accepts. The reader must never crash.
- **XML mutants.** Random edits to valid XML go to both the prototype importer (expat) and lxml (libxml2), which must agree. The exceptions are constructs the importer rejects on purpose, and three understood divergences between the two parsers, which are counted rather than failed: namespace URI validation, XML 1.0 5th-edition name characters, and malformed version numbers.

**Important:** The prototype was written alongside the specification and has not been validated independently, so passing these tests is evidence for the design, not certification. Visual rendering is not compared, and KDL is not run. The authoritative acceptance criteria are in `SVG-Round-Trip-Conformance-Profile.md`.
