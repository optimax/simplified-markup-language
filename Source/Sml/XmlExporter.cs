using System.Text;

namespace Sml;

/// <summary>Writes the information model as XML 1.0 in UTF-8 (specification §11, §14.3).</summary>
public static class XmlExporter
{
    public static string Write(SmlDocument doc)
    {
        var declaration = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"");
        if (doc.Standalone is not null)
            declaration.Append($" standalone=\"{doc.Standalone}\"");
        declaration.Append("?>");
        var parts = new List<string> { declaration.ToString() };
        parts.AddRange(doc.Prolog.Select(Misc));
        var body = new StringBuilder();
        Element(doc.Root, body);
        parts.Add(body.ToString());
        parts.AddRange(doc.Epilog.Select(Misc));
        return string.Join('\n', parts) + "\n";
    }

    private static string EscapeText(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r", "&#13;");

    private static string EscapeAttribute(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace("\"", "&quot;")
            .Replace("\t", "&#9;").Replace("\n", "&#10;").Replace("\r", "&#13;");

    private static string Misc(SmlNode item)
    {
        switch (item)
        {
            case CommentNode comment:
                return comment.Data.Length > 0 ? $"<!-- {comment.Data} -->" : "<!---->";
            case ProcessingInstructionNode pi:
                return pi.Data.Length > 0 ? $"<?{pi.Target} {pi.Data}?>" : $"<?{pi.Target}?>";
            case DoctypeNode dt:
                var s = new StringBuilder("<!DOCTYPE " + dt.Name);
                if (dt.SystemId is not null)
                {
                    char quote = dt.SystemId.Contains('"') ? '\'' : '"';
                    s.Append(dt.PublicId is not null ? $" PUBLIC \"{dt.PublicId}\"" : " SYSTEM");
                    s.Append($" {quote}{dt.SystemId}{quote}");
                }
                return s.Append('>').ToString();
            default:
                throw new ArgumentException($"unexpected node {item.GetType().Name}");
        }
    }

    private static void Element(ElementNode element, StringBuilder output)
    {
        output.Append('<').Append(element.QualifiedName);
        foreach (var (prefix, uri) in element.NamespaceDeclarations)
            output.Append(prefix.Length == 0 ? " xmlns=\"" : $" xmlns:{prefix}=\"").Append(EscapeAttribute(uri)).Append('"');
        foreach (var (name, value) in element.Attributes)
            output.Append(' ').Append(name).Append("=\"").Append(EscapeAttribute(value)).Append('"');
        if (element.Children.Count == 0)
        {
            output.Append("/>");
            return;
        }
        output.Append('>');
        foreach (SmlNode child in element.Children)
        {
            switch (child)
            {
                case ElementNode e:
                    Element(e, output);
                    break;
                case TextNode t:
                    output.Append(EscapeText(t.Data));
                    break;
                default:
                    output.Append(Misc(child));
                    break;
            }
        }
        output.Append("</").Append(element.QualifiedName).Append('>');
    }
}
