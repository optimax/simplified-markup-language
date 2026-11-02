# SVG round-trip experiment

Run `python build_and_test.py` after installing `lxml`.

`corpus/`: 18 synthetic SVG fixtures. `results/*.sml`: generated expanded-notation SML (restricted subset). `results/results.json`: actual pass/fail/not-run by fixture.

**Important:** This is an experiment, **not** a full SML or KDL implementation. The script supports only the SML syntax it generates and does not compare document prologs, namespace prefix spelling, or render output. Do not call this test suite a certification of SVG-RTCP RT-2.

The authoritative acceptance criteria are in `SVG-Round-Trip-Conformance-Profile.md`.
