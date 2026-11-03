# Simplified Markup Language (SML) 1.0 — SVG-first revised specification

**Status:** Proposed normative revision (design baseline, not yet reference-parser certified)  
**Revision:** 1.0-SVG-1 · 2026-11-03  
**Goal:** Human-editable representation of SVG/XML with deterministic, information-preserving two-way translation  
**Extension:** `.sml` (provisional; naming and extension choice are independent of syntax)  
**Naming note:** Both the name and the extension collide with existing uses: `.sml` is the standard extension for Standard ML source files, and "SML" is also W3C's XML-based Service Modeling Language. A distinct name and extension SHOULD be chosen before 1.0 is final.

> **Conformance boundary:** This revision specifies a well-defined mapping for the **SVG-safe XML subset** described in §3. It does **not** promise lossless import of every legal XML document, original XML bytes, DTD internal subsets, external DTD content, or entity declaration syntax. A strict converter (§1.2) MUST reject information outside the supported subset instead of silently deleting it.

The capitalized words **MUST**, **MUST NOT**, **SHOULD**, and **MAY** have their ordinary normative meanings.

## 1. Core design

SML is an alternate *serialization* of an XML-compatible document tree, **not** an SVG drawing language, a schema, a template system, or a geometry engine. SVG properties such as `d`, `viewBox`, `transform`, `points`, `style`, `href`, `xml:space` and CSS source are opaque Unicode strings to the converter. A renderer or editor may interpret them later; import/export MUST NOT normalize numbers, paths, styles, IDs, references, or geometry.

These design decisions are in force.

1. Ordinary elements are closed with an explicit `end`. Two intentionally compact forms (`name/` and `name = value`) close themselves.
2. Inline text and parenthesized inline attributes are supported.
3. Indentation does **not** nest elements; literal multiline blocks remove shared structural indentation.
4. XML **information** is preserved, not original XML quotation, indentation, or entity spelling. Attribute order, which XML does not treat as information, is kept as well, so converted files read like their sources.
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

A **strict, lossless** SML form. The whitespace between child elements follows two-space pretty-printing, which SML assumes unless an `@indent` header says otherwise (§6.4), so the reader regenerates those whitespace-only text nodes and the SML does not spell them out:

```sml
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

`svg` and `g` are *laid out*: the reader inserts `LF` plus two spaces per depth level before each of their children and before their closing tags, reproducing the XML indentation exactly. `text` begins with a text statement, so its mixed content is taken exactly as written; the quoted `"Hello "` preserves the space before the `tspan`. With `@indent none`, the same statements would describe XML with no whitespace between elements.

A block-oriented path representation is available whenever an in-scope alias has been declared. It is not equivalent to the original; see below:

```sml
svg
  @ns http://www.w3.org/2000/svg
  @block path .d data
  path
    .fill none
    data """
      M 0 0
      L 100 50
      Z
      """
  end
end
```

Here `data """ … """` means the same as `.d """ … """`; the resulting attribute contains LF between the displayed commands. It is **not** text content or an SVG child element. A bare `data` line, without the block marker, would open a child element named `data`. This example's `d` value is not identical to the single-line original, since its whitespace differs; it illustrates authoring rather than an automatic fidelity-preserving rewrite. It has no `@indent` header, so its XML is indented by two spaces.

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
- **Readable import profile** (§§3.1–3.2): lossy, SVG-aware import options that normalize formatting whitespace, reflow structured attribute values, or tidy code such as CSS.

Examples, notes, and §16 are informative; all other text is normative. Where an example conflicts with normative text, the normative text governs.

## 2. Core information model

A document contains exactly one root **element**, plus an ordered sequence of top-level comments and processing instructions before/after the root, an optional document type declaration (name, optional public ID, optional system ID) positioned among the comments and PIs before the root (§11.1), and whether the XML declaration says `standalone="yes"` (§11). An element has:

- A lexical qualified name (`prefix:local` or `local`), its resolved expanded name `(namespace URI or null, local name)`, and its explicit in-scope namespace declarations (including declarations on that very element).
- Attributes, each with a lexical qualified name, expanded name, and exact string value, in document order. XML gives attribute order no meaning, but SML keeps it. Namespace declarations keep their order among themselves too; they come before the attributes, wherever the XML put them.
- An ordered sequence of element, text, XML comment, and processing-instruction children.

Adjacent text nodes may be coalesced and zero-length text nodes ignored for semantic equality. All attribute and text characters otherwise remain exact, **including whitespace-only text**. A comment's data, by contrast, is normalized from XML and SML alike (§9): it never begins or ends with whitespace, and the lines of a multi-line comment share no indentation. Duplicate attribute expanded names are illegal.

**SVG-safe prefix rule:** Unlike a minimal namespace-only XML model, the strict profile (§1.2) retains **the original lexical prefixes and the places where `xmlns` bindings are declared**, including unused bindings. These can affect QName-like values, prefix-sensitive content, vendor metadata, or CSS. The writer MUST NOT arbitrarily rename/rebind prefixes. This stronger rule is a conservative SVG interoperability guarantee; it does not mean full byte-level XML source preservation.

## 3. Supported SVG/XML subset and round-trip promise

**In scope for strict SVG import/export:** XML 1.0 document elements and attributes; Namespaces in XML 1.0; Unicode text, including whitespace; comments; processing instructions (including those outside the root); predefined XML entities and numeric character references **after** normal XML decoding; the `standalone` flag, where `no` means the same as no flag (§11); DOCTYPE declarations **without** an internal subset, preserved as opaque name/public ID/system ID metadata whose external DTD is never loaded (§11.1); CDATA *character content* (not its delimiters); unknown/vendor-namespaced content; embedded CSS or script as inert text; foreignObject markup; relative and data-URI attributes as raw strings.

**Out of scope for Core import:** DTD internal subsets (including empty ones), loading or processing an external DTD subset, declared general or parameter entities, references to entities other than the five predefined ones, external entity resolution, XInclude processing, XML 1.1, and source-level lexical details such as exact entity references, quote styles, CDATA section boundaries, XML declaration spelling, and original encoding. When an input contains any construct outside the supported subset, a strict converter MUST return an explicit error. It MUST NOT quietly strip or expand it. A future optional *legacy XML import* profile may safely materialize DTD-derived content, but would require its own security and preservation contract.

SML source is UTF-8. XML import MAY accept other encodings declared by a secure XML parser; exported SVG XML is UTF-8. Input and output need not be byte-identical.

For every representable document `D`:

```text
ParseSML(WriteSML(D))  ≡  D
ParseXML(WriteXML(D))  ≡  D
```

The equality `≡` includes the ordered content, the order of each element's namespace declarations and of its attributes, the SVG-safe prefix/declaration contract above, and the document type declaration's name, public ID, system ID, and position among prolog comments/PIs. It excludes merely lexical XML trivia such as indentation outside element text, the whitespace at the ends of a comment and the indentation its lines share, where namespace declarations stand among the attributes, self-closing notation, character-reference spelling, CDATA boundaries, and the quotation marks around DOCTYPE literals. SML representation choices are not part of `D`. These include `@block` and `@node` aliases, `@indent`/`@noindent`, raw versus quoted values, continuation lines, block terminators, inline versus vertical attributes and namespace declarations, and `end QName`.

**Do not confuse SVG rendering equivalence with information equivalence:** the XML text `<path d="M0 0 L2 2"/>` and `<path d="M0,0L2,2"/>` may render alike, but are not string-identical and MUST NOT be silently substituted during conversion.

### 3.1 Readable (lossy) import profile

A converter MAY offer a *readable* XML import that normalizes formatting whitespace. Its output is ordinary SML, but the conversion is **lossy**: it MUST NOT be described as strict or SVG-safe, and it SHOULD mark its output as such with `tidy` on its `@sml` line (§11). Unlike Core parsing, this profile is deliberately SVG-aware.

Apart from the cleanup of §3.2, a readable import may change only the whitespace-only text children of an **eligible** element. Whitespace here means U+0020, U+0009, U+000A, and U+000D. An element is eligible only if all of the following hold:

1. it is not the SVG element `foreignObject` and has no such ancestor;
2. the nearest `xml:space` attribute on it or an ancestor, if any, is not `preserve`, unless it is an SVG element that is neither SVG text content nor inside such an element;
3. it has at least one child element, comment, or PI, and every text child is whitespace-only;
4. if it is SVG text content (`text`, `tspan`, `textPath`, `tref`, or `altGlyph`), or is inside such an element: a text child containing a space or tab lies between every two of its other children, and, if it is inside a text content element, also before the first and after the last of them.

In an eligible element, the readable import replaces all whitespace-only text children with the §6.4 prediction of `@indent 2`, whatever the input's indentation was. Its output therefore uses the default indentation, without an `@indent` header, and needs no per-element `@indent`, `@noindent`, or whitespace-only text statements for these elements, and the XML written back from it is indented by two spaces. Every other element is converted exactly as in strict import.

These conditions are deliberately conservative. In SVG text content, whitespace renders: with the default `xml:space`, a run of spaces and tabs between two children renders as one space, and at the edges of a `text` element as nothing, so the prediction, which puts a line break and spaces there, renders the same. SVG 1.1 removes line breaks without adding a space, though, so a gap of line breaks only (as in `<tspan>A</tspan>` LF `<tspan>B</tspan>`, which renders `AB`), and the edges of an element nested in text, keep their whitespace. SVG applies `xml:space` to the text of text content elements only; whitespace between other SVG elements never renders, so they stay eligible under `preserve`. In text content under `preserve`, every space and line break renders, in elements of other namespaces there the vocabulary may give it meaning, and in `foreignObject` content it can change rendering in other ways, so a readable import MUST NOT normalize it.

### 3.2 Readable cleanup: reflow and tidy text

A readable import MAY also *clean* the SML: *reflow* structured attribute values, rewriting each value so that every item is on its own line, and *tidy* multi-line text and comments. A writer can then show such a value as a block (§7), an attribute value usually through an alias (§8). Cleaning changes attribute strings and text, so its result is a different document (§3). It MUST be selected explicitly, and its output SHOULD be marked `tidy` on its `@sml` line (§11). A converter reflows only the attributes it has been given a *layout* for, such as by a writer profile. Core SML has no knowledge of layouts, and an SML reader needs none: the output contains only the cleaned values and ordinary alias declarations.

A layout names how a value divides into items. Whitespace here means U+0020, U+0009, U+000A, and U+000D.

| Layout | Item | Form the value must have |
|---|---|---|
| `path` | A path command: its letter and everything up to the next command letter | Only digits, `.`, `+`, `-`, `e`, `E`, `,`, whitespace, and the command letters `MmZzLlHhVvCcSsQqTtAa`; the first character other than whitespace is a command letter |
| `numbers N` | N numbers (the last item may have fewer) | Numbers in SVG number syntax, separated by whitespace with at most one comma, or by nothing where the syntax allows, as in `1-2` |
| `declarations` | A CSS declaration, with its `;` | Quotes, parentheses, and comments closed; a `;` outside them ends an item |
| `functions` | A function, `name(…)` | Functions with balanced parentheses, separated by whitespace with at most one comma |
| `list` | An entry, with its `;` | Any value; each `;` ends an item |

An item never begins or ends with whitespace. Between items there is only whitespace, a comma (`numbers`, `functions`), or nothing. The reflowed value is the items joined by single LFs, without leading or trailing whitespace. In the `path` layout, each item is also spaced. A space is inserted after the command letter, after each comma, and between two parameters written together, wherever no space follows and the item continues: `M10,20` becomes `M 10, 20`, and `a.75.75 0 01-.69-.35` becomes `a .75 .75 0 0 1 -.69 -.35`. Parameters are read as SVG path data reads them: numbers in SVG number syntax, each as long as possible, and the two flags of an arc command (the 4th and 5th of every 7 parameters) as single characters `0` or `1`. A space therefore goes only where path data already has a boundary. If the rest of an item is not such parameters, separated by spaces and at most one comma, only the letter and the commas are spaced. Items of the `numbers` layout are spaced the same way, without the letter: `10,20` becomes `10, 20`, and `1-2` becomes `1 -2`. Reflow MUST NOT be applied to a value that does not have its layout's form, that has fewer than two items, or that has an item containing TAB, LF, or CR; such a value is converted exactly. In SVG path data, coordinate and number lists, CSS declarations, transform lists, and SMIL value lists, a line feed is an allowed separator wherever these layouts put one, and path data and number lists allow a space after a command letter or a comma and between parameters. So for the attributes these layouts are meant for, the reflowed document renders as the original does.

A writer MAY still use a layout to show a value one item per line exactly, such as one that cannot be reflowed: as a quoted value split into continuation lines (§6.5) at item boundaries, with the separators kept inside the quotes. That value is unchanged.

**Text and comments.** Tidying applies to multi-line text: the text of an element whose content is a single text node outside SVG text content, `foreignObject`, and `xml:space="preserve"`, such as a `style` sheet, a `script`, a `desc`, or metadata, and the data of a comment anywhere. It removes the spaces and tabs at the end of every line, the empty lines at the start and the end, runs of empty lines down to one, and the indentation that all non-empty lines share. A comment's data is already normalized (§9), so for a comment only the line ends and the empty lines change. Text without a line break, with a CR, or of whitespace only is left alone. Tidied text with more than one line is written as a block: text as a laid-out text block (§6.4) in a laid-out position, which puts the line breaks and indentation of pretty-printed text back around it, and otherwise, and for every comment, as a literal block. Tidying is not applied to SVG text content, because SVG 1.1 removes line breaks without adding a space, so removing the indentation after one could join two words. In CSS such whitespace never matters, and in scripts only inside multi-line strings such as JavaScript template literals.

**Style sheets.** The CSS of an SVG `style` element whose `type` is absent, empty, or `text/css`, outside SVG text content and `foreignObject`, and also under `xml:space="preserve"`, since whitespace in CSS never renders, is laid out instead, on one line or several: each statement on its own line; a rule as its prelude (such as a selector), a space, and `{`, then its contents one level (two spaces) deeper, then `}`; and declarations, at-rules such as `@import`, and comments each on a line of their own. Nested blocks, such as those of `@media`, are laid out the same way. Strings, comments, and the text inside parentheses, such as `url(data:…;base64,…)`, are kept as they are, and so is the whitespace inside a statement, except that a run of whitespace with a line break becomes one space. A style sheet with an unclosed string, comment, parenthesis, or block, a stray `}` or `)`, or `<!--` or `-->` is tidied as text instead. For example, `.fnt26 {font-weight:normal;font-size:97;font-family:FontID19, Symbol}` becomes

```css
.fnt26 {
  font-weight:normal;
  font-size:97;
  font-family:FontID19, Symbol
}
```

## 4. Encoding, lines, names, and structural model

- SML source MUST be valid UTF-8; a UTF-8 BOM is permitted on input, and writers MUST NOT emit one. Invalid byte sequences are errors.
- CRLF and lone CR source *line separators* are normalized to LF for lexical processing. `\r` within a quoted value represents an actual U+000D data character.
- Only LF, CR, and CRLF separate lines. U+0085 (NEL), U+2028 (LINE SEPARATOR), and U+2029 (PARAGRAPH SEPARATOR) are ordinary data characters. Writers SHOULD write them, the C1 control characters, and other invisible format characters (Unicode general category Cf, such as bidirectional controls) as `\u{…}` escapes in quoted values, so that no data is invisible.
- Unicode normalization (NFC, NFD, etc.) MUST NOT be performed.
- An ordinary statement is one physical line. Outside multiline blocks, **leading and trailing spaces/tabs are removed from every line** before it is interpreted; there they never carry data. Data that begins or ends with a space or tab must therefore be quoted (§6.2), or be in a block, whose lines keep them (§7). Blank lines outside blocks are ignored.
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
| `@block element-qname .attribute-qname alias` | Core declaration mapping a named multiline block to an attribute on matching elements; before the root or in a declaration section (§8.1) |
| `alias """` … `"""` | In a matching element's declaration section, supplies the declared attribute as a multiline value (`alias"""` is equivalent) |
| `@node element-qname alias` | Element alias: an opener spelled `alias` opens an element named `element-qname`; before the root or in a declaration section (§8.7) |
| `@noindent` | Marks the current element's content as exact: no predicted formatting whitespace is inserted (§6.4); only in its declaration section |
| `@rem value` | Preserved XML comment node; `@comment value` is a synonym (§9) |
| `@comments """` … `"""` | Several preserved XML comment nodes, one per payload line (§9) |
| `@pi target [value]` | Preserved processing instruction |
| `# ...` | Source-only comment (omitted from XML) |
| `end` or `end QName` | Close current ordinary element; with a QName, the name is checked (§5.3) |
| `"""`, `""""`, … | Block marker after `.name`, `=`, `name =`, `@rem`, or an alias; the block ends at a line that repeats the marker (§7) |
| `@element QName` | Explicit normal element opener, required for an element named `end` or named like an element alias in scope; never uses element aliases |
| `@sml VERSION [tidy]` | Optional document version header; must come first; `tidy` marks the output of a readable import (§11) |
| `@standalone yes` | XML `standalone="yes"`; `@standalone no` is the default (§11) |
| `@indent N` / `@indent tab` | Optional header enabling the formatting-whitespace model; in a declaration section, the indentation step of that element's children (§6.4) |
| `@doctype name [PUBLIC "pubid" "sysid" \| SYSTEM "sysid"]` | Optional document type declaration, preserved as opaque metadata (§11.1) |
| `@use identifier version [optional]` | Opt-in extension declaration (see §13) |
| `@identifier:name [value]` | Directive of a declared extension (see §13) |

XML export always targets XML 1.0 in UTF-8.

### 5.1 Declaration section

A normal element opener is followed by a **declaration section**. It contains, in this order:

1. zero or more `@ns` namespace declarations (§10), which **MUST come first**;
2. then, in any order, `@block` declarations (§8), ordinary `.attribute` statements, alias invocations (`alias """`), `@node` element aliases (§8.7), at most one `@noindent` marker, and at most one `@indent` step (§6.4).

Blank lines, `#` source comments, and extension directives (§13) may appear anywhere. An `@ns` line after any statement of the second group is an error.

Because all of an element's namespace declarations precede everything else, its name, attributes, `@block` declarations, and alias invocations resolve against one fixed set of bindings in a single pass. Those declarations are the inline `xmlns` entries on the opener (§6.3) and the `@ns` lines. This is the only ordering rule for namespaces; other sections refer to it.

Once the first actual child/text/`@rem`/`@pi` occurs, the declaration section closes. Any subsequent `.attribute`, `@ns`, `@block`, `@node`, `@noindent`, `@indent`, or alias invocation is an error. A bare name line is always an element opener, whether or not it matches an alias name (§8.3). A streaming `StartElement` event is emitted when the declaration section closes, after the parser has collected namespace bindings, block declarations, and all ordinary and aliased attributes.

An empty element (`name/`) and an inline-text element (`name = value`) have no declaration section. They cannot have `.attribute`, `@ns`, `@block`, `@node`, `@noindent`, or `@indent` statements or invoke a named block, and they declare namespaces only through inline `xmlns` entries (§6.3). An element that needs vertical attributes, aliases, or `@ns` lines uses the normal opener plus `end`, even if it is otherwise empty or text-only.

### 5.2 Explicit element opener (`@element`)

An XML element named exactly `end` is written:

```sml
@element end
  .id example
  = text
end
```

`@element` accepts any valid QName (including prefixed names), always opens a normal element, and requires a matching ordinary `end`. It has no inline-attribute, inline-text, or self-closing variants. It is not an alias invocation. A plain `end` line always closes an element, never opens one.

The unprefixed name `end` is **reserved** in ordinary element forms: `end()`, `end/`, `end(id=a)/`, and `end = x` are errors, not elements. `@element end` opens an element named `end`, and so does an element alias for it (§8.7). Prefixed names such as `x:end` are not reserved.

`@element` never uses element aliases (§8.7): its operand is the element's name as written. It is how an element whose name equals an element alias in scope is written.

`@element` is **not** needed to distinguish a child element from a named attribute block. An alias invocation always carries a block marker (§8.3), so a bare `style` line opens a `style` child element even where a `style` alias is active:

```sml
body
  @block div .style style
  div
    style
      = A genuine child element, not the .style attribute
    end
  end
end
```

### 5.3 Checked end (`end QName`)

`end QName` closes the current element exactly like `end`, and also asserts its name. The QName MUST be spelled identically to the element's opener (for `@element`, its operand), or, for an element opened with an element alias (§8.7), either the alias or the element's QName; otherwise the reader reports an error. The check compares lexical spellings, not expanded names. It carries no information and is optional; writers MAY use it to label the ends of long elements.

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

1. **Raw:** All characters from the first to the last non-space/tab character are literal, with no backslash escapes or entity decoding. A raw value never begins or ends with a space or tab (§4). This mode applies unless the value begins with `"`. In every position, including inline attributes, `@pi`, and `@ns`, a raw value cannot begin with `"`.
2. **Quoted:** If the first value character is `"` and the value is not a block marker, the **entire** value MUST be a correctly terminated double-quoted token with no characters after the closing quote. (Trailing spaces/tabs on the line were already removed by §4 and are permitted.) Quoted values support `\\`, `\"`, `\n`, `\r`, `\t`, and `\u{H…}`. In `\u{H…}`, `H…` is 1 to 6 hexadecimal digits in either case, naming a Unicode scalar value: not above U+10FFFF and not a surrogate code point (U+D800–U+DFFF). Any other escape, any surrogate, and any character that is not a valid XML 1.0 `Char` (such as `\u{0}`) is an error. A quoted value may continue on following lines (§6.5).
3. **Block:** A value that consists of three or more quotation marks and nothing else (`"""`, `""""`, …) starts a multiline block (§7). The space before the marker is optional: `.d"""`, `="""`, `name ="""`, `@rem"""`, and `alias"""` mean the same as `.d """`, `= """`, `name = """`, `@rem """`, and `alias """`. Two quotation marks are a quoted value, the empty string; anything after a run of three or more is an error.

No quoted/raw fallback is attempted for a malformed leading quotation mark; it is an error. A serializer MUST quote any value that begins or ends with a space or tab, or begins with `"`; written raw, such a value would be trimmed or reinterpreted.

For SVG information preservation, a value containing LF or CR cannot be raw: a writer MUST use quoted escapes or, where §7 permits, a block. XML output MUST encode U+000D in text and U+0009/U+000A/U+000D in attribute values as character references where required to survive XML parsing unchanged.

### 6.3 Inline attributes

The compact opener syntax is `QName(` *attributes* `)` with no whitespace between the element name and `(`. Inline attributes are comma-separated `QName=VALUE` pairs. Optional horizontal whitespace surrounds `=`, `,`, and the list. `name()` is legal. `name(x=)/` means an attribute with an empty value.

A raw inline value runs to the next `,` or `)` that is **not inside balanced parentheses**. Each `(` in the value opens a group that a later `)` closes, and a `,` inside a group is data. So `transform=translate(10,20)` and `fill=url(#grad)` need no quotes. A raw inline value:

- has its leading and trailing spaces/tabs removed;
- cannot begin with `"`;
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

**Header.** `@indent N`, where `N` is a decimal integer from 0 to 16 without leading zeros meaning `N` spaces, `@indent tab`, or `@indent none`, MAY appear once in the prolog. `@indent 0` describes XML that puts each child on its own line without indentation, as many design tools export it: the predicted whitespace is then a single LF. It sets the indent unit `I`. **Without an `@indent` header, `I` is two spaces**, as if `@indent 2` were written, so SML written by hand without one gives XML indented by two spaces. `@indent none` turns the model off: the reader never inserts whitespace, and every text node is written explicitly. The same directive in an element's declaration section sets that element's step instead (see *Per-element steps* below).

**Laid-out and exact elements.** Unless the document has `@indent none`, an element `E` is **laid out** when both of the following hold:

1. `E` is not marked `@noindent`; and
2. `E`'s first content statement is a child element, `@rem`, or `@pi`, not a text statement.

Every other element is **exact**: elements with no content, elements whose first content statement is text (`=`), and elements marked `@noindent`. An exact element's content is exactly what its statements say.

**Indentation and step.** Every element has an *indentation* `W` and a *step* `S`, both strings of spaces or tabs. The root's indentation is empty. Every other element's indentation is its parent's indentation followed by its parent's step, whether or not the parent is laid out. An element's step is `I`, unless the element has its own `@indent` (see *Per-element steps*). Without per-element steps, an element at depth `d` (the root has depth 0) therefore has indentation `I×d`, where `I×n` means `I` repeated `n` times.

**Predicted whitespace.** For a laid-out element with indentation `W` and step `S` whose non-text children are `N1 … Nk`, the reader inserts the text `LF + W + S` before each `Ni` and `LF + W` after `Nk`. A text statement inside a laid-out element is an error; mixed content whose first node is an element, comment, or PI MUST be marked `@noindent`.

**`@noindent`.** A declaration-section directive with no operands, allowed at most once per element. It applies only to that element, never to its descendants.

**Per-element steps.** `@indent N` or `@indent tab` in an element's declaration section, with `N` as in the header, sets that element's step. It is not allowed under `@indent none`, and appears at most once per element. Like `@noindent`, it applies only to that element: its children's indentation includes it, but their own steps are `I` again unless they have their own `@indent`. It is how a subtree indented differently from the rest of the document, such as metadata copied from another file, stays laid out:

```sml
svg
  @ns http://www.w3.org/2000/svg
  metadata
    @indent 4
    title = Map
    source = Survey
  end
  rect/
end
```

is the XML

```xml
<svg xmlns="http://www.w3.org/2000/svg">
  <metadata>
      <title>Map</title>
      <source>Survey</source>
  </metadata>
  <rect/>
</svg>
```

`metadata` has indentation `I` and step four spaces, so the reader inserts `LF` and six spaces before each child, and `LF` and two spaces before `</metadata>`.

**Streaming.** The reader decides whether `E` is laid out when its declaration section closes, so predicted whitespace is emitted as ordinary `Text` events without lookahead.

Example:

```sml
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

**Writer rule.** Unless it writes `@indent none`, a writer MUST, for each element that has at least one non-text child, compare the element's actual content with the prediction for its indentation and a step, after coalescing adjacent text nodes and dropping empty ones. The step is `I`, or another step that the writer then states with `@indent` in the element's declaration section:

- **Equal:** write the non-text children only.
- **Different, and the first node is text:** write all nodes explicitly. The element is exact because its first statement is text.
- **Different otherwise:** emit `@noindent` and write all nodes explicitly.

Each case reproduces the element's content exactly, so the round trip of §3 holds. Elements without non-text children are written explicitly, as under `@indent none`. An element that is not laid out may still state a step with `@indent`, so that its children's indentation, and so their own layout, follows it (§14.2 rule 9). The indentation of an element's children follows from the step written for it, so a writer compares each element after deciding its parent's step.

**Laid-out positions.** A node is in a *laid-out position* when it is a child of a laid-out element, or when it comes before, after, or is the root element in a document without `@indent none`. Its *indentation* `W` is the whitespace predicted before it, without the LF: empty at the top level, and otherwise its parent's indentation followed by its parent's step. For an element, this is the indentation defined above.

**Laid-out text blocks.** Pretty-printers lay out the multi-line text of an element such as `desc`, `style`, or `script` like the content around it: the start tag is followed by a line break and the text's lines, indented one unit deeper than the element, then a line break and the element's own indentation before the end tag. A block cannot reproduce that whitespace (§7), so `@indent` predicts it as well. A compact text element whose value is a block, `name = """` or `name(…) = """`, in a laid-out position with indentation `W` has this text:

1. LF;
2. each payload line, prefixed with `W + I` unless it is empty, joined by LF;
3. LF and `W`.

A block with no payload lines gives LF and `W`. The payload is indented by the unit `I`, never by a per-element step. A text statement `= """` in the normal form is literal, and so is a compact text element elsewhere. Comment blocks are always literal (§9). For example,

```sml
svg
  @ns http://www.w3.org/2000/svg
  desc = """
    A map of the town centre,
    drawn from the 2025 survey.
    """
end
```

is the XML

```xml
<svg xmlns="http://www.w3.org/2000/svg">
  <desc>
    A map of the town centre,
    drawn from the 2025 survey.
  </desc>
</svg>
```

A writer MUST NOT write the text of a compact element in a laid-out position as a block unless the text has exactly this layout and the payload is a block that §7 reproduces exactly. Text that a literal block reproduces, but that does not have this layout, uses the normal form, whose `= """` statement is literal. Under `@indent 2`,

```sml
desc
  = """
    first line
    second line
    """
end
```

is `<desc>first line` LF `second line</desc>`, with no whitespace at the edges. Other multi-line text in a laid-out compact element is quoted.

### 6.5 Long values: quoted continuation lines

A quoted value may be split across lines. A line whose first character (after trimming) is `"` is a **continuation line**. It MUST consist of exactly one quoted string, which is appended, with nothing in between, to the value of the statement on the immediately preceding line. That statement MUST be an `.attribute`, `=`, inline-text, `@rem`, or `@pi` statement whose value is quoted, or another continuation line. No blank line or `#` comment may come between them. Continuation lines are not available for inline attributes, `@ns`, or `@doctype`.

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

A value of three quotation marks, `"""`, begins a multiline block; the space before the marker may be omitted (§6.2). The block ends at the first following physical line that is the marker, with nothing else on it but spaces and tabs before and after it. That closing line belongs to the *value*, not to the surrounding element. The payload is verbatim: no escapes are processed, lines that look like statements are payload, and spaces and tabs are data, at the end of a line too. There is no nested block syntax.

**Indentation.** The spaces and tabs before the closing marker are the block's *indentation*. It is removed from the start of every payload line, and the rest of the line is data, so a block can sit at any depth of the SML while its content keeps its own indentation:

```sml
style = """
  .road { stroke: #123456; }
    .label { font: 12px sans-serif; }
  """
```

The `style` text is `.road { stroke: #123456; }` LF `  .label { font: 12px sans-serif; }`: the closing marker's two spaces come off each line, and the second line keeps its two more.

Precisely, the payload lines before the closing marker give the value as follows. Tabs and spaces are compared as characters, never as visual columns.

1. A line that begins with the block's indentation gives the rest of the line, trailing spaces and tabs included.
2. A line of spaces and tabs only that does not begin with the indentation gives an empty line. An editor that empties whitespace-only lines therefore does not break a block.
3. Any other line is an error.
4. The lines are joined with LF. No terminal LF is added, and a block with no payload lines is the empty string.

A block therefore holds any text without CR and without the characters §4 says to escape, including indentation that every line shares, spaces and tabs at the ends of lines, and empty or whitespace-only first and last lines. For example, CSS as an editor wrote it, indented with tabs and with a last line of four spaces before the closing tag:

```sml
style = """

  		:root {
  			--notice-blue: #303b5f;
  		}
      
  """
```

Each payload line starts with the closing marker's two spaces; the CSS lines then start with TABs, and the line before the closing marker is six spaces. The text is LF, `\t\t:root {`, LF, `\t\t\t--notice-blue: #303b5f;`, LF, `\t\t}`, LF, and four spaces. Whitespace at the end of a line is invisible, and an editor that removes it changes the block's data; writers use a block only for a value with a character other than whitespace (§14.2).

A payload line `end` needs no special care, because `end` closes only elements:

```sml
desc = """
  if fill is none then
    use stroke
  end
  """
```

The `desc` text is `if fill is none then\n  use stroke\nend`.

**Longer markers.** A marker may be any run of three or more quotation marks, and the block ends at a line that is exactly the same run. Use a longer marker when a payload line is exactly `"""`, apart from spaces and tabs:

```sml
desc = """"
  Write a block like this:
  """
  and close it.
  """"
```

The `desc` text is `Write a block like this:\n"""\nand close it.`. Everything else about the block is unchanged.

Blocks may be used for `.attribute`, `=`, inline text, `@rem`, `@comments` (which takes only a block, §9), and alias invocations. Unless the document has `@indent none`, the block of a compact `name = """` element in a laid-out position gets predicted edges (§6.4). A `@rem` block is always literal. For a compact `name = """` statement, the closing marker closes the value *and* implicitly closes the inline element; no `end` is needed.

## 8. Named attribute blocks (`@block`) and element aliases (`@node`) — SML Core

**SML-CORE-014 (normative).** Named attribute blocks are built into SML 1.0 Core. They are a general-purpose alternative spelling of an existing **multiline attribute value**, not a new XML node type, not a text child, and not a macro or an SVG instruction. The parser MUST NOT interpret SVG commands, CSS, or any vocabulary-specific content.

### 8.1 Declaration syntax and examples

The declaration is:

```sml
@block element-qname .attribute-qname alias-name
```

For example, `@block svg:path .d data`. The XML names come first and the alias last, as in `@node` (§8.7).

The operands are:

- `element-qname`: QName identifying the kind of element that can use the alias.
- `.attribute-qname`: normal dot-prefixed attribute QName receiving the block's decoded string.
- `alias-name`: unprefixed `NCName` that names the block. It is invoked as `alias-name """` (§8.3) and MUST NOT be `end`.

`@block` MAY appear before the root element, where it applies to the whole document, the root included. It MAY also appear in a normal element's declaration section, where its binding is lexically scoped to that declaring element and its descendants (including the declaring element itself). It MUST NOT appear after the root element. It is not written to XML.

### 8.2 Namespace resolution

In a declaration section, the element QName is resolved **at declaration time** against the in-scope namespace bindings. A prefixed name requires a preceding `@ns prefix URI` declaration (possibly inherited). An unprefixed element name uses the current default namespace; if no default namespace is declared, it means **no namespace**. A default namespace is never inferred from SVG, HTML, file extensions, or the element's spelling.

The target attribute QName uses ordinary XML attribute rules: an unprefixed `.style` or `.d` has **no namespace**, regardless of the default namespace. A prefixed attribute QName requires its prefix to be bound in scope. Both element and attribute names are stored as expanded names, along with their lexical prefix information where the SVG-safe profile requires it.

Because an element's `@ns` lines precede its `@block` declarations (§5.1), the bindings a declaration resolves against are final. A later shadowing of a prefix or default namespace in a descendant does not retroactively alter an already resolved alias mapping.

**Before the root,** no namespace is bound yet, so a declaration keeps its element and attribute QNames as written and resolves them **where the alias is invoked**, against the bindings of the invoking element (after its own `@ns` declarations), as an element alias does (§8.7). The alias matches an element whose expanded name equals its element QName so resolved; an element QName with a prefix unbound there matches nothing. Declarations in declaration sections take precedence, since the nearest scope wins (§8.4). If two declarations before the root with the same alias match one element, the invocation is an error. For example,

```sml
@block path .d data
svg
  @ns http://www.w3.org/2000/svg
  path
    data """
      M 0 0
      L 10 10
      """
  end
end
```

gives the SVG `path` its `d`, because `path` is resolved inside `svg`, where the SVG namespace is the default.

Examples:

```sml
body
  @block div .style style
  div
    style """
      margin: 10 0 10 0;
      padding: 0 5 0 5;
      font-weight: bold;
      """
  end
end
```

`body`, `div`, and the target `style` attribute have no namespace. This is valid **SML** regardless of whether the CSS value itself is valid; SML treats the content as opaque text and does not validate CSS lengths or units. The `style` attribute value is exactly `margin: 10 0 10 0;\npadding: 0 5 0 5;\nfont-weight: bold;`, where each `\n` denotes an actual LF. The equivalent XML is:

```xml
<body>
  <div style="margin: 10 0 10 0;&#10;padding: 0 5 0 5;&#10;font-weight: bold;"/>
</body>
```

In contrast:

```sml
svg
  @ns http://www.w3.org/2000/svg
  @ns s http://www.w3.org/2000/svg
  @block path .d data
  @block s:polyline .points points

  path
    data """
      M 10 10
      C 20 30 40 30 50 10
      Z
      """
  end
end
```

`path` and `s:polyline` both resolve to SVG element expanded names. The target attributes `d` and `points` are unqualified strings. The parser has no knowledge of what these values mean.

### 8.3 Invocation and value semantics

An alias invocation is a physical line consisting of the `alias-name`, optional spaces/tabs, and a block marker (`"""` or a longer run of quotation marks, §7), so `data """` and `data"""` are the same invocation. As for every line, leading and trailing spaces/tabs are removed first (§4). An invocation is valid only in the **declaration section of a matching normal element**. It is followed by payload lines and a line that repeats the marker. It is exactly equivalent to `.attribute-qname` followed by the same marker, payload, and closing marker.

```sml
path
  data """
    M 10 10
    L 100 100
    Z
    """
  .stroke black
end
```

Given `@block path .d data` in scope with an appropriate default namespace, this sets `d` to the exact string `M 10 10\nL 100 100\nZ` (where `\n` in this explanation denotes LF). The block ends at its own closing marker; the enclosing element still needs its `end`.

The §7 rules for indentation, LF joining, and termination, including longer markers, apply **unchanged**. No implicit SVG command parsing, folding, trimming, whitespace optimization, escaping, or type conversion occurs. A block may hold arbitrary XML-compatible string data, not only SVG path commands.

A named block is an **attribute declaration** for ordering and duplicate detection, so additional ordinary attributes may follow it. An invocation after child content has begun is an error, and so is an invocation when no active alias of that name matches the current element. A bare `alias-name` line without the marker is never an invocation: it is always an ordinary element opener, inside the declaration section or after it. An alias therefore never changes how a line without a block marker parses, and a real child element whose name equals an alias needs no special spelling.

### 8.4 Scope, collision, and duplicate rules

- Alias lookup compares the **resolved expanded name** of the current element, then the **literal alias spelling**. It never relies on an SVG element-name table.
- An alias declared in an enclosing scope applies to matching descendant elements; it does not apply to unrelated element expanded names.
- A child scope MAY shadow an inherited mapping for the same element expanded name and alias. The nearest scope wins. Multiple declarations of that same key in **one** scope are an error, even if their targets are identical.
- Different aliases MAY map to the same attribute, but invoking more than one (or using a normal dot attribute plus an alias) in the same element MUST produce a duplicate-attribute error by expanded attribute name.
- The alias name MUST be a valid `NCName`, MUST NOT equal `end`, and MUST NOT be interpreted as a generic macro or directive outside the matching declaration context.
- Alias declarations are SML-only formatting metadata. They are not children, namespace declarations, attributes, or XML output content. An SML serializer MAY emit them to support readable formatting; it need not retain the same alias statements when serializing only the XML information model.

### 8.5 XML-to-SML writer requirements

An XML-to-SML writer MAY introduce or use `@block` aliases as a formatting choice. It MUST emit any required alias declaration before the root, where its names are resolved at each invocation, or on the invoking element itself or an ancestor, where the target element and attribute names resolve correctly, before any invocation. The result MUST remain self-contained: decoding it cannot depend on an external alias registry.

A writer MUST choose a named block only if the complete attribute string can be represented **exactly** by a block (§7): it has no CR and none of the characters §4 says to escape, and a longer marker is used if a payload line is `"""`. Otherwise it MUST fall back to an ordinary `.attribute` value, usually quoted with explicit escapes. It MUST preserve XML namespace prefixes/declaration locations under the SVG-safe profile. Because invocations carry a block marker, a real child element whose name equals an active alias is written as an ordinary element. Writers MUST NOT alter the text of SVG `d`, CSS `style`, `points`, or any other attribute to make an alias representation possible. The only exception is the readable attribute reflow of §3.2, which must be selected explicitly and is not a strict conversion.

### 8.6 Invalid and ambiguous cases

This is invalid due to duplicate attribute assignments:

```sml
container
  @block path .d data
  path
    .d M 0 0
    data """
      L 10 10
      """
  end
end
```

This is invalid because the alias is invoked after the declaration section has closed (the `@rem` is content):

```sml
container
  @block path .d data
  path
    @rem geometry follows
    data """
      M 0 0
      """
  end
end
```

This is invalid because no `data` alias is active for `rect`:

```sml
container
  @block path .d data
  rect
    data """
      M 0 0
      """
  end
end
```

This is invalid because the block declaration follows actual content:

```sml
container
  item/
  @block path .d data
end
```

This is invalid because the prefix is not bound before its use, and because `@ns` follows another declaration statement (§5.1):

```sml
container
  @block s:path .d data
  @ns s http://www.w3.org/2000/svg
end
```

The XML information model contains no alias declarations, so XML-to-SML-to-XML information equivalence is unaffected by whether a writer chooses aliases or standard `.attribute` notation.

### 8.7 Element aliases (`@node`)

`@node` gives an element name another spelling, such as a longer, more descriptive one:

```sml
@node element-qname alias-name
```

- `element-qname`: the QName the alias stands for, exactly as it would be written, with its prefix if it has one.
- `alias-name`: an unprefixed `NCName` other than `end`.

Like a `@block` alias, an element alias is an SML spelling: it is not part of the information model and is never written to XML.

**Scope.** `@node` MAY appear before the root element, where it applies to the whole document, the root included. It MAY also appear in a normal element's declaration section (§5.1), where it applies to that element's content: its children and their descendants, but not the element itself, whose name is already read. It MUST NOT appear after the root element. Two declarations of the same alias in one scope, the prolog or one declaration section, are an error, even with the same QName. A declaration in a declaration section MAY shadow an alias of an enclosing scope; the nearest scope wins.

**Use.** While an alias is in scope, an element opener whose name is the alias, in any form (`alias`, `alias(…)`, `alias/`, `alias(…)/`, `alias = value`, `alias(…) = value`), opens an element named `element-qname`, exactly as if that name had been written. The substitution is lexical. The prefix of `element-qname`, if any, is resolved where the element is opened, after the element's own namespace declarations (§10), so an alias may use a prefix that is bound only where the alias is used. `element-qname` is never itself looked up as an alias, so aliases do not chain, and `element-qname` can still be written directly. `end QName` may name either the alias or the element's QName (§5.3).

Only element openers use element aliases:

- `@element QName` (§5.2) always opens an element named `QName`. It is how an element whose name equals an alias in scope is written.
- The element QName of a `@block` declaration (§8.1) is an XML name, never an element alias.
- An element alias and a `@block` alias may have the same name: an alias invocation always carries a block marker (§8.3), and an opener never does.

For example,

```sml
@node g group
@node p para
svg(xmlns=http://www.w3.org/2000/svg)
  group(id=layer1)
    rect/
  end group
  foreignObject
    @node div box
    box(xmlns=http://www.w3.org/1999/xhtml)
      para = Hello
      @element para
      end
    end
  end
end
```

is the XML

```xml
<svg xmlns="http://www.w3.org/2000/svg">
  <g id="layer1">
    <rect/>
  </g>
  <foreignObject>
    <div xmlns="http://www.w3.org/1999/xhtml">
      <p>Hello</p>
      <para/>
    </div>
  </foreignObject>
</svg>
```

`box` means `div` only inside `foreignObject`, and `@element para` opens an element that is really named `para`.

**Writers.** An XML-to-SML writer MAY use element aliases as a formatting choice. It MUST declare each alias before the root, or on an ancestor of every element it opens with that alias. It MUST open an element whose name equals an alias in scope with `@element`, or not use that alias. The canonical form (§14.2) uses no element aliases.

## 9. XML comments, PIs and source comments

`#` introduces an SML-only source comment when it is the first non-indentation character. There are no end-of-line comments: `#` later in a line, and anywhere in a value, is ordinary data. Source comments are discarded on XML export, so they do not survive an SML → XML → SML round trip. A project that relies on `#` comments must keep the SML file as its source of truth.

`@rem VALUE` creates an XML comment node. `@comment VALUE` is a synonym that readers MUST accept; writers write `@rem`. It may use raw, quoted, or block value syntax; a block is literal wherever it is (§7). XML comment constraints apply: no `--`, and the decoded comment **cannot contain CR (U+000D)**. A literal XML source CR is normalized to LF before XML parsing, while XML character references are not decoded inside comments; thus an SML comment containing CR cannot be represented without information loss, and SML readers MUST reject it. Empty comments are permitted. Comments inside or outside the root are preserved in order.

**Comment data.** XML import and SML readers normalize a comment's text, the value of `@rem` or the content between `<!--` and `-->`, to its data:

1. The spaces, tabs, and line breaks at its end are removed.
2. If the text starts on its own line, the lines before its first character other than space and tab are removed, and so is the indentation that all its lines share. The text of an SML comment always starts on its own line, whatever the value's form; an XML comment's text does when nothing but spaces and tabs follows `<!--` on its line.
3. Otherwise, as in `<!-- Header:`, the first line loses the spaces and tabs at its start, and the other lines the indentation they share.

The *shared indentation* is the longest run of spaces and tabs that begins every line with a character other than space and tab. A line of spaces and tabs only that does not begin with it becomes empty. So `@rem text`, `@rem " text "`, and the XML `<!-- text -->` are the same comment, and

```xml
  <!--
    Usage:
      smlxml file
  -->
```

is the comment `Usage:` LF `  smlxml file`, written

```sml
  @rem """
    Usage:
      smlxml file
    """
```

**XML output.** A comment without a line break is written `<!--`, a space, the data, a space, and `-->`, as in `<!-- text -->`, so its data may end with `-`; an empty comment is `<!---->`. A multi-line comment is written as pretty-printers write it: `<!--`, then each line on a line of its own, preceded by `W + S` unless it is empty, then a line break, `W`, and `-->`, as in the XML above. `W` is the comment's indentation: the spaces and tabs after the last line break of the text node just before it, if only spaces and tabs follow that line break, and otherwise the indentation of its parent element, which is defined the same way; it is empty for the root and outside it. `S` is one step: what `W` adds to the parent's indentation when it begins with it and is longer, and otherwise two spaces. Reading the output gives the same data, by rule 2.

`@comments` followed by a block creates one XML comment per payload line, in order, and is exactly equivalent to one `@rem` statement per line. It is meant for banners of one-line comments:

```sml
@comments """
  ================================
  =  Example test case           =
  ================================
  """
```

Each line, after the block's indentation is removed (§7) and the line is trimmed like any comment, is one comment's data and obeys the constraints above. A payload line that is empty or only spaces and tabs is an error, and so is `@comments` without a block. Wherever the comments are, they are placed as that many `@rem` statements would be; in a laid-out element each is preceded by its predicted whitespace (§6.4). A line's data is never predicted.

`@pi TARGET [VALUE]` creates an XML processing instruction. `TARGET` is an `NCName` (Namespaces in XML 1.0 §7 forbids colons in PI targets) and cannot be `xml` in any case combination. Optional VALUE follows one or more spaces/tabs and uses raw or quoted form; no data means empty string. To represent multiline PI data, use quoted `\n` escapes.

PI data MUST NOT contain `?>` or CR (U+000D), and MUST NOT begin with an XML whitespace character (U+0020, U+0009, U+000A, U+000D). XML discards all whitespace between the PI target and its data, so data beginning with whitespace cannot survive an XML round trip. As with XML comments, PI data does not recognize character references, and an XML parser normalizes a literal CR. SML readers MUST reject PI targets and data that violate these constraints. PI data that ends with whitespace is representable, but must be quoted (§6.2), e.g. `@pi app-hint "compact "`.

**No `@cdata` directive:** CDATA is ordinary text in the XML information model, so SML has no construct for it, and where CDATA sections began and ended is not kept. XML output writes a text node of a `style` or `script` element as a CDATA section when it contains `<`, `&`, or `>`, so that style sheets and scripts stay readable, splitting each `]]>` across two sections (`]]]]><![CDATA[>`). Text with a CR, which a CDATA section cannot keep, and all other text are escaped (`&lt;`, `&amp;`, `&gt;`, `&#13;`).

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

**Version header.** `@sml VERSION`, optionally followed by `tidy`, MAY appear once. If present, it MUST precede every other statement except blank lines and `#` source comments.

- `VERSION` is `1.0-SVG-1` for documents written to this draft revision, or `1.0` for the final 1.0 specification.
- A reader implementing this revision MUST accept `1.0-SVG-1`. It MUST reject any other draft identifier.
- Until 1.0 is final, a reader treats `@sml 1.0` or a missing header as this revision.
- Writers SHOULD emit `@sml 1.0-SVG-1` while the specification is a draft.
- `tidy` states that the document was written by a readable import (§§3.1–3.2), so it differs from the XML it was converted from in whitespace. It has no effect on reading. A writer of exact SML, such as the canonical writer, never writes it.

**Other headers.** `@standalone yes` MAY appear once before the root, for an XML declaration with `standalone="yes"`. Without it, the document has no standalone flag. `@standalone no` is accepted and means the same: XML 1.0 §2.9 assumes `no` when external markup declarations exist and the flag is missing, and gives the flag no meaning when there are none, so an importer reads `standalone="no"` as no flag. `@indent` (§6.4) and `@doctype` (§11.1) MAY each appear once before the root (`@indent` also in declaration sections, as a per-element step), and `@block` (§8.1), `@node` (§8.7), and `@use` (§13) MAY appear before the root.

**XML output.** All XML output uses version 1.0 and UTF-8 without a BOM. It begins with the declaration `<?xml version="1.0" encoding="UTF-8"?>`, adding `standalone="yes"` when recorded; it never writes `standalone="no"`. Top-level items (the declaration, prolog and epilog comments and PIs, the DOCTYPE, and the root) are separated by single LFs, and the output ends with an LF. Comments, and text in CDATA sections, are written as §9 says. The literal presence of the original `<?xml ...?>` declaration, its quote style, and its original encoding are not retained.

Top-level `@rem`, `@comments`, and `@pi` before or after the root are valid and their relative order is preserved. Ordinary text, attributes, and extra root elements outside the root are errors. XML permits prolog/epilog whitespace between markup; the strict importer need not preserve that top-level lexical whitespace because it is outside this information model.

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

- **Placement.** `@doctype` MAY appear at most once, in the prolog, before the root element. Its position relative to prolog `@rem` and `@pi` statements is preserved; XML permits comments and PIs both before and after the DOCTYPE.
- **Name.** The first operand is the declared name, an XML `Name`, recorded as written. It is not checked against the root element's name.
- **External identifier.** `PUBLIC` requires both a public ID and a system ID, and `SYSTEM` requires a system ID. With neither keyword, the declaration has no external identifier. The keywords are case-sensitive, as in XML.
- **Literals.** Both identifiers MUST be written as quoted values (§6.2). A public ID may contain only XML `PubidChar` characters and MUST already be normalized as described in XML 1.0 §4.2.2: no TAB, LF, or CR, no leading or trailing space, and no run of consecutive spaces. An XML importer records the public ID in this normalized form. A system ID MUST NOT contain both `"` and `'`, and MUST NOT contain CR (U+000D): DOCTYPE literals cannot use character references, and XML parsers normalize a literal CR to LF, so such a system ID cannot survive an XML round trip.
- **Opaque, never loaded.** `@doctype` is information, not configuration. SML readers and XML converters MUST NOT fetch, parse, or validate against the referenced DTD, and MUST NOT apply its attribute defaults or entity declarations. This matches how browsers handle SVG.
- **Internal subsets stay out of scope.** An XML DOCTYPE with an internal subset, even an empty one (`[ ]`), MUST be rejected by strict import.
- **Undeclared entities.** When a DOCTYPE has an external identifier but the DTD is not read, a non-validating XML parser may skip a reference to an undeclared entity, such as `&nbsp;`, instead of failing. A strict importer MUST treat any skipped or unexpanded entity reference as an error, because skipping it would silently lose information.
- **XML output.** The XML writer emits `<!DOCTYPE name PUBLIC "pubid" "sysid">`, or the `SYSTEM` or bare form, at the recorded prolog position. It delimits the system ID with `"` unless the ID contains `"`, in which case it uses `'`. A public ID never contains `"`.

## 12. Normative parsing procedure and compact grammar

The following is normative **together with** the lexical value rules in §§4–8. This avoids pretending that a line-oriented lexer can be fully specified by a short context-free EBNF alone.

### 12.1 Statement recognition, in order

After UTF-8 decoding and line-ending normalization, process each logical line. Outside blocks, strip its leading and trailing spaces/tabs and skip it if nothing remains. Then:

1. If currently inside a block (an ordinary block value or a named-attribute block), interpret the line as a payload line or, if its trimmed content is the block's marker, as the end of the block; do not recognize other statements there.
2. `#...` is a source-only comment.
3. `"...` is a continuation line (§6.5). It is an error unless the immediately preceding line was a value-bearing statement ending in a quoted value, or another continuation line.
4. `end`, or `end` followed by spaces/tabs and a single QName, closes the current ordinary element; the QName, if present, is checked (§5.3). This is an error when no element is open. `end` followed by anything else is an error.
5. `.QName...` declares an attribute in the current declaration section.
6. `=...` appends a text child.
7. `@...` dispatches a known directive (`@sml`, `@standalone`, `@indent`, `@doctype`, `@ns`, **`@block`**, `@node`, `@noindent`, `@rem` or `@comment`, `@comments`, `@pi`, `@element`, `@use`) or a directive `@identifier:name` of a declared extension (§13). Unknown directives are errors.
8. A statement consisting of an `NCName`, optional spaces/tabs, and a block marker (`"""` or a longer run of quotation marks) is an **alias invocation**. It is an error unless the current element is still in its declaration section and an active `@block` alias with that name matches the current element's resolved expanded name. Otherwise, parse the following lines as that attribute's literal block (§8). A statement without a block marker is never an alias invocation.
9. Otherwise, require a legal `QName` other than the reserved unprefixed `end` (§5.2), optional immediately attached `(...)` inline attributes, then exactly one of: end-of-line (open), terminal `/` (empty), or whitespace + `=` + value (inline text). Anything else is invalid. If the QName is an element alias in scope (§8.7), the element is named by the alias's QName instead.

The parser maintains a scoped block-alias table and a scoped element-alias table; block alias declarations are resolved when encountered, before matched invocations, and element aliases when used. Namespace ordering follows §5.1.

### 12.2 Statement grammar (lexically parameterized EBNF)

```ebnf
Document          = Prolog, RootElement, Epilog, EOF ;
Prolog            = { Blank | SourceComment }, [ SmlHeader ],
                    { PrologItem }, [ Doctype, { PrologItem } ] ;
PrologItem        = Blank | SourceComment | Header | NodeAlias | BlockDeclaration | XMLComment | XMLComments | PI
                  | ExtensionDirective ;
Epilog            = { Blank | SourceComment | XMLComment | XMLComments | PI
                    | ExtensionDirective } ;
RootElement       = Element ;
Element           = Normal | Empty | InlineText | ExplicitElement ;
Normal            = Opener, EOL, DeclarationSection, { Content }, EndLine ;
Empty             = Opener, "/", EOL ;
InlineText        = Opener, HSpace, "=", ValueTail ;
ExplicitElement   = "@element", HSpace, QName, EOL,
                    DeclarationSection, { Content }, EndLine ;
Opener            = QName, [ InlineAttributes ] ;   (* not "end"; may be an element alias *)
InlineAttributes  = "(", HSpaceOpt, [ InlineAttribute,
                    { HSpaceOpt, ",", HSpaceOpt, InlineAttribute } ],
                    HSpaceOpt, ")" ;
InlineAttribute   = QName, HSpaceOpt, "=", HSpaceOpt, InlineValue ;
DeclarationSection = { Neutral | Namespace },
                    { Neutral | Attribute | BlockDeclaration
                    | NamedAttributeBlock | NodeAlias | NoIndent | LocalIndent } ;
Content           = Neutral | Element | Text | XMLComment | XMLComments | PI ;
Neutral           = Blank | SourceComment | ExtensionDirective ;
Attribute         = ".", QName, ValueTail ;
Namespace         = "@ns", HSpace, [ NCName, HSpace ], NsOperand, EOL ;
NsOperand         = RawToken | Quoted ;
BlockDeclaration  = "@block", HSpace, QName, HSpace, ".", QName,
                    HSpace, NCName, EOL ;
NamedAttributeBlock = AliasName, HSpaceOpt, Block ;
NodeAlias         = "@node", HSpace, QName, HSpace, NCName, EOL ;   (* §8.7 *)
NoIndent          = "@noindent", EOL ;
LocalIndent       = "@indent", HSpace, ( "tab" | IndentWidth ), EOL ;
Text              = "=", ValueTail ;
XMLComment        = ( "@rem" | "@comment" ), ValueTail ;
XMLComments       = "@comments", HSpaceOpt, Block ;   (* one comment per payload line *)
PI                = "@pi", HSpace, NCName,
                    [ HSpace, ( Raw | QuotedValue ) ], EOL ;
ValueTail         = EOL
                  | HSpace, Raw, EOL
                  | HSpace, QuotedValue, EOL
                  | HSpaceOpt, Block ;
QuotedValue       = Quoted, { EOL, Quoted } ;      (* continuation, §6.5 *)
Block             = Marker, EOL, { BlockLine }, BlockEnd ;
Marker            = '"""', { '"' } ;                (* three or more *)
BlockEnd          = Marker, EOL ;                   (* the same marker as the opener *)
EndLine           = "end", [ HSpace, QName ], EOL ;
SmlHeader         = "@sml", HSpace, SmlVersion, [ HSpace, "tidy" ], EOL ;
SmlVersion        = "1.0" | "1.0-SVG-", Digits ;
Header            = "@standalone", HSpace, ( "yes" | "no" ), EOL
                  | "@indent", HSpace, ( "tab" | "none" | IndentWidth ), EOL
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
- `HSpace` is one or more spaces or tabs, and `HSpaceOpt` is zero or more. `IndentWidth` is a decimal integer from 0 to 16 without leading zeros.
- `Raw`, `RawToken` (a raw value containing no spaces or tabs), `Quoted`, `InlineValue`, and `BlockLine` are defined by the deterministic scanner in §§6–9. `Raw`, `RawToken`, and `InlineValue` never begin with `"` (§6.2).
- A `Block` consumes its closing marker **instead of** an additional statement EOL; this prevents a double-terminator ambiguity. A `QuotedValue` never consists of three or more quotation marks alone; such a value is a `Marker`. Namespace, PI, and DOCTYPE operands have no block form.
- For `@ns`, an optional `NCName` prefix is recognized only when **two** operands are present (§10).
- `EOL` may be EOF where no open construct remains.
- Leading and trailing spaces/tabs are removed from **every** line outside blocks (§4). Block payload lines lose only the block's indentation, the spaces and tabs before its closing marker (§7).
- `Neutral` statements (blank lines, source comments, extension directives) never end a declaration section; a preserved XML comment or PI does. `DeclarationSection` encodes the `@ns`-first rule of §5.1. `NoIndent` and `LocalIndent` may each appear at most once in a declaration section, and `LocalIndent` only in a document with the `@indent` header.

The EBNF is a *statement grammar*, not a substitute for XML name validity, UTF-8 validity, whitespace and delimiter interpretation, namespace resolution, or semantic validation.

### 12.3 Error behavior

SML readers MUST reject, among other malformed syntax:

- **Names and structure:** unbound prefixes anywhere; invalid names or characters; use of the reserved name `end` in ordinary element forms; mismatched `end QName`; document content outside the root; unclosed elements or blocks.
- **Attributes and namespaces:** duplicate expanded attributes (including aliased duplicates); `.xmlns` attributes; namespace declarations that break §10 (repeated, reserved, or raw-relative URIs, or the wrong number of `@ns` operands); `@ns` after another declaration statement; attribute, namespace, or block declarations after content.
- **Aliases:** unbound prefixes in `@block` declarations; conflicting aliases within a lexical scope; declarations whose operands are not an element QName, a dot-prefixed attribute QName, and an alias, in that order; alias invocations with no matching active alias or after the declaration section has closed; element aliases that are prefixed or `end`, declared twice in one scope, or declared after the root or after an element's content has begun (§8.7).
- **Values:** malformed escapes; raw values beginning with `"`; anything after a block marker on its line; a block line, other than one of spaces and tabs only, that does not begin with the block's indentation (§7); misplaced continuation lines; unbalanced parentheses in inline raw values.
- **Comments and PIs:** invalid comments or PIs, including PI targets containing colons and PI data beginning with whitespace; `@comments` without a block, or with a blank payload line.
- **Headers:** text statements in a laid-out element (§6.4); repeated or misplaced `@sml`, `@indent`, `@noindent`, or `@doctype`; an `@indent` step in a declaration section under `@indent none`, and `@indent none` there (§6.4); unknown draft versions; malformed `@doctype` identifiers.
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
  2. **A block** when the value contains LF and a character other than space, tab, and LF, and neither CR nor the characters §4 says to escape, with a longer marker if a payload line is `"""`. A value of whitespace only is clearer quoted (`"\n  "`) than as a block of blank lines.
  3. **Quoted** otherwise, split with continuation lines (§6.5) when long.

  Quoting is **required** (§6.2) for values that begin or end with a space or tab, or begin with `"`.
- All namespace declarations explicitly at their original scopes, with lexical prefixes retained, under the strict profile.
- `@block` declarations preceding their uses, and alias invocations written with a space before the marker (`alias """`). Use a normal `.attribute` when a value cannot be represented faithfully as a block.
- `@node` element aliases, if any, declared before the root or on an ancestor of the elements that use them, and `@element` for an element named like an element alias in scope (§8.7).
- A space before every block marker (`.attribute """`, `= """`, `name = """`, `@rem """`); readers also accept the forms without the space.
- `@element` only for elements named `end`.
- `@indent` and `@noindent` as §6.4's writer rule requires, including an `@indent` step for an element whose children are indented by a step other than the unit, and `@doctype` exactly as recorded, at its original prolog position.
- `@comments` for consecutive one-line comments, literal blocks for multi-line comments, and, unless the document has `@indent none`, laid-out text blocks (§6.4) for element text in the layout pretty-printers give it. The block of a compact text element in a laid-out position MUST have that layout; other multi-line text there uses the normal form's literal `= """` statement when a block reproduces it, and is quoted otherwise.

SML formatters MUST be idempotent. Under the strict profile they MUST NOT modify element, text, comment, or PI order or values, SVG strings, namespace declaration locations, or lexical prefixes. They SHOULD preserve `#` comments, blank-line grouping, and alias declarations, and MAY change alias spelling or layout only if every attribute value is preserved. Because alias invocations carry a block marker, introducing an alias cannot change how an existing child-element opener parses.

### 14.2 Canonical form

The canonical form is a function of the document alone: canonical writers MUST produce byte-identical output for equal documents. It is intended for diffing, hashing, and conformance testing. Canonical output follows these rules:

1. **File.** UTF-8 without BOM, LF line endings, and a final LF. No blank lines, `#` comments, `@block` or `@node` aliases, `end QName`, or `@use`.
2. **Prolog.** `@sml 1.0-SVG-1`, then `@standalone yes` if recorded (never `@standalone no`), then `@indent` if rule 9 calls for it, then the prolog comments, PIs, and `@doctype` in document order. The epilog follows the root in document order.
3. **Indentation.** Two spaces per element depth. Block payload lines, a block's closing marker, and continuation lines are indented one level deeper than their statement; an empty payload line is written empty.
4. **Order.** Namespace declarations come first, then attributes, each in document order. A converter MAY offer to write them in the order of Canonical XML instead: the default namespace, then prefixes, then attributes without a namespace by local name, then namespaced ones by namespace URI and then local name, all in ascending code-point order. That output is not canonical, and it reads back with the attributes in that order.
5. **Inline or vertical.** An element's namespace declarations and attributes are written inline, as one list, when every value is representable as a raw inline value (§6.3) under rule 7, and the indentation, element name, and inline list together are at most 100 code points long. Otherwise all are vertical: `@ns` lines, then `.attribute` lines.
6. **Element form.** For an element with an inline list, or with no declarations or attributes: `name/` if it has no content; `name = value` if its only content is one text node, unless rule 7 says the text needs the normal form. Otherwise the normal form with `end`. An element named `end` is always written `@element end` in the normal form.
7. **Value form.** Each value is written in the first form that applies:
   1. **Raw:** §6.2 allows it, it contains none of TAB, CR, LF, or the characters §4 says to escape, and the statement line is at most 100 code points.
   2. **Block:** it contains LF and a character other than space, tab, and LF, and neither CR nor the characters §4 says to escape. A block reproduces every such value exactly (§7). The marker is the shortest of `"""`, `""""`, `"""""`, … that no payload line equals after trimming. TAB is literal in block payloads.
   3. **Quoted:** otherwise.

   The only text of an element in a laid-out position, under `@indent`, is written in the compact form the same way, as a laid-out text block when it has that layout, with one exception: text that 7.2 would write as a block, but that does not have the layout, makes the element use the normal form, where its `=` statement follows 7.1–7.3.
8. **Quoted values.** Escapes are used only where required (`\\`, `\"`) and for CR, LF, TAB, and the characters listed in §4. They are written `\r`, `\n`, `\t`, and `\u{…}` with uppercase hexadecimal digits and no leading zeros. When a quoted value's statement line would exceed 100 code points, the value is split into continuation lines (§6.5). Each segment takes as many source characters as fit in 80 code points, ends after the last space within that span if there is one, and never splits an escape.
9. **Whitespace model.** For a unit `I`, each element with a non-text child is compared with the prediction for its indentation (§6.4), from the root down:
   1. With `I` as its step: if equal, it is laid out.
   2. Otherwise, with the step its first node suggests: if that node is text that begins with LF and the element's indentation, and the rest of it is one of the `@indent` widths (0 to 16 spaces, or one tab), and the content equals the prediction with that step, the element is laid out with it. `@indent` with that step is the last statement of its declaration section.
   3. Otherwise the element is exact: `@noindent` and explicit text follow §6.4's writer rule. Its step is `I`, unless its first node suggests a step as in 9.2 and, with that step as its children's indentation, 9.1–9.2 lay out more of its child elements than with `I`. Then that step is written as in 9.2. Pretty-printed XML nested deeper than its root, as SVG copied out of HTML is, has a root like this.

   `@indent` uses the unit with which 9.1–9.3 lay out the most elements; among those, the one with the fewest per-element `@indent` statements; then, in this order: two spaces, four spaces, tab, then the other widths ascending from 0. The header is omitted when the unit is two spaces, the default. If no element would be laid out, the writer writes `@indent none`, unless reading without it would give the same document: when no element has a non-text child, no comment before or after the root has a line break, and the root's only content is not text with a line break.
10. **Text.** Adjacent text nodes are coalesced and empty ones dropped. Each remaining text node is one `=` statement.
11. **Comment runs.** Each maximal run of two or more consecutive comments that can be `@comments` lines is one `@comments` block. Consecutive means adjacent in the written statements: prolog or epilog items next to each other, or children with nothing between them but predicted whitespace (§6.4). A comment can be such a line when its data is not empty, contains no LF or CR, and contains none of the characters §4 says to escape. The marker is chosen as in rule 7.2, and each comment is a payload line.

### 14.3 XML output

SVG XML writers MUST encode character data and attribute values according to XML 1.0, including preserving CR characters in text through character references and preserving TAB/LF/CR in attributes using character references. The XML emitter MUST NOT insert indentation/newline text nodes for pretty-printing in strict round-trip mode.

## 15. Conformance tests and acceptance gates

The tests are in two groups: **SV** fixtures, which exercise XML↔SML conversion under the strict profile, and **CP** fixtures (§15.1), which exercise the SML reader and writers alone and need no XML.

The fixtures are in this repository under `Tests/svg-roundtrip/`:

- `corpus/` holds the 18-fixture baseline corpus (SV-1–SV-18, the earlier RT-1 subset) and the XML fixtures below. A file's numeric prefix is its SV number; for example, `66-doctype-public.svg` is SV-66.
- `corpus-negative/` holds inputs that a strict importer must reject.
- `test_sml_core.py` holds the CP fixtures (§15.1) and the SV fixtures that need only SML.
- `fuzz_roundtrip.py` runs seeded fuzz and differential tests: random documents, random mutations of SML and XML, and comparison with an independent XML parser.
- `build_and_test.py` runs all of them against the prototype in `sml_prototype.py` and writes `results/REPORT.md`.
- A second implementation, the C# converter `smlxml` in `Source/`, runs the same corpus in `Source/Sml.Tests`, and `crosscheck_csharp.py` compares the two implementations on random documents and mutants.

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
| SV-26 | CSS/style payload with a physical line `end` | Canonical writer uses a plain `"""` block; the `end` line is payload |
| SV-27 | Namespace declared directly on an XML empty element | Writer uses `name(xmlns:p=…)/` or open/`@ns`/`end`; both round-trip |
| SV-28 | DTD internal subset (including empty `[ ]`), custom entity, or external resource expansion attempt | Strict import refuses without loading resources |
| SV-29 | Unknown foreign attributes/elements, nested prefixes | Preserved without interpretation |
| SV-30 | XML with `standalone="no"`, with `"yes"`, with a declaration without the flag, and without a declaration; SML `@standalone no` and a bare `@standalone` | `yes` preserved as `@standalone yes`; `no` read as no flag, so no `@standalone` line and no flag in the XML output; `@standalone no` the same as none; the bare form rejected |
| SV-31 | An SML XML comment or PI containing `\r` | SML reader rejects nonrepresentable data |
| SV-32 | Attribute whose namespace is bound to several prefixes | Lexical QName and declarations remain stable |
| SV-33 | No-default-namespace `@block div .style style` | Alias targets no-namespace `div` and no-namespace `style` attribute |
| SV-34 | Default-namespace unprefixed `@block path .d data` | Alias targets the current default namespace, but attribute `d` remains unqualified |
| SV-35 | Prefixed `@block s:path .d data` | Resolves bound prefix to expanded element name; descendant prefix changes do not retarget mapping |
| SV-36 | Missing or later prefix declaration | Reject alias with unbound prefix, or `@ns` after any other declaration statement |
| SV-37 | Alias `data` vs. real XML `<data>` child | Bare `data` opens the child; `data """` creates the attribute only; no `@element` needed |
| SV-38 | Named block after another dot attribute | Both attributes retained; declaration section remains open |
| SV-39 | Named block and `.d` in same element | Duplicate expanded-name attribute error |
| SV-40 | Two block aliases mapping to same target attribute | Duplicate if both are invoked |
| SV-41 | Alias declared after child content | Reject late `@block` |
| SV-42 | Alias name `end` | Reject reserved alias name |
| SV-43 | Inherited alias shadowed in nested scope | Nearest mapping applies; same-scope duplicate declaration fails |
| SV-44 | Named block containing a line `"""` as data | Writer uses the longer marker `""""` |
| SV-45 | Values with leading spaces on every line, trailing spaces or tabs, a first or last empty line, or a terminal LF; values with CR, an escaped character, or whitespace only | The first written as blocks that keep them exactly; the others quoted |
| SV-46 | Malformed `@block` declarations: `@block g data .d` (operands out of order), `@block g .d` (no alias), `@block g .d data x` (an extra operand) | Rejected |
| SV-47 | `style """` after the declaration section closed | Rejected; a bare `style` line there is a child element |
| SV-48 | Block aliases under two default namespace scopes | Expanded-name lookup distinguishes namespaces without SVG-specific code |
| SV-49 | XML → SML with generated alias → XML | Prefix/declaration scope and exact attribute values unchanged |
| SV-50 | Alias declaration on root with no explicit namespace | Implicit no-namespace mapping is valid |
| SV-51 | Bare alias name as the first statement in a matching element's declaration section | Parsed as a child element; declaration section closes |
| SV-52 | `alias """` with no matching active alias | Rejected |
| SV-53 | Leading/trailing spaces and tabs on value lines, including an `@ns` URI and a `"""` marker followed by spaces | Trimmed; `.d """ ` starts a block; the namespace URI has no trailing space |
| SV-54 | Attribute, text, and PI values with significant outer whitespace; a comment quoted with spaces at its ends | Writer quotes the values, exact round trip; the comment trimmed and written raw |
| SV-55 | Quoted value followed by trailing spaces on the line | Accepted; value unchanged |
| SV-56 | Verbatim blocks: payload lines ending in spaces or tabs, lines indented beyond the closing marker, whitespace-only lines with and without its indentation, a line without it; Inkscape-style CSS indented with tabs | Trailing whitespace and extra indentation kept; whitespace-only lines without the indentation empty; the line without it rejected; the CSS written as a block and read back exactly |
| SV-57 | PI target containing `:` | SML reader rejects; XML importer rejects |
| SV-58 | PI data beginning with space, TAB, LF, or CR | SML reader rejects |
| SV-59 | PI data ending with whitespace | Quoted on write; preserved through the XML round trip |
| SV-60 | Pretty-printed SVG indented with 2 spaces, 4 spaces, and tabs | No header for 2 spaces, `@indent 4` and `@indent tab` otherwise; no explicit whitespace text; exact strict round trip |
| SV-61 | Mixed content beginning with an element, e.g. `<text><tspan>A</tspan> B</text>`, under `@indent` | Writer emits `@noindent`; exact round trip |
| SV-62 | Minified children (no whitespace) inside an otherwise indented document | Writer emits `@noindent`; exact round trip |
| SV-63 | Irregular indentation, blank lines between elements, missing final newline before a closing tag | Affected elements written exactly; exact round trip |
| SV-64 | Text statement in a laid-out element; `@noindent` after content; repeated `@indent`; `@indent 17`; `@indent 01` | Rejected |
| SV-65 | Readable import (§3.1) of pretty-printed XML nested deeper than its root and of minified XML; whitespace between `tspan`s with spaces, with a line break only, and at a nested element's edges; inside `foreignObject`; under `xml:space="preserve"`, an SVG group, `text`, and an element of another namespace | No `@indent` header and no whitespace statements in eligible elements, the group under `preserve` included, the XML written back indented by two spaces; the `tspan` gap with spaces laid out, the others and the ineligible elements preserved; output marked lossy |
| SV-66 | SVG 1.1 `PUBLIC` DOCTYPE | `@doctype` preserved; DTD never fetched (verified with network access disabled) |
| SV-67 | `SYSTEM`-only DOCTYPE and bare `<!DOCTYPE svg>` | Preserved exactly |
| SV-68 | DOCTYPE between prolog comments/PIs | Position preserved |
| SV-69 | External-ID DOCTYPE with an undeclared entity reference such as `&nbsp;` | Strict import rejects; no skipped-entity loss |
| SV-70 | `@doctype` with a non-normalized public ID, or a system ID containing both quote kinds or CR | SML reader rejects |
| SV-71 | `data"""`, `.d"""`, `="""`, `name ="""`, `@rem"""` | Parsed identically to the spaced forms; canonical writer emits the space |
| SV-72 | Raw values beginning with `\|`: `g(a=\|x)`, `.c \|x y`, `@rem \|c`, `@pi t \|p`, `= \|`; `.d\|x` without a space; a name followed by `\|` (`x \|`) | The raw values are read as written, and the canonical writer writes them raw; the last two rejected |
| SV-73 | `]]>` in text, including text from CDATA sections | Escaped correctly on export; exact round trip |
| SV-74 | `xmlns=""` on the root element | Preserved as `@ns ""` |
| SV-75 | Explicit `xmlns:xml="http://www.w3.org/XML/1998/namespace"` | Preserved |
| SV-76 | SML-authored corpus using blocks, `end` payload lines, aliases, continuation lines, inline `xmlns`, and `@indent` | SML → XML → SML → XML preserves information (reverse round trip) |
| SV-77 | Runs of one-line comments in the prolog and in laid-out and exact elements, including lines `# x`, `end`, and `"""`; a comment with spaces at its ends; `@comments` lines with spaces at their ends; `@comments` with a blank line, a line of spaces and tabs, without a block, or with `--` | One `@comments` block per run of two or more (marker `""""` when a line is `"""`); the spaced comment and lines trimmed, the comment part of the run; the invalid forms rejected |
| SV-78 | XML comments: pretty-printed multi-line ones at depths 0, 1, and 2, one with an empty line and indented lines; one starting on the `<!--` line; one ending with `-` before spaces; one of a tab only; multi-line comments in documents indented by four spaces and by tabs. SML comments with spaces or line breaks at their ends and with indentation on every line, quoted and as blocks, in laid-out positions and under `@indent none`; `@rem "a--b"` | Data normalized from XML and SML alike: trimmed, and multi-line data without the indentation its lines share, keeping their relative indentation; multi-line comments as literal blocks; XML output `<!-- data -->`, `<!---->` for the empty comment, and multi-line comments pretty-printed one step deeper, the step following the document's; exact round trips; `--` rejected |
| SV-79 | XML that puts each child on its own line without indentation, including a comment of one line between line breaks | `@indent 0`, no text statements, and the comment trimmed (`@rem line`); reads back with the same whitespace |
| SV-80 | Text and attribute values made only of whitespace, such as line breaks, in exact elements and in a laid-out position; a comment of a line break | Quoted (`= "\n"`, `.a "\n"`), never blocks of blank lines; the comment empty (`@rem`); a block of blank lines is still read |
| SV-81 | Under `@indent 2`, a subtree whose children are indented by four more spaces, one indented by a tab, and the grandchildren of a four-space element; a per-element `@indent` without the header, twice in one element, after content, or after the root | `@indent 4` and `@indent tab` steps in the declaration sections; the grandchildren follow the unit again; exact round trip; the four invalid forms rejected |
| SV-82 | Under `@indent`, pretty-printed multi-line text in `desc`, `style`, and `title`, including an empty line and a child of a four-space element; text without whitespace at its edges; text not indented one unit deeper; a compact block under `@noindent`, in mixed content, and without `@indent` | Laid-out text blocks (`desc = """`), read back with the same text; the edgeless text in the normal form with a literal `= """`; the unindented text quoted; the last three literal |
| SV-83 | `@node` before the root and in a declaration section, used in every opener form, shadowed in a nested scope, and naming a prefixed element whose prefix is bound where it is used; `@element` and `end QName` with aliases; an alias that is `end` or prefixed, an alias declared twice in one scope, and `@node` after the root or after content | The document of the real names; aliases never chain; the invalid forms rejected; canonical output has no `@node` |
| SV-85 | `@block` before the root, used inside an element of another vocabulary and with a prefix bound only where it is used; shadowed by a declaration section; on an element it does not resolve to; with an unbound prefix; two declarations that match one element; twice; after the root; with an invalid name | Attributes set where the names resolve at the invocation; the nearer declaration wins; no active alias where nothing matches; ambiguity, duplicates, placement, and the name rejected |
| SV-84 | Pretty-printed XML whose root's children are indented one level deeper than its closing tag, as SVG copied out of HTML; an exact element whose own step is zero spaces; uniformly indented XML with 4 spaces and with tabs | No `@indent` header, the root exact with `@indent 4`, everything below laid out; the zero step written `@indent 0`; one `@indent` line and no per-element steps; exact round trips |

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
| CP-8 | Block payload lines `end path` and `end` | Payload, not element ends |
| CP-9 | `""""` with a `"""` payload line; `""""` closed only by `"""`; `"""x`; `""` | Exact value; rejected (unterminated); rejected; the empty string |
| CP-10 | Values beginning with `\|` in an inline list, `@pi`, and `@ns`, raw and quoted | Accepted, with the same value either way |
| CP-11 | `a( x = 1 , y=2 )` | Accepted |
| CP-12 | `transform=translate(10,20)`, `fill=url(#a)`; `a=(x`; `a=x)y` | First two accepted with exact values; last two rejected |
| CP-13 | Continuation lines after `.attr`, `=`, inline text, `@rem`, and `@pi` with quoted values; after a raw value; after a blank or `#` line; after `@ns` | Joined exactly; rejected; rejected; rejected |
| CP-14 | Blocks with blank lines, whitespace-only lines, mixed tab/space indentation, and no payload lines | The closing marker's indentation removed per §7; whitespace-only lines without it empty; an empty block is the empty string |
| CP-15 | Alias invoked inside an element opened with `@element` | Works as for any element |
| CP-16 | `@ns` after `.attr`, `@block`, an alias invocation, or `@noindent` | Rejected |
| CP-17 | `@ns s`; `@ns a b c`; `@ns ""`; `@ns "relative"`; inline `xmlns=s` | Rejected; rejected; accepted; accepted; rejected |
| CP-18 | Vertical `.xmlns` and `.xmlns:p`; inline `xmlns` and `xmlns:p` on compact forms; the same prefix inline and in `@ns` | Rejected; namespace declarations; rejected |
| CP-19 | `@ns xml http://www.w3.org/XML/1998/namespace`; `xml` with another URI; any prefix or the default namespace bound to the XML or xmlns namespace URI; declaring `xmlns` | Accepted; rejected; rejected; rejected |
| CP-20 | `@sml` after another statement; another draft identifier, `@sml 1.0-SVG-2`; `@sml 1.0-SVG-1`; `@sml 1.0`; no header; `@sml 1.0-SVG-1 tidy`; `@sml 1.0-SVG-1 neat`, `@sml tidy`, `@sml` | Rejected; rejected; accepted; accepted as this revision; accepted as this revision; accepted, read the same; rejected |
| CP-21 | Unknown required extension; unknown optional extension with directives (including a block value) in the prolog, a declaration section, and content | Rejected; directives skipped and the document is identical to one without them |
| CP-22 | Extension directive between two `.attribute` lines | Declaration section stays open |
| CP-23 | Documents equal in information but differing in quoting, inline/vertical form, aliases, `end QName`, and comments | Canonical writer output byte-identical |
| CP-24 | Canonical writer applied to its own output | Byte-identical (idempotent) |
| CP-25 | XML with attributes out of alphabetical order and namespace declarations among them; SML with an inline list and `.attribute` statements; two documents that differ only in attribute order | Declarations first, then attributes, in document order, in SML and in the XML written back; the two documents are not equal |

The prototype in `Tests/svg-roundtrip/sml_prototype.py` implements this revision's Core syntax, the strict profile, and the canonical form, and `results/REPORT.md` records its current results. Two fixtures are not executed because the prototype does not implement the feature: SV-49 (writer-generated aliases) and SV-65 (readable import). The C# converter `smlxml` generates aliases through its writer profiles; `Source/Sml.Tests/ProfileTests.cs` checks SV-49's requirement for it, and also the attribute reflow of §3.2. The prototype was written alongside this revision and has not been validated independently. Its results are therefore evidence for the design, **not** certification. An independent conforming SML parser and XML converter are needed before claiming verified full round-trip conformance.

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
  @block svg:path .d data
  path
    data """
      M 10 10
      C 20 30 40 30 50 10
      Z
      """
  end
end
```

The `svg:path` declaration matches the unprefixed SVG `path` element because matching uses expanded element names, not prefixes.

**Real child element sharing a name with a block alias:** a bare `style` line is always an element; only `style |` would invoke the alias.

```sml
body
  @block div .style style
  div
    style
      = This is an element, not a CSS attribute.
    end
  end
end
```

**Multiline text with a literal `"""` line:** a `"""` block would end at that line, so the writer uses a longer marker. A line `end` needs nothing special:

```sml
style = """"
  line one
  """
  end
  """"
```

A quoted value is equivalent: `style = "line one\n\"\"\"\nend"`.

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
  @rem Keep editor metadata.
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
@rem Generator: Example Editor 1.0
@doctype svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd"
svg
  @ns http://www.w3.org/2000/svg
  .version 1.1
  rect(width=10, height=10)/
end
```

The comment is trimmed (§9), and XML output puts one space back on each side. The DOCTYPE is preserved but its DTD is never loaded. The default two-space indentation regenerates the whitespace around `rect`.

## 17. Remaining limitations and open engineering tasks

This revision addresses the design-level issues identified in review so far. It cannot guarantee the absence of further design issues or of implementation bugs before a reference parser, differential XML comparison suite, and fuzzing exist. In particular:

- The language name and file extension collide with Standard ML and with W3C's Service Modeling Language; a final name is still to be chosen.
- SV-49 (writer-generated aliases) is executed only against the C# converter, and SV-65 (readable whitespace normalization) not at all; the prototype implements neither feature (§15).

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

**Status after this revision:** SML 1.0-SVG-1 is a compact, SVG-first design baseline. It has `"""`-delimited literal blocks, so that `end` closes only elements, generic, explicitly marked `@block` aliases in Core, whitespace-trimmed raw values, an indentation model for readable strict conversions, opaque DOCTYPE preservation, exact long-value wrapping, a deterministic canonical form, and defined conformance classes. Its required acceptance criterion is an information-preserving *strict-profile round trip* in both directions, not simply readable generated text or visual similarity.
