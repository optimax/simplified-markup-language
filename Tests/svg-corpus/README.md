# SVG corpus

505 real-world SVG files from 16 open-source projects, kept as examples for testing `smlxml` and the Python prototype on files that neither was written against. The files are unchanged copies; their sources and licenses (MIT, ISC, and BSD-3-Clause) are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The corpus is part of the source distribution only; `smlxml` builds and publishes do not include it.

| Folder | Files | What they exercise |
|---|---|---|
| `lucide`, `feather`, `heroicons`, `tabler`, `bootstrap-icons`, `octicons`, `ionicons`, `phosphor`, `iconoir` | 153 | Icon sets: stroke and fill icons in each set's conventions; Phosphor in all six weights, including duotone opacity |
| `fluent-emoji` | 40 | Color emoji with gradients, and Flat and High Contrast emoji: many paths |
| `flag-icons` | 30 | National flags in 4×3 and 1×1, including the six largest (detailed coats of arms) |
| `weather-icons` | 24 | Animated (SMIL) icons with gradients, and tab-indented design exports |
| `svg-spinners`, `svg-loaders` | 38 | SMIL and CSS animation; `svg-loaders` has CRLF line endings |
| `w3c-svg11` | 120 | W3C SVG 1.1 test cases: scripts, CDATA, `xml:space`, `xml:lang`, DOCTYPEs, entities, fonts, filters, animation |
| `resvg` | 100 | resvg renderer tests: edge cases of filters, masking, paint servers, painting, shapes, structure, and text |

Each folder keeps the files' paths within their source (see the notices), so every file can be traced to its original. `.gitattributes` turns off line-end conversion, so the files stay byte-identical to the originals on every platform.

## Selection

`gather_corpus.py` builds the corpus. It downloads each source at a pinned npm version or git commit, selects the files, copies them unchanged, and writes `THIRD-PARTY-NOTICES.md`. It needs Python 3.10 or later, git, and network access. Run it from the repository root:

```bash
python Tests/svg-corpus/gather_corpus.py
```

It replaces each source's folder and the notices, and leaves the other files alone. `--list` prints the selection without writing anything; `--dest` writes elsewhere. A source is added or updated in the script's `SOURCES` list.

The selection is deterministic: an evenly spaced sample of each source's sorted file list, with each file at most 200 KB.

- Brand and product logos are left out, because they are trademarks even where the drawing is MIT-licensed. Flags are national flags only.
- From `w3c-svg11` and `resvg`, every file is included that has a DOCTYPE, an entity declaration, `xml:space`, `xml:lang`, a non-UTF encoding, or a character reference. The rest of each is an even spread across feature areas.
- Not covered: files saved by Inkscape or Illustrator with their editor metadata (`sodipodi:`, `inkscape:`, Adobe namespaces). No such files were found under an MIT-like license.

## Expected results

Both implementations accept 495 files, and each round-trips (XML → SML → XML) to the same document. The C# and Python canonical SML is byte-identical for every file.

The other 10 files are rejected by both, as the specification requires (§3), because they have a DTD internal subset (entity declarations):

- `resvg/structure/svg/attribute-value-via-ENTITY-reference.svg`
- `resvg/structure/svg/elements-via-ENTITY-reference-1.svg`, `-2.svg`, `-3.svg`
- `w3c-svg11/coords-viewattr-01-b-manual.svg`, `-02-b-manual.svg`, `-04-f-manual.svg`
- `w3c-svg11/render-elems-03-t-manual.svg`
- `w3c-svg11/text-tref-02-b-manual.svg`, `text-tref-03-b-manual.svg`

To repeat the check, copy the folder first, because `smlxml` writes beside its inputs:

```bash
cp -r Tests/svg-corpus /tmp/svg-corpus
cd /tmp
smlxml "svg-corpus/**/*.svg"
smlxml -o "*.out.svg" "svg-corpus/**/*.sml"
```

The first command converts 495 files and reports the 10 rejections; the second writes `name.out.svg` beside each `name.svg`.
