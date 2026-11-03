# SML vs. KDL vs. NDL for Human-Editable SVG

**Status:** Architecture decision study · 1 November 2026  
**Primary objective:** Represent SVG in text that is straightforward to read and hand-edit, with unambiguous, bidirectional SVG XML conversion.

## Executive conclusion

**Continue evaluating SML and KDL 2.0; eliminate NDL from the primary shortlist.** SML is optimized for hand-editing XML-like trees, whereas KDL offers a standardized, maintained node language with existing tooling. Neither format's mere ability to hold tree-shaped data proves faithful SVG round trips. A strict *SVG Round-Trip Conformance Profile* (SVG-RTCP) and adversarial corpus must decide the question.

This is a provisional engineering recommendation, not a claim that the two formats have already passed full conformance testing.

## 1. What the languages model

| Property | SML 1.0 draft | KDL 2.0 | NDL (Nested Data Language) |
|---|---|---|---|
| Native structure | XML-like elements, attributes, text | Ordered named nodes, positional arguments, properties, children | Nested maps and arrays |
| Repeated named children | Natural | Natural | Requires special array/list representation |
| Attribute vs child distinction | Explicit `.` | Properties vs child nodes | Not intrinsic to XML semantics |
| Mixed character content | Explicit `=` text nodes | Requires XML-specific convention (e.g. XiK) | Requires XML-specific wrapper schema |
| Namespace behavior | Proposed in core | Implemented by XML conversion profile | Requires convention |
| Typed native values | Strings (XML compatible) | Strings, booleans, numbers, null, etc. | Structured data values |
| Editing attributes with spaces | Unquoted dot attributes | Usually quoted strings | Typically quoted strings |
| Closure | Explicit `end` | Braces for children | Data delimiters |
| Reuse existing language ecosystem | No | Yes | Some, but weaker fit |

**NDL is not intrinsically incorrect.** Any sufficiently expressive nested format can encode XML using explicit tagged arrays/objects. That solution is simply less natural and less readable for SVG than either SML or KDL.

## 2. Simple SVG example

Original SVG:

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">
  <g transform="translate(10,20)">
    <path d="M 0 0 L 100 50 L 0 80 Z" fill="none" stroke="black"/>
    <text x="20" y="70">Hello SVG</text>
  </g>
</svg>
```

SML:

```text
svg
  @ns http://www.w3.org/2000/svg
  .viewBox 0 0 200 100
  g
    .transform translate(10,20)
    path
      .d M 0 0 L 100 50 L 0 80 Z
      .fill none
      .stroke black
    end
    text(x=20, y=70) = Hello SVG
  end
end
```

KDL (illustrative standard KDL 2.0 syntax; XML mapping remains a separate convention):

```kdl
svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100" {
  g transform="translate(10,20)" {
    path d="M 0 0 L 100 50 L 0 80 Z" fill="none" stroke="black"
    text x="20" y="70" "Hello SVG"
  }
}
```

The `text` example is illustrative rather than a universal XML conversion rule; a strict converter must specify which KDL positional values are XML text, whether child nodes can coexist, and how repeated text is ordered.

## 3. The SVG properties that determine the decision

1. **Path data:** the exact `d` string is preserved, including numeric spelling and command letters; rendering equivalence is insufficient.
2. **Text/tspan:** character content, tails, adjacent text ordering, XML whitespace and `xml:space` must survive.
3. **Namespaces and qualified content:** expanded names must remain stable, and original prefixes must be retained where their spelling is referenced by QName-valued application data or where lexical stability is required.
4. **Unknown vendor content:** XML foreign-namespaced elements and attributes must be retained, even when not understood by an SVG renderer.
5. **CSS and presentation attributes:** `style`, class names, selectors, `url(#id)`, paint values, and animation values are opaque strings during conversion.
6. **References/IDs:** `href`, `xlink:href`, fragment IDs, clip/filter references and embedded resources must not be rewritten.
7. **Comments and processing instructions:** preserve the document-level and element-level structures in the strict information profile.
8. **XML input policy:** prohibit DTD/entity/network resolution by default; report unsupported information rather than silently dropping it.

## 4. Semantic versus lexical fidelity

### Required: XML/SVG information preservation

The target is equality of normalized XML information trees: expanded element/attribute names, attribute string values, children (ordered elements/text/comments/PIs), and relevant document prolog metadata. This is stricter than merely producing an SVG image that looks the same.

### Optional: source-spelling fidelity

Attribute ordering, quote choice, self-closing syntax, CDATA boundaries, XML entity reference spellings, and original line endings need not match by default. Some of these matter to source control workflows and can be tracked in an optional lossless-source mode. Prefix spelling should be preserved by default wherever practical; namespace URI identity is mandatory.

**Important exception:** An XML document using prefixed names inside values (e.g., `xsi:type`, XPath expressions, or custom vendor attributes) can depend on lexical namespace prefixes. A converter that renames prefixes without adjusting such values may change application behavior even if expanded element names are equal. A rigorous profile must explicitly test this.

## 5. SML strengths and gaps

SML's greatest practical benefit is vertical, unquoted attributes:

```text
path
  .d M10 10 C20 30 40 30 50 10 L90 70 Z
  .transform translate(10,20) rotate(-30.5)
  .style stroke: black; fill: none; stroke-width: 1.25
end
```

Its `.` and `=` markers also make XML attributes and text visibly different from elements. These properties are valuable in graphics editing.

However, the current SML 1.0 design is a **draft**: the complete grammar, trailing whitespace semantics, source-only comments, prefix preservation, and XML prolog handling need executable conformance tests. New syntax means the project owns its parsers, formatters, diagnostics and tooling.

## 6. KDL strengths and gaps

KDL 2.0 has a published grammar, established parsers, line-based authoring, and a node-tree structure close to XML. It is particularly compact for many small SVG primitives. The existing XML-in-KDL (XiK) work is a useful starting point, but the exact mapping and comment/prolog retention must be audited against the SVG-RTCP profile. A KDL parser may discard ordinary source comments; it cannot automatically be assumed to preserve XML comment nodes.

When representing SVG, the converter should restrict KDL properties to **string values** unless explicit, reversible coercion is part of the profile. Numeric conversion could change `1.00` to `1`, violating the attribute-string equality rule.

## 7. NDL assessment

NDL's hierarchical data syntax is attractive for general configuration data. But SVG is an ordered XML node stream containing attributes and potentially interleaved text. NDL would need an explicit tagged representation for elements, attributes, and text entries; such a representation is likely more verbose and less convenient to edit. Therefore **do not prioritize NDL** for SVG interchange.

## 8. Engineering recommendation and acceptance criteria

Do not decide by syntactic appearance alone. Build conversion prototypes for SML and KDL, then require both to pass the same reference corpus and report:

- XML information-tree identity after `SVG → format → SVG`.
- Reparse success with secure standard XML libraries.
- Preservation of foreign namespaces, mixed text, comments, and processing instructions.
- Opaque string preservation for paths/styles/reference attributes.
- Stability under a second conversion cycle.
- Optional browser/render-based pixel checks as *supplemental* tests, never replacements for information equality.
- Editing ergonomics assessment on realistic SVG files with extensive attribute lists.

**Current practical recommendation:** use SML if hand-editing ergonomics is the dominant criterion; prefer KDL if interoperability with existing language tools dominates *and* it passes the full SVG-RTCP profile. See `svg-roundtrip/SVG-Round-Trip-Conformance-Profile.md`.

## 9. References

- KDL 2.0 formal specification: https://kdl.dev/spec/
- KDL project: https://kdl.dev/
- XML-in-KDL (XiK) project: https://github.com/kdl-org/kdl/blob/main/XML-IN-KDL.md
- NDL: https://ndlang.org/
- SVG 2 structural specification, including foreign namespace support: https://www.w3.org/TR/SVG/struct.html
- SVG 2: https://www.w3.org/TR/SVG/
- XML Namespaces: https://www.w3.org/TR/xml-names/
- XML Information Set: https://www.w3.org/TR/xml-infoset/
