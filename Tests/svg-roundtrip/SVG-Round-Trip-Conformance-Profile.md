# SVG Round-Trip Conformance Profile (SVG-RTCP)

**Version:** 1.0 draft · **Date:** 1 November 2026  
**Purpose:** Determine whether a human-editable text serialization can translate SVG/XML in both directions without losing relevant information. This profile is intentionally independent of SML, KDL, or NDL.

## 1. Conformance goals

A candidate implementation consists of importer `I`, text-format serializer `W`, text parser `P`, and SVG/XML exporter `E`:

```
SVG_XML --I--> model --W--> readable_text --P--> model --E--> SVG_XML
```

For source SVG `X`, define `N(X)` as the normalized information model. **Mandatory:**

```
N(X) == N(E(P(W(I(X)))))
```

Also require `N(X1) == N(X2)` after repeated round trips, and ensure converters reject unsupported constructs explicitly rather than silently dropping them. A successful parse or visual resemblance alone is NOT a pass.

## 2. Conformance levels

| Level | Name | Mandatory |
|---|---|---|
| RT-0 | Syntax well-formedness | Secure SVG/XML parse and export; no malformed XML |
| RT-1 | Element-tree information | Exact expanded names, attribute string values, ordered child elements, text/tails, element-level comments and PIs |
| RT-2 | Full document information | RT-1 plus prolog/epilog comments/PIs and supported XML declaration metadata; prefix handling rules below |
| RT-3 | Source-aware fidelity | Optional lexical preservation: prefixes, attribute order, CDATA layout, entity notation, line endings and original self-closing choices |
| RT-V | Visual compatibility | Supplemental render comparisons only; does not replace RT-1/RT-2 |

**Proposed acceptance bar for a production SVG serialization format: RT-2**, plus prefix-sensitive tests and application-specific preservation of unknown vendor data. RT-3 is optional for source-editing applications.

## 3. Normalized information model, RT-1

Document normalization MUST capture:

- Expanded element name `(namespace URI, local-name)`, not merely prefixed spelling.
- All expanded attribute names and **exact decoded Unicode values**, sorted by expanded name solely for equality.
- Child sequence with elements, text, comment and processing-instruction nodes in original order.
- Element text and each child tail, including whitespace-only strings. Adjacent text nodes may be concatenated during normalization.
- Comments and PI target/data, including comments/PIs *inside* the root element.
- The `xml:space`, `xml:lang`, `xml:base`, `href`, `xlink:href`, `style`, `d`, `points`, `transform`, CSS, IDs and all unknown attributes as ordinary exact strings.

Namespace declaration prefixes are metadata, not attributes under the usual expanded-name comparison. Do not rename prefixes when the document contains QName-valued attributes or content without a proven, semantics-preserving transformation. For a safe initial profile, require retaining original declared prefixes (including shadowed prefixes) as a separate checked invariant even at RT-2.

## 4. RT-2 document-level checks

Include XML version and standalone declaration presence/value when available, plus leading/trailing comments and processing instructions. The XML encoding declaration is a serialization-level fact; it need not preserve original spelling when both files are UTF-8, but the exporter must produce a valid encoding declaration if emitted. If source contains DOCTYPE, DTD defaults, unresolved entities, entity definitions, XML 1.1 constructs, or other information not supported by the chosen converter profile, conversion MUST fail with a meaningful `UNSUPPORTED_XML_INFORMATION` result. It must not silently ignore or expand them.

## 5. SVG-specific invariants

- Preserve *exact string* in path `d`: command case, numeric precision, delimiters, and trailing spaces.
- Preserve `viewBox`, length units, transforms, opacity, gradient stops, filters, animation, paint and marker properties as opaque strings.
- Preserve all IDs and URI references without refactoring.
- Preserve text/tspan/textPath mixed content, `xml:space` and whitespace even when a renderer collapses it.
- Preserve CSS `<style>` text and `style` attributes, including whitespace and comment-like tokens inside CSS.
- Preserve native `<defs>`, `<symbol>`, `<use>`, `<clipPath>`, filters, and nested SVGs.
- Preserve XML foreign namespace content (`foreignObject`, XHTML, vendor metadata, Inkscape-like and custom attributes), regardless of whether the renderer understands it.
- Avoid any geometry optimization or numeric/string coercion during interchange.

## 6. Security and deterministic parsing

Use secure XML parsing: disallow external entities, DTDs unless a separately declared profile supports them, network access, and uncontrolled expansion. Pin parser settings. Apply configurable limits on nesting, node count, attribute count, document bytes and long values. Invalid XML, invalid UTF-8, duplicate expanded-name attributes, and namespace errors are strict failures. Ensure differential tests do not rely on browser XML repair or HTML parsing.

## 7. Corpus design

The supplied `corpus/` folder includes 18 *synthetic* SVG fixtures:

| Fixture | Focus |
|---|---|
| 01-basic-shapes | Standard shape elements and numeric strings |
| 02-path-exact | Mixed path commands, case and numeric spelling |
| 03-text-mixed | text/tspan and `xml:space` |
| 04-whitespace | Whitespace-only tails and multiline text |
| 05-namespaces | XLink and vendor namespaces |
| 06-prefix-shadow | Namespace prefix shadowing |
| 07-definitions-references | Gradients, filters, clip paths, symbols and references |
| 08-styles-css | Style text and inline CSS exactness |
| 09-transforms | Multi-transform value preservation |
| 10-foreign-object | XHTML inside foreignObject |
| 11-comments-pi | Root-level XML comments and PIs; prolog PI |
| 12-unicode-entities | Non-ASCII, emoji and XML reference decoding |
| 13-images-href | Data URI and external link strings |
| 14-metadata-vendor | Dublin Core and foreign application markup |
| 15-svg-cdata | CDATA vs ordinary text equivalence |
| 16-attribute-order | Noncanonical order; attribute-value exactness |
| 17-empty-and-siblings | Empty elements and repeated sibling names |
| 18-whitespace-control | XML character-reference tabs, CRs and LF |

**Future required expansion:** genuine Inkscape/Illustrator exports, deeply nested SVG, animations, `textPath`, `switch`, malformed/DTD inputs, huge paths, `xml:base`, prefix-dependent QName values, namespace undeclaration, root-external comments and PIs, XML standalone metadata, and fuzz/differential tests. Synthetic fixture success is only the first layer of qualification.

## 8. Mandatory comparison algorithm

1. Securely parse input SVG and construct normalized information signature.
2. Convert into candidate SML/KDL serialization using the candidate converter.
3. Parse the readable form with an independently validated parser for that language.
4. Export as ordinary SVG XML.
5. Securely parse exported XML and compare normalized signatures.
6. Verify that the second cycle is stable.
7. Separately verify namespace prefix mapping and RT-2 prolog/epilog content.
8. Report exact mismatch path, node kind, and before/after values.
9. Run negative tests that must be rejected, not silently modified.
10. Optionally run raster rendering in at least two engines at known viewport sizes for additional confidence.

A system must not declare RT-2 compliance based on a comparison algorithm that ignores top-level processing instructions or declared prefixes.

## 9. Results from supplied harness

The included Python harness uses `lxml` and a **prototype-only canonical expanded SML subset**. It tests RT-1 tree equality; it does not test the complete SML grammar. There is no installed KDL 2.0 parser or converter in the test runtime, so **KDL was NOT run**. The XML baseline checks lxml parse/serialize/reparse equality, not SVG rendering. The comparison omits top-level prolog/epilog nodes, standalone declaration metadata and namespace-prefix lexical identity; therefore neither full RT-2 nor RT-3 has been established.

Actual fixture-level results are saved in `results/results.json`, and the canonical SML generated outputs are in `results/*.sml`. Re-run using `python build_and_test.py` from this directory (requires `lxml`).

## 10. Exit gates before choosing a language

- **Gate A:** all positive synthetic corpus cases pass RT-1 with zero string coercion.
- **Gate B:** all RT-2 document-level and prefix-sensitive cases pass; hard failure on unsupported XML constructs.
- **Gate C:** real-world SVG exports and fuzzing pass; failures triaged rather than silently tolerated.
- **Gate D:** human editing review on equivalent KDL and SML documents, with exact byte counts, diff behavior and developer feedback.
- **Gate E:** optional rendering and application-specific round-trip tests (including retained foreign application metadata).

Only after Gate B should the format be called a verified lossless SVG information interchange. Passing Gate A alone is useful but insufficient.

## References

- https://www.w3.org/TR/SVG/
- https://www.w3.org/TR/SVG/struct.html
- https://www.w3.org/TR/xml-names/
- https://www.w3.org/TR/xml-infoset/
- https://kdl.dev/spec/
- https://github.com/kdl-org/kdl/blob/main/XML-IN-KDL.md
