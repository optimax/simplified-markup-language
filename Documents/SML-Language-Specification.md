# Simplified Markup Language (SML) 1.0 — SVG-first revised specification

**Status:** Proposed normative revision (design baseline, not yet reference-parser certified)  
**Revision:** 1.0-SVG-3 · 2026-11-02  
**Goal:** Human-editable representation of SVG/XML with deterministic, information-preserving two-way translation  
**Extension:** `.sml` (provisional; naming and extension choice are independent of syntax)

> **Conformance boundary:** This revision specifies a well-defined mapping for the **SVG-safe XML subset** described in §3. It does **not** promise lossless import of every legal XML document, original XML bytes, DTD internal subsets, external DTD content, or entity declaration syntax. The SVG converter MUST reject information outside the supported subset instead of silently deleting it.

The capitalized words **MUST**, **MUST NOT**, **SHOULD**, and **MAY** have their ordinary normative meanings.

**Changes since 1.0-SVG-2:**

1. **Marked alias invocation.** A named attribute block is invoked as `alias |`. A bare name line is always an element opener, so an alias can no longer capture a real child element, and `@element` is no longer needed to disambiguate alias collisions (§§5.2, 8.3, 12.1).
2. **Trimmed lines.** Leading and trailing spaces/tabs are removed from every line outside multiline blocks, and trailing spaces/tabs from every block payload line. Raw values therefore never begin or end with whitespace; such data MUST be quoted (§§4, 6.1, 6.2, 7, 12).
3. **Processing instructions.** PI targets are `NCName`s, and PI data MUST NOT begin with XML whitespace, because neither survives an XML round trip (§§4, 9).
4. **Formatting whitespace.** The new `@indent` header and `@noindent` directive let strict conversions of pretty-printed XML omit predictable whitespace-only text nodes without loss (§6.4). The readable (lossy) import profile is now specified (§3.1).
5. **Document type declarations.** The new `@doctype` directive preserves a DOCTYPE that has no internal subset as opaque metadata. The external DTD is never loaded. Internal subsets remain rejected (§§2, 3, 11.1).

Conformance cases SV-51–SV-70 cover these changes. (1.0-SVG-2 introduced Core `@block` aliases and cases SV-33–SV-50.) All other SVG-safe XML preservation rules are retained.

## 1. Core design

SML is an alternate *serialization* of an XML-compatible document tree, **not** an SVG drawing language, a schema, a template system, or a geometry engine. SVG properties such as `d`, `viewBox`, `transform`, `points`, `style`, `href`, `xml:space` and CSS source are opaque Unicode strings to the converter. A renderer or editor may interpret them later; import/export MUST NOT normalize numbers, paths, styles, IDs, references, or geometry.

Five inherited decisions remain in force:

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

An equivalent, block-oriented path representation is available whenever an in-scope alias has been declared:

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

## 2. Core information model

A document contains exactly one root **element**, plus an ordered sequence of top-level comments and processing instructions before/after the root, an optional document type declaration (name, optional public ID, optional system ID) positioned among the comments and PIs before the root (§11.1), and optional standalone declaration metadata. An element has:

- A lexical qualified name (`prefix:local` or `local`), its resolved expanded name `(namespace URI or null, local name)`, and its explicit in-scope namespace declarations (including declarations on that very element).
- Attributes, each with a lexical qualified name, expanded name, and exact string value. Attribute order is not semantically significant.
- An ordered sequence of element, text, XML comment, and processing-instruction children.

Adjacent text nodes may be coalesced and zero-length text nodes ignored for semantic equality. All attribute and text characters otherwise remain exact, **including whitespace-only text**. Duplicate attribute expanded names are illegal.

**SVG-safe prefix rule:** Unlike a minimal namespace-only XML model, the mandatory SVG profile retains **the original lexical prefixes and the places where `xmlns` bindings are declared**, including unused bindings. These can affect QName-like values, prefix-sensitive content, vendor metadata, or CSS. The writer MUST NOT arbitrarily rename/rebind prefixes. This stronger rule is a conservative SVG interoperability guarantee; it does not mean full byte-level XML source preservation.

## 3. Supported SVG/XML subset and round-trip promise

**In scope for strict SVG import/export:** XML 1.0 document elements and attributes; Namespaces in XML 1.0; Unicode text, including whitespace; comments; processing instructions (including those outside the root); predefined XML entities and numeric character references **after** normal XML decoding; optional `standalone=yes|no` metadata; DOCTYPE declarations **without** an internal subset, preserved as opaque name/public ID/system ID metadata whose external DTD is never loaded (§11.1); CDATA *character content* (not its delimiters); unknown/vendor-namespaced content; embedded CSS or script as inert text; foreignObject markup; relative and data-URI attributes as raw strings.

**Out of scope for Core import:** DTD internal subsets (including empty ones), loading or processing an external DTD subset, declared general or parameter entities, references to entities other than the five predefined ones, external entity resolution, XInclude processing, XML 1.1, and source-level lexical details such as exact entity references, quote styles, CDATA section boundaries, XML declaration spelling, and original encoding. When an input contains any construct outside the supported subset, a strict converter MUST return an explicit error. It MUST NOT quietly strip or expand it. A future optional *legacy XML import* profile may safely materialize DTD-derived content, but would require its own security and preservation contract.

SML source is UTF-8. XML import MAY accept other encodings declared by a secure XML parser; exported SVG XML is UTF-8. Input and output need not be byte-identical.

For every representable document `D`:

```text
ParseSML(WriteSML(D))  ≡  D
ParseXML(WriteXML(D))  ≡  D
```

The equality `≡` includes the ordered content, the SVG-safe prefix/declaration contract above, and the document type declaration's name, public ID, system ID, and position among prolog comments/PIs. It excludes merely lexical XML trivia such as indentation outside element text, attribute order, self-closing notation, character-reference spelling, CDATA boundaries, and the quotation marks around DOCTYPE literals. SML representation choices (`@block` aliases, `@indent`/`@noindent`, raw versus quoted values, inline versus vertical attributes) are not part of `D`.

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

- SML source MUST be valid UTF-8; a UTF-8 BOM is permitted. Invalid byte sequences are errors.
- CRLF and lone CR source *line separators* are normalized to LF for lexical processing. `\r` within a quoted value represents an actual U+000D data character.
- Unicode normalization (NFC, NFD, etc.) MUST NOT be performed.
- An ordinary statement is one physical line. Outside multiline blocks, **leading and trailing spaces/tabs are removed from every line** before it is interpreted; they never carry data. Data that begins or ends with a space or tab must therefore be quoted (§6.2). Blank lines outside blocks are ignored.
- A name is an XML Namespaces 1.0 **QName** (`NCName` or `NCName:NCName`); names are case-sensitive. PI targets are **NCName**s: Namespaces in XML 1.0 §7 forbids colons in PI targets, and namespace-aware XML parsers reject them.
- XML 1.0 character validity and namespace restrictions apply to all names and decoded values.
- The parser maintains an element stack. Only `end` closes a normal element. Indentation is advisory and never closes a node.
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
| `@ns [prefix] URI` | Namespace declaration on current element, only in its declaration section |
| `@block element-qname alias .attribute-qname` | Core declaration mapping a named multiline block to an attribute on matching elements |
| `alias \| ... end` | In a matching element's declaration section, supplies the declared attribute as a multiline value |
| `@noindent` | Marks the current element's content as exact: no predicted formatting whitespace is inserted (§6.4); only in its declaration section |
| `@comment value` | Preserved XML comment node |
| `@pi target [value]` | Preserved processing instruction |
| `# ...` | Source-only comment (omitted from XML) |
| `end` | Close current ordinary element |
| `@element QName` | Explicit normal element opener, required for an element named `end` |
| `@sml 1.0` | Optional document version header |
| `@standalone yes` / `@standalone no` | Optional XML standalone metadata |
| `@indent N` / `@indent tab` | Optional header enabling the formatting-whitespace model (§6.4) |
| `@doctype name [PUBLIC "pubid" "sysid" \| SYSTEM "sysid"]` | Optional document type declaration, preserved as opaque metadata (§11.1) |
| `@use identifier version` | Opt-in extension declaration (see §13) |

There is **no** `end name`, `@cdata`, `@encoding`, `@xml`, `|+`, or `|-` in this simplified Core. Those old exploratory forms are **not valid 1.0-SVG-3 syntax**. XML export always targets XML 1.0 in UTF-8.

### 5.1 Declaration section

A normal element opener is followed by a **declaration section** containing zero or more `@ns` namespace declarations, `@block` declarations, and ordinary/aliased attributes, plus at most one `@noindent` marker (§6.4), potentially interspersed with blank lines and source-only `#` comments. Ordinary dot-prefixed attributes may appear before or after `@ns` declarations. However, **all local `@ns` declarations MUST precede the first local `@block` declaration or aliased-block invocation**. This allows alias names and their target element names to resolve against stable, already-declared namespace bindings. Once the first actual child/text/`@comment`/`@pi` occurs, the declaration section closes. Any subsequent `.attribute`, `@ns`, `@block`, `@noindent`, or alias invocation (`alias |`) is an error. A bare name line is always an element opener, whether or not it matches an alias name (§8.3).

Because namespaces may be declared *after* the element opener (and ordinary dot attributes), the parser MUST resolve the opener and all attributes after collecting the declaration section. A streaming `StartElement` event is emitted only after the parser has collected namespace bindings, block declarations, and all ordinary and aliased attributes. An alias invocation is recognized using the current element's resolved name with the namespace bindings established **before** that invocation; no local namespace declaration may follow the first alias invocation.

An empty element (`name/`) and inline-text element (`name = value`) cannot have following dot attributes, `@ns`, or `@block` directives, or invoke a named block. For an element that declares namespaces or aliases, or needs vertical attributes, use the normal opener plus `end`, even if it is otherwise empty or text-only.

### 5.2 Explicit element opener (`@element`)

An XML element named exactly `end` is written:

```sml
@element end
  .id example
  = text
end
```

`@element` accepts any valid QName (including prefixed names), always opens a normal element, and requires a matching ordinary `end`. It has no inline-attribute, inline-text, or self-closing variants. It is not an alias invocation. A plain `end` line is always a terminator, never an opener.

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

The source `&lt;` is five literal characters in SML, not XML entity syntax. The XML writer performs any necessary escaping.

### 6.2 Values: exact interpretation rule

After the syntactic separator, there are exactly three value modes:

1. **Raw:** All characters from the first to the last non-space/tab character are literal, with no backslash escapes or entity decoding. A raw value never begins or ends with a space or tab (§4). This mode applies unless the value begins with `"` or is the exact block token `|`.
2. **Quoted:** If the first value character is `"`, the **entire** value MUST be a correctly terminated double-quoted token with no characters after the closing quote. (Trailing spaces/tabs on the line were already removed by §4 and are permitted.) Quoted values support `\\`, `\"`, `\n`, `\r`, `\t`, and `\u{HEX}` (Unicode scalar value). Any other escape or unpaired surrogate is an error.
3. **Block:** Only the exact, unquoted value marker `|` starts a multiline block (§7). To represent the literal single character `|`, write `"|"`.

No quoted/raw fallback is attempted for a malformed leading quotation mark; it is an error. A serializer MUST quote any value that begins or ends with a space or tab, begins with `"`, or is exactly `|`; written raw, such a value would be trimmed or reinterpreted.

For SVG information preservation, a value containing LF or CR cannot be raw: a writer MUST use quoted escapes or, where §7 permits, a block. XML output MUST encode U+000D in text and U+0009/U+000A/U+000D in attribute values as character references where required to survive XML parsing unchanged.

### 6.3 Inline attributes

The compact opener syntax is `QName(` *attributes* `)` with no whitespace between the element name and `(`. Inline attributes are comma-separated `QName=VALUE` pairs. Optional horizontal whitespace surrounds `=`, `,`, and the list. `name()` is legal. `name(x=)/` means an attribute with an empty value.

Raw inline values may contain internal spaces, but **cannot** contain `,`, `)`, LF, or a leading `"`; leading/trailing horizontal whitespace is ignored. If those characters, exact outer whitespace, or a literal leading quote are needed, use a quoted inline string or a vertical `.attribute`.

```sml
rect(x=10, y=20, width=100, height=50, fill="#4477cc")/
text(x=20, y=50) = Hello
```

Quoted inline strings obey the same escaping rules as other quoted values. `(` and `#` are ordinary characters in an inline raw value; `)` and `,` are delimiters. Duplicate attributes, including collisions across inline and vertical forms, are errors by expanded name.

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

## 7. Multiline literal blocks — one form only

A value exactly equal to `|` begins a multiline block. The block terminates at the first subsequent physical line whose trimmed content is exactly `end`, regardless of its indentation. This `end` belongs to the *value*, not to the surrounding element. There is no nested block syntax.

```sml
style = |
    .road { stroke: #123456; }
    .label { font: 12px sans-serif; }
end
```

**Dedentation algorithm:** collect all physical payload lines preceding the terminator; remove trailing spaces/tabs from each line (as in §4, they never carry data); determine the longest shared character prefix consisting only of spaces/tabs among all *nonblank* payload lines; remove that prefix from every nonblank line; convert blank lines (all spaces/tabs) to empty lines; join with LF. Do **not** automatically add a terminal LF. For zero lines, the result is the empty string. Tabs and spaces are compared as characters, never visual columns.

This transformation deliberately strips *shared* leading whitespace. Therefore, for values where leading indentation itself is significant (particularly a one-line value or CSS whose first character must be a space), and for values in which any line ends with a space or tab, use a quoted value with explicit `\n` and `\t` escapes. A line consisting only of `end` cannot appear literally in a block, and must likewise use quoted escapes. This avoids making indentation structural in contradiction with §4.

Blocks may be used for `.attribute`, `=`, inline text, and `@comment`. For a compact `name = |` statement, the `end` closes the value *and* implicitly closes the inline element; no second terminator is needed.

## 8. Named attribute blocks (`@block`) — SML Core

**SML-CORE-014 (normative).** Named attribute blocks are built into SML 1.0 Core. They are a general-purpose alternative spelling of an existing **multiline attribute value**, not a new XML node type, not a text child, and not a macro or an SVG instruction. The parser MUST NOT interpret SVG commands, CSS, or any vocabulary-specific content.

### 8.1 Declaration syntax and examples

The canonical declaration is:

```sml
@block element-qname alias-name .attribute-qname
```

For compatibility with the earlier draft of this feature, the separator `=` between `alias-name` and `.attribute-qname` is **OPTIONAL** in a reader. A canonical SML writer MUST omit it:

Canonical form:

```sml
@block svg:path data .d
```

Also accepted by readers, but not emitted by canonical writers:

```sml
@block svg:path data = .d
```

These are **alternative spellings** of one declaration. Declaring both forms for the same element and alias in one scope is an error. SML does not support inline `#` comments.

The operands are:

- `element-qname`: QName identifying the kind of element that can use the alias.
- `alias-name`: unprefixed `NCName` that names the block. It is invoked as `alias-name |` (§8.3) and MUST NOT be `end`.
- `.attribute-qname`: normal dot-prefixed attribute QName receiving the block's decoded string.

`@block` is allowed **only inside a normal element's declaration section**, never in a document prolog. Its binding is lexically scoped to that declaring element and its descendants (including the declaring element itself). It is not written to XML.

### 8.2 Namespace resolution

The element QName is resolved **at declaration time** against the in-scope namespace bindings. A prefixed name requires a preceding `@ns prefix URI` declaration (possibly inherited). An unprefixed element name uses the current default namespace; if no default namespace is declared, it means **no namespace**. A default namespace is never inferred from SVG, HTML, file extensions, or the element's spelling.

The target attribute QName uses ordinary XML attribute rules: an unprefixed `.style` or `.d` has **no namespace**, regardless of the default namespace. A prefixed attribute QName requires its prefix to be bound in scope. Both element and attribute names are stored as expanded names, along with their lexical prefix information where the SVG-safe profile requires it.

Because a current element's `@ns` declarations apply to that entire element, all `@ns` directives in the declaration section MUST appear before the first `@block` declaration or named-block invocation. They MAY follow ordinary `.attributes` if necessary. A later shadowing of a prefix or default namespace in a descendant does not retroactively alter an already resolved alias mapping.

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

An alias invocation is a physical line consisting of the `alias-name`, one or more spaces/tabs, and the block marker `|`. As for every line, leading and trailing spaces/tabs are removed first (§4). An invocation is valid only in the **declaration section of a matching normal element**. It is followed by payload lines and a terminating standalone `end`. It is exactly equivalent to `.attribute-qname |` followed by the same payload and block terminator.

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

The standard §7 dedentation, LF joining, termination, and exact-value fallback rules apply **unchanged**. No implicit SVG command parsing, folding, trimming, whitespace optimization, escaping, or type conversion occurs. A block may hold arbitrary XML-compatible string data, not only SVG path commands.

A named block is an **attribute declaration** for ordering and duplicate detection, so additional ordinary attributes may follow it. An invocation after child content has begun is an error, and so is an invocation when no active alias of that name matches the current element. A bare `alias-name` line without the marker is never an invocation: it is always an ordinary element opener, inside the declaration section or after it. An alias therefore never changes how a line without `|` parses, and a real child element whose name equals an alias needs no special spelling.

### 8.4 Scope, collision, and duplicate rules

- Alias lookup compares the **resolved expanded name** of the current element, then the **literal alias spelling**. It never relies on an SVG element-name table.
- An alias declared in an enclosing scope applies to matching descendant elements; it does not apply to unrelated element expanded names.
- A child scope MAY shadow an inherited mapping for the same element expanded name and alias. The nearest scope wins. Multiple declarations of that same key in **one** scope are an error, even if their targets are identical.
- Different aliases MAY map to the same attribute, but invoking more than one (or using a normal dot attribute plus an alias) in the same element MUST produce a duplicate-attribute error by expanded attribute name.
- The alias name MUST be a valid `NCName`, MUST NOT equal `end`, and MUST NOT be interpreted as a generic macro or directive outside the matching declaration context.
- Alias declarations are SML-only formatting metadata. They are not children, namespace declarations, attributes, or XML output content. An SML serializer MAY emit them to support readable formatting; it need not retain the same alias statements when serializing only the XML information model.

### 8.5 XML-to-SML writer requirements

An XML-to-SML writer MAY introduce or use `@block` aliases as a formatting choice. It MUST emit any required alias declaration at an ancestor where the target element and attribute names resolve correctly, before any invocation. The result MUST remain self-contained: decoding it cannot depend on an external alias registry.

A writer MUST choose a named block only if the complete attribute string can be represented **exactly** by §7's dedentation and delimiter rules. Otherwise it MUST fall back to an ordinary `.attribute` value, usually quoted with explicit escapes. It MUST preserve XML namespace prefixes/declaration locations under the SVG-safe profile. Because invocations carry the `|` marker, a real child element whose name equals an active alias is written as an ordinary element. Writers MUST NOT alter the text of SVG `d`, CSS `style`, `points`, or any other attribute to make an alias representation possible.

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

This is invalid because the prefix is not bound before its use:

```sml
container
  @block s:path data .d
  @ns s http://www.w3.org/2000/svg
end
```

The XML information model contains no alias declarations, so XML-to-SML-to-XML information equivalence is unaffected by whether a writer chooses aliases or standard `.attribute` notation.

## 9. XML comments, PIs and source comments

`#` introduces an SML-only source comment when it is the first non-indentation character. It is discarded on XML export; `#` within any value is ordinary data.

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

`@ns URI` binds the default namespace; `@ns prefix URI` binds a named prefix. `@ns ""` explicitly clears the default namespace. The URI follows §6 value rules; if it contains whitespace, quote it. Repeated declarations of the same prefix on the same element are errors. The built-in `xml` prefix is fixed to `http://www.w3.org/XML/1998/namespace`; `xmlns` is reserved. Undeclaring a nondefault prefix is not supported under Namespaces 1.0.

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

**Special-case rule:** If an SVG XML element declares a namespace of its own, it cannot use an SML self-closing or inline-text form (which have no declaration section); it must use an ordinary opener with `@ns` and `end`. This is a syntactic lengthening, not a loss of information.

## 11. Document metadata and external XML content

`@sml 1.0` MAY appear once before the root, otherwise 1.0 is assumed. `@standalone yes|no` MAY appear once before the root to preserve an explicitly present XML standalone declaration. `@indent` (§6.4) and `@doctype` (§11.1) MAY each appear once before the root. If absent, the standalone state is unspecified. All XML output uses version 1.0 and UTF-8; the literal presence of the original `<?xml ...?>` declaration, its quote style, and its original encoding are not retained.

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

1. If currently inside **either** an ordinary `|` multiline value or a named-attribute block, interpret the line as a block payload line or exact `end` delimiter; do not recognize other statements there.
2. `#...` is a source-only comment.
3. Exactly `end` closes the current ordinary element; it is an error when none is open.
4. `.QName...` declares an attribute in the current declaration section.
5. `=...` appends a text child.
6. `@...` dispatches a known directive (`@sml`, `@standalone`, `@indent`, `@doctype`, `@ns`, **`@block`**, `@noindent`, `@comment`, `@pi`, `@element`, `@use`) or an activated extension; unknown directives are errors.
7. A statement consisting of an `NCName`, one or more spaces/tabs, and `|` is an **alias invocation**. It is an error unless the current element is still in its declaration section and an active `@block` alias with that name matches the current element's resolved expanded name. Otherwise, parse the following lines as that attribute's literal block (§8). A statement without the `|` marker is never an alias invocation.
8. Otherwise, require a legal `QName`, optional immediately attached `(...)` inline attributes, then exactly one of: end-of-line (open), terminal `/` (empty), or whitespace + `=` + value (inline text). Anything else is invalid.

The `end` check is exact after line trimming: `end foo` is an error in this revision. A normal element named `end` must use `@element end`. The parser maintains a scoped block-alias table; alias declarations are resolved when encountered, before matched invocations. Local `@ns` directives are forbidden after the first `@block` declaration or named-block invocation in the same element.

### 12.2 Statement grammar (lexically parameterized EBNF)

```ebnf
Document          = Prolog, RootElement, Epilog, EOF ;
Prolog            = { PrologItem }, [ Doctype, { PrologItem } ] ;
PrologItem        = Blank | SourceComment | Header | XMLComment | PI ;
Epilog            = { Blank | SourceComment | XMLComment | PI } ;
RootElement       = Element ;
Element           = Normal | Empty | InlineText | ExplicitElement ;
Normal            = Opener, EOL, { DeclarationOrContent }, EndLine ;
Empty             = Opener, "/", EOL ;
InlineText        = Opener, HSpace, "=", ValueTail ;
ExplicitElement   = "@element", HSpace, QName, EOL,
                    { DeclarationOrContent }, EndLine ;
Opener            = QName, [ InlineAttributes ] ;
InlineAttributes  = "(", [ InlineAttribute,
                    { ",", InlineAttribute } ], ")" ;
InlineAttribute   = QName, HSpaceOpt, "=", HSpaceOpt, InlineValue ;
DeclarationOrContent = Blank | SourceComment | Attribute | Namespace
                    | BlockDeclaration | NamedAttributeBlock | NoIndent
                    | Element | Text | XMLComment | PI ;
NoIndent          = "@noindent", EOL ;
Attribute         = ".", QName, ValueTail ;
BlockDeclaration  = "@block", HSpace, QName, HSpace, NCName,
                    HSpace, [ "=", HSpace ], ".", QName, EOL ;
NamedAttributeBlock = AliasName, HSpace, "|", EOL, { BlockLine }, EndLine ;
Text              = "=", ValueTail ;
XMLComment        = "@comment", ValueTail ;
ValueTail         = EOL | HSpace, ScalarValue, EOL
                  | HSpace, Block ;
ScalarValue       = Raw | Quoted ;
Block             = "|", EOL, { BlockLine }, EndLine ;
Namespace         = "@ns", HSpace, [ NCName, HSpace ], ScalarValue, EOL ;
PI                = "@pi", HSpace, NCName,
                    [ HSpace, PIValue ], EOL ;
EndLine           = "end", EOL ;
Header            = "@sml", HSpace, "1.0", EOL
                  | "@standalone", HSpace, ("yes"|"no"), EOL
                  | "@indent", HSpace, ( "tab" | IndentWidth ), EOL
                  | "@use", HSpace, Identifier, HSpace, Version, EOL ;
Doctype           = "@doctype", HSpace, XMLName,
                    [ HSpace, ExternalId ], EOL ;
ExternalId        = "PUBLIC", HSpace, Quoted, HSpace, Quoted
                  | "SYSTEM", HSpace, Quoted ;
```

`QName`, `NCName`, and `XMLName` use the XML/Namespaces productions; `AliasName` is an `NCName` that must name an active, matching block alias in the containing element's still-open declaration section (otherwise the statement is an error); `HSpace` is one or more spaces or tabs; `IndentWidth` is a decimal integer from 1 to 16 without leading zeros. `Raw`, `Quoted`, `InlineValue`, `PIValue`, and `BlockLine` are defined by the deterministic scanner in §§6–9. `ValueTail` consumes the block terminator **instead of** an additional statement EOL; this prevents a double-terminator ambiguity. Namespace and PI payloads use scalar values only (no `|` block). For `@ns`, an optional `NCName` prefix is recognized only when **two** tokens are present (the prefix and its URI); a single token is the default URI. Structural `EOL` may also be EOF where no open construct remains and no block delimiter is missing. Leading and trailing spaces/tabs are removed from **every** line outside blocks, including value-bearing lines (§4); block payload lines lose trailing spaces/tabs and their shared indentation (§7). Comments/blank lines do not end a declaration section; a preserved XML comment or PI does. Document headers and `@doctype` are allowed only in the prolog; `@ns`, `@block`, and `@noindent` are allowed only inside a normal element's declaration section. `@ns` MUST precede the first local `@block` declaration or alias invocation. The optional `=` in `BlockDeclaration` is a legacy spelling; canonical writers omit it.

The EBNF is a *statement grammar*, not a substitute for XML name validity, UTF-8 validity, whitespace and delimiter interpretation, namespace resolution, or semantic validation.

### 12.3 Error behavior

Strict readers MUST reject malformed syntax, duplicate expanded attributes (including aliased duplicates), unbound prefixes in `@block` declarations, conflicting aliases within a lexical scope, alias invocations with no matching active alias or after the declaration section has closed, invalid comments/PIs (including PI targets containing colons and PI data beginning with whitespace), text statements in a laid-out element (§6.4), repeated or misplaced `@indent`, `@noindent`, or `@doctype`, malformed `@doctype` identifiers, attribute/namespace/block declarations after content, namespace declarations after a local `@block` declaration or alias invocation, unsupported extensions, document content outside the root, and unclosed elements/blocks. No implicit recovery or HTML-like repair is allowed. Editors MAY maintain a separately flagged recovery tree for interactive editing.

## 13. Extensions and implementation contracts

Extensions remain opt-in (`@use identifier version`) and MAY define additional `@` directives or validation/transformations. **`@block` is Core syntax and MUST function without `@use` or any extension.** They MUST NOT redefine ordinary SML element, attribute, text, value, namespace, or `end` semantics. The core SVG round-trip pipeline MUST operate without extensions. No include, interpolation, template, schema, typed scalar, or arbitrary code execution is included in Core.

A reader SHOULD expose streaming events `StartDocument`, `DocumentType`, `StartElement`, `Text`, `Comment`, `ProcessingInstruction`, `EndElement`, `EndDocument`, with expanded + lexical names, namespace declaration metadata, attributes, source positions, and exact text. It buffers the *current element's declarations*, including ordinary attributes and decoded named-attribute blocks, before emitting `StartElement`. Alias definitions are held in a scoped mapping table and never emitted as XML nodes. Self-closing and inline-text elements emit matching start/end events. Whitespace predicted by `@indent` (§6.4) is emitted as ordinary `Text` events. A DOM preserves heterogeneous ordered children. A writer MUST never inject XML formatting whitespace into element content in strict mode.

Source locations SHOULD include original UTF-8 byte offsets and 1-based line/Unicode-scalar columns. Parsers SHOULD be linear-time in input length, with configurable limits on document bytes, depth, attribute count, and value length. XML import MUST disable network entity fetching, loading of external DTD subsets, DTD processing, automatic XInclude, and processing-instruction execution. `@doctype` records the external identifier only (§11.1).

## 14. Canonical writer rules

SML writers SHOULD emit:

- Two-space indentation, LF physical line endings, UTF-8, and plain `end`.
- `.attribute` on its own line when values contain spaces or are long; compact inline attributes only when their delimiters and strings are unambiguous.
- `name = value` for a short text-only element *only if* it has no local `@ns` directives and no other children.
- `name/` only if it has no local namespace declarations and no children.
- Quoted values for text or attributes containing CR/LF/TAB. Quoting is **required** (§6.2) for values that begin or end with a space or tab, begin with `"`, or are exactly `|`.
- All namespace declarations explicitly at their original scopes and lexical prefixes retained for SVG imports.
- Literal blocks, including named-attribute blocks, only if the shared-indent rule reproduces the exact source value, no payload line collides with `end`, and no payload line ends with a space or tab.
- `@block element-qname alias .attribute-qname` without `=`, preceding its uses; a normal `.attribute` fallback when a value cannot be represented faithfully as a block.
- Alias invocations as `alias |`. Preserve alias declarations during source-only reformatting or rewrite their invocations consistently. `@element` only for elements named `end`.
- `@indent` with the unit (tab, or 1–16 spaces) that matches the most elements, preferring two spaces on a tie, with `@noindent` and explicit text exactly as §6.4's writer rule requires. Omit `@indent` when no element matches.
- `@doctype` exactly as recorded, at its original prolog position.

SML formatters MUST be idempotent and MUST NOT modify element, text, comment/PI order or values, SVG strings, namespace declaration locations, or lexical prefixes under the strict SVG profile. Canonicalization MAY change alias spelling/layout only if it preserves every attribute value. Because alias invocations carry the `|` marker, introducing an alias cannot change how an existing child-element opener parses.

SVG XML writers MUST encode character data and attribute values according to XML 1.0, including preserving CR characters in text through character references and preserving TAB/LF/CR in attributes using character references. The XML emitter MUST NOT insert indentation/newline text nodes for pretty-printing in strict round-trip mode.

## 15. SVG-specific tests and acceptance gates

Use the existing 18-fixture SVG corpus as the initial positive baseline, **plus** the following mandatory new fixtures:

| ID | Fixture | Required assertion |
|---|---|---|
| SV-19 | Prolog/epilog comment and PI | Exact node order preserved |
| SV-20 | An otherwise unused namespace declaration | Retained on the original element |
| SV-21 | Prefix used in CSS selector or QName-like attribute | Prefix text and binding not rewritten |
| SV-22 | Default namespace undeclaration inside `foreignObject` | Exact scope and expanded names |
| SV-23 | Text including CR, LF and TAB | Exact XML-reparsed characters |
| SV-24 | Attribute containing CR, LF and TAB | Correct character-reference round trip |
| SV-25 | XML `<end>` foreign element | `@element end` supported |
| SV-26 | CSS/style payload with a physical line `end` | Canonical writer selects quoted fallback |
| SV-27 | Namespace declared directly on an XML empty element | SML writer uses ordinary open/`@ns`/`end` |
| SV-28 | DTD internal subset (including empty `[ ]`), custom entity, or external resource expansion attempt | Strict import refuses without loading resources |
| SV-29 | Unknown foreign attributes/elements, nested prefixes | Preserved without interpretation |
| SV-30 | Original `standalone="no"` and `"yes"` cases | Declaration state preserved if present |
| SV-31 | An SML XML comment or PI containing `\r` | SML reader rejects nonrepresentable data |
| SV-32 | Attribute with multiple aliases for same URI | Lexical QName and declarations remain stable |
| SV-33 | No-default-namespace `@block div style .style` | Alias targets no-namespace `div` and no-namespace `style` attribute |
| SV-34 | Default-namespace unprefixed `@block path data .d` | Alias targets the current default namespace, but attribute `d` remains unqualified |
| SV-35 | Prefixed `@block s:path data .d` | Resolves bound prefix to expanded element name; descendant prefix changes do not retarget mapping |
| SV-36 | Missing or later prefix declaration | Reject alias with unbound prefix or `@ns` after local `@block` |
| SV-37 | Alias `data` vs. real XML `<data>` child | Bare `data` opens the child; `data \|` creates the attribute only; no `@element` needed |
| SV-38 | Named block after another dot attribute | Both attributes retained; declaration section remains open |
| SV-39 | Named block and `.d` in same element | Duplicate expanded-name attribute error |
| SV-40 | Two block aliases mapping to same target attribute | Duplicate if both are invoked |
| SV-41 | Alias declared after child content | Reject late `@block` |
| SV-42 | Alias name `end` | Reject reserved alias name |
| SV-43 | Inherited alias shadowed in nested scope | Nearest mapping applies; same-scope duplicate declaration fails |
| SV-44 | Named block containing bare `end` as data | Writer falls back to quoted `.attribute` with escapes |
| SV-45 | Named block with significant leading spaces or terminal LF | Writer falls back if dedentation/termination would change exact string |
| SV-46 | Optional legacy `=` in declaration | Parses identically; canonical writer omits `=` |
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

**Mandatory round-trip acceptance:** strict XML parse → write SML → parse SML → write SVG XML → parse XML → compare a normalized tree that includes lexical prefix bindings, declaration locations, ordered comments/PIs including root-external ones, all attributes and exact text; then repeat for idempotent information equality. Reject—not drop—unsupported input. Rendering comparisons are useful supplements, **not** substitutes for tree equality. Readable imports (§3.1) are expressly **lossy** and cannot claim this strict profile.

The existing prototype's 18/18 result establishes only an earlier restricted **RT-1 element-tree** subset. The SV-33–SV-70 tests are **specified but have not been executed against a conforming implementation**. It does **not** certify this revision's full syntax, document-level information, or SVG-safe prefix rules. A new independent conformant SML parser and XML converter are needed before claiming verified full round-trip conformance.

## 16. Worked edge cases

**Namespaced, empty XML element** (cannot self-close in SML because it has a declaration):

```xml
<p:x xmlns:p="urn:vendor"/>
```

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

**Core `@block` applied to CSS-like attribute text without a namespace:**

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

This represents a no-namespace `body` element containing a `div` with the unqualified `style` attribute value `margin: 10 0 10 0;\npadding: 0 5 0 5;\nfont-weight: bold;`. Whether these CSS lengths are valid is an application concern, not SML syntax validation.

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

**Multiline style with literal `end` line:** the serializer must use quoted `\n`, not a block whose first bare `end` line would close the value:

```sml
style = "line one\nend\nline three"
```

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

This revision removes the identified *design-level* contradictions, but it cannot guarantee the absence of all implementation bugs before a reference parser, differential XML comparison suite, and fuzzing exist. In particular:

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

**Status after this revision:** SML 1.0-SVG-3 is a compact, SVG-first design baseline with generic, explicitly marked `@block` aliases integrated into Core, whitespace-trimmed raw values, an indentation model for readable strict conversions, and opaque DOCTYPE preservation. Its required acceptance criterion is an information-preserving *strict SVG-safe round trip*, not simply readable generated text or visual similarity.
