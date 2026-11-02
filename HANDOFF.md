# Handoff

## Current objective

Test the SML 1.0-SVG-3 specification (`Documents/SML-Language-Specification.md`) with a prototype implementation, fixture corpus, and fuzz/differential tests in `Tests/svg-roundtrip/`.

## Current state

- `sml_prototype.py` implements:
  - an SML reader for the complete Core syntax;
  - the §14.2 canonical writer;
  - an expat-based XML importer and an XML exporter;
  - the strict-profile comparison.
- `build_and_test.py` runs the fixture pipeline over `corpus/`, which includes a full-document lxml cross-check. It checks that `corpus-negative/` is rejected, runs `test_sml_core.py`, and runs a fixed-seed fuzz pass. It writes `results/REPORT.md` and `results/results.json`.
- `fuzz_roundtrip.py` runs three campaigns:
  - **Generated documents:** each must round-trip through SML, randomized non-canonical SML (`VariantWriter`), and XML, and lxml must agree on the exported XML.
  - **SML mutants:** the reader must reject each one cleanly, or accept a document that round-trips.
  - **XML mutants:** expat and libxml2 must agree on each one.

  Findings are shrunk and saved to `results/fuzz-findings/`, which git ignores.
- **Fuzzer sensitivity check:** nine deliberately planted correctness bugs in the writer, reader, and exporter were all caught by a 150-document run, and the control run was clean.

## Fixed after fuzzing (prototype)

- An unknown XML `encoding` crashed the importer with `LookupError`; it now raises `XmlInputError`.
- Encodings expat lacks are transcoded through Python codecs, limited to registered names plus `UTF8`.
- DOCTYPE system IDs get the XML §2.11 line-end normalization that expat skips.
- Public-ID normalization uses XML whitespace only.

## Fixed after fuzzing (specification)

§11.1 and SV-70: a DOCTYPE system ID must not contain CR, because XML cannot represent it.

## Known divergences (counted, not failed)

- libxml2 rejects namespace names that are not URI references (non-ASCII characters, spaces, `|`, `{}`); expat and the prototype accept them.
- expat uses XML 1.0 4th-edition name characters, so names such as one containing U+FEFF, valid in the 5th edition, export XML that expat cannot read.
- libxml2 accepts a malformed `version="1."` with only a warning.
- lxml quirk: it raises only when the *last* logged message is an error, so the fuzzer reads libxml2's error log instead.

## Test result

`python build_and_test.py`:
- 47/47 positive fixtures pass and 9/9 negative fixtures are rejected;
- 54/54 unit tests pass;
- the fuzz pass (300 documents, 1,200 SML and 1,200 XML mutants) has 0 findings.

Seeds 1–20 at 1,000 documents each also ran with 0 findings. There is no build step; this is Python only.

## Unresolved

- **Spec decision:** should SML require namespace names to be URI references, as Namespaces in XML says and libxml2 enforces?
- **Spec decision:** should the spec warn that 5th-edition-only name characters are not portable to parsers such as expat?
- SV-49 (writer-generated aliases) and SV-65 (readable import) are not executed; the prototype implements neither.
- The fuzzer checks that canonical output round-trips but not that it is the canonical form of §14.2. An independent canonical-form checker would close that gap.
- The prototype has not been validated independently; the language name still collides with Standard ML and W3C SML; `CLAUDE.md` imports a missing `implementation.md`.

## Recommended next step

Run the harness against real-world SVG exports (Inkscape, Illustrator), which need files from the user or permission to download them, and decide the two spec questions above.
