// Created by Andrew Wozniewicz with Claude
// Copyright (C) 2026 Optimax Technologies, Inc.
// Distributed under MIT License.
//
using System.Text;

namespace OptimaxTools.SMLReadWrite;

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
        Element(doc.Root, body, "");
        parts.Add(body.ToString());
        parts.AddRange(doc.Epilog.Select(Misc));
        return string.Join('\n', parts) + "\n";
    }

    private static string EscapeText(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r", "&#13;");

    /// <summary>Text as XML (§9): in a style or script element, text with &lt;, &amp;, or &gt; is a CDATA section, so
    /// that style sheets and scripts stay readable; <c>]]&gt;</c> is split across two sections. Text with a CR, which a
    /// CDATA section cannot keep, and all other text are escaped.</summary>
    private static string Text(string s, ElementNode parent) =>
        parent.Name.Local is "style" or "script" && s.AsSpan().ContainsAny('<', '&', '>') && !s.Contains('\r')
            ? "<![CDATA[" + s.Replace("]]>", "]]]]><![CDATA[>") + "]]>"
            : EscapeText(s);

    private static string EscapeAttribute(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace("\"", "&quot;")
            .Replace("\t", "&#9;").Replace("\n", "&#10;").Replace("\r", "&#13;");

    private static string Misc(SmlNode item)
    {
        switch (item)
        {
            case CommentNode comment:
                return Comment(comment.Data, "", "  ");
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

    /// <summary>A comment as XML (§11): <c>&lt;!-- data --&gt;</c> on one line, or a multi-line comment as
    /// pretty-printers write it, its lines one step deeper than the comment and <c>--&gt;</c> at the comment's
    /// indentation.</summary>
    private static string Comment(string data, string indentation, string step)
    {
        if (!data.Contains('\n'))
            return data.Length > 0 ? $"<!-- {data} -->" : "<!---->";
        string inner = indentation + step;
        return "<!--\n" + string.Join('\n', data.Split('\n').Select(l => l.Length == 0 ? "" : inner + l)) + "\n" + indentation + "-->";
    }

    /// <summary>The spaces and tabs after the last line break of a text node, if nothing else follows it there.</summary>
    private static string? TailIndentation(SmlNode? node)
    {
        if (node is not TextNode { Data: var text } || !text.Contains('\n'))
            return null;
        string tail = text[(text.LastIndexOf('\n') + 1)..];
        return tail.TrimStart(' ', '\t').Length == 0 ? tail : null;
    }

    /// <summary>Writes an element; indentation is the spaces and tabs before it on its line, as far as its preceding
    /// text shows them (§11), else its parent's.</summary>
    private static void Element(ElementNode element, StringBuilder output, string indentation)
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
        SmlNode? previous = null;
        foreach (SmlNode child in element.Children)
        {
            string at = TailIndentation(previous) ?? indentation;
            switch (child)
            {
                case ElementNode e:
                    Element(e, output, at);
                    break;
                case TextNode t:
                    output.Append(Text(t.Data, element));
                    break;
                case CommentNode comment:
                    bool deeper = at.Length > indentation.Length && at.StartsWith(indentation, StringComparison.Ordinal);
                    output.Append(Comment(comment.Data, at, deeper ? at[indentation.Length..] : "  "));
                    break;
                default:
                    output.Append(Misc(child));
                    break;
            }
            previous = child;
        }
        output.Append("</").Append(element.QualifiedName).Append('>');
    }
}
