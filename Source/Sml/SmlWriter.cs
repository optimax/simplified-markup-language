using System.Text;

namespace Sml;

/// <summary>Writes the canonical SML form (specification §14.2): a function of the document alone.</summary>
public sealed class SmlWriter
{
    private const int LineLimit = 100;
    private const int SegmentLimit = 80;

    private static readonly string[] IndentCandidates =
        ["  ", "    ", "\t", .. new[] { 1, 3, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }.Select(n => new string(' ', n))];

    private readonly SmlDocument _doc;
    private readonly List<string> _out = [];
    private readonly string? _unit;

    private SmlWriter(SmlDocument doc)
    {
        _doc = doc;
        _unit = ChooseIndent(doc);
    }

    public static string Write(SmlDocument doc) => new SmlWriter(doc).Write();

    private string Write()
    {
        _out.Add("@sml " + SmlReader.Version);
        if (_doc.Standalone is not null)
            _out.Add("@standalone " + _doc.Standalone);
        if (_unit is not null)
            _out.Add("@indent " + (_unit == "\t" ? "tab" : _unit.Length.ToString()));
        foreach (SmlNode item in _doc.Prolog)
            Misc(item, 0);
        Element(_doc.Root, 0);
        foreach (SmlNode item in _doc.Epilog)
            Misc(item, 0);
        return string.Join('\n', _out) + "\n";
    }

    // ---------------------------------------------------------------------------------------------
    // Shared helpers (also used by tests and the XML exporter)

    /// <summary>Children with adjacent text coalesced and empty text dropped.</summary>
    public static List<SmlNode> ContentNodes(ElementNode element)
    {
        var result = new List<SmlNode>();
        foreach (SmlNode child in element.Children)
        {
            if (child is TextNode text)
            {
                if (text.Data.Length == 0)
                    continue;
                if (result.Count > 0 && result[^1] is TextNode previous)
                {
                    result[^1] = new TextNode(previous.Data + text.Data);
                    continue;
                }
            }
            result.Add(child);
        }
        return result;
    }

    private static bool MatchesPrediction(ElementNode element, int depth, string unit)
    {
        List<SmlNode> nodes = ContentNodes(element);
        int k = nodes.Count(n => n is not TextNode);
        if (k == 0 || nodes.Count != 2 * k + 1)
            return false;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (i % 2 == 1)
            {
                if (nodes[i] is TextNode)
                    return false;
                continue;
            }
            string want = "\n" + string.Concat(Enumerable.Repeat(unit, i == 2 * k ? depth : depth + 1));
            if (nodes[i] is not TextNode text || text.Data != want)
                return false;
        }
        return true;
    }

    private static string? ChooseIndent(SmlDocument doc)
    {
        string? best = null;
        int bestCount = 0;
        foreach (string unit in IndentCandidates)
        {
            int count = 0;
            var stack = new Stack<(ElementNode, int)>();
            stack.Push((doc.Root, 0));
            while (stack.Count > 0)
            {
                var (element, depth) = stack.Pop();
                if (MatchesPrediction(element, depth, unit))
                    count++;
                foreach (ElementNode child in element.Children.OfType<ElementNode>())
                    stack.Push((child, depth + 1));
            }
            if (count > bestCount)
                (best, bestCount) = (unit, count);
        }
        return best;
    }

    private static List<string> EscapeUnits(string value)
    {
        var units = new List<string>();
        foreach (Rune r in value.EnumerateRunes())
        {
            units.Add(r.Value switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when XmlRules.NeedsEscape(r) => $"\\u{{{r.Value:X}}}",
                _ => r.ToString(),
            });
        }
        return units;
    }

    public static string Quote(string value) => "\"" + string.Concat(EscapeUnits(value)) + "\"";

    /// <summary>A non-empty value that can be written raw (§6.2) under canonical rule 7.</summary>
    private static bool RawOk(string value)
    {
        if (value.Length == 0 || value[0] is '"' or '|' or ' ' or '\t' || value[^1] is ' ' or '\t')
            return false;
        foreach (Rune r in value.EnumerateRunes())
        {
            if (r.Value is '\t' or '\r' or '\n' || XmlRules.NeedsEscape(r))
                return false;
        }
        return true;
    }

    private static bool RawInlineOk(string value)
    {
        if (value.Length == 0)
            return true;
        if (!RawOk(value))
            return false;
        int depth = 0;
        foreach (char c in value)
        {
            if (c == '(')
                depth++;
            else if (c == ')' && depth-- == 0)
                return false;
            else if (c == ',' && depth == 0)
                return false;
        }
        return depth == 0;
    }

    /// <summary>The terminator to write a value as a block (§7), or null if a block cannot reproduce it.</summary>
    public static string? BlockTag(string value)
    {
        if (!value.Contains('\n') || value.Contains('\r') || value.EnumerateRunes().Any(XmlRules.NeedsEscape))
            return null;
        string[] lines = value.Split('\n');
        if (lines.Any(l => l != l.TrimEnd(' ', '\t')))
            return null;
        if (SmlReader.DedentBlock(lines) != value)
            return null;
        var stripped = lines.Select(XmlRules.StripSpacesAndTabs).ToHashSet(StringComparer.Ordinal);
        if (!stripped.Contains("end"))
            return "end";
        string tag = "EOT";
        for (int n = 1; stripped.Contains(tag); n++)
            tag = $"EOT{n}";
        return tag;
    }

    private static List<string> SplitSegments(List<string> units)
    {
        var segments = new List<string>();
        int i = 0;
        while (i < units.Count)
        {
            int j = i, length = 0;
            while (j < units.Count && length + XmlRules.CodePointLength(units[j]) <= SegmentLimit)
            {
                length += XmlRules.CodePointLength(units[j]);
                j++;
            }
            if (j == i)
                j = i + 1;
            if (j < units.Count)
            {
                int lastSpace = units.FindLastIndex(j - 1, j - i, u => u == " ");
                if (lastSpace >= 0)
                    j = lastSpace + 1;
            }
            segments.Add(string.Concat(units.GetRange(i, j - i)));
            i = j;
        }
        return segments;
    }

    private static string Indentation(int depth) => new(' ', 2 * depth);

    // ---------------------------------------------------------------------------------------------
    // Statements

    /// <summary>Writes prefix followed by a value in canonical form (rule 7); depth is the statement's.</summary>
    private void Value(string prefix, string value, int depth, bool allowBlock = true)
    {
        string ind = Indentation(depth);
        if (value.Length == 0)
        {
            _out.Add(prefix);
            return;
        }
        if (RawOk(value) && XmlRules.CodePointLength(prefix) + 1 + XmlRules.CodePointLength(value) <= LineLimit)
        {
            _out.Add(prefix + " " + value);
            return;
        }
        string? tag = allowBlock ? BlockTag(value) : null;
        if (tag is not null)
        {
            _out.Add(prefix + " |" + (tag == "end" ? "" : tag));
            string inner = ind + "  ";
            _out.AddRange(value.Split('\n').Select(line => line.Length == 0 ? "" : inner + line));
            _out.Add(ind + tag);
            return;
        }
        List<string> units = EscapeUnits(value);
        if (XmlRules.CodePointLength(prefix) + 3 + units.Sum(XmlRules.CodePointLength) <= LineLimit)
        {
            _out.Add(prefix + " \"" + string.Concat(units) + "\"");
            return;
        }
        List<string> segments = SplitSegments(units);
        _out.Add(prefix + " \"" + segments[0] + "\"");
        _out.AddRange(segments.Skip(1).Select(segment => ind + "  \"" + segment + "\""));
    }

    private void Misc(SmlNode item, int depth)
    {
        string ind = Indentation(depth);
        switch (item)
        {
            case CommentNode comment:
                Value(ind + "@comment", comment.Data, depth);
                break;
            case ProcessingInstructionNode pi:
                Value(ind + "@pi " + pi.Target, pi.Data, depth, allowBlock: false);
                break;
            case DoctypeNode dt:
                var line = new StringBuilder(ind + "@doctype " + dt.Name);
                if (dt.PublicId is not null)
                    line.Append(" PUBLIC ").Append(Quote(dt.PublicId)).Append(' ').Append(Quote(dt.SystemId!));
                else if (dt.SystemId is not null)
                    line.Append(" SYSTEM ").Append(Quote(dt.SystemId));
                _out.Add(line.ToString());
                break;
        }
    }

    private void Element(ElementNode element, int depth)
    {
        string ind = Indentation(depth), inner = Indentation(depth + 1);
        List<SmlNode> nodes = ContentNodes(element);
        bool noIndent = false;
        if (_unit is not null && nodes.Any(n => n is not TextNode))
        {
            if (MatchesPrediction(element, depth, _unit))
                nodes = nodes.Where(n => n is not TextNode).ToList();
            else if (nodes[0] is not TextNode)
                noIndent = true;
        }
        var declarations = element.NamespaceDeclarations
            .OrderBy(d => d.Key.Length > 0)
            .ThenBy(d => d.Key, Comparer<string>.Create(XmlRules.CompareCodePoints))
            .ToList();
        var attributes = element.Attributes
            .OrderBy(a => element.AttributeNames[a.Key].Namespace is not null)
            .ThenBy(a => element.AttributeNames[a.Key].Namespace ?? "", Comparer<string>.Create(XmlRules.CompareCodePoints))
            .ThenBy(a => element.AttributeNames[a.Key].Local, Comparer<string>.Create(XmlRules.CompareCodePoints))
            .ToList();
        var entries = declarations
            .Select(d => (Name: d.Key.Length == 0 ? "xmlns" : "xmlns:" + d.Key, d.Value, IsNamespace: true))
            .Concat(attributes.Select(a => (Name: a.Key, a.Value, IsNamespace: false)))
            .ToList();
        bool isEnd = element.QualifiedName == "end";
        bool inline = !isEnd && entries.All(e => RawInlineOk(e.Value) && (!e.IsNamespace || e.Value.Contains(':')));
        string opener = element.QualifiedName;
        if (entries.Count > 0 && inline)
        {
            opener += "(" + string.Join(", ", entries.Select(e => e.Name + "=" + e.Value)) + ")";
            inline = XmlRules.CodePointLength(ind + opener) <= LineLimit;
        }
        if ((inline || entries.Count == 0) && !isEnd)
        {
            if (nodes.Count == 0)
            {
                _out.Add(ind + opener + "/");
                return;
            }
            if (nodes is [TextNode only])
            {
                Value(ind + opener + " =", only.Data, depth);
                return;
            }
        }
        if (isEnd)
            _out.Add(ind + "@element end");
        else
            _out.Add(ind + (inline || entries.Count == 0 ? opener : element.QualifiedName));
        if (entries.Count > 0 && !(inline && !isEnd))
        {
            foreach (var (prefix, uri) in declarations)
            {
                string token = RawOk(uri) && uri.Contains(':') && !uri.Contains(' ') ? uri : Quote(uri);
                _out.Add(inner + "@ns " + (prefix.Length == 0 ? "" : prefix + " ") + token);
            }
            foreach (var (name, value) in attributes)
                Value(inner + "." + name, value, depth + 1);
        }
        if (noIndent)
            _out.Add(inner + "@noindent");
        foreach (SmlNode node in nodes)
        {
            switch (node)
            {
                case TextNode text:
                    Value(inner + "=", text.Data, depth + 1);
                    break;
                case ElementNode child:
                    Element(child, depth + 1);
                    break;
                default:
                    Misc(node, depth + 1);
                    break;
            }
        }
        _out.Add(ind + "end");
    }
}
