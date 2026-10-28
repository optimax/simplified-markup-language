# Simplified Markup Language (SML)

**Version:** 1.0 — Consolidated Language Specification  
**Status:** Proposed normative design baseline (not an independently certified standard)  
**Date:** October 27, 2026  
**Default extension:** `.sml` (provisional; collides with Standard ML)  
**Interoperability:** XML 1.0 Fifth Edition; Namespaces in XML 1.0 Third Edition

## 1. Purpose and scope

Simplified Markup Language (SML) is a human-editable, hierarchical notation for an XML-compatible information model. It avoids XML's angle brackets, repeated closing names, ubiquitous attribute quotation marks, and entity escaping in source text while retaining ordered mixed content, attributes, namespace identity, comments, and processing instructions.

The key words **MUST**, **MUST NOT**, **SHOULD**, **SHOULD NOT**, and **MAY** are normative.

### 1.1 Approved architectural decisions

| Topic | SML 1.0 decision |
|---|---|
| Element termination | Explicit `end` for ordinary nonempty blocks; inline-text and self-closing forms terminate implicitly |
| Compact syntax | Inline text and parenthesized inline attributes supported |
| Indentation | Advisory for structure; common structural prefix removed in multiline blocks |
| XML compatibility | Preserve defined XML information, not exact XML lexical syntax |
| Extensions | Stable Core grammar; separately activated extension dialects |

### 1.2 Design goals

1. Ordinary documents SHOULD be pleasant to write without a generator.
2. Parsing MUST be deterministic and independent of visual indentation outside blocks.
3. All values in Core are Unicode strings, never implicitly typed.
4. Element child order and mixed text/element content MUST be preserved.
5. Strict parsing MUST detect malformed documents, not silently recover them.
6. A streaming reader SHOULD need memory proportional only to nesting depth, current declarations, and current value.
7. Extensions MUST be opt-in and MUST NOT silently reinterpret Core constructs.

### 1.3 Non-goals

Core is not an expression language or schema language. It does not evaluate interpolations, import external files, process DTDs, preserve XML entity references or CDATA boundaries as distinct semantic nodes, or guarantee byte-for-byte XML round trips.

## 2. Overview and examples

XML:

```xml
<node attr1="value1" attr2="value2">
  <child attr3="value3 with spaces"></child>
</node>
```

SML:

```sml
node
  .attr1 value1
  .attr2 value2
  child
    .attr3 value3 with spaces
  end
end
```

Compact SML:

```sml
person(id=123, active=true)
  name = John Smith
  city = Milwaukee
  image(source="photo.jpg", width=640)/
end
```

An inline-text element is equivalent to a full element with one text value and an `end`:

```sml
title = My Document
```

```sml
title
  = My Document
end
```

## 3. Core Information Model (CIM)

A document has one root element and optionally ordered top-level XML comments and processing instructions. It also carries XML version (1.0), an optional standalone state, and optional serialization metadata. The root and content nodes are:

| Node | Information |
|---|---|
| Element | Expanded name `(namespace URI or null, local name)`, effective namespace bindings, attribute set, ordered child nodes |
| Attribute | Expanded name and Unicode string value |
| Text | Unicode string value |
| Comment | Unicode string value |
| Processing instruction | Target and data string |

Namespace prefixes and attribute order MAY be retained for source editing but are not intrinsic to semantic equivalence. Adjacent text nodes are equivalent to their concatenation. Empty text events do not create a semantically distinct node.

The model MUST distinguish an absent attribute from a present empty-string attribute. It MUST reject attributes whose expanded names duplicate one another. Comment and PI order is significant. All names and values must be representable by the targeted XML 1.0 information model.

### 3.1 XML compatibility limitations

Core guarantees preservation of the CIM, **not** the full XML Infoset including DTD and entity-specific properties. The following distinctions are not preserved: source quotation style, use of entity/character references, prefixes where equivalent expanded names are used, ordering of attributes, CDATA boundaries, XML self-closing syntax, incidental formatting, original encoding spelling, DTD declarations and defaulting provenance. An XML importer MUST reject unsupported information in strict preservation mode rather than silently discard it.

## 4. Encoding, lexical rules, and source lines

- Source encoding MUST be UTF-8; a UTF-8 BOM MAY occur.
- Invalid UTF-8 is a fatal error.
- Source line endings CRLF and CR are normalized to LF before lexical parsing. Explicit `\r` escapes may still encode carriage returns in values.
- After line-ending normalization, trailing spaces and tabs are removed from every physical line, including lines inside block values. Whitespace at the end of a source line is therefore never data; a value that must end in whitespace is quoted (§6.3).
- Unicode normalization (NFC/NFD) is not performed.
- Physical lines normally contain one statement.
- Leading spaces/tabs preceding ordinary statements are ignored. In a block they are handled by the block algorithm.
- Blank lines outside block values are ignored.
- XML names are case-sensitive.
- Element and attribute qualified names must satisfy the XML Namespaces `QName` grammar: `NCName` or `NCName:NCName`.
- Reserved prefixes, XML-valid characters, and namespace URIs are validated according to XML 1.0 and Namespaces in XML 1.0.

### 4.1 Statement classes

| Prefix / form | Meaning |
|---|---|
| `name` | Open a normal element |
| `name(...)` | Open an element with inline attributes |
| `name/`, `name(...)/` | Empty element |
| `name = value`, `name(...) = value` | Inline-text element |
| `.name value` | Attribute declaration |
| `= value` | Text item |
| `end`, `end name` | Close open element (or plain `end` for a block) |
| `# ...` | SML-source-only comment |
| `@...` | Core directive or explicitly enabled extension |

Classification occurs after leading indentation is discarded. Syntax-like characters inside a raw value are not reinterpreted as markup. Unknown directives cause an error in Core.

### 4.2 Name collisions

An element literally named `end` uses `@element`:

```sml
@element end
  = Content
end
```

`@element name` supports a name that would otherwise collide with a Core statement keyword. It is an opener and requires a matching `end`; it can have ordinary declaration and content statements. Unescaped ordinary names that collide with reserved directive forms are prohibited.

## 5. Elements and termination

### 5.1 Ordinary elements

```sml
root
  child
    = Text
  end
end
```

`end` closes the most recently opened normal element. The parser maintains a stack. `end name` additionally verifies the exact lexical qualified name of the open element; it is not necessary for ordinary files.

### 5.2 Empty elements

```sml
separator/
image(source="photo.jpg", width=640)/
```

An empty element cannot have subsequent dot-attribute lines. If attributes must be listed on following lines, use a normal element and `end`:

```sml
image
  .source photo.jpg
  .width 640
end
```

These forms are semantically equivalent, subject to the attribute sets. No `end` follows a self-closing opener.

### 5.3 Inline-text elements

```sml
title = My Document
label(lang=en) = Hello
```

An inline-text element closes itself once the value is read. It MUST NOT receive a subsequent body statement or closing `end`. A block value may be used inline; the block's `end` terminates the block and implicitly the element:

```sml
message = |
  First line.
  Second line.
end
```

### 5.4 Declaration section

Immediately after an ordinary element opener, attributes and namespace declarations may occur in any order, interspersed with blanks and source comments. The first child/text/preserved comment/PI ends this declaration section. No attributes or namespace declarations are permitted after it.

```sml
item(id=100)
  @ns ex urn:example
  .ex:code ABC
  name = Example
end
```

The start-element event cannot be emitted with resolved names until the declaration section has been read.

## 6. Attributes and values

### 6.1 Dot attributes

The syntax is `.QName`, optionally followed by horizontal whitespace and the value. The whitespace separates the name from the value and is not data; the value itself is read as described in §6.3.

```sml
item
  .id          123
  .description A value with spaces, punctuation & symbols
  .path        C:\Program Files\MyApp\config.xml
  .empty
  .leading     " Starts with one space"
end
```

`.empty` means the empty string. Aligning values in columns, as above, does not change them: `.id` is `123`. Whitespace inside a raw value is kept, so `.path` keeps the space in `Program Files`. In unquoted values, backslashes and XML-like entity strings are literal characters; `&lt;` means the five characters `&lt;`, not `<`.

### 6.2 Inline attributes

```sml
person(id=123, name="John Smith", active=true)
```

Comma separates attributes; `=` separates the name from the value. Spaces around delimiters are formatting-only. An unquoted inline value ends at comma or closing parenthesis, and leading/trailing horizontal whitespace around it is discarded. Quote values containing commas, parentheses, significant outside whitespace, or ambiguous quote characters.

`person()` is valid and means no attributes. `item(value=)/` means an empty attribute value. Duplicate attributes across inline and dot forms are forbidden.

An inline attribute list must parse fully as inline attributes; malformed syntax MUST NOT be silently reinterpreted as a different element name.

### 6.3 Raw and quoted values

Every value is read the same way, whichever statement carries it. Leading and trailing spaces and tabs around the value are discarded; they are formatting, never data. What remains is classified as follows:

- If it starts with `"`, it MUST be exactly one syntactically valid quoted string, and the value is the decoded content of that string. Anything else that starts with `"` is a syntax error; to represent literal leading quotation marks, quote the whole value.
- If it is exactly one of the block markers `|`, `|+`, or `|-`, it opens a multiline block (§7).
- Otherwise it is a **raw value**: its characters are taken literally, including any spaces or tabs between its first and last non-whitespace characters.

Whitespace at the start or end of a value is therefore data only inside a quoted string. A quoted string cannot span lines; use `\n` for line breaks.

In quoted strings these escapes are recognized: `\\`, `\"`, `\n`, `\r`, `\t`, `\u{HEX}` (valid Unicode scalar). Unknown escapes, surrogate code points, and XML-forbidden characters are errors. Raw values do not process backslashes.

Examples:

```sml
item
  .value "  padded  "
  .quote "\"quoted\""
  .slash C:\Temp\file.txt
end
```

### 6.4 Text items

`=`, optionally followed by whitespace and a value, represents text:

```sml
message
  = Hello,
  = " world!"
end
```

The text is `Hello, world!`: successive text items concatenate with **no implicit space or newline**. A space at the edge of a text item is kept only inside quotes, as in `" world!"`. Use quoted `\n` or a multiline block for explicit newlines. `=` alone means empty text.

### 6.5 Inline text

```sml
title = Hello
```

Requires a separator between the element opener and `=`. The value is read as described in §6.3, so values with significant leading/trailing spaces use a quoted string. If an inline value is a block marker, its block's `end` closes the block and the inline element as noted above.

## 7. Multiline values

The markers `|`, `|+`, and `|-` are reserved as complete value tokens. They may follow `.attribute`, `=`, `name =`, `@comment`, or `@cdata`.

```sml
paragraph = |
    First line.
      Indented second line.
    Third line.
end
```

The value is `First line.\n  Indented second line.\nThird line.`.

### 7.1 Normative block algorithm

1. After a marker, collect subsequent physical lines up to the first line that, after trimming leading and trailing spaces/tabs, is exactly `end`. The `end` line is not content.
2. Identify nonblank content lines (a blank content line consists only of spaces/tabs or is zero length).
3. Compute the longest common **character prefix of horizontal whitespace** among nonblank content lines. If there are no nonblank lines, the common prefix is empty.
4. Remove that exact prefix from every nonblank content line. Convert each blank content line into an empty line.
5. Join lines with LF. No LF is implicitly appended merely because there is a physical line before the terminator.
6. `|` returns this joined result unchanged. `|+` removes any trailing LF and then appends exactly one LF. `|-` removes all trailing LF characters.

Tabs are literal characters in the prefix algorithm, not visual columns. Formatters SHOULD discourage mixing tabs and spaces in blocks.

Because trailing spaces and tabs are removed from every line before the block is read (§4), no content line ends in whitespace, and a line containing only whitespace is already empty. A value whose lines must end in whitespace is written as a quoted string instead of a block.

If there are zero content lines: `|` and `|-` yield `""`, while `|+` yields `"\n"`. If there are only blank content lines, their line boundaries follow the join and chomping rules above.

### 7.2 Literal terminator collisions

Because the delimiter line is recognized independent of indentation, a line consisting only of `end` cannot be placed directly in a block. Use quoted `\n` escapes or multiple text items instead:

```sml
value = "first\nend\nlast"
```

Blocks cannot nest. An unterminated block is an error. A future extension MAY add arbitrary-delimiter or length-delimited blocks.

## 8. Comments, processing instructions, CDATA hints

### 8.1 Source comments

A line beginning with `#` (after optional indentation) is an SML source comment, omitted from the CIM and XML output. Inline comments are not recognized; in `title = Example # text`, `# text` is data.

### 8.2 Preserved XML comments

```sml
@comment This comment appears in XML
@comment |
  A two-line
  comment.
end
```

XML comments cannot contain `--` or end with `-`; violations are errors. Like any value, the comment text loses leading and trailing whitespace unless quoted, so the XML comment `<!-- note -->` is written `@comment " note "`.

### 8.3 Processing instructions

```sml
@pi xml-stylesheet type="text/xsl" href="style.xsl"
```

The PI target is the first valid XML Name token after `@pi`; the rest of the line forms the PI data, read as a raw or quoted value (§6.3). A block marker is not recognized here. XML PI data can end in whitespace (`<?target data ?>`), so such data is quoted: `@pi target "data "`. The target `xml` (any case) is reserved, and PI data must not contain `?>`. Processing instructions are retained in document order.

### 8.4 CDATA

```sml
script
  @cdata |
    if (a < b && c > d) execute();
  end
end
```

`@cdata` contributes ordinary text to the CIM but records a nonsemantic serializer hint. XML writers MAY emit one or more CDATA sections where legal; they MUST preserve text character content even if they emit escaped text instead. `]]>` may require splitting CDATA if a writer honors the hint.

## 9. Directives and document metadata

```sml
@sml 1.0
@xml 1.0
@encoding utf-8
@standalone yes
```

- `@sml 1.0` selects this Core version; absent means SML 1.0 in a 1.0-only reader.
- `@xml 1.0` declares XML export compatibility (only 1.0 in Core).
- `@encoding utf-8` chooses the baseline XML output encoding. It does not override source-file decoding, which is UTF-8 regardless.
- `@standalone yes` or `@standalone no` controls optional XML declaration standalone metadata; absent means unspecified.
- Duplicate or conflicting single-valued directives are errors.
- Document directives and `@use` extension declarations may appear only in the prolog before the root opener.
- Comments and PIs may appear in the prolog and epilog; ordinary text or extra root elements may not.

Serialization preferences (including exact XML declaration spelling) are not guaranteed to survive every XML import/export. A parser's CIM may retain these as explicit document properties without conflating them with XML text or element content.

## 10. Namespaces

SML Core mandates XML Namespaces 1.0 expanded-name behavior.

### 10.1 Prefix declarations

```sml
svg:svg
  @ns svg http://www.w3.org/2000/svg
  .width 400
  svg:circle(cx=200, cy=100, r=45)/
end
```

`@ns prefix URI` binds a prefix on the declaring element and its descendants; it resolves the declaring element itself even though the directive follows the opener. Namespace declarations must be in the declaration section.

### 10.2 Default namespaces

```sml
catalog
  @ns https://example.org/catalog
  product(id=100)
    name = Paper
  end
end
```

The default namespace applies to the declaring element and unprefixed descendant element names. It does **not** apply to unprefixed attributes. `@ns ""` clears the default namespace for the scope.

### 10.3 Namespace validation

- Prefixes must be declared before name resolution completes.
- `xml` has its fixed namespace binding.
- `xmlns` is reserved and cannot be used as an ordinary name prefix.
- Ordinary `.xmlns` / `.xmlns:foo` are invalid; use `@ns`.
- Nested declarations MAY shadow inherited prefix mappings, subject to XML Namespaces rules.
- Prefix undeclarations other than the default namespace reset are not supported by the 1.0 profile.
- Attributes with different lexical QNames that resolve to the same expanded name are duplicates.
- Namespace values MUST satisfy namespace-name constraints; serializer-generated namespace declarations must be XML-valid.

An event parser must buffer an element's declarations, resolve expanded names, and detect collisions before emitting its resolved start event.

## 11. Grammar and disambiguation

This is **structural EBNF**, supplemented by the lexical rules in Sections 4–10. `QName` and `NCName` have the meanings defined by Namespaces in XML; `EOL` denotes a normalized physical newline. A full concrete parser grammar must also apply line-ending, spacing, quoted-token, and block-scanning rules in this document.

```ebnf
Document        ::= Prolog Root Epilog EOF ;
Prolog          ::= { Blank | SourceComment | DocumentDirective
                    | NamespaceIndependentComment | PI } ;
Epilog          ::= { Blank | SourceComment
                    | NamespaceIndependentComment | PI } ;
Root            ::= Element ;
Element         ::= NormalElement | InlineTextElement
                  | EmptyElement | EscapedElement ;
NormalElement   ::= Opener EOL { DeclarationOrContent } ElementEnd ;
Opener          ::= QName [ InlineAttributes ] ;
InlineTextElement ::= Opener HSpace '=' [ HSpace Value ] EOL ;
EmptyElement    ::= Opener '/' EOL ;
EscapedElement  ::= '@element' HSpace QName EOL
                    { DeclarationOrContent } ElementEnd ;
ElementEnd      ::= 'end' [ HSpace QName ] EOL ;
DeclarationOrContent ::= Blank | SourceComment | Attribute
                       | Namespace | Content ;
Content         ::= Element | Text | XMLComment | PI | CDATA ;
Attribute       ::= '.' QName [ HSpace Value ] EOL ;
InlineAttributes ::= '(' [ InlineAttribute { ',' InlineAttribute } ] ')' ;
InlineAttribute ::= QName '=' InlineValue ;
Namespace       ::= '@ns' HSpace [ NCName HSpace ] Value EOL ;
Text            ::= '=' [ HSpace Value ] EOL ;
XMLComment      ::= '@comment' [ HSpace Value ] EOL ;
CDATA           ::= '@cdata' [ HSpace Value ] EOL ;
PI              ::= '@pi' HSpace NCName [ HSpace ( RawValue | QuotedValue ) ] EOL ;
Value           ::= RawValue | QuotedValue | BlockValue ;
BlockValue      ::= ( '|' | '|+' | '|-' ) EOL
                    { BlockLine } 'end' EOL ;
```

**Disambiguation rules:** a line is recognized by its first nonindentation token. A valid opener ending in `/` is self-closing. An opener followed by whitespace, `=`, then a value is inline text. Dot and at-sign prefixes are reserved. A line that attempts but fails reserved syntax is invalid rather than reinterpreted as an arbitrary name. A block's `end` has priority over all other interpretations. The named element terminator must match the *lexical* QName of the opener, even when different prefixes could denote the same expanded name.

The grammar shown is normative at statement-structure level; individual character validity and the treatment of inline whitespace are governed by the lexical prose. A machine-readable lexer/parser grammar should be delivered with the reference implementation and validated against all conformance vectors.

## 12. XML conversion contract

### 12.1 Required preservation

XML export must preserve element and attribute expanded names, attribute string values, exact text characters, child order, comment content and positioning, processing instructions, and applicable document metadata when representable. XML escaping is the serializer's task; SML source never decodes XML entities.

```sml
query
  .expression a < b && c > d
  .description Johnson & Johnson "Special Edition"
  = Price < $100 & quantity > 5
end
```

An XML serializer must escape reserved characters correctly. In attribute values, tabs, LF, and CR must be serialized using appropriate character references to avoid XML attribute-value normalization changing the parsed value.

### 12.2 Whitespace-only XML text

Whitespace in XML element content may be semantically significant. **Strict information-preserving import** MUST represent it:

```xml
<root>
  <a>1</a>
  <b>2</b>
</root>
```

A preserving import can be:

```sml
root
  = "\n  "
  a = 1
  = "\n  "
  b = 2
  = "\n"
end
```

An optional **readable import** MAY drop whitespace-only nodes only under explicit caller policy or schema knowledge and MUST be documented as potentially lossy.

### 12.3 Round-trip equivalence

For each representable CIM document `D`:

```text
ParseSml(WriteSml(D)) ≡ D
ParseXml(WriteXml(D)) ≡ D
```

For representable XML `X`, `WriteXml(ParseSml(WriteSml(ImportXml(X))))` must be semantically equivalent under the CIM projection. Semantic equivalence ignores attribute order, original prefix spelling, CDATA boundaries, entity-reference spelling, and other lexical distinctions outside the CIM.

### 12.4 XML import security

Baseline import MUST disallow untrusted external entity fetching, DTD processing, external schema fetching, automatic XInclude, and PI execution. Importers SHOULD impose configurable limits on depth, attributes, values, entity expansion if optionally enabled, and total input. Strict import reports unsupported XML information rather than silently dropping it.

## 13. Extension dialects

Core extensions are explicitly declared in the document prolog:

```sml
@sml 1.0
@use sml.schema 1.0
@use sml.include 1.0
```

`@use extension-id version` activates a known, versioned extension. Unknown directives or unsupported requested extensions cause strict parsing errors. Editors MAY retain unknown constructs in error-tolerant mode without claiming Core conformance.

An extension MAY add named directives, validations, explicit transformations, or application data conventions. It MUST NOT silently change the Core meaning of plain elements, text, attributes, `end`, or raw values. Extensions needing incompatible lexical rules must be advertised as separate dialects.

Proposed but **not part of Core**: `sml.schema`, `sml.include`, `sml.template`, `sml.document`, `sml.config`, `sml.xml-lexical`, `sml.binary`.

Processing stages SHOULD be: decode source → Core lex/parse → check registered extension directives → resolve CIM → optionally run expressly enabled transformations → serialize or deliver to consumer. Core XML export must not unexpectedly evaluate includes or scripts.

## 14. Parser and serializer contracts

### 14.1 Strict reader

A Core Reader MUST:

- Decode and validate UTF-8 and XML-permitted characters.
- Track physical and logical source positions.
- Parse statements using deterministic precedence.
- Maintain a stack of open elements/blocks.
- Keep declaration sections separate from content.
- Buffer each element's declarations until namespace resolution is complete.
- Validate duplicate expanded-name attributes and illegal namespace bindings.
- Preserve content order and exact decoded text values.
- Reject invalid documents with useful diagnostics.

### 14.2 Streaming events

Recommended event kinds:

```csharp
public enum SmlEventKind
{
    StartDocument,
    StartElement,
    Text,
    Comment,
    ProcessingInstruction,
    EndElement,
    EndDocument
}
```

For:

```sml
root(id=1)
  name = Alice
end
```

The logical event sequence is:

```text
StartDocument
StartElement(root, id="1")
StartElement(name)
Text("Alice")
EndElement(name)
EndElement(root)
EndDocument
```

Self-closing elements also generate matching start and end events. CDATA is emitted as text. Adjacent text MAY be coalesced. Namespace-resolved `StartElement` MUST include all attributes from the declaration section.

### 14.3 Suggested C# API (illustrative)

```csharp
public interface ISmlReader : IDisposable
{
    SmlEventKind NodeType { get; }
    int Depth { get; }
    SmlName? Name { get; }
    string? Value { get; }
    IReadOnlyList<SmlAttribute> Attributes { get; }
    SmlSourceSpan SourceSpan { get; }
    bool Read();
}

public readonly record struct SmlName(string? NamespaceUri, string LocalName);
```

An async reader SHOULD support `ReadAsync(CancellationToken)`. A callback interface MAY provide `StartDocument`, `StartElement`, `Text`, `Comment`, `ProcessingInstruction`, `EndElement`, `EndDocument`; any borrowed character spans must have documented lifetimes.

### 14.4 DOM

Suggested node classes: `SmlDocument`, `SmlElement`, `SmlText`, `SmlComment`, `SmlProcessingInstruction`, and `SmlAttribute`. `SmlElement.Children` MUST be an ordered collection of heterogeneous node kinds; never model child elements merely as a string-keyed dictionary.

### 14.5 Source spans and diagnostics

Source spans SHOULD retain source ID, original UTF-8 byte start/end offsets, line/column start/end, with one-based lines and columns. Offsets are half-open `[start,end)`. Columns count Unicode scalar values after decoding. Diagnostics SHOULD name the faulty construct, expected syntax, and source span.

### 14.6 Resource behavior

Target overall parse time `O(N)` for `N` input characters. Target streaming working memory `O(D + A + V)`, where `D` is open nesting depth, `A` current declaration buffering, and `V` current value/block storage. Materialized DOM memory scales with total document size. Configurable limits MUST be available, with controlled errors rather than uncontrolled exhaustion.

## 15. Diagnostics catalog

| Code | Condition |
|---|---|
| SML1001 | Invalid UTF-8 |
| SML1002 | XML-forbidden or invalid Unicode character |
| SML1101 | Invalid element name |
| SML1102 | Invalid attribute name |
| SML1103 | Invalid inline attribute syntax |
| SML1104 | Invalid quoted escape or malformed quoted value |
| SML1201 | Unexpected `end` |
| SML1202 | Missing `end` |
| SML1203 | Mismatched named `end` |
| SML1204 | More than one root element |
| SML1205 | Content outside root |
| SML1301 | Attribute or namespace declaration after content |
| SML1302 | Duplicate expanded-name attribute |
| SML1401 | Undeclared namespace prefix |
| SML1402 | Invalid namespace declaration |
| SML1501 | Unterminated multiline block |
| SML1601 | Invalid XML comment |
| SML1602 | Invalid processing instruction |
| SML1701 | Unknown directive |
| SML1702 | Unsupported extension |
| SML1801 | Unsupported XML information in strict import |
| SML1901 | Configured resource limit exceeded |

Strict mode MUST NOT silently repair errors. Editor recovery mode MAY produce a partial tree but MUST mark it as nonconforming.

## 16. Formatter and canonical output

A formatter MUST preserve CIM semantics. Recommended canonical profile:

| Property | Default |
|---|---|
| Encoding | UTF-8 without BOM |
| Line ending | LF |
| Indentation | Two spaces |
| Normal termination | Plain `end` |
| Empty element | `/` shorthand |
| Short text-only element | Inline `name = value` |
| Attributes | Dot form, one per line (compact mode may use inline list) |
| Namespace declarations | Before ordinary attributes |
| Significant whitespace | Preserved exactly |
| Final file newline | One LF |

A formatter MAY preserve source attribute order; otherwise it SHOULD impose a stable sort by expanded name. It MUST not omit whitespace-only text merely for aesthetics. The formatter MUST be idempotent: `Format(Format(s)) == Format(s)`.

Because raw values lose leading and trailing whitespace (§6.3), a writer or formatter MUST quote any value that begins or ends with a space or tab, and MUST NOT write as a block any text whose lines end in whitespace (§7.1).

## 17. Worked examples

### 17.1 Configuration

```sml
@sml 1.0
# Application configuration
configuration
  .version 1.0
  .environment production
  database
    host = localhost
    port = 1433
    database-name = Products
    connection
      .timeout 30
      .pooling true
      .max-pool-size 100
    end
  end
  logging
    .enabled true
    .level information
    file = C:\Logs\application.log
  end
end
```

### 17.2 Product catalog

```sml
catalog
  .currency USD
  product(id=1001)
    name = Business Cards
    description = Premium full-color business cards
    options
      option(name=stock, value=16pt)/
      option(name=sides, value=double)/
      option(name=finish, value=matte)/
      option(name=quantity, value=500)/
    end
    pricing
      .base-price 49.95
      .taxable true
    end
  end
end
```

### 17.3 Mixed text and elements

```sml
paragraph
  = "This is "
  bold = important
  = " information."
end
```

Produces conceptual XML: `<paragraph>This is <bold>important</bold> information.</paragraph>`.

### 17.4 SVG

```sml
svg:svg
  @ns svg http://www.w3.org/2000/svg
  .width 400
  .height 200
  .viewBox 0 0 400 200
  svg:rect(x=10, y=10, width=180, height=80, fill=steelblue)/
  svg:circle(cx=300, cy=50, r=40, fill=orange)/
  svg:text(x=20, y=150) = Hello SVG
end
```

### 17.5 XAML-like markup

```sml
Window
  @ns http://schemas.microsoft.com/winfx/2006/xaml/presentation
  @ns x http://schemas.microsoft.com/winfx/2006/xaml
  .x:Class Demo.MainWindow
  .Title My Window
  .Width 800
  .Height 600
  StackPanel
    .Margin 20
    TextBlock
      .FontSize 24
      = Welcome
    end
    Button
      .Content Click Me
      .Width 120
    end
  end
end
```

### 17.6 Namespace shadowing

```sml
root
  @ns p urn:first
  p:item = First namespace
  group
    @ns p urn:second
    p:item = Second namespace
  end
  p:item = First namespace again
end
```

### 17.7 Significant whitespace

```sml
root
  .attribute " leading and trailing "
  text = "A\tB\nC"
  whitespace = "   "
  multiline = |+
    First line.
      Indented second line.
  end
end
```

### 17.8 Processing instruction and preserved XML comment

```sml
@sml 1.0
@pi xml-stylesheet type="text/xsl" href="style.xsl"
@comment This comment is part of the XML output.
document/
```

## 18. Conformance suite

Tests should live in portable UTF-8 files with expected CIM results or expected diagnostic codes plus source spans. Recommended repository subdirectories: `syntax`, `values`, `whitespace`, `namespaces`, `mixed-content`, `xml-conversion`, `malformed`, `extensions`, and `stress`.

### 18.1 Positive grammar and value tests

| ID | Case | Required expectation |
|---|---|---|
| SML-T001 | `root` / `end` | Empty root |
| SML-T002 | `root/` | Empty root |
| SML-T003 | `title = Hello` | One text child |
| SML-T004 | `.name John Smith` | Entire value includes space |
| SML-T005 | `item(id=123)/` | Empty element with attribute |
| SML-T006 | Quoted inline name | Spaces retained |
| SML-T007 | Nested elements | Correct stack order |
| SML-T008 | `end root` | Named termination verified |
| SML-T009 | Arbitrary indentation | Same tree |
| SML-T010 | `@element end` | Literal element name |
| SML-T011 | Windows path | Backslashes literal |
| SML-T012 | Empty attribute | Present, empty value |
| SML-T013 | Empty text | No unintended spaces |
| SML-T014 | `<`, `&`, `>` in value | Literal characters |
| SML-T015 | Non-BMP Unicode | Preserved |
| SML-T016 | `person()` | Empty inline attribute list |
| SML-T017 | `item(value=)/` | Empty inline attribute |
| SML-T018 | Raw `&lt;` | Literal entity-looking text |

### 18.2 Multiline and whitespace

| ID | Case | Required expectation |
|---|---|---|
| SML-T101 | Adjacent `=` lines | Concatenate without newline |
| SML-T102 | Extra spaces after `.name` | Not part of the value |
| SML-T103 | Trailing whitespace after a raw value | Discarded |
| SML-T104 | Quoted padded value | Exact value |
| SML-T105 | Common block indentation | Stripped |
| SML-T106 | Relative indentation | Preserved |
| SML-T107 | Blank line in block | LF retained |
| SML-T108 | `|` | No extra terminal LF |
| SML-T109 | `|+` | Exactly one terminal LF |
| SML-T110 | `|-` | Trailing LFs removed |
| SML-T111 | Mixed tabs/spaces | Character-prefix algorithm |
| SML-T112 | `\n`, `\r`, `\t` | Exact decoded characters |
| SML-T113 | Quoted line `end` | Literal text |
| SML-T114 | CRLF source | Equivalent after line normalization |
| SML-T115 | Empty multiline block | Empty value |
| SML-T116 | Block of blank lines | Defined join/chomp behavior |
| SML-T117 | Dot-attribute values aligned in columns | Alignment not part of the values |
| SML-T118 | Block marker followed by trailing whitespace | Opens a block |
| SML-T119 | Trailing whitespace on block content lines | Removed |
| SML-T120 | Quoted value after several spaces (`.v   "x"`) | Quoted value `x` |

### 18.3 Namespaces

| ID | Case | Required expectation |
|---|---|---|
| SML-T201 | Root prefix declaration | Correct expanded name |
| SML-T202 | Default namespace | Elements inherit |
| SML-T203 | Unprefixed attribute | No namespace |
| SML-T204 | Prefix shadowing | Nested URI mapping |
| SML-T205 | Reset default namespace | No namespace |
| SML-T206 | Predefined `xml` | Correct URI |
| SML-T207 | Duplicate expanded attribute names | SML1302 |
| SML-T208 | Undeclared prefix | SML1401 |
| SML-T209 | Namespace declaration after content | SML1301 |
| SML-T210 | Illegal `xmlns` as normal attribute | SML1402 |

### 18.4 Required invalid examples

Unexpected terminator (`SML1201`):

```sml
root/
end
```

Unclosed root (`SML1202`):

```sml
root
  child = Hello
```

Mismatched named end (`SML1203`):

```sml
root
end other
```

Attribute after content (`SML1301`):

```sml
root
  child = Hello
  .id 123
end
```

Duplicate attribute (`SML1302`):

```sml
item(id=1)
  .id 2
end
```

Negative coverage MUST also include invalid encoding, illegal XML code points, malformed inline lists, unterminated blocks, invalid escape sequences, illegal comments/PIs, unsupported directives, namespace errors, and resource limit failures.

### 18.5 Round-trip properties

Conformance runners MUST test:

```text
ParseSml(WriteSml(D)) ≡ D
ParseXml(WriteXml(D)) ≡ D
Format(Format(S)) == Format(S)
ParseSml(WriteCompact(D)) ≡ ParseSml(WriteExpanded(D))
```

Use CIM equality for semantic round-trips, not raw XML or SML string comparison. Include mixed content, attributes containing tabs/newlines, namespace shadowing, comments, PI order, and whitespace-only text.

### 18.6 Stress cases

Test deep nesting (1, 10, 100, 10,000 with configured limits), million-element documents, huge text values, thousands of attributes, buffer splits inside UTF-8 multibyte sequences, arbitrary stream chunking, cancellation, malformed EOF, and attribute/namespace declaration buffering.

## 19. Conformance profiles

| Profile | Requirements |
|---|---|
| **Core Reader** | Strict Core parsing and semantic events/nodes |
| **Core Writer** | Correct serialization of representable CIM |
| **XML Interoperability** | Secure import/export and preservation rules |
| **Editor Tooling** | Source spans, formatting, optional recovery and incremental editing |

The first three profiles can be independently tested. Editor Tooling is recommended, not required to claim Core Reader or Writer conformance.

## 20. Reference implementation roadmap

Recommended .NET 10 solution layout:

```text
SML/
  src/
    Sml.Core/
    Sml.Parser/
    Sml.Dom/
    Sml.Xml/
    Sml.Formatting/
    Sml.Extensions/
    Sml.CommandLine/
  tests/
    Sml.Core.Tests/
    Sml.Parser.Tests/
    Sml.Xml.Tests/
    Sml.Conformance.Tests/
    TestData/
  docs/
    SML-Language-Specification.md
    sml-1.0-grammar.md
    sml-extension-contract.md
    sml-implementation-guide.md
```

Milestones:

1. **M0 — Specification freeze:** Machine-readable grammar, normative test fixtures, diagnostic definitions.
2. **M1 — Streaming parser:** UTF-8 decoder, lexer, values, element stack, events.
3. **M2 — Semantics:** Namespace resolution, duplicate detection, ordered DOM.
4. **M3 — Serialization:** SML formatter/writer, XML import/export, round-trip tests.
5. **M4 — Editing:** Source mapping, incremental recovery, formatting profiles.
6. **M5 — Extensions and CLI:** Registry, explicit transforms, conformance runner, editor integration.

The reference implementation SHOULD prioritize a streaming parser and layer the DOM above it, rather than requiring document materialization in the reader.

## 21. Resolved ambiguities and remaining standardization work

This consolidated specification explicitly resolves the most important disagreements from exploratory drafts:

- Self-closing elements MUST carry all attributes inline; dot attributes cannot follow `/`.
- An inline-text element has no additional `end`, except that a multiline block necessarily ends with its own `end` marker.
- Declaration sections finish at the first content item; namespace declarations may resolve the declaring opener retroactively.
- Consecutive text statements do not insert newlines.
- Raw values discard leading and trailing whitespace, keep internal whitespace, and do not decode XML entity syntax; whitespace at the edge of a value is data only inside quotes.
- `|`, `|+`, and `|-` have deterministic common-prefix and terminal-LF behavior, including empty and all-blank blocks.
- Literal block-delimiter collisions are handled with quoted/escaped values, not indentation heuristics.
- Prefix spelling is not the semantic identity of a namespaced node; the expanded name is.
- Information-preserving XML import retains whitespace-only text.
- Extensions are opt-in and cannot redefine Core syntax.

Before assigning this document a final frozen standard status, the reference implementation should publish a machine-readable lexical grammar and executable fixtures for edge cases such as inline values containing reserved punctuation, quoted `|` markers, Unicode name validity, and preservation of top-level comment/PI ordering. Those artifacts must refine the prose without altering the approved architectural decisions.

## 22. References

- W3C, *Extensible Markup Language (XML) 1.0 (Fifth Edition)*: https://www.w3.org/TR/xml/
- W3C, *Namespaces in XML 1.0 (Third Edition)*: https://www.w3.org/TR/xml-names/
- W3C, *XML Information Set (Second Edition)*: https://www.w3.org/TR/xml-infoset/
- W3C, *XML 1.0 — Attribute-Value Normalization*: https://www.w3.org/TR/xml/#AVNormalize

---

**End of SML 1.0 Consolidated Language Specification.**
