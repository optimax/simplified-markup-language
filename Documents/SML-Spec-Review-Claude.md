# Review: SML 1.0-SVG-2 Specification

**Document reviewed:** *Simplified Markup Language (SML) 1.0 — SVG-first revised specification*, Revision 1.0-SVG-2 (2026-11-01)
**Review date:** 2026-11-02
**Scope:** Discrepancies, contradictions, correctness gaps against XML, underspecified behavior, questionable design decisions, and suggested enhancements.

Section references (§) point to the reviewed specification. Claims about XML parser behavior were checked against expat and libxml2 (via lxml); see [Appendix A](#appendix-a-parser-verification).

---

## Contents

1. [Summary](#1-summary)
2. [Fix first](#2-fix-first)
3. [Contradictions and factual errors](#3-contradictions-and-factual-errors)
4. [Gaps and ambiguities](#4-gaps-and-ambiguities)
5. [Questionable design decisions](#5-questionable-design-decisions)
6. [Editorial issues](#6-editorial-issues)
7. [Missing conformance tests](#7-missing-conformance-tests)
8. [Appendix A: Parser verification](#appendix-a-parser-verification)

---

## 1. Summary

The specification is careful about information preservation and is clear about its scope. However, five issues should be resolved before the design is frozen:

| # | Issue | Impact |
|---|---|---|
| 2.1 | `@block` aliases can silently turn a real child element into an attribute | Silent misparse, no error |
| 2.2 | Trailing whitespace in raw values is significant data, including in `@ns` URIs | Silent data change when editors strip whitespace |
| 2.3 | PI data with leading whitespace, and PI targets with colons, cannot round-trip | Breaks the §3 round-trip guarantee |
| 2.4 | Strict mode doubles the line count of typical pretty-printed SVG; readable mode is unspecified | Undermines the "human-editable" goal |
| 2.5 | Any DOCTYPE is rejected, along with a §3/§11 contradiction on when | Many real-world SVG files cannot be imported |

---

## 2. Fix first

### 2.1 `@block` aliases can silently swallow a real child element

**Sections:** §8, §12.1 rule 7

An alias is invoked by a bare name. As a result, how a line parses depends on a declaration that may sit far away, possibly at the root. Natural alias names are attribute names, and several SVG attributes share a name with an SVG element: `style`, `filter`, `mask`, `cursor`, `font`.

Take a root that declares `@block svg style .style` and then starts with the very common first child `<style>`:

```sml
svg
  @block svg style .style
  style
    = .a { fill: red }
  end
end
```

This parses without error as a `style` *attribute* whose value is the text `= .a { fill: red }`. The author's intended `<style>` element disappears.

**Cost versus benefit.** The feature saves about two characters over the existing `.style |` form. In exchange it requires:

- the `@element` collision escape;
- the rules about `@ns` ordering relative to `@block`;
- lexical shadowing rules;
- 18 dedicated conformance tests (SV-33 to SV-50).

**Recommendation.** Remove `@block`. If it is kept, require an explicit marker such as `data |`. That form is currently a syntax error under §12.1 rule 8, so it costs nothing. With a marker:

- a bare name is always an element;
- `@element` is no longer needed for alias collisions;
- the "bare alias after content is an element" rule (§5.1, §8.3) goes away.

### 2.2 Invisible trailing whitespace counts as data

**Sections:** §6.1, §6.2, §12.2

Raw values keep leading and trailing spaces, and §12.2 forbids trimming lines that carry values. Editors and pre-commit hooks commonly strip trailing whitespace, so data changes without anyone noticing:

| Source | Intended | Actual result |
|---|---|---|
| `@ns http://www.w3.org/2000/svg␠` | SVG namespace | Namespace URI with a trailing space. libxml2 rejects the exported XML; a lenient consumer produces a document that is not SVG. |
| `.d \|␠` | Start of a multiline block | Raw value `"\| "`. The payload lines that follow are parsed as SML statements. |
| `.x␠␠foo` | Value `foo` | Value `" foo"` |

(`␠` marks a space.)

**Recommendation.** Make leading or trailing whitespace in raw values an error, or trim it, and require quotes when outer whitespace matters. Inline raw values already trim (§6.3), so this would also make inline and vertical values behave the same.

### 2.3 Processing instructions break the round-trip promise

**Sections:** §3, §4, §9

**Leading whitespace in PI data is lost.** XML discards all whitespace between the PI target and its data. A parser test confirms that `<?foo    bar ?>` yields data `"bar "`. So `@pi foo "  bar"` cannot satisfy `ParseXML(WriteXML(D)) ≡ D`. The specification needs a constraint here, like the one it already has for CR in PI data.

**PI targets with colons are invalid.** §4 says PI targets use XML `Name`, "not necessarily NCName". But Namespaces in XML 1.0 §7 requires that, in a namespace-well-formed document, "no entity names, processing instruction targets, or notation names contain any colons". libxml2 rejects such input with "colons are forbidden from PI names". The SML-to-XML writer would therefore emit documents that namespace-aware parsers refuse.

**Recommendation.** Require PI targets to be `NCName`s. Reject PI data that begins with whitespace, or document it as unrepresentable.

### 2.4 Strict mode is barely readable for real SVGs

**Sections:** §1.1, §3, §15

Nearly every SVG in the wild is pretty-printed. Strict import must preserve every whitespace-only text node, so it adds a line such as `= "\n  "` between every pair of elements. That roughly doubles the line count and undercuts the goal of a "human-editable representation".

The readable (lossy) mode used in the §1.1 example is never specified. Simply dropping whitespace-only nodes is not safe. In `<tspan>A</tspan> <tspan>B</tspan>`, the space between the two `tspan` elements is a whitespace-only text node that affects rendering.

**Recommendation.** Add a reversible convention. For example:

- A document-level indentation hint records the pretty-print pattern, such as two-space indentation with LF.
- The SML writer emits explicit `=` text only where the actual whitespace differs from the predicted pattern.
- The XML writer regenerates the predicted whitespace.

This stays lossless while removing most of the noise. Separately, specify the readable profile, including that whitespace inside text-content elements must be kept.

### 2.5 Rejecting every DOCTYPE rules out a lot of real-world SVG

**Sections:** §3, §11, §17

The following are all rejected:

- older SVG 1.1 files that carry the standard DOCTYPE;
- older Adobe Illustrator exports, which declare internal entity subsets.

There is also a contradiction. §3 requires an error only for constructs "affecting the document", while §11 says any document containing a DOCTYPE "MUST be rejected".

**Recommendation.**

- Add an `@doctype` directive for DOCTYPEs that carry only a public or system identifier.
- Keep such a DOCTYPE as opaque metadata and never load the DTD, which matches browser behavior.
- Keep rejecting internal subsets and custom entities.
- Reconcile the wording of §3 and §11.

---

## 3. Contradictions and factual errors

| Where | Problem |
|---|---|
| §1 | "Five inherited decisions" is followed by six items. Item 6 (`@block`) is new in this revision, not inherited. |
| §6.1 | Says "`&lt;` is five literal characters." It is four. |
| §1.1 | The block example is called "equivalent", then the text says its `d` value differs. The example also drops `viewBox`, `g`, `stroke` and `text`. |
| §6.2, §14 | Quoting a value that starts with `"`, or a value that is exactly `\|`, is only a SHOULD. A raw value starting with `"` is read as a quoted value, and a lone `\|` starts a block. Quoting both must be a MUST. |
| §14 | One rule says writers SHOULD quote values containing LF or TAB. Another says writers SHOULD use blocks when they reproduce the value exactly. Both apply to every multiline value, and no precedence is given. |
| §5.2 vs §12.1 | Only a bare `end` is a terminator, so rule 8 accepts `end()`, `end/` and `end = x` as elements named `end`. It also accepts `style()` as a way around an alias collision. This makes `@element` redundant, unless those forms are explicitly banned. |
| §12.1 | The preamble processes only "nonblank" lines, but blank lines inside blocks are payload (§7). |
| §12.2 vs §6.3 | The `InlineAttributes` grammar has no optional whitespace around `,` or inside the parentheses, but the prose allows it. |
| §12.2 vs §12.1 | `HSpace` means exactly one space or tab, so `@block  div …` and `name  = v` are invalid. §12.1 rule 8 just says "whitespace". |
| §12.2 vs §8.3 | Trailing whitespace is tolerated only on openers, `end` and headers. That list leaves out `@element`, `@block` and alias invocation lines, yet §8.3 says alias lines ignore trailing whitespace. |
| §9 vs SV-31 | §9 makes CR in a comment or PI a constraint on the decoded value, which is a parse-time error. SV-31 says the *serializer* rejects it. Pick one stage. |
| §8.5 vs §8.1 | §8.5 says the alias declaration goes "at an ancestor", but §8.1's scope includes the declaring element itself. |
| §6.2 | "Unpaired surrogate is an error" implies paired surrogates are allowed. `\u{HEX}` is defined as a Unicode scalar value, so every surrogate code point should be an error. The allowed number of hex digits is not stated. |
| SV-32 | Uses "aliases" to mean several prefixes bound to one namespace URI. This collides with the `@block` term "alias". |
| §8.1 | Keeps a "legacy" `=` spelling "for compatibility with the earlier draft" of a feature this same revision introduces, in an unreleased 1.0. Drop it. |
| §11 | `@sml 1.0` is used by several mutually incompatible drafts: 1.0-SVG-1, 1.0-SVG-2, and earlier forms such as `end name` and `@cdata`. The version header cannot tell them apart. |
| §17 | Claims "this revision removes the identified design-level contradictions", which this review disputes. |

---

## 4. Gaps and ambiguities

### 4.1 Namespaces

- **`xmlns` written as an attribute.** `.xmlns:p …`, `.xmlns …` and an inline `xmlns=` are not forbidden. They collide with `@ns` and could produce duplicate or invalid declarations.
- **Explicit `xmlns:xml` declarations.** These are legal XML and fall under "preserve unused declarations" (§2). The specification does not say whether `@ns xml http://www.w3.org/XML/1998/namespace` may be written.
- **`@ns` with one token.**
  - `@ns s`, with the URI forgotten, silently makes `"s"` the default namespace.
  - `@ns ` with a trailing space produces an empty raw value, which may clear the default namespace.
  - Consider explicit syntax for the default namespace, or at least warn when a namespace URI is not absolute.
- **`@ns` value rule versus tokenization.** §10 says the URI "follows §6 value rules", where a raw value runs to end of line. §12.2 splits on tokens. These need reconciling, for example for `@ns urn:a b`.

### 4.2 Values

- **Edge values.** The specification does not say what an inline-attribute or PI value of exactly `|` means. Blocks are not allowed in either position.
- **Whitespace after a closing quote.** It is unspecified whether `.x "foo"␠` is valid or an error.
- **Block payload lines.** Trailing whitespace on payload lines is kept as data. This should be stated explicitly, and writers should avoid blocks for such values.
- **Line separators.** NEL, LS and PS (U+0085, U+2028, U+2029) are not stated to be ordinary characters.

### 4.3 Conformance and canonical form

- **Conformance classes are undefined.** These terms are used but never formally defined, so it is unclear who must do what:
  - "Core"
  - "strict"
  - "mandatory SVG profile"
  - "SVG-safe profile"
  - "readable import"
  - "canonical writer"
- **The canonical form is not unique.** Writer rules are SHOULDs with judgment calls ("long", "short"). Several orders and outputs are unspecified:
  - attribute order;
  - namespace-declaration order;
  - whether the `<?xml?>` declaration is emitted;
  - whether a BOM is emitted.

  As a result, writer output cannot be byte-stable across implementations, and "idempotent formatter" tests are implementation-specific.

### 4.4 Grammar and extensions

- **Undefined grammar terms:** `Identifier`, `Version`, `SourceComment`, `Blank`, `EOL`, `HSpaceOpt`.
- **Extensions.**
  - The syntax of `@use` identifiers and versions is undefined.
  - It is unspecified whether extension directives close the declaration section.
  - No mechanism exists for extensions that a reader may safely ignore.

### 4.5 Smaller gaps

- `@sml` should be required to be the first statement, before `@use`, comments and PIs.
- The "existing 18-fixture SVG corpus" (§15) is referenced but not included or identified.
- Examples (§1.1, §16) are not marked normative or informative.

---

## 5. Questionable design decisions

| Decision | Concern | Suggestion |
|---|---|---|
| Two name-resolution timings in one declaration section (§5.1, §8.2) | The opener and attributes are resolved late, while `@block` is resolved eagerly. This drives the `@ns`-before-`@block` rule, which is restated about six times. | Require all `@ns` lines to come first in the declaration section. This also removes the need to buffer for resolution in streaming readers. |
| Inline raw value delimiters (§6.3) | `(` is an ordinary character but `)` is a delimiter, so `fill=url(#a)` and `transform=translate(10,20)` fail. | Use balanced-parenthesis scanning, and treat commas inside parentheses as data. |
| No exact wrapping for long values (§7) | Path `d` values often run to thousands of characters, and blocks inject LF. | Add a continuation form that concatenates without a separator. |
| `end name` removed (§5) | Indentation is advisory, so a misplaced `end` silently restructures the tree. | Allow an optional, checked `end name` that is an error on mismatch. |
| Block terminator is the English word `end` (§7) | Prose or code blocks containing a line `end` need quoted fallback. | Add an optional custom delimiter, such as `\|EOS` … `EOS`. |
| Compact forms cannot declare namespaces (§5.1, §10) | `<p:x xmlns:p="urn:vendor"/>` becomes three lines. | Allow `p:x(xmlns:p=urn:vendor)/`. |
| Naming (header) | `.sml` is Standard ML's file extension. "SML" is also W3C's XML-based Service Modeling Language. | Choose a distinct name and extension before 1.0. |
| "SVG-first" branding | The rules are entirely generic XML. "SVG-safe" is effectively a strict XML profile. | Rename the profile to "strict XML", and keep SVG as the primary test corpus. |
| Unknown extensions are a hard error (§12.3, §13) | Any `@use` makes a document unreadable by Core-only readers. | Add a must-understand flag. |
| Source comments (§9) | `#` comments are lost in SML → XML → SML. | State plainly that the SML file must remain the source of truth when `#` comments are used. |

---

## 6. Editorial issues

- The `@ns`-before-`@block` rule is restated in about six places (§5.1 twice, §8.2, §12.1, §12.2, §12.3), which invites drift.
- §16 repeats the §8.2 CSS example word for word.
- The note "SML does not support inline `#` comments" sits oddly in §8.1.
- `fill="#4477cc"` in §6.3 is quoted unnecessarily. This suggests `#` needs quoting in inline values when it does not.
- §8.3 refers to §7's "exact-value fallback rules", but §7 has no reader-side fallback. Fallback is a writer concern (§8.5).

---

## 7. Missing conformance tests

The SV-19 to SV-50 table covers namespaces and `@block` well, but misses several lexical and XML edge cases.

**Values and whitespace**

- Trailing whitespace in raw values and in `@ns` URIs.
- Whitespace after a closing quote.
- Whitespace inside inline attribute lists.
- `\u{}` limits: digit count, values above U+10FFFF, and surrogates.
- XML-invalid characters produced by escapes, such as `\u{0}`.
- `]]>` in text.

**Processing instructions**

- PI data with leading whitespace.
- PI targets containing colons.

**Element and attribute names**

- `.xmlns` and `.xmlns:p` written as attributes.
- `end()`, `end/` and `end = x`.
- An alias inside an element opened with `@element`.

**Blocks**

- Blank lines and whitespace-only lines in blocks.
- Mixed tab and space indentation in blocks.
- Empty blocks.

**Namespaces**

- Explicit `xmlns:xml`.
- `@ns ""` on the root.

**Source encoding**

- BOM and CRLF in source.

**Test structure**

- Add an SML → XML → SML direction for SML-authored documents. The mandatory round trip in §15 only starts from XML.
- Separate Core parser tests from SVG-profile tests.

---

## Appendix A: Parser verification

Parser behavior cited in this review was checked with Python's expat binding and with libxml2 via lxml.

| Input | Parser | Result |
|---|---|---|
| `<?foo    bar ?><r/>` | expat | PI data is `'bar '`. Leading whitespace is discarded. |
| `<?a:b data?><r/>` | expat (namespaces on) | `not well-formed (invalid token)` |
| `<?a:b data?><r/>` | libxml2 | `colons are forbidden from PI names 'a:b'` |
| `<r><!--x&#13;y--></r>` | expat | Comment is `'x&#13;y'`. Character references are not decoded in comments, which is consistent with §9. |
| `<r a="x<TAB>y<LF>z<CR><LF>w" b="x&#9;y&#10;z&#13;w"/>` | expat | `a` = `'x y z w'`, normalized to spaces. `b` = `'x\ty\nz\rw'`, preserved through character references. Consistent with §6.2. |
| `<svg xmlns="http://www.w3.org/2000/svg "/>` | libxml2 | `xmlns: 'http://www.w3.org/2000/svg ' is not a valid URI` |
| `len('&lt;')` | Python | `4` |
