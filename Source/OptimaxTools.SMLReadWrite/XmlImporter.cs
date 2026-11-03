using System.Text;
using System.Xml;

namespace OptimaxTools.SMLReadWrite;

/// <summary>Reads XML into the information model under the strict profile (specification §§2–3, 11.1).</summary>
public static class XmlImporter
{
    static XmlImporter()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static SmlDocument Read(byte[] data)
    {
        // Namespace processing is off: System.Xml would otherwise reject xml:space values other than
        // "default" and "preserve", which are well-formed, opaque attribute values without a DTD. Names and
        // namespaces are validated here instead, exactly as the SML reader does.
        using var stream = new MemoryStream(data, writable: false);
        using var text = new XmlTextReader(stream)
        {
            Namespaces = false,
            Normalization = true,                   // attribute-value and line-end normalization
            WhitespaceHandling = WhitespaceHandling.All,
            EntityHandling = EntityHandling.ExpandEntities,
            DtdProcessing = DtdProcessing.Parse,    // accept a DOCTYPE; internal subsets are rejected below
            XmlResolver = null,                     // never load an external DTD or entity
        };
        var settings = new XmlReaderSettings { CheckCharacters = true, DtdProcessing = DtdProcessing.Parse, XmlResolver = null };
        using var reader = XmlReader.Create(text, settings);
        var doc = new SmlDocument();
        var stack = new Stack<(ElementNode Element, Dictionary<string, string> Ns)>();
        var baseNs = new Dictionary<string, string>(StringComparer.Ordinal) { ["xml"] = XmlRules.XmlNamespace };
        var info = (IXmlLineInfo)text;
        try
        {
            while (reader.Read())
            {
                int line = info.LineNumber;
                switch (reader.NodeType)
                {
                    case XmlNodeType.XmlDeclaration:
                        if (reader.GetAttribute("version") != "1.0")
                            throw new UnsupportedXmlException($"XML {reader.GetAttribute("version")} is not supported", line);
                        // standalone="no" is the same as no flag (§11).
                        doc.Standalone = reader.GetAttribute("standalone") == "yes" ? "yes" : null;
                        break;
                    case XmlNodeType.DocumentType:
                        doc.Prolog.Add(ReadDoctype(reader, data, line));
                        break;
                    case XmlNodeType.Element:
                        ReadElement(reader, doc, stack, baseNs, line);
                        break;
                    case XmlNodeType.EndElement:
                        stack.Pop();
                        break;
                    case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace:
                        if (stack.Count > 0)
                            AppendText(stack.Peek().Element, reader.Value);
                        break;
                    case XmlNodeType.Comment:
                        string comment = reader.Value;
                        Rethrow(() => comment = XmlRules.CommentData(comment, line, ownLine: false));
                        Place(doc, stack, new CommentNode(comment));
                        break;
                    case XmlNodeType.ProcessingInstruction:
                        Rethrow(() => XmlRules.CheckProcessingInstruction(reader.Name, reader.Value, line));
                        Place(doc, stack, new ProcessingInstructionNode(reader.Name, reader.Value));
                        break;
                    case XmlNodeType.EntityReference:
                        throw new UnsupportedXmlException($"reference to the entity '{reader.Name}'", line);
                }
            }
        }
        catch (XmlException e)
        {
            throw new XmlInputException("malformed XML: " + e.Message, e.LineNumber == 0 ? null : e.LineNumber);
        }
        catch (ArgumentException e) when (e.Message.Contains("encoding", StringComparison.OrdinalIgnoreCase))
        {
            throw new XmlInputException("unsupported encoding: " + e.Message);
        }
        if (doc.Root is null)
            throw new XmlInputException("the document has no root element");
        return doc;
    }

    private static void Rethrow(Action check)
    {
        try
        {
            check();
        }
        catch (SmlException e) when (e is not XmlInputException)
        {
            throw new XmlInputException(e.Reason, e.Line);
        }
    }

    private static DoctypeNode ReadDoctype(XmlReader reader, byte[] data, int line)
    {
        if (reader.Value.Length > 0 || HasInternalSubset(data))
            throw new UnsupportedXmlException("DTD internal subsets are not supported", line);
        string? publicId = reader.GetAttribute("PUBLIC");
        string? systemId = reader.GetAttribute("SYSTEM");
        if (publicId is not null)       // XML 1.0 §4.2.2 normalization (XML whitespace only)
            publicId = string.Join(' ', publicId.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        if (systemId is not null)       // XML §2.11 line-end normalization
            systemId = systemId.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var doctype = new DoctypeNode(reader.Name, publicId, systemId);
        try
        {
            XmlRules.CheckDoctype(doctype, line);
        }
        catch (SmlException e)
        {
            throw new UnsupportedXmlException(e.Reason, line);
        }
        return doctype;
    }

    /// <summary>System.Xml reports an empty internal subset ("[]") like none at all, so scan the prolog
    /// for the DOCTYPE's "[" directly. Structural characters are ASCII in every supported encoding except
    /// UTF-16, which is decoded first.</summary>
    private static bool HasInternalSubset(byte[] data)
    {
        string text = data switch
        {
            [0xFE, 0xFF, ..] or [0x00, 0x3C, ..] => Encoding.BigEndianUnicode.GetString(data),
            [0xFF, 0xFE, ..] or [0x3C, 0x00, ..] => Encoding.Unicode.GetString(data),
            _ => Encoding.Latin1.GetString(data),
        };
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c is ' ' or '\t' or '\r' or '\n' or '﻿' or 'ï' or '»' or '¿')
            {
                i++;
                continue;
            }
            if (string.CompareOrdinal(text, i, "<?", 0, 2) == 0)
            {
                int end = text.IndexOf("?>", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 2;
            }
            else if (string.CompareOrdinal(text, i, "<!--", 0, 4) == 0)
            {
                int end = text.IndexOf("-->", i + 4, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 3;
            }
            else if (string.CompareOrdinal(text, i, "<!DOCTYPE", 0, 9) == 0)
            {
                char quote = '\0';
                for (int j = i + 9; j < text.Length; j++)
                {
                    char d = text[j];
                    if (quote != '\0')
                    {
                        if (d == quote)
                            quote = '\0';
                    }
                    else if (d is '"' or '\'')
                    {
                        quote = d;
                    }
                    else if (d == '[')
                    {
                        return true;
                    }
                    else if (d == '>')
                    {
                        return false;
                    }
                }
                return false;
            }
            else
            {
                return false;
            }
        }
        return false;
    }

    private static void ReadElement(
        XmlReader reader, SmlDocument doc, Stack<(ElementNode Element, Dictionary<string, string> Ns)> stack,
        Dictionary<string, string> baseNs, int line)
    {
        var element = new ElementNode(reader.Name);
        var attributes = new List<(string Name, string Value)>();
        bool empty = reader.IsEmptyElement;
        for (int k = 0; k < reader.AttributeCount; k++)
        {
            reader.MoveToAttribute(k);
            string name = reader.Name, value = reader.Value;
            if (name == "xmlns" || name.StartsWith("xmlns:", StringComparison.Ordinal))
            {
                string prefix = name == "xmlns" ? "" : name[6..];
                Rethrow(() => XmlRules.CheckNamespaceDeclaration(prefix, value, line));
                element.NamespaceDeclarations[prefix] = value;
            }
            else
            {
                attributes.Add((name, value));
            }
        }
        reader.MoveToElement();
        var ns = new Dictionary<string, string>(stack.Count > 0 ? stack.Peek().Ns : baseNs, StringComparer.Ordinal);
        foreach (var (prefix, uri) in element.NamespaceDeclarations)
            ns[prefix] = uri;
        Rethrow(() => element.Name = XmlRules.Resolve(element.QualifiedName, ns, attribute: false, line));
        foreach (var (name, value) in attributes)
        {
            ExpandedName expanded = default;
            Rethrow(() => expanded = XmlRules.Resolve(name, ns, attribute: true, line));
            if (element.AttributeNames.ContainsValue(expanded))
                throw new XmlInputException($"duplicate expanded attribute name '{name}'", line);
            element.Attributes.Add(name, value);
            element.AttributeNames.Add(name, expanded);
        }
        if (stack.Count > 0)
            stack.Peek().Element.Children.Add(element);
        else
            doc.Root = element;
        if (!empty)
            stack.Push((element, ns));
    }

    private static void AppendText(ElementNode element, string data)
    {
        if (element.Children.Count > 0 && element.Children[^1] is TextNode previous)
            previous.Data += data;
        else
            element.Children.Add(new TextNode(data));
    }

    private static void Place(SmlDocument doc, Stack<(ElementNode Element, Dictionary<string, string> Ns)> stack, SmlNode node)
    {
        if (stack.Count > 0)
            stack.Peek().Element.Children.Add(node);
        else if (doc.Root is null)
            doc.Prolog.Add(node);
        else
            doc.Epilog.Add(node);
    }
}
