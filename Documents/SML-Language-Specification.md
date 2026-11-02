# Simplified Markup Language (SML) 1.0 — SVG-first revised specification

**Status:** Proposed normative revision (design baseline, not yet reference-parser certified)  
**Revision:** 1.0-SVG-3 · 2026-11-02  
**Goal:** Human-editable representation of SVG/XML with deterministic, information-preserving two-way translation  
**Extension:** `.sml` (provisional; naming and extension choice are independent of syntax)  
**Naming note:** Both the name and the extension collide with existing uses: `.sml` is the standard extension for Standard ML source files, and "SML" is also W3C's XML-based Service Modeling Language. A distinct name and extension SHOULD be chosen before 1.0 is final.

> **Conformance boundary:** This revision specifies a well-defined mapping for the **SVG-safe XML subset** described in §3. It does **not** promise lossless import of every legal XML document, original XML bytes, DTD internal subsets, external DTD content, or entity declaration syntax. A strict converter (§1.2) MUST reject information outside the supported subset instead of silently deleting it.

The capitalized words **MUST**, **MUST NOT**, **SHOULD**, and **MAY** have their ordinary normative meanings.

**Changes since 1.0-SVG-2:**

1. **Marked alias invocation.** A named attribute block is invoked as `alias |`. The space before `|` is optional for every block marker (`data|`, `.d|`, `=|`). A bare name line is always an element opener, so an alias can no longer capture a real child element, and `@element` is no longer needed to disambiguate alias collisions (§§5.2, 8.3, 12.1).
2. **Trimmed lines.** Leading and trailing spaces/tabs are removed from every line outside multiline blocks, and trailing spaces/tabs from every block payload line. Raw values therefore never begin or end with whitespace; such data MUST be quoted (§§4, 6.1, 6.2, 7, 12).
3. **Processing instructions.** PI targets are `NCName`s, and PI data MUST NOT begin with XML whitespace, because neither survives an XML round trip (§§4, 9).
4. **Formatting whitespace.** The new `@indent` header and `@noindent` directive let strict conversions of pretty-printed XML omit predictable whitespace-only text nodes without loss (§6.4). The readable (lossy) import profile is now specified (§3.1).
5. **Document type declarations.** The new `@doctype` directive preserves a DOCTYPE that has no internal subset as opaque metadata. The external DTD is never loaded. Internal subsets remain rejected (§§2, 3, 11.1).
6. **Namespaces.** All `@ns` lines come first in a declaration section, so an element resolves in a single pass. Compact forms can declare namespaces with inline `xmlns` entries. `.xmlns` attributes are forbidden. A raw namespace URI must contain `:`. The `xml` and `xmlns` prefix rules are explicit (§§5.1, 6.3, 10).
7. **Values.** Inline raw values may contain balanced parentheses, e.g. `transform=translate(10,20)` (§6.3). Long quoted values can be wrapped exactly with continuation lines (§6.5). `\u{}` escapes are fully specified. A raw value cannot begin with `|` (§6.2).
8. **Blocks and elements.** Blocks may use a custom terminator (`|EOT`) when the payload contains an `end` line (§7). `end QName` is an optional, checked close (§5.3). `end` can no longer be used as a compact element name (§5.2). The legacy `=` in `@block` declarations is removed (§8.1).
9. **Conformance.** Conformance classes and profiles are defined (§1.2). A fully deterministic canonical form is specified (§14.2). Versioning, XML output, extension syntax, and optional extensions are specified (§§11, 13). Core parser fixtures (CP series) and a reverse round trip are added (§15).

Conformance cases SV-51–SV-76 and CP-1–CP-24 cover these changes. (1.0-SVG-2 introduced Core `@block` aliases and cases SV-33–SV-50.) All other SVG-safe XML preservation rules are retained.

## 1. Core design

SML is an alternate *serialization* of an XML-compatible document tree, **not** an SVG drawing language, a schema, a template system, or a geometry engine. SVG properties such as `d`, `viewBox`, `transform`, `points`, `style`, `href`, `xml:space` and CSS source are opaque Unicode strings to the converter. A renderer or editor may interpret them later; import/export MUST NOT normalize numbers, paths, styles, IDs, references, or geometry.

These design decisions are in force. Items 1–5 are inherited from earlier drafts; item 6 was added in 1.0-SVG-2.

1. Ordinary elements are closed with an explicit `end`. Two intentionally compact forms (`name/` and `name = value`) close themselves.
2. Inline text and parenthesized inline attributes are supported.
3. Indentation does **not** nest elements; literal multiline blocks remove shared structural indentation.
4. XML **information** is preserved, not original XML quotation, indentation, attribute ordering, or entity spelling.
5. Extensions remain separate, opt-in dialects and cannot redefine Core constructs.
6. Named multiline attribute blocks (`@block`) are built into **Core**, with no vocabulary-specific or SVG-specific parser knowledge.

### 1.1 Example

XML:

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">
  <g id="layer" transform="translate(10,20)">
    <path d="M 0 0 L 100 50 Z" fill="none" stroke="black"/>
    <text x="5" y="80">Hello <tspan>SVG</tspan>!</text>
  </g>
</svg>
```

A **strict, lossless** SML form. The `@indent 2` header (§6.4) states that the whitespace between child elements follows two-space pretty-printing, so the reader regenerates those whitespace-only text nodes and the SML does not spell them out:

```sml
@indent 2
svg
  @ns http://www.w3.org/2000/svg
  .viewBox 0 0 200 100
  g(id=layer)
    .transform translate(10,20)
    path
      .d M 0 0 L 100 50 Z
      .fill none
      .stroke black
    end
    text(x=5, y=80)
      = "Hello "
      tspan = SVG
      = !
    end
  end
end
```

`svg` and `g` are *laid out*: the reader inserts `LF` plus two spaces per depth level before each of their children and before their closing tags, reproducing the XML indentation exactly. `text` begins with a text statement, so its mixed content is taken exactly as written; the quoted `"Hello "` preserves the space before the `tspan`. Without the `@indent 2` line, the same statements would describe XML with no whitespace between elements.

A block-oriented path representation is available whenever an in-scope alias has been declared. It is not equivalent to the original; see below:

```sml
svg
  @ns http://www.w3.org/2000/svg
  @block path data .d
  path
    .fill none
    data |
      M 0 0
      L 100 50
      Z
    end
  end
end
```

Here `data | ... end` means the same as `.d | ... end`; the resulting attribute contains LF between the displayed commands. It is **not** text content or an SVG child element. A bare `data` line, without the `|` marker, would open a child element named `data`. This example's `d` value is not identical to the single-line original, since its whitespace differs; it illustrates authoring rather than an automatic fidelity-preserving rewrite. It also has no `@indent` header, so its XML contains no whitespace between elements.

### 1.2 Conformance classes and profiles

This specification defines requirements for these classes of software:

- **SML reader:** parses SML into the information model (§2), or reports an error (§12.3).
- **SML writer:** serializes a document as SML that a reader parses back to an equal document (§3).
- **Canonical writer:** an SML writer whose output is the canonical form (§14.2), a function of the document alone.
- **SML formatter:** rewrites SML source as SML source without changing the document (§14.1).
- **XML importer:** converts XML to the information model. **XML exporter:** converts the information model to XML.

It defines these profiles:

- **Core:** the SML syntax itself. Core has no vocabulary-specific knowledge.
- **Strict profile:** the information-preserving XML↔SML conversion of §§2–3, including preservation of namespace prefixes and declaration locations. This document also calls it the *SVG-safe* profile; despite the name, its rules are generic XML, and SVG is its primary test corpus. Strict importers and exporters MUST implement it. In this document, "strict" always refers to this profile.
- **Readable import profile** (§3.1): a lossy, SVG-aware import option.

Examples, notes, and §16 are informative; all other text is normative. Where an example conflicts with normative text, the normative text governs.

## 2. Core information model

A document contains exactly one root **element**, plus an ordered sequence of top-level comments and processing instructions before/after the root, an optional document type declaration (name, optional public ID, optional system ID) positioned among the comments and PIs before the root (§11.1), and optional standalone declaration metadata. An element has:

- A lexical qualified name (`prefix:local` or `local`), its resolved expanded name `(namespace URI or null, local name)`, and its explicit in-scope namespace declarations (including declarations on that very element).
- Attributes, each with a lexical qualified name, expanded name, and exact string value. Attribute order is not semantically significant.
- An ordered sequence of element, text, XML comment, and processing-instruction children.

Adjacent text nodes may be coalesced and zero-length text nodes ignored for semantic equality. All attribute and text characters otherwise remain exact, **including whitespace-only text**. Duplicate attribute expanded names are illegal.

**SVG-safe prefix rule:** Unlike a minimal namespace-only XML model, the strict profile (§1.2) retains **the original lexical prefixes and the places where `xmlns` bindings are declared**, including unused bindings. These can affect QName-like values, prefix-sensitive content, vendor metadata, or CSS. The writer MUST NOT arbitrarily rename/rebind prefixes. This stronger rule is a conservative SVG interoperability guarantee; it does not mean full byte-level XML source preservation.

## 3. Supported SVG/XML subset and round-trip promise

**In scope for strict SVG import/export:** XML 1.0 document elements and attributes; Namespaces in XML 1.0; Unicode text, including whitespace; comments; processing instructions (including those outside the root); predefined XML entities and numeric character references **after** normal XML decoding; optional `standalone=yes|no` metadata; DOCTYPE declarations **without** an internal subset, preserved as opaque name/public ID/system ID metadata whose external DTD is never loaded (§11.1); CDATA *character content* (not its delimiters); unknown/vendor-namespaced content; embedded CSS or script as inert text; foreignObject markup; relative and data-URI attributes as raw strings.

**Out of scope for Core import:** DTD internal subsets (including empty ones), loading or processing an external DTD subset, declared general or parameter entities, references to entities other than the five predefined ones, external entity resolution, XInclude processing, XML 1.1, and source-level lexical details such as exact entity references, quote styles, CDATA section boundaries, XML declaration spelling, and original encoding. When an input contains any construct outside the supported subset, a strict converter MUST return an explicit error. It MUST NOT quietly strip or expand it. A future optional *legacy XML import* profile may safely materialize DTD-derived content, but would require its own security and preservation contract.

SML source is UTF-8. XML import MAY accept other encodings declared by a secure XML parser; exported SVG XML is UTF-8. Input and output need not be byte-identical.

For every representable document `D`:

```text
ParseSML(WriteSML(D))  ≡  D
ParseXML(WriteXML(D))  ≡  D
```

The equality `≡` includes the ordered content, the SVG-safe prefix/declaration contract above, and the document type declaration's name, public ID, system ID, and position among prolog comments/PIs. It excludes merely lexical XML trivia such as indentation outside element text, attribute order, self-closing notation, character-reference spelling, CDATA boundaries, and the quotation marks around DOCTYPE literals. SML representation choices are not part of `D`. These include `@block` aliases, `@indent`/`@noindent`, raw versus quoted values, continuation lines, block terminators, inline versus vertical attributes and namespace declarations, and `end QName`.

**Do not confuse SVG rendering equivalence with information equivalence:** the XML text `<path d="M0 0 L2 2"/>` and `<path d="M0,0L2,2"/>` may render alike, but are not string-identical and MUST NOT be silently substituted during conversion.

### 3.1 Readable (lossy) import profile

A converter MAY offer a *readable* XML import that normalizes formatting whitespace. Its output is ordinary SML, but the conversion is **lossy**: it MUST NOT be described as strict or SVG-safe, and it SHOULD begin its output with a `#` source comment stating that formatting whitespace was normalized. Unlike Core parsing, this profile is deliberately SVG-aware.

A readable import may change only the whitespace-only text children of an **eligible** element. Whitespace here means U+0020, U+0009, U+000A, and U+000D. An element is eligible only if all of the following hold:

1. it is in the SVG namespace (`http://www.w3.org/2000/svg`);
2. it is not `text`, `tspan`, `textPath`, `tref`, `altGlyph`, or `foreignObject`, and has no such ancestor;
3. the nearest `xml:space` attribute on it or an ancestor, if any, is not `preserve`;
4. it has at least one child element, comment, or PI, and every text child is whitespace-only.

In an eligible element, the readable import replaces all whitespace-only text children with the §6.4 prediction when its output uses `@indent`, and removes them otherwise. Every other element is converted exactly as in strict import.

These conditions are deliberately conservative. Whitespace between `tspan` elements (as in `<tspan>A</tspan> <tspan>B</tspan>`), inside `xml:space="preserve"` scopes, and in foreign content can change rendering, so a readable import MUST NOT drop or normalize it.

## 4. Encoding, lines, names, and structural model

- SML source MUST be valid UTF-8; a UTF-8 BOM is permitted on input, and writers MUST NOT emit one. Invalid byte sequences are errors.
- CRLF and lone CR source *line separators* are normalized to LF for lexical processing. `\r` within a quoted value represents an actual U+000D data character.
- Only LF, CR, and CRLF separate lines. U+0085 (NEL), U+2028 (LINE SEPARATOR), and U+2029 (PARAGRAPH SEPARATOR) are ordinary data characters. Writers SHOULD write them, the C1 control characters, and other invisible format characters (Unicode general category Cf, such as bidirectional controls) as `\u{…}` escapes in quoted values, so that no data is invisible.
- Unicode normalization (NFC, NFD, etc.) MUST NOT be performed.
- An ordinary statement is one physical line. Outside multiline blocks, **leading and trailing spaces/tabs are removed from every line** before it is interpreted; they never carry data. Data that begins or ends with a space or tab must therefore be quoted (§6.2). Blank lines outside blocks are ignored.
- A name is an XML Namespaces 1.0 **QName** (`NCName` or `NCName:NCName`); names are case-sensitive. PI targets are **NCName**s: Namespaces in XML 1.0 §7 forbids colons in PI targets, and namespace-aware XML parsers reject them.
- XML 1.0 character validity and namespace restrictions apply to all names and decoded values.
- The parser maintains an element stack. Only `end` (or a checked `end QName`, §5.3) closes a normal element. Indentation is advisory and never closes a node.
- A line's first non-indentation token determines its statement kind; a value's interior characters are never rescanned as SML statements.

## 5. Statements and elements

| Form | Effect |
|---|---|
| `name` | Open an ordinary element; must eventually be closed by `end` |
| `name(a=v, b="two words")` | Open an ordinary element with inline attributes |
| `name/` or `name(a=v)/` | Empty, implicitly closed element |
| `name = value` or `name(a=v) = value` | Element with exactly one text value, implicitly closed |
| `.name value` | Attribute of the currently open element, allowed only in its declaration section |
| `= value` | Text child of current element |
| `"more"` | Continuation line: appends a quoted string to the previous quoted value (§6.5) |
| `@ns [prefix] URI` | Namespace declaration on current element, only at the start of its declaration section |
| `@block element-qname alias .attribute-qname` | Core declaration mapping a named multiline block to an attribute on matching elements |
| `alias \| ... end` | In a matching element's declaration section, supplies the declared attribute as a multiline value (`alias\|` is equivalent) |
| `@noindent` | Marks the current element's content as exact: no predicted formatting whitespace is inserted (§6.4); only in its declaration section |
| `@comment value` | Preserved XML comment node |
| `@pi target [value]` | Preserved processing instruction |
| `# ...` | Source-only comment (omitted from XML) |
| `end` or `end QName` | Close current ordinary element; with a QName, the name is checked (§5.3) |
| `\|` or `\|TAG` | Block marker after `.name`, `=`, `name =`, `@comment`, or an alias; `\|TAG` ends at a line `TAG` instead of `end` (§7) |
| `@element QName` | Explicit normal element opener, required for an element named `end` |
| `@sml VERSION` | Optional document version header; must come first (§11) |
| `@standalone yes` / `@standalone no` | Optional XML standalone metadata |
| `@indent N` / `@indent tab` | Optional header enabling the formatting-whitespace model (§6.4) |
| `@doctype name [PUBLIC "pubid" "sysid" \| SYSTEM "sysid"]` | Optional document type declaration, preserved as opaque metadata (§11.1) |
| `@use identifier version [optional]` | Opt-in extension declaration (see §13) |
| `@identifier:name [value]` | Directive of a declared extension (see §13) |

There is **no** `@cdata`, `@encoding`, `@xml`, `|+`, or `|-` in this simplified Core. Those old exploratory forms are **not valid 1.0-SVG-3 syntax**. XML export always targets XML 1.0 in UTF-8.

### 5.1 Declaration section

A normal element opener is followed by a **declaration section**. It contains, in this order:

1. zero or more `@ns` namespace declarations (§10), which **MUST come first**;
2. then, in any order, `@block` declarations (§8), ordinary `.attribute` statements, alias invocations (`alias |`), and at most one `@noindent` marker (§6.4).

Blank lines, `#` source comments, and extension directives (§13) may appear anywhere. An `@ns` line after any statement of the second group is an error.

Because all of an element's namespace declarations precede everything else, its name, attributes, `@block` declarations, and alias invocations resolve against one fixed set of bindings in a single pass. Those declarations are the inline `xmlns` entries on the opener (§6.3) and the `@ns` lines. This is the only ordering rule for namespaces; other sections refer to it.

Once the first actual child/text/`@comment`/`@pi` occurs, the declaration section closes. Any subsequent `.attribute`, `@ns`, `@block`, `@noindent`, or alias invocation is an error. A bare name line is always an element opener, whether or not it matches an alias name (§8.3). A streaming `StartElement` event is emitted when the declaration section closes, after the parser has collected namespace bindings, block declarations, and all ordinary and aliased attributes.

An empty element (`name/`) and an inline-text element (`name = value`) have no declaration section. They cannot have `.attribute`, `@ns`, `@block`, or `@noindent` statements or invoke a named block, and they declare namespaces only through inline `xmlns` entries (§6.3). An element that needs vertical attributes, aliases, or `@ns` lines uses the normal opener plus `end`, even if it is otherwise empty or text-only.

### 5.2 Explicit element opener (`@element`)

An XML element named exactly `end` is written:

```sml
@element end
  .id example
  = text
end
```

`@element` accepts any valid QName (including prefixed names), always opens a normal element, and requires a matching ordinary `end`. It has no inline-attribute, inline-text, or self-closing variants. It is not an alias invocation. A plain `end` line is always a terminator, never an opener.

The unprefixed name `end` is **reserved** in ordinary element forms: `end()`, `end/`, `end(id=a)/`, and `end = x` are errors, not elements. `@element end` is the only way to open an element named `end`. Prefixed names such as `x:end` are not reserved.

`@element` is **not** needed to distinguish a child element from a named attribute block. An alias invocation always carries the `|` marker (§8.3), so a bare `style` line opens a `style` child element even where a `style` alias is active:

```sml
body
  @block div style .style
  div
    style
      = A genuine child element, not the .style attribute
    end
  end
end
```

### 5.3 Checked end (`end QName`)

`end QName` closes the current element exactly like `end`, and also asserts its name. The QName MUST be spelled identically to the element's opener (for `@element`, its operand); otherwise the reader reports an error. The check compares lexical spellings, not expanded names. It carries no information and is optional; writers MAY use it to label the ends of long elements.

```sml
g(id=layer)
  path(d=M0 0 L10 10)/
end g
```

Block terminators (§7) never take a name. Inside a block, a line `end path` is payload. `end` followed by anything other than a single QName is an error.

## 6. Attributes, text, and scalar values

**All values are Unicode strings.** Numeric-looking strings, Boolean-looking strings, SVG colors, path data, and style declarations receive no implicit typing.

### 6.1 Vertical attributes and text

In `.QName VALUE`, one or more spaces/tabs separate the QName from the value. Because every line's leading and trailing spaces/tabs are removed (§4), a raw value never begins or ends with a space or tab; data that does MUST be quoted (§6.2). This keeps invisible whitespace from carrying data, so an editor that strips trailing whitespace cannot change a document's meaning. `.QName` alone means an empty string. The same value rule applies to `= VALUE` and to the value after the `=` of an inline-text element. `=` alone represents an empty text string.

```sml
path
  .d M10,20 C30 40 50 60 70 80 Z
  .style fill: none; stroke-width: 1.00; opacity: .5
  .data-label Two words & punctuation < > are literal
end
```

Consecutive `=` statements concatenate with **no** implicit newline. Whitespace must be explicit, and whitespace at the start or end of a value must be quoted:

```sml
text
  = "Hello "
  tspan = SVG
  = " world"
end
```

The source `&lt;` is four literal characters in SML, not XML entity syntax. The XML writer performs any necessary escaping.

### 6.2 Values: exact interpretation rule

After the syntactic separator, there are exactly three value modes:

1. **Raw:** All characters from the first to the last non-space/tab character are literal, with no backslash escapes or entity decoding. A raw value never begins or ends with a space or tab (§4). This mode applies unless the value begins with `"` or `|`. In every position, including inline attributes, `@pi`, and `@ns`, a raw value cannot begin with `"` or `|`.
2. **Quoted:** If the first value character is `"`, the **entire** value MUST be a correctly terminated double-quoted token with no characters after the closing quote. (Trailing spaces/tabs on the line were already removed by §4 and are permitted.) Quoted values support `\\`, `\"`, `\n`, `\r`, `\t`, and `\u{H…}`. In `\u{H…}`, `H…` is 1 to 6 hexadecimal digits in either case, naming a Unicode scalar value: not above U+10FFFF and not a surrogate code point (U+D800–U+DFFF). Any other escape, any surrogate, and any character that is not a valid XML 1.0 `Char` (such as `\u{0}`) is an error. A quoted value may continue on following lines (§6.5).
3. **Block:** A value that is exactly `|`, or `|` immediately followed by an `NCName` terminator tag such as `|EOT`, starts a multiline block (§7). The space before the marker is optional: `.d|`, `=|`, `name =|`, `@comment|`, and `alias|` mean the same as `.d |`, `= |`, `name = |`, `@comment |`, and `alias |`. Any other value beginning with `|` is an error. To represent a value that begins with `|`, quote it, e.g. `"|"` or `"|x"`.

No quoted/raw fallback is attempted for a malformed leading quotation mark; it is an error. A serializer MUST quote any value that begins or ends with a space or tab, or begins with `"` or `|`; written raw, such a value would be trimmed or reinterpreted.

For SVG information preservation, a value containing LF or CR cannot be raw: a writer MUST use quoted escapes or, where §7 permits, a block. XML output MUST encode U+000D in text and U+0009/U+000A/U+000D in attribute values as character references where required to survive XML parsing unchanged.

### 6.3 Inline attributes

The compact opener syntax is `QName(` *attributes* `)` with no whitespace between the element name and `(`. Inline attributes are comma-separated `QName=VALUE` pairs. Optional horizontal whitespace surrounds `=`, `,`, and the list. `name()` is legal. `name(x=)/` means an attribute with an empty value.

A raw inline value runs to the next `,` or `)` that is **not inside balanced parentheses**. Each `(` in the value opens a group that a later `)` closes, and a `,` inside a group is data. So `transform=translate(10,20)` and `fill=url(#grad)` need no quotes. A raw inline value:

- has its leading and trailing spaces/tabs removed;
- cannot begin with `"` or `|`;
- cannot contain an unbalanced parenthesis.

Values that need any of these use a quoted inline string or a vertical `.attribute`. `#` is an ordinary character and needs no quoting.

```sml
rect(x=10, y=20, width=100, height=50, fill=#4477cc)/
use(href=#icon, transform=translate(10,20) rotate(45))/
text(x=20, y=50) = Hello
```

Quoted inline strings obey the same escaping rules as other quoted values, without continuation lines. Duplicate attributes, including collisions across inline and vertical forms, are errors by expanded name.

**Inline namespace declarations.** An inline entry named `xmlns` or `xmlns:prefix` is a namespace declaration, not an attribute. It is equivalent to `@ns URI` or `@ns prefix URI` and obeys every rule of §10. It is the only way to declare a namespace on a compact element:

```sml
p:x(xmlns:p=urn:vendor)/
```

Inline declarations count as made before the element's `@ns` lines. Declaring the same prefix both inline and with `@ns` is an error.

**Mandatory parsing rule:** If an opener attempts an inline attribute list but it is malformed, it is an error. It MUST NOT be reinterpreted as a plain element name. The same rule applies to malformed quoted values.

### 6.4 Formatting whitespace model (`@indent`, `@noindent`)

Strict conversion preserves every whitespace-only text node (§3). In pretty-printed XML those nodes sit between nearly all elements, and writing each one as `= "\n  "` would make the SML hard to read. The optional `@indent` header states the indentation pattern once, and the reader regenerates the nodes. Like `@block`, this is an SML representation choice: `@indent` and `@noindent` are not part of the information model and never appear in XML output.

**Header.** `@indent N`, where `N` is a decimal integer from 1 to 16 without leading zeros meaning `N` spaces, or `@indent tab`, MAY appear once in the prolog. It sets the indent unit `I`. Without `@indent`, the reader never inserts whitespace, and every text node is written explicitly.

**Laid-out and exact elements.** When `@indent` is present, an element `E` is **laid out** when both of the following hold:

1. `E` is not marked `@noindent`; and
2. `E`'s first content statement is a child element, `@comment`, or `@pi`, not a text statement.

Every other element is **exact**: elements with no content, elements whose first content statement is text (`=`), and elements marked `@noindent`. An exact element's content is exactly what its statements say.

**Predicted whitespace.** For a laid-out element at depth `d` (the root has depth 0) whose non-text children are `N1 … Nk`, the reader inserts the text `LF + I×(d+1)` before each `Ni` and `LF + I×d` after `Nk`. Here `I×n` means `I` repeated `n` times. A text statement inside a laid-out element is an error; mixed content whose first node is an element, comment, or PI MUST be marked `@noindent`.

**`@noindent`.** A declaration-section directive with no operands, allowed at most once per element. It applies only to that element, never to its descendants.

**Streaming.** The reader decides whether `E` is laid out when its declaration section closes, so predicted whitespace is emitted as ordinary `Text` events without lookahead.

Example:

```sml
@indent 2
svg
  @ns http://www.w3.org/2000/svg
  g
    rect/
    circle/
  end
  text
    @noindent
    tspan = A
    = " B"
  end
end
```

is the XML:

```xml
<svg xmlns="http://www.w3.org/2000/svg">
  <g>
    <rect/>
    <circle/>
  </g>
  <text><tspan>A</tspan> B</text>
</svg>
```

`svg` (depth 0) and `g` (depth 1) are laid out. `text` is exact because of `@noindent`; without it, `= " B"` would be an error. `tspan` has no element children, so nothing is predicted for it.

**Writer rule.** A writer that emits `@indent` MUST, for each element that has at least one non-text child, compare the element's actual content with the prediction, after coalescing adjacent text nodes and dropping empty ones:

- **Equal:** write the non-text children only.
- **Different, and the first node is text:** write all nodes explicitly. The element is exact because its first statement is text.
- **Different otherwise:** emit `@noindent` and write all nodes explicitly.

Each case reproduces the element's content exactly, so the round trip of §3 holds. Elements without non-text children are written explicitly, as without `@indent`.

### 6.5 Long values: quoted continuation lines

A quoted value may be split across lines. A line whose first character (after trimming) is `"` is a **continuation line**. It MUST consist of exactly one quoted string, which is appended, with nothing in between, to the value of the statement on the immediately preceding line. That statement MUST be an `.attribute`, `=`, inline-text, `@comment`, or `@pi` statement whose value is quoted, or another continuation line. No blank line or `#` comment may come between them. Continuation lines are not available for inline attributes, `@ns`, or `@doctype`.

```sml
image
  .href "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAAB"
    "CAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="
end
path
  .d "M 10 10 C 20 30 40 30 50 10 "
    "S 80 -10 90 10 Z"
end
```

Spaces at a split point are visible inside the quotes, so the joined value is exact, even for values such as base64 data that contain no spaces. Indentation of continuation lines is advisory, as everywhere.

## 7. Multiline literal blocks — one form only

A value exactly equal to `|` begins a multiline block; the space before the marker may be omitted (§6.2). The block terminates at the first subsequent physical line whose trimmed content is exactly `end`, regardless of its indentation. This `end` belongs to the *value*, not to the surrounding element, and never takes a name (§5.3). There is no nested block syntax.

```sml
style = |
    .road { stroke: #123456; }
    .label { font: 12px sans-serif; }
end
```

**Custom terminator.** `|TAG`, where `TAG` is an `NCName` other than `end`, starts a block that ends at the first line whose trimmed content is exactly `TAG`. Use it when the payload contains a line `end`:

```sml
desc = |EOT
  if fill is none then
    use stroke
  end
EOT
```

The `desc` text is `if fill is none then\n  use stroke\nend`. Everything else about the block is unchanged.

**Dedentation algorithm:** collect all physical payload lines preceding the terminator; remove trailing spaces/tabs from each line (as in §4, they never carry data); determine the longest shared character prefix consisting only of spaces/tabs among all *nonblank* payload lines; remove that prefix from every nonblank line; convert blank lines (all spaces/tabs) to empty lines; join with LF. Do **not** automatically add a terminal LF. For zero lines, the result is the empty string. Tabs and spaces are compared as characters, never visual columns.

This transformation deliberately strips *shared* leading whitespace. Therefore, for values where leading indentation itself is significant (particularly a one-line value or CSS whose first character must be a space), and for values in which any line ends with a space or tab, use a quoted value with explicit `\n` and `\t` escapes. A line consisting only of `end` cannot appear in a block opened with a plain `|`; use a custom terminator or a quoted value. This avoids making indentation structural in contradiction with §4.

Blocks may be used for `.attribute`, `=`, inline text, `@comment`, and alias invocations. For a compact `name = |` statement, the terminator closes the value *and* implicitly closes the inline element; no second terminator is needed.

## 8. Named attribute blocks (`@block`) — SML Core

**SML-CORE-014 (normative).** Named attribute blocks are built into SML 1.0 Core. They are a general-purpose alternative spelling of an existing **multiline attribute value**, not a new XML node type, not a text child, and not a macro or an SVG instruction. The parser MUST NOT interpret SVG commands, CSS, or any vocabulary-specific content.

### 8.1 Declaration syntax and examples

The declaration is:

```sml
@block element-qname alias-name .attribute-qname
```

For example, `@block svg:path data .d`. The `=` separator accepted by earlier drafts (`@block svg:path data = .d`) is no longer valid.

The operands are:

- `element-qname`: QName identifying the kind of element that can use the alias.
- `alias-name`: unprefixed `NCName` that names the block. It is invoked as `alias-name |` (§8.3) and MUST NOT be `end`.
- `.attribute-qname`: normal dot-prefixed attribute QName receiving the block's decoded string.

`@block` is allowed **only inside a normal element's declaration section**, never in a document prolog. Its binding is lexically scoped to that declaring element and its descendants (including the declaring element itself). It is not written to XML.

### 8.2 Namespace resolution

The element QName is resolved **at declaration time** against the in-scope namespace bindings. A prefixed name requires a preceding `@ns prefix URI` declaration (possibly inherited). An unprefixed element name uses the current default namespace; if no default namespace is declared, it means **no namespace**. A default namespace is never inferred from SVG, HTML, file extensions, or the element's spelling.

The target attribute QName uses ordinary XML attribute rules: an unprefixed `.style` or `.d` has **no namespace**, regardless of the default namespace. A prefixed attribute QName requires its prefix to be bound in scope. Both element and attribute names are stored as expanded names, along with their lexical prefix information where the SVG-safe profile requires it.

Because an element's `@ns` lines precede its `@block` declarations (§5.1), the bindings a declaration resolves against are final. A later shadowing of a prefix or default namespace in a descendant does not retroactively alter an already resolved alias mapping.

Examples:

```sml
body
  @block div style .style
  div
    style |
      margin: 10 0 10 0;
      padding: 0 5 0 5;
      font-weight: bold;
    end
  end
end
```

`body`, `div`, and the target `style` attribute have no namespace. This is valid **SML** regardless of whether the CSS value itself is valid; SML treats the content as opaque text and does not validate CSS lengths or units. The `style` attribute value is exactly `margin: 10 0 10 0;\npadding: 0 5 0 5;\nfont-weight: bold;`, where each `\n` denotes an actual LF. The equivalent XML is:

```xml
<body><div style="margin: 10 0 10 0;&#10;padding: 0 5 0 5;&#10;font-weight: bold;"/></body>
```

In contrast:

```sml
svg
  @ns http://www.w3.org/2000/svg
  @ns s http://www.w3.org/2000/svg
  @block path data .d
  @block s:polyline points .points

  path
    data |
      M 10 10
      C 20 30 40 30 50 10
      Z
    end
  end
end
```

`path` and `s:polyline` both resolve to SVG element expanded names. The target attributes `d` and `points` are unqualified strings. The parser has no knowledge of what these values mean.

### 8.3 Invocation and value semantics

An alias invocation is a physical line consisting of the `alias-name`, optional spaces/tabs, and a block marker (`|` or `|TAG`, §7), so `data |` and `data|` are the same invocation. As for every line, leading and trailing spaces/tabs are removed first (§4). An invocation is valid only in the **declaration section of a matching normal element**. It is followed by payload lines and a terminating standalone `end` (or `TAG`). It is exactly equivalent to `.attribute-qname` followed by the same marker, payload, and terminator.

```sml
path
  data |
    M 10 10
    L 100 100
    Z
  end
  .stroke black
end
```

Given `@block path data .d` in scope with an appropriate default namespace, this sets `d` to the exact string `M 10 10\nL 100 100\nZ` (where `\n` in this explanation denotes LF). The block consumes its own `end`; the enclosing element still needs a separate `end`.

The §7 rules for dedentation, LF joining, and termination, including custom terminators, apply **unchanged**. No implicit SVG command parsing, folding, trimming, whitespace optimization, escaping, or type conversion occurs. A block may hold arbitrary XML-compatible string data, not only SVG path commands.

A named block is an **attribute declaration** for ordering and duplicate detection, so additional ordinary attributes may follow it. An invocation after child content has begun is an error, and so is an invocation when no active alias of that name matches the current element. A bare `alias-name` line without the marker is never an invocation: it is always an ordinary element opener, inside the declaration section or after it. An alias therefore never changes how a line without `|` parses, and a real child element whose name equals an alias needs no special spelling.

### 8.4 Scope, collision, and duplicate rules

- Alias lookup compares the **resolved expanded name** of the current element, then the **literal alias spelling**. It never relies on an SVG element-name table.
- An alias declared in an enclosing scope applies to matching descendant elements; it does not apply to unrelated element expanded names.
- A child scope MAY shadow an inherited mapping for the same element expanded name and alias. The nearest scope wins. Multiple declarations of that same key in **one** scope are an error, even if their targets are identical.
- Different aliases MAY map to the same attribute, but invoking more than one (or using a normal dot attribute plus an alias) in the same element MUST produce a duplicate-attribute error by expanded attribute name.
- The alias name MUST be a valid `NCName`, MUST NOT equal `end`, and MUST NOT be interpreted as a generic macro or directive outside the matching declaration context.
- Alias declarations are SML-only formatting metadata. They are not children, namespace declarations, attributes, or XML output content. An SML serializer MAY emit them to support readable formatting; it need not retain the same alias statements when serializing only the XML information model.

### 8.5 XML-to-SML writer requirements

An XML-to-SML writer MAY introduce or use `@block` aliases as a formatting choice. It MUST emit any required alias declaration on the invoking element itself or on an ancestor, where the target element and attribute names resolve correctly, before any invocation. The result MUST remain self-contained: decoding it cannot depend on an external alias registry.

A writer MUST choose a named block only if the complete attribute string can be represented **exactly** by §7's dedentation and terminator rules, using a custom terminator if a payload line is `end`. Otherwise it MUST fall back to an ordinary `.attribute` value, usually quoted with explicit escapes. It MUST preserve XML namespace prefixes/declaration locations under the SVG-safe profile. Because invocations carry the `|` marker, a real child element whose name equals an active alias is written as an ordinary element. Writers MUST NOT alter the text of SVG `d`, CSS `style`, `points`, or any other attribute to make an alias representation possible.

### 8.6 Invalid and ambiguous cases

This is invalid due to duplicate attribute assignments:

```sml
container
  @block path data .d
  path
    .d M 0 0
    data |
      L 10 10
    end
  end
end
```

This is invalid because the alias is invoked after the declaration section has closed (the `@comment` is content):

```sml
container
  @block path data .d
  path
    @comment geometry follows
    data |
      M 0 0
    end
  end
end
```

This is invalid because no `data` alias is active for `rect`:

```sml
container
  @block path data .d
  rect
    data |
      M 0 0
    end
  end
end
```

This is invalid because the block declaration follows actual content:

```sml
container
  item/
  @block path data .d
end
```

This is invalid because the prefix is not bound before its use, and because `@ns` follows another declaration statement (§5.1):

```sml
container
  @block s:path data .d
  @ns s http://www.w3.org/2000/svg
end
```

The XML information model contains no alias declarations, so XML-to-SML-to-XML information equivalence is unaffected by whether a writer chooses aliases or standard `.attribute` notation.

## 9. XML comments, PIs and source comments

`#` introduces an SML-only source comment when it is the first non-indentation character. There are no end-of-line comments: `#` later in a line, and anywhere in a value, is ordinary data. Source comments are discarded on XML export, so they do not survive an SML → XML → SML round trip. A project that relies on `#` comments must keep the SML file as its source of truth.

`@comment VALUE` creates an XML comment node. It may use raw, quoted, or block value syntax. XML comment constraints apply: no `--`, the final character cannot be `-`, and the decoded comment **cannot contain CR (U+000D)**. A literal XML source CR is normalized to LF before XML parsing, while XML character references are not decoded inside comments; thus an SML comment containing CR cannot be represented without information loss, and SML readers MUST reject it. Empty comments are permitted. Comments inside or outside the root are preserved in order. Comment text that begins or ends with a space, as in the common `<!-- text -->`, must be quoted: `@comment " text "`.

`@pi TARGET [VALUE]` creates an XML processing instruction. `TARGET` is an `NCName` (Namespaces in XML 1.0 §7 forbids colons in PI targets) and cannot be `xml` in any case combination. Optional VALUE follows one or more spaces/tabs and uses raw or quoted form; no data means empty string. To represent multiline PI data, use quoted `\n` escapes.

PI data MUST NOT contain `?>` or CR (U+000D), and MUST NOT begin with an XML whitespace character (U+0020, U+0009, U+000A, U+000D). XML discards all whitespace between the PI target and its data, so data beginning with whitespace cannot survive an XML round trip. As with XML comments, PI data does not recognize character references, and an XML parser normalizes a literal CR. SML readers MUST reject PI targets and data that violate these constraints. PI data that ends with whitespace is representable, but must be quoted (§6.2), e.g. `@pi app-hint "compact "`.

**No `@cdata` directive:** CDATA is ordinary text in the XML information model. The exporter may select XML escaped text or CDATA, provided the exact character content is preserved. CDATA `]]>` must be escaped/split correctly. This removes a redundant language construct.

## 10. Namespace declarations and SVG safety

The namespace directive in an element declaration section is:

```sml
@ns URI
@ns prefix URI
```

`@ns URI` binds the default namespace; `@ns prefix URI` binds a named prefix. `@ns ""` explicitly clears the default namespace. Repeated declarations of the same prefix (or of the default namespace) on the same element are errors, whether written with `@ns` or inline (§6.3). Undeclaring a nondefault prefix is not supported under Namespaces 1.0.

**Operands.** `@ns` takes one or two operands separated by spaces/tabs. Each operand is either raw, containing no spaces or tabs, or quoted. With two operands, the first is the prefix (an `NCName`) and the second the URI. With one, it is the default-namespace URI. Any other number of operands is an error, so a URI containing whitespace must be quoted.

**Raw URIs must look absolute.** A namespace URI written raw, in `@ns` or in an inline `xmlns` entry, MUST contain a `:`. A relative or empty namespace name MUST be quoted (`@ns ""`, `@ns "relative"`). This catches `@ns s`, where a prefix was written but its URI forgotten. Relative namespace URIs are deprecated by Namespaces in XML 1.0 but remain representable.

**Reserved names.** The `xml` prefix is bound to `http://www.w3.org/XML/1998/namespace` without any declaration. A document MAY declare it explicitly (`@ns xml http://www.w3.org/XML/1998/namespace`), and the strict profile preserves that declaration like any other; declaring `xml` with any other URI is an error. The prefix `xmlns` MUST NOT be declared. No other prefix, and not the default namespace, may be bound to `http://www.w3.org/XML/1998/namespace` or `http://www.w3.org/2000/xmlns/`.

**`xmlns` is never an attribute.** Names `xmlns` and `xmlns:*` denote namespace declarations. In an inline list they declare namespaces (§6.3); as a vertical `.xmlns` or `.xmlns:prefix` statement they are an error.

A declaration applies to the *declaring element* and all descendants in scope, including its own name and attributes. Default namespaces apply to unprefixed **element names**, not unprefixed attributes.

```sml
svg
  @ns http://www.w3.org/2000/svg
  @ns xlink http://www.w3.org/1999/xlink
  image
    .xlink:href #logo
  end
end
```

Every namespace used by a prefixed element or attribute must be bound in scope. An unprefixed element name in `@block` resolves through the current default namespace (or no namespace when absent); an unprefixed attribute target has no namespace. The bindings required by `@block` MUST already be in scope when that declaration is read (§8). The strict SVG importer/exporter MUST preserve prefixes and the namespace declaration locations as represented by the source XML, including otherwise-unused prefixes; names are still additionally resolved to expanded names for collision detection. QName-like values remain opaque strings and are **not** automatically renamed.

**Compact forms:** An element that declares namespaces of its own can still use a self-closing or inline-text form by declaring them inline, e.g. `p:x(xmlns:p=urn:vendor)/` (§6.3). Otherwise it uses an ordinary opener with `@ns` and `end`.

## 11. Document metadata and external XML content

**Version header.** `@sml VERSION` MAY appear once. If present, it MUST precede every other statement except blank lines and `#` source comments.

- `VERSION` is `1.0-SVG-3` for documents written to this draft revision, or `1.0` for the final 1.0 specification.
- A reader implementing this revision MUST accept `1.0-SVG-3`. It MUST reject any other draft identifier, such as `1.0-SVG-2`, whose syntax differs.
- Until 1.0 is final, a reader treats `@sml 1.0` or a missing header as this revision.
- Writers SHOULD emit `@sml 1.0-SVG-3` while the specification is a draft.

**Other headers.** `@standalone yes|no` MAY appear once before the root to preserve an explicitly present XML standalone declaration; if it is absent, the standalone state is unspecified. `@indent` (§6.4) and `@doctype` (§11.1) MAY each appear once before the root, and `@use` (§13) MAY appear before the root.

**XML output.** All XML output uses version 1.0 and UTF-8 without a BOM. It begins with the declaration `<?xml version="1.0" encoding="UTF-8"?>`, adding `standalone="yes"` or `standalone="no"` when recorded. Top-level items (the declaration, prolog and epilog comments and PIs, the DOCTYPE, and the root) are separated by single LFs, and the output ends with an LF. The literal presence of the original `<?xml ...?>` declaration, its quote style, and its original encoding are not retained.

Top-level `@comment` and `@pi` before or after the root are valid and their relative order is preserved. Ordinary text, attributes, and extra root elements outside the root are errors. XML permits prolog/epilog whitespace between markup; the strict importer need not preserve that top-level lexical whitespace because it is outside this information model.

A document containing a DTD internal subset, declared entities, or references to entities other than the five predefined ones MUST be rejected by strict import (see §3). A DOCTYPE without an internal subset is preserved by `@doctype` (§11.1). Predefined XML entities and numeric character references are decoded by the XML parser into the exact resulting Unicode data; SML itself does not decode XML entities.

**External references:** `href`, `xlink:href`, `url(...)`, `xml:base`, data URLs, and CSS `@import` are preserved as strings. A converter MUST NOT fetch them, rewrite them, or assume a new filesystem base path. Applications that move the output document must explicitly account for changed relative-reference resolution; a pure lexical round trip does not guarantee external resources are still at the same effective URLs.

### 11.1 Document type declarations (`@doctype`)

Many SVG 1.1 files begin with a DOCTYPE such as:

```xml
<!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd">
```

A DOCTYPE that has **no internal subset** is preserved as opaque metadata:

```sml
@doctype svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd"
@doctype svg SYSTEM "svg11.dtd"
@doctype svg
```

(These are three alternative examples; a document contains at most one `@doctype`.)

- **Placement.** `@doctype` MAY appear at most once, in the prolog, before the root element. Its position relative to prolog `@comment` and `@pi` statements is preserved; XML permits comments and PIs both before and after the DOCTYPE.
- **Name.** The first operand is the declared name, an XML `Name`, recorded as written. It is not checked against the root element's name.
- **External identifier.** `PUBLIC` requires both a public ID and a system ID, and `SYSTEM` requires a system ID. With neither keyword, the declaration has no external identifier. The keywords are case-sensitive, as in XML.
- **Literals.** Both identifiers MUST be written as quoted values (§6.2). A public ID may contain only XML `PubidChar` characters and MUST already be normalized as described in XML 1.0 §4.2.2: no TAB, LF, or CR, no leading or trailing space, and no run of consecutive spaces. An XML importer records the public ID in this normalized form. A system ID MUST NOT contain both `"` and `'`.
- **Opaque, never loaded.** `@doctype` is information, not configuration. SML readers and XML converters MUST NOT fetch, parse, or validate against the referenced DTD, and MUST NOT apply its attribute defaults or entity declarations. This matches how browsers handle SVG.
- **Internal subsets stay out of scope.** An XML DOCTYPE with an internal subset, even an empty one (`[ ]`), MUST be rejected by strict import.
- **Undeclared entities.** When a DOCTYPE has an external identifier but the DTD is not read, a non-validating XML parser may skip a reference to an undeclared entity, such as `&nbsp;`, instead of failing. A strict importer MUST treat any skipped or unexpanded entity reference as an error, because skipping it would silently lose information.
- **XML output.** The XML writer emits `<!DOCTYPE name PUBLIC "pubid" "sysid">`, or the `SYSTEM` or bare form, at the recorded prolog position. It delimits the system ID with `"` unless the ID contains `"`, in which case it uses `'`. A public ID never contains `"`.

## 12. Normative parsing procedure and compact grammar

The following is normative **together with** the lexical value rules in §§4–8. This avoids pretending that a line-oriented lexer can be fully specified by a short context-free EBNF alone.

### 12.1 Statement recognition, in order

After UTF-8 decoding and line-ending normalization, process each logical line. Outside blocks, strip its leading and trailing spaces/tabs and skip it if nothing remains. Then:

1. If currently inside a block (an ordinary `|`/`|TAG` value or a named-attribute block), interpret the line as a payload line or as the block's terminator (`end`, or `TAG`); do not recognize other statements there.
2. `#...` is a source-only comment.
3. `"...` is a continuation line (§6.5). It is an error unless the immediately preceding line was a value-bearing statement ending in a quoted value, or another continuation line.
4. `end`, or `end` followed by spaces/tabs and a single QName, closes the current ordinary element; the QName, if present, is checked (§5.3). This is an error when no element is open. `end` followed by anything else is an error.
5. `.QName...` declares an attribute in the current declaration section.
6. `=...` appends a text child.
7. `@...` dispatches a known directive (`@sml`, `@standalone`, `@indent`, `@doctype`, `@ns`, **`@block`**, `@noindent`, `@comment`, `@pi`, `@element`, `@use`) or a directive `@identifier:name` of a declared extension (§13). Unknown directives are errors.
8. A statement consisting of an `NCName`, optional spaces/tabs, and a block marker (`|` or `|TAG`) is an **alias invocation**. It is an error unless the current element is still in its declaration section and an active `@block` alias with that name matches the current element's resolved expanded name. Otherwise, parse the following lines as that attribute's literal block (§8). A statement without a block marker is never an alias invocation.
9. Otherwise, require a legal `QName` other than the reserved unprefixed `end` (§5.2), optional immediately attached `(...)` inline attributes, then exactly one of: end-of-line (open), terminal `/` (empty), or whitespace + `=` + value (inline text). Anything else is invalid.

The parser maintains a scoped block-alias table; alias declarations are resolved when encountered, before matched invocations. Namespace ordering follows §5.1.

### 12.2 Statement grammar (lexically parameterized EBNF)

```ebnf
Document          = Prolog, RootElement, Epilog, EOF ;
Prolog            = { Blank | SourceComment }, [ SmlHeader ],
                    { PrologItem }, [ Doctype, { PrologItem } ] ;
PrologItem        = Blank | SourceComment | Header | XMLComment | PI
                  | ExtensionDirective ;
Epilog            = { Blank | SourceComment | XMLComment | PI
                    | ExtensionDirective } ;
RootElement       = Element ;
Element           = Normal | Empty | InlineText | ExplicitElement ;
Normal            = Opener, EOL, DeclarationSection, { Content }, EndLine ;
Empty             = Opener, "/", EOL ;
InlineText        = Opener, HSpace, "=", ValueTail ;
ExplicitElement   = "@element", HSpace, QName, EOL,
                    DeclarationSection, { Content }, EndLine ;
Opener            = QName, [ InlineAttributes ] ;   (* QName is not "end" *)
InlineAttributes  = "(", HSpaceOpt, [ InlineAttribute,
                    { HSpaceOpt, ",", HSpaceOpt, InlineAttribute } ],
                    HSpaceOpt, ")" ;
InlineAttribute   = QName, HSpaceOpt, "=", HSpaceOpt, InlineValue ;
DeclarationSection = { Neutral | Namespace },
                    { Neutral | Attribute | BlockDeclaration
                    | NamedAttributeBlock | NoIndent } ;
Content           = Neutral | Element | Text | XMLComment | PI ;
Neutral           = Blank | SourceComment | ExtensionDirective ;
Attribute         = ".", QName, ValueTail ;
Namespace         = "@ns", HSpace, [ NCName, HSpace ], NsOperand, EOL ;
NsOperand         = RawToken | Quoted ;
BlockDeclaration  = "@block", HSpace, QName, HSpace, NCName,
                    HSpace, ".", QName, EOL ;
NamedAttributeBlock = AliasName, HSpaceOpt, Block ;
NoIndent          = "@noindent", EOL ;
Text              = "=", ValueTail ;
XMLComment        = "@comment", ValueTail ;
PI                = "@pi", HSpace, NCName,
                    [ HSpace, ( Raw | QuotedValue ) ], EOL ;
ValueTail         = EOL
                  | HSpace, Raw, EOL
                  | HSpace, QuotedValue, EOL
                  | HSpaceOpt, Block ;
QuotedValue       = Quoted, { EOL, Quoted } ;      (* continuation, §6.5 *)
Block             = "|", [ BlockTag ], EOL, { BlockLine }, BlockEnd ;
BlockTag          = NCName ;                        (* not "end" *)
BlockEnd          = ( "end" | BlockTag ), EOL ;     (* the tag given, else "end" *)
EndLine           = "end", [ HSpace, QName ], EOL ;
SmlHeader         = "@sml", HSpace, SmlVersion, EOL ;
SmlVersion        = "1.0" | "1.0-SVG-", Digits ;
Header            = "@standalone", HSpace, ( "yes" | "no" ), EOL
                  | "@indent", HSpace, ( "tab" | IndentWidth ), EOL
                  | "@use", HSpace, Identifier, HSpace, Version,
                    [ HSpace, "optional" ], EOL ;
Doctype           = "@doctype", HSpace, XMLName,
                    [ HSpace, ExternalId ], EOL ;
ExternalId        = "PUBLIC", HSpace, Quoted, HSpace, Quoted
                  | "SYSTEM", HSpace, Quoted ;
ExtensionDirective = "@", Identifier, ":", NCName, ValueTail ;
Identifier        = NCName ;
Version           = Digits, ".", Digits ;
Digits            = Digit, { Digit } ;
SourceComment     = "#", { AnyChar }, EOL ;
Blank             = EOL ;                           (* empty after trimming *)
EOL               = LF | EOF ;
```

Notes on the grammar:

- `QName`, `NCName`, and `XMLName` use the XML/Namespaces productions. `Digit` is `0`–`9`, `LF` is U+000A, and `AnyChar` is any character except LF.
- `AliasName` is an `NCName` that must name an active, matching block alias in the containing element's still-open declaration section; otherwise the statement is an error.
- `HSpace` is one or more spaces or tabs, and `HSpaceOpt` is zero or more. `IndentWidth` is a decimal integer from 1 to 16 without leading zeros.
- `Raw`, `RawToken` (a raw value containing no spaces or tabs), `Quoted`, `InlineValue`, and `BlockLine` are defined by the deterministic scanner in §§6–9. `Raw`, `RawToken`, and `InlineValue` never begin with `"` or `|`.
- A `Block` consumes its terminator **instead of** an additional statement EOL; this prevents a double-terminator ambiguity. Namespace, PI, and DOCTYPE operands have no block form.
- For `@ns`, an optional `NCName` prefix is recognized only when **two** operands are present (§10).
- `EOL` may be EOF where no open construct remains.
- Leading and trailing spaces/tabs are removed from **every** line outside blocks (§4). Block payload lines lose trailing spaces/tabs and their shared indentation (§7).
- `Neutral` statements (blank lines, source comments, extension directives) never end a declaration section; a preserved XML comment or PI does. `DeclarationSection` encodes the `@ns`-first rule of §5.1.

The EBNF is a *statement grammar*, not a substitute for XML name validity, UTF-8 validity, whitespace and delimiter interpretation, namespace resolution, or semantic validation.

### 12.3 Error behavior

SML readers MUST reject, among other malformed syntax:

- **Names and structure:** unbound prefixes anywhere; invalid names or characters; use of the reserved name `end` in ordinary element forms; mismatched `end QName`; document content outside the root; unclosed elements or blocks.
- **Attributes and namespaces:** duplicate expanded attributes (including aliased duplicates); `.xmlns` attributes; namespace declarations that break §10 (repeated, reserved, or raw-relative URIs, or the wrong number of `@ns` operands); `@ns` after another declaration statement; attribute, namespace, or block declarations after content.
- **Aliases:** unbound prefixes in `@block` declarations; conflicting aliases within a lexical scope; the legacy `=` separator; alias invocations with no matching active alias or after the declaration section has closed.
- **Values:** malformed escapes; raw values beginning with `"` or `|`; misplaced continuation lines; unbalanced parentheses in inline raw values.
- **Comments and PIs:** invalid comments or PIs, including PI targets containing colons and PI data beginning with whitespace.
- **Headers:** text statements in a laid-out element (§6.4); repeated or misplaced `@sml`, `@indent`, `@noindent`, or `@doctype`; unknown draft versions; malformed `@doctype` identifiers.
- **Extensions:** unknown directives, and required extensions the reader does not implement.

No implicit recovery or HTML-like repair is allowed. Editors MAY maintain a separately flagged recovery tree for interactive editing.

## 13. Extensions and implementation contracts

Extensions are opt-in and are declared in the prolog with `@use identifier version` (a *required* extension) or `@use identifier version optional`. `identifier` is an `NCName`; `version` is `major.minor`. **`@block` is Core syntax and MUST function without `@use` or any extension.** The core SVG round-trip pipeline MUST operate without extensions. No include, interpolation, template, schema, typed scalar, or arbitrary code execution is included in Core.

- **Directive names.** An extension's directives are spelled `@identifier:name` and use the ordinary value syntax: nothing, a raw or quoted value, or a block. Extensions cannot define bare-name statements or new value syntax. They MUST NOT redefine ordinary SML element, attribute, text, value, namespace, alias, or `end` semantics. A directive whose `identifier` was not declared by `@use` is an error.
- **Placement.** Extension directives may appear wherever a `#` source comment may. They neither close a declaration section nor count as content.
- **Required extensions.** A reader that does not implement a required extension MUST reject the document. A required extension MAY add validation or transformations, which it documents.
- **Optional extensions.** A reader that does not implement an optional extension MUST skip that extension's directives, including any block payloads. An optional extension MUST NOT change the document a Core reader produces, so skipping it is always safe.

A reader SHOULD expose streaming events `StartDocument`, `DocumentType`, `StartElement`, `Text`, `Comment`, `ProcessingInstruction`, `EndElement`, `EndDocument`, with expanded + lexical names, namespace declaration metadata, attributes, source positions, and exact text. It buffers the *current element's declarations*, including ordinary attributes and decoded named-attribute blocks, before emitting `StartElement`. Alias definitions are held in a scoped mapping table and never emitted as XML nodes. Self-closing and inline-text elements emit matching start/end events. Whitespace predicted by `@indent` (§6.4) is emitted as ordinary `Text` events. A DOM preserves heterogeneous ordered children. A writer MUST never inject XML formatting whitespace into element content in strict mode.

Source locations SHOULD include original UTF-8 byte offsets and 1-based line/Unicode-scalar columns. Parsers SHOULD be linear-time in input length, with configurable limits on document bytes, depth, attribute count, and value length. XML import MUST disable network entity fetching, loading of external DTD subsets, DTD processing, automatic XInclude, and processing-instruction execution. `@doctype` records the external identifier only (§11.1).

## 14. Writer rules and canonical form

### 14.1 General writer rules

SML writers SHOULD emit:

- Two-space indentation, LF line endings, UTF-8 without BOM, and plain `end`. `end QName` (§5.3) MAY label the ends of long elements.
- Attributes in the order the input supplied them, when it supplied one.
- `.attribute` on its own line when values contain spaces or are long; compact inline attributes only when their delimiters and strings are unambiguous.
- `name = value` for a short text-only element and `name/` for an empty one, declaring any namespaces with inline `xmlns` entries (§6.3).
- Value forms by this precedence:
  1. **Raw** when §6.2 allows it and the value contains no TAB and none of the characters §4 says to escape.
  2. **A block** when the value contains LF and §7 reproduces it exactly, with a custom terminator if a payload line is `end`.
  3. **Quoted** otherwise, split with continuation lines (§6.5) when long.

  Quoting is **required** (§6.2) for values that begin or end with a space or tab, or begin with `"` or `|`.
- All namespace declarations explicitly at their original scopes, with lexical prefixes retained, under the strict profile.
- `@block` declarations preceding their uses, and alias invocations written with a space before the marker (`alias |`). Use a normal `.attribute` when a value cannot be represented faithfully as a block.
- A space before every block marker (`.attribute |`, `= |`, `name = |`, `@comment |`); readers also accept the forms without the space.
- `@element` only for elements named `end`.
- `@indent` and `@noindent` as §6.4's writer rule requires, and `@doctype` exactly as recorded, at its original prolog position.

SML formatters MUST be idempotent. Under the strict profile they MUST NOT modify element, text, comment, or PI order or values, SVG strings, namespace declaration locations, or lexical prefixes. They SHOULD preserve `#` comments, blank-line grouping, and alias declarations, and MAY change alias spelling or layout only if every attribute value is preserved. Because alias invocations carry the `|` marker, introducing an alias cannot change how an existing child-element opener parses.

### 14.2 Canonical form

The canonical form is a function of the document alone: canonical writers MUST produce byte-identical output for equal documents. It is intended for diffing, hashing, and conformance testing. Canonical output follows these rules:

1. **File.** UTF-8 without BOM, LF line endings, and a final LF. No blank lines, `#` comments, `@block` aliases, `end QName`, or `@use`.
2. **Prolog.** `@sml 1.0-SVG-3`, then `@standalone` if recorded, then `@indent` if chosen (rule 9), then the prolog comments, PIs, and `@doctype` in document order. The epilog follows the root in document order.
3. **Indentation.** Two spaces per element depth. Block payload lines and continuation lines are indented one level deeper than their statement; a block terminator is at the statement's level.
4. **Order.** Namespace declarations come first: the default namespace, then prefixes in ascending code-point order. Then attributes: those without a namespace by local name, then namespaced ones by namespace URI and then local name, all in ascending code-point order. This is the attribute order of Canonical XML.
5. **Inline or vertical.** An element's namespace declarations and attributes are written inline, as one list, when every value is representable as a raw inline value (§6.3) under rule 7, and the indentation, element name, and inline list together are at most 100 code points long. Otherwise all are vertical: `@ns` lines, then `.attribute` lines.
6. **Element form.** For an element with an inline list, or with no declarations or attributes: `name/` if it has no content; `name = value` if its only content is one text node. Otherwise the normal form with `end`. An element named `end` is always written `@element end` in the normal form.
7. **Value form.** Each value is written in the first form that applies:
   1. **Raw:** §6.2 allows it, it contains none of TAB, CR, LF, or the characters §4 says to escape, and the statement line is at most 100 code points.
   2. **Block:** it contains LF but no CR, and §7 reproduces it exactly. The marker is `|`, or, if a payload line is `end`, the first of `|EOT`, `|EOT1`, `|EOT2`, … that no payload line equals. TAB is literal in block payloads.
   3. **Quoted:** otherwise.
8. **Quoted values.** Escapes are used only where required (`\\`, `\"`) and for CR, LF, TAB, and the characters listed in §4. They are written `\r`, `\n`, `\t`, and `\u{…}` with uppercase hexadecimal digits and no leading zeros. When a quoted value's statement line would exceed 100 code points, the value is split into continuation lines (§6.5). Each segment takes as many source characters as fit in 80 code points, ends after the last space within that span if there is one, and never splits an escape.
9. **Whitespace model.** `@indent` uses the unit that makes the most elements laid out (§6.4). Ties are broken in this order: two spaces, four spaces, tab, then other widths ascending. `@indent` is omitted if no element would be laid out. `@noindent` and explicit text follow §6.4's writer rule.
10. **Text.** Adjacent text nodes are coalesced and empty ones dropped. Each remaining text node is one `=` statement.

### 14.3 XML output

SVG XML writers MUST encode character data and attribute values according to XML 1.0, including preserving CR characters in text through character references and preserving TAB/LF/CR in attributes using character references. The XML emitter MUST NOT insert indentation/newline text nodes for pretty-printing in strict round-trip mode.

## 15. Conformance tests and acceptance gates

The tests are in two groups: **SV** fixtures, which exercise XML↔SML conversion under the strict profile, and **CP** fixtures (§15.1), which exercise the SML reader and writers alone and need no XML.

The fixtures are in this repository under `Tests/svg-roundtrip/`:

- `corpus/` holds the 18-fixture baseline corpus (SV-1–SV-18, the earlier RT-1 subset) and the XML fixtures below. A file's numeric prefix is its SV number; for example, `66-doctype-public.svg` is SV-66.
- `corpus-negative/` holds inputs that a strict importer must reject.
- `test_sml_core.py` holds the CP fixtures (§15.1) and the SV fixtures that need only SML.
- `build_and_test.py` runs all of them against the prototype in `sml_prototype.py` and writes `results/REPORT.md`.

The following fixtures are mandatory in addition to the baseline corpus:

| ID | Fixture | Required assertion |
|---|---|---|
| SV-19 | Prolog/epilog comment and PI | Exact node order preserved |
| SV-20 | An otherwise unused namespace declaration | Retained on the original element |
| SV-21 | Prefix used in CSS selector or QName-like attribute | Prefix text and binding not rewritten |
| SV-22 | Default namespace undeclaration inside `foreignObject` | Exact scope and expanded names |
| SV-23 | Text including CR, LF and TAB | Exact XML-reparsed characters |
| SV-24 | Attribute containing CR, LF and TAB | Correct character-reference round trip |
| SV-25 | XML `<end>` foreign element | `@element end` supported |
| SV-26 | CSS/style payload with a physical line `end` | Canonical writer uses a custom terminator (`\|EOT`) |
| SV-27 | Namespace declared directly on an XML empty element | Writer uses `name(xmlns:p=…)/` or open/`@ns`/`end`; both round-trip |
| SV-28 | DTD internal subset (including empty `[ ]`), custom entity, or external resource expansion attempt | Strict import refuses without loading resources |
| SV-29 | Unknown foreign attributes/elements, nested prefixes | Preserved without interpretation |
| SV-30 | Original `standalone="no"` and `"yes"` cases | Declaration state preserved if present |
| SV-31 | An SML XML comment or PI containing `\r` | SML reader rejects nonrepresentable data |
| SV-32 | Attribute whose namespace is bound to several prefixes | Lexical QName and declarations remain stable |
| SV-33 | No-default-namespace `@block div style .style` | Alias targets no-namespace `div` and no-namespace `style` attribute |
| SV-34 | Default-namespace unprefixed `@block path data .d` | Alias targets the current default namespace, but attribute `d` remains unqualified |
| SV-35 | Prefixed `@block s:path data .d` | Resolves bound prefix to expanded element name; descendant prefix changes do not retarget mapping |
| SV-36 | Missing or later prefix declaration | Reject alias with unbound prefix, or `@ns` after any other declaration statement |
| SV-37 | Alias `data` vs. real XML `<data>` child | Bare `data` opens the child; `data \|` creates the attribute only; no `@element` needed |
| SV-38 | Named block after another dot attribute | Both attributes retained; declaration section remains open |
| SV-39 | Named block and `.d` in same element | Duplicate expanded-name attribute error |
| SV-40 | Two block aliases mapping to same target attribute | Duplicate if both are invoked |
| SV-41 | Alias declared after child content | Reject late `@block` |
| SV-42 | Alias name `end` | Reject reserved alias name |
| SV-43 | Inherited alias shadowed in nested scope | Nearest mapping applies; same-scope duplicate declaration fails |
| SV-44 | Named block containing bare `end` as data | Writer uses `alias \|EOT` |
| SV-45 | Named block with significant leading spaces or terminal LF | Writer falls back if dedentation/termination would change exact string |
| SV-46 | Legacy `=` separator in a `@block` declaration | Rejected |
| SV-47 | `style \|` after the declaration section closed | Rejected; a bare `style` line there is a child element |
| SV-48 | Block aliases under two default namespace scopes | Expanded-name lookup distinguishes namespaces without SVG-specific code |
| SV-49 | XML → SML with generated alias → XML | Prefix/declaration scope and exact attribute values unchanged |
| SV-50 | Alias declaration on root with no explicit namespace | Implicit no-namespace mapping is valid |
| SV-51 | Bare alias name as the first statement in a matching element's declaration section | Parsed as a child element; declaration section closes |
| SV-52 | `alias \|` with no matching active alias | Rejected |
| SV-53 | Leading/trailing spaces and tabs on value lines, including an `@ns` URI and a `\|` marker followed by spaces | Trimmed; `.d \| ` starts a block; the namespace URI has no trailing space |
| SV-54 | Attribute, text, comment, and PI values with significant outer whitespace | Writer quotes them; exact round trip |
| SV-55 | Quoted value followed by trailing spaces on the line | Accepted; value unchanged |
| SV-56 | Block payload lines ending in spaces or tabs | Trailing whitespace removed on read; writer uses quoted fallback for values whose lines end in whitespace |
| SV-57 | PI target containing `:` | SML reader rejects; XML importer rejects |
| SV-58 | PI data beginning with space, TAB, LF, or CR | SML reader rejects |
| SV-59 | PI data ending with whitespace | Quoted on write; preserved through the XML round trip |
| SV-60 | Pretty-printed SVG indented with 2 spaces, 4 spaces, and tabs | Writer emits matching `@indent`; no explicit whitespace text; exact strict round trip |
| SV-61 | Mixed content beginning with an element, e.g. `<text><tspan>A</tspan> B</text>`, under `@indent` | Writer emits `@noindent`; exact round trip |
| SV-62 | Minified children (no whitespace) inside an otherwise indented document | Writer emits `@noindent`; exact round trip |
| SV-63 | Irregular indentation, blank lines between elements, missing final newline before a closing tag | Affected elements written exactly; exact round trip |
| SV-64 | Text statement in a laid-out element; `@noindent` after content; repeated `@indent`; `@indent 0` | Rejected |
| SV-65 | Readable import of whitespace between `tspan`s, inside `xml:space="preserve"`, and in `foreignObject` | Whitespace preserved in ineligible elements; output marked lossy |
| SV-66 | SVG 1.1 `PUBLIC` DOCTYPE | `@doctype` preserved; DTD never fetched (verified with network access disabled) |
| SV-67 | `SYSTEM`-only DOCTYPE and bare `<!DOCTYPE svg>` | Preserved exactly |
| SV-68 | DOCTYPE between prolog comments/PIs | Position preserved |
| SV-69 | External-ID DOCTYPE with an undeclared entity reference such as `&nbsp;` | Strict import rejects; no skipped-entity loss |
| SV-70 | `@doctype` with a non-normalized public ID, or a system ID containing both quote kinds | SML reader rejects |
| SV-71 | `data\|`, `.d\|`, `=\|`, `name =\|`, `@comment\|` | Parsed identically to the spaced forms; canonical writer emits the space |
| SV-72 | `.d\|EOT` and `.d \|EOT`; `.d \|x y` | First two start a block ending at a line `EOT`; third rejected (quote values beginning with `\|`) |
| SV-73 | `]]>` in text, including text from CDATA sections | Escaped correctly on export; exact round trip |
| SV-74 | `xmlns=""` on the root element | Preserved as `@ns ""` |
| SV-75 | Explicit `xmlns:xml="http://www.w3.org/XML/1998/namespace"` | Preserved |
| SV-76 | SML-authored corpus using blocks, custom terminators, aliases, continuation lines, inline `xmlns`, and `@indent` | SML → XML → SML → XML preserves information (reverse round trip) |

**Mandatory round-trip acceptance:**

- **Forward:** strict XML parse → write SML → parse SML → write SVG XML → parse XML → compare a normalized tree that includes lexical prefix bindings, declaration locations, ordered comments/PIs including root-external ones, the DOCTYPE, all attributes, and exact text. Then repeat for idempotent information equality.
- **Reverse:** parse SML → write XML → parse XML → write SML → parse SML → compare information, for SML-authored documents (SV-76).

Reject—not drop—unsupported input. Rendering comparisons are useful supplements, **not** substitutes for tree equality. Readable imports (§3.1) are expressly **lossy** and cannot claim the strict profile.

### 15.1 Core parser and writer fixtures (CP)

| ID | Fixture | Required assertion |
|---|---|---|
| CP-1 | Source with and without a UTF-8 BOM | Same document; writers emit no BOM |
| CP-2 | CRLF and lone CR line endings | Same document as with LF |
| CP-3 | U+0085, U+2028, U+2029 inside a raw value | Ordinary characters, not line breaks |
| CP-4 | `\u{}` with 0, 1, 6, and 7 digits; upper- and lowercase hex; `\u{D800}`; `\u{110000}`; `\u{0}` | 1–6 digits in either case accepted; surrogates, out-of-range values, and XML-invalid characters rejected |
| CP-5 | Unknown escape such as `\x` | Rejected |
| CP-6 | `end()`, `end/`, `end(id=a)/`, `end = x`; `x:end/` | First four rejected; prefixed name accepted |
| CP-7 | `end g` matching, `end g` mismatching, `end g h` | Accepted, rejected, rejected |
| CP-8 | Block payload line `end path` | Payload, not a terminator |
| CP-9 | `\|EOT` with `end` payload lines; `\|end`; `\|` followed by a non-NCName | Exact value; rejected; rejected |
| CP-10 | Raw value beginning with `\|` or `"` in an inline list, `@pi`, and `@ns` | Rejected; quoted forms accepted |
| CP-11 | `a( x = 1 , y=2 )` | Accepted |
| CP-12 | `transform=translate(10,20)`, `fill=url(#a)`; `a=(x`; `a=x)y` | First two accepted with exact values; last two rejected |
| CP-13 | Continuation lines after `.attr`, `=`, inline text, `@comment`, and `@pi` with quoted values; after a raw value; after a blank or `#` line; after `@ns` | Joined exactly; rejected; rejected; rejected |
| CP-14 | Blocks with blank lines, whitespace-only lines, mixed tab/space indentation, and no payload lines | Dedented per §7; an empty block is the empty string |
| CP-15 | Alias invoked inside an element opened with `@element` | Works as for any element |
| CP-16 | `@ns` after `.attr`, `@block`, an alias invocation, or `@noindent` | Rejected |
| CP-17 | `@ns s`; `@ns a b c`; `@ns ""`; `@ns "relative"`; inline `xmlns=s` | Rejected; rejected; accepted; accepted; rejected |
| CP-18 | Vertical `.xmlns` and `.xmlns:p`; inline `xmlns` and `xmlns:p` on compact forms; the same prefix inline and in `@ns` | Rejected; namespace declarations; rejected |
| CP-19 | `@ns xml http://www.w3.org/XML/1998/namespace`; `xml` with another URI; any prefix or the default namespace bound to the XML or xmlns namespace URI; declaring `xmlns` | Accepted; rejected; rejected; rejected |
| CP-20 | `@sml` after another statement; `@sml 1.0-SVG-2`; `@sml 1.0-SVG-3`; `@sml 1.0`; no header | Rejected; rejected; accepted; accepted as this revision; accepted as this revision |
| CP-21 | Unknown required extension; unknown optional extension with directives (including a block value) in the prolog, a declaration section, and content | Rejected; directives skipped and the document is identical to one without them |
| CP-22 | Extension directive between two `.attribute` lines | Declaration section stays open |
| CP-23 | Documents equal in information but differing in attribute order, quoting, inline/vertical form, aliases, `end QName`, and comments | Canonical writer output byte-identical |
| CP-24 | Canonical writer applied to its own output | Byte-identical (idempotent) |

The prototype in `Tests/svg-roundtrip/sml_prototype.py` implements this revision's Core syntax, the strict profile, and the canonical form, and `results/REPORT.md` records its current results. Two fixtures are not executed because the prototype does not implement the feature: SV-49 (writer-generated aliases) and SV-65 (readable import). The prototype was written alongside this revision and has not been validated independently. Its results are therefore evidence for the design, **not** certification. An independent conforming SML parser and XML converter are needed before claiming verified full round-trip conformance.

## 16. Worked edge cases

**Namespaced, empty XML element** (self-closes with an inline `xmlns` entry):

```xml
<p:x xmlns:p="urn:vendor"/>
```

```sml
p:x(xmlns:p=urn:vendor)/
```

The normal form is equivalent:

```sml
p:x
  @ns p urn:vendor
end
```

**Exact mixed content including spaces:**

```xml
<text xmlns="http://www.w3.org/2000/svg">A <tspan>B</tspan> C</text>
```

```sml
text
  @ns http://www.w3.org/2000/svg
  = "A "
  tspan = B
  = " C"
end
```

**Path data remains an opaque string:**

```sml
path
  .d M10,10 C 20.00 30 40 30 50 10 z
  .transform translate(10,20) rotate(-30.5)
  .style stroke-width: 1.00; fill: url(#grad)
end
```

**Core `@block` applied to CSS-like attribute text without a namespace:** see the first example in §8.2.

**SVG-prefixed alias with an ordinary `path` element in the default SVG namespace:**

```sml
svg
  @ns http://www.w3.org/2000/svg
  @ns svg http://www.w3.org/2000/svg
  @block svg:path data .d
  path
    data |
      M 10 10
      C 20 30 40 30 50 10
      Z
    end
  end
end
```

The `svg:path` declaration matches the unprefixed SVG `path` element because matching uses expanded element names, not prefixes.

**Real child element sharing a name with a block alias:** a bare `style` line is always an element; only `style |` would invoke the alias.

```sml
body
  @block div style .style
  div
    style
      = This is an element, not a CSS attribute.
    end
  end
end
```

**Multiline text with a literal `end` line:** a plain `|` block would end at that line, so the writer uses a custom terminator:

```sml
style = |EOT
  line one
  end
  line three
EOT
```

A quoted value is equivalent: `style = "line one\nend\nline three"`.

**Long values wrapped exactly:**

```sml
path
  .d "M 0 0 C 10 20 30 40 50 60 S 70 80 90 100 S 110 120 130 140 "
    "S 150 160 170 180 Z"
end
```

The `d` value is the two quoted segments joined with nothing in between; the space at the split point is inside the first segment.

**Metadata and XML comment:**

```sml
@pi xml-stylesheet type="text/css" href="theme.css"
svg
  @ns http://www.w3.org/2000/svg
  @comment Keep editor metadata.
  metadata
    @ns app urn:editor
    app:settings
      .app:mode advanced
    end
  end
end
```

Here `@ns app urn:editor` belongs on `metadata` and is inherited by `app:settings`. This is a demonstration of the declarations' scoped ownership, not a special SVG element rule.

**Legacy SVG 1.1 file with a DOCTYPE and a padded comment:**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!-- Generator: Example Editor 1.0 -->
<!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd">
<svg xmlns="http://www.w3.org/2000/svg" version="1.1">
  <rect width="10" height="10"/>
</svg>
```

```sml
@indent 2
@comment " Generator: Example Editor 1.0 "
@doctype svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd"
svg
  @ns http://www.w3.org/2000/svg
  .version 1.1
  rect(width=10, height=10)/
end
```

The comment's surrounding spaces are data, so it is quoted. The DOCTYPE is preserved but its DTD is never loaded. `@indent 2` regenerates the whitespace around `rect`.

## 17. Remaining limitations and open engineering tasks

This revision addresses the design-level issues identified in review so far. It cannot guarantee the absence of further design issues or of implementation bugs before a reference parser, differential XML comparison suite, and fuzzing exist. In particular:

- The language name and file extension collide with Standard ML and with W3C's Service Modeling Language; a final name is still to be chosen.
- SV-49 (writer-generated aliases) and SV-65 (readable import) are not yet executed, because the prototype implements neither feature (§15).

- The Core `@block` feature requires parser/conformance tests for namespace resolution, declaration ordering, alias shadowing, real-child collisions, duplicate target attributes, and exact multiline value decoding. No implementation-level certification is claimed yet.
- A production lexer needs an executable definition of the full Unicode `QName`/`Name` productions and detailed diagnostics for malformed compact syntax.
- The XML comparison engine needs a complete document-level signature that records prefix/declaration scopes and root-external comments/PIs.
- Actual vendor SVG documents should be tested, including editors' custom metadata, `foreignObject`, unusual `xml:base` handling, and legacy DTD-bearing files. External-ID DOCTYPEs are preserved by `@doctype`; files with internal subsets should fail clearly in strict mode.
- The readable import profile's eligibility rules (§3.1) are SVG-specific and should be checked against real renderers, particularly for CSS `white-space` and foreign content.
- Prefix preservation is deliberately stronger than ordinary XML namespace-expanded-name equality. An optional future *normalized* profile can safely relax this when application semantics permit it.
- A standalone semantic SML editor or graphics application should retain XML original-source metadata separately only if minimizing source diffs matters; Core does not preserve original XML bytes.

## 18. Standards references

- W3C XML 1.0 Fifth Edition: https://www.w3.org/TR/xml/
- W3C Namespaces in XML 1.0 Third Edition: https://www.w3.org/TR/xml-names/
- W3C XML Information Set: https://www.w3.org/TR/xml-infoset/
- W3C SVG 2, text and whitespace: https://www.w3.org/TR/SVG/text.html
- W3C SVG 2, structure and foreign-namespaced content: https://www.w3.org/TR/SVG/struct.html

---

**Status after this revision:** SML 1.0-SVG-3 is a compact, SVG-first design baseline. It has generic, explicitly marked `@block` aliases in Core, whitespace-trimmed raw values, an indentation model for readable strict conversions, opaque DOCTYPE preservation, exact long-value wrapping, a deterministic canonical form, and defined conformance classes. Its required acceptance criterion is an information-preserving *strict-profile round trip* in both directions, not simply readable generated text or visual similarity.
