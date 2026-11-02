# Handoff

## Current objective

Bring the SVG round-trip prototype and its tests up to the SML 1.0-SVG-3 specification (`Documents/SML-Language-Specification.md`), and point the specification's §15 at the corpus in this repository.

## Current state

- `Tests/svg-roundtrip/sml_prototype.py` implements:
  - an SML reader for the complete 1.0-SVG-3 Core syntax;
  - the §14.2 canonical writer;
  - an expat-based XML importer and an XML exporter;
  - the strict-profile comparison: prolog/epilog, DOCTYPE, standalone, prefixes and declaration locations.
- `Tests/svg-roundtrip/build_and_test.py` runs the full pipeline over `corpus/` and checks that `corpus-negative/` is rejected. It also runs `test_sml_core.py` and writes `results/results.json` and `results/REPORT.md`.
- The specification's §15 and §17 now point at `Tests/svg-roundtrip/`, and §14.2 rule 5 states how the 100-code-point limit is measured.

## Files changed

- `Documents/SML-Language-Specification.md` (§§14.2, 15, 17)
- `Tests/svg-roundtrip/sml_prototype.py` (new)
- `Tests/svg-roundtrip/test_sml_core.py` (new)
- `Tests/svg-roundtrip/build_and_test.py` (rewritten; the original 18 fixture definitions are unchanged)
- `Tests/svg-roundtrip/README.md`, `Tests/svg-roundtrip/SVG-Round-Trip-Conformance-Profile.md` (§§7, 9)
- `Tests/svg-roundtrip/corpus/` (new fixtures 19–75, X1, X2), `Tests/svg-roundtrip/corpus-negative/` (new)
- `Tests/svg-roundtrip/results/` (regenerated), `Tests/svg-roundtrip/.gitignore` (new)

## Decisions

- **expat instead of lxml for import.** lxml cannot report an attribute's lexical prefix or where the DOCTYPE sits among prolog comments and PIs, and the strict profile needs both. lxml remains as an independent RT-1 cross-check.
- **Fixture naming.** A corpus file's numeric prefix is its SV number in the specification; `X` files are extra coverage.

## Test result

`python build_and_test.py` passes 47/47 positive fixtures, rejects 9/9 negative fixtures, and passes 54/54 unit tests. There is no build step; this is Python only.

## Unresolved

- SV-49 (writer-generated aliases) and SV-65 (readable import profile) are not executed; the prototype implements neither.
- The prototype was written alongside the specification and has not been validated independently; its passes are evidence, not certification.
- The language name and `.sml` extension still collide with Standard ML and W3C's Service Modeling Language (spec §17).
- `CLAUDE.md` imports `implementation.md`, which does not exist yet.

## Recommended next step

Run the harness against real-world SVG exports (Inkscape, Illustrator) and add fuzz/differential tests (profile §7, Gate C).
