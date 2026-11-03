# OptimaxTools.SMLReadWrite

The .NET 10 library that reads and writes SML 1.0-SVG-1 and XML/SVG. `smlxml` is a thin command line around it; an editor or any other application can use it the same way. It has no console or file-system code: it converts bytes and strings, and the application reads, writes, and names files. It uses no third-party packages.

Namespace: `OptimaxTools.SMLReadWrite`.

## Converting in one call

`SmlConverter` does what `smlxml` does for one file:

```csharp
using OptimaxTools.SMLReadWrite;

byte[] svg = File.ReadAllBytes("drawing.svg");

// Exact, canonical SML.
string sml = SmlConverter.XmlToSml(svg);

// Readable SML, cleaned with a profile.
SmlProfile profile = SmlProfile.Parse(File.ReadAllText("svg.smlprofile"));
string tidy = SmlConverter.XmlToSml(svg, new SmlWriterOptions { Clean = profile });

// Back to XML; the extension is ".svg" for SVG and ".xml" otherwise. Save as UTF-8 without a BOM.
var (xml, extension) = SmlConverter.SmlToXml(sml);
File.WriteAllText("drawing" + extension, xml, SmlConverter.Encoding);
```

Invalid input raises `SmlException`, with `Reason` and, where known, `Line`. `XmlInputException` (malformed XML) and `UnsupportedXmlException` (outside the supported subset, such as a DTD internal subset) derive from it.

## The parts

| Type | Purpose |
|---|---|
| `SmlConverter` | One-call conversions, the output extension, and the output encoding |
| `SmlReader` | Reads SML text (`Read(string)`) or UTF-8 bytes (`Read(byte[])`) into an `SmlDocument` |
| `SmlWriter` | Writes an `SmlDocument` as SML: `Write(doc)` gives the canonical form (specification §14.2), `Write(doc, options)` applies `SmlWriterOptions` |
| `SmlWriterOptions` | `Clean` (an `SmlProfile`: tidy whitespace, one item per line, CSS laid out, aliases), `VerticalAttributes`, `SortAttributes` |
| `XmlImporter` | Reads XML bytes, in the encoding the declaration names, into an `SmlDocument` |
| `XmlExporter` | Writes an `SmlDocument` as XML 1.0 in UTF-8 |
| `SmlDocument`, `ElementNode`, `TextNode`, `CommentNode`, `ProcessingInstructionNode`, `DoctypeNode` | The information model (specification §2), which an editor can inspect or change between reading and writing |
| `SmlProfile` | Parses a profile (`SmlProfile.Parse(text)`), the configuration that cleaning uses; `svg.smlprofile`, shipped with `smlxml`, is an example |
| `DocumentComparer` | Compares two documents as the round-trip tests do, and describes the first difference |
| `CssFormatter`, `ValueLayout`, `XmlRules` | Lower-level helpers: CSS layout, the layouts of attribute values, and XML name and character rules |

The behavior is specified in [`Documents/SML-Language-Specification.md`](../../Documents/SML-Language-Specification.md); the design decisions are in the root [`implementation.md`](../../implementation.md).
