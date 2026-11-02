# SVG round-trip experiment

Run `python build_and_test.py` from this folder. It requires Python 3.10 or later and `lxml`.

The script rewrites the fixture files, runs every check, and exits with status 1 if anything fails. The unit tests can also be run on their own with `python -m unittest test_sml_core`.

| Path | Contents |
|---|---|
| `sml_prototype.py` | Prototype for SML 1.0-SVG-3: SML reader, canonical writer (§14.2), expat-based XML importer, XML exporter, and the strict-profile comparison |
| `build_and_test.py` | The harness and the fixture definitions it writes to `corpus/` and `corpus-negative/` |
| `test_sml_core.py` | CP-1–CP-24 and the SML-only SV fixtures of the specification's §15 |
| `corpus/` | Positive SVG fixtures. The numeric prefix is the SV number in the specification (`01`–`18` are the original baseline); `X` fixtures are extra coverage |
| `corpus-negative/` | Inputs a strict importer must reject |
| `results/*.sml` | Canonical SML generated for each positive fixture |
| `results/results.json`, `results/REPORT.md` | Per-fixture results |

Each positive fixture goes through these checks:

1. **XML baseline:** lxml parse, serialize, and reparse agree.
2. **lxml cross-check:** the prototype's importer and lxml agree on the element tree (RT-1).
3. **SML round trip:** XML → model → canonical SML → model is unchanged. The comparison covers the prolog and epilog, the DOCTYPE, the standalone flag, namespace prefixes and declaration locations, attributes, and exact text.
4. **XML re-import:** model → XML → model is unchanged, and lxml still agrees.
5. **Stable:** a second cycle produces byte-identical canonical SML.

**Important:** The prototype was written alongside the specification and has not been validated independently, so passing these tests is evidence for the design, not certification. Visual rendering is not compared, and KDL is not run. The authoritative acceptance criteria are in `SVG-Round-Trip-Conformance-Profile.md`.
