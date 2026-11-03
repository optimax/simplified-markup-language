# SVG corpus

897 real-world SVG files from 15 open-source projects, kept as examples for testing `smlxml` and the Python prototype on files that neither was written against. The files are unchanged copies; their sources and licenses (MIT and ISC) are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The corpus is part of the source distribution only; `smlxml` builds and publishes do not include it. The `other` folder (20 files) was added by hand and is not built by `gather_corpus.py` or listed in the notices.

| Folder | Files | What they exercise |
|---|---|---|
| `lucide`, `feather`, `heroicons`, `tabler`, `bootstrap-icons`, `octicons`, `ionicons`, `phosphor`, `iconoir` | 153 | Icon sets: stroke and fill icons in each set's conventions; Phosphor in all six weights, including duotone opacity |
| `fluent-emoji` | 40 | Color emoji with gradients, and Flat and High Contrast emoji: many paths |
| `flag-icons` | 542 | Every flag of the set in 4×3 and 1×1: countries, regions (`gb-eng`, `es-ct`, …), organizations (`eu`, `un`, `asean`, …), and the `xx` placeholder; detailed coats of arms, stars, and stripes |
| `weather-icons` | 24 | Animated (SMIL) icons with gradients, and tab-indented design exports |
| `svg-spinners`, `svg-loaders` | 38 | SMIL and CSS animation; `svg-loaders` has CRLF line endings |
| `resvg` | 100 | resvg renderer tests: edge cases of filters, masking, paint servers, painting, shapes, structure, and text; DOCTYPEs, entities, `xml:space`, and a non-UTF encoding |

Each folder keeps the files' paths within their source (see the notices), so every file can be traced to its original. `.gitattributes` turns off line-end conversion, so the files stay byte-identical to the originals on every platform.

## Selection

`gather_corpus.py` builds the corpus. It downloads each source at a pinned npm version or git commit, selects the files, copies them unchanged, and writes `THIRD-PARTY-NOTICES.md`. It needs Python 3.10 or later, git, and network access. Run it from the repository root:

```bash
python Tests/svg-corpus/gather_corpus.py
```

It replaces each source's folder and the notices, and leaves the other files alone. `--list` prints the selection without writing anything; `--dest` writes elsewhere. A source is added or updated in the script's `SOURCES` list.

The selection is deterministic: all of `flag-icons`, and an evenly spaced sample of each other source's sorted file list, with each file at most 200 KB.

- Brand and product logos are left out, because they are trademarks even where the drawing is MIT-licensed.
- A file with an "All Rights Reserved" notice is never selected, whatever its source's license says. For this reason the W3C SVG 1.1 test suite, which web-platform-tests distributes under BSD but whose files say "All Rights Reserved", is not included.
- From `resvg`, every file is included that has a DOCTYPE, an entity declaration, `xml:space`, `xml:lang`, a non-UTF encoding, or a character reference. The rest is an even spread across feature areas.
- Not covered: files saved by Inkscape or Illustrator with their editor metadata (`sodipodi:`, `inkscape:`, Adobe namespaces). No such files were found under an MIT-like license.

## Expected results

Both implementations accept 893 files, and each round-trips (XML → SML → XML) to the same document. The C# and Python canonical SML is byte-identical for every file.

The other 4 files are rejected by both, as the specification requires (§3), because they have a DTD internal subset (entity declarations):

- `resvg/structure/svg/attribute-value-via-ENTITY-reference.svg`
- `resvg/structure/svg/elements-via-ENTITY-reference-1.svg`, `-2.svg`, `-3.svg`

To repeat the check, copy the folder first, because `smlxml` writes beside its inputs:

```bash
cp -r Tests/svg-corpus /tmp/svg-corpus
cd /tmp
smlxml "svg-corpus/**/*.svg"
smlxml -o "*.out.svg" "svg-corpus/**/*.sml"
```

The first command converts 893 files and reports the 4 rejections; the second writes `name.out.svg` beside each `name.svg`.
