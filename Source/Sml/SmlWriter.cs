using System.Text;

namespace Sml;

/// <summary>Writes SML: the canonical form (specification §14.2), a function of the document alone, or with an
/// <see cref="SmlProfile"/>, the canonical form adjusted by the profile's aliases and layouts.</summary>
public sealed class SmlWriter
{
    private const int LineLimit = 100;
    private const int SegmentLimit = 80;

    private static readonly string[] IndentCandidates =
        ["  ", "    ", "\t", .. new[] { 0, 1, 3, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }.Select(n => new string(' ', n))];

    private readonly SmlDocument _doc;
    private readonly List<string> _out = [];
    private readonly string? _unit;
    private readonly SmlProfile? _profile;
    private readonly bool _reflow;
    private readonly bool _vertical;

    /// <summary>The alias declarations written on the root: element QName, alias, and attribute QName.</summary>
    private readonly List<(string Element, string Alias, string Attribute)> _aliasDeclarations = [];

    /// <summary>The attributes written with an alias: element expanded name and attribute lexical name.</summary>
    private readonly Dictionary<(ExpandedName Element, string Attribute), string> _aliasOf = [];

    /// <summary>The element aliases declared before the root: element QName and alias.</summary>
    private readonly List<(string Element, string Alias)> _nodeDeclarations = [];

    /// <summary>The elements opened with an alias: expanded name and lexical name.</summary>
    private readonly Dictionary<(ExpandedName Name, string QName), string> _nodeAliasOf = [];

    /// <summary>The lexical names of the attributes whose values were reflowed.</summary>
    private readonly SortedSet<string> _reflowed = new(Comparer<string>.Create(XmlRules.CompareCodePoints));

    private SmlWriter(SmlDocument doc, SmlWriterOptions options)
    {
        _doc = doc;
        _unit = ChooseIndent(doc);
        _profile = options.Profile;
        _reflow = options.Reflow && _profile is not null;
        _vertical = options.VerticalAttributes;
        if (_profile is not null)
        {
            PlanAliases(_profile);
            PlanNodeAliases(_profile);
        }
    }

    /// <summary>Writes the canonical form of the document.</summary>
    public static string Write(SmlDocument doc) => new SmlWriter(doc, new SmlWriterOptions()).Write();

    /// <summary>Writes the document with options; anything but the default options gives output that is not
    /// canonical.
    /// <para>With a profile and without reflow, the output reads back as the same document. A value written as a
    /// block uses the profile's alias for its attribute, declared on the root element. A value with a layout and
    /// at least two items is quoted, with a continuation line for each item (§6.5).</para>
    /// <para>With reflow, the readable conversion of §3.2: each value with a layout is written with one item per
    /// line, so the output reads back as a different document whose values contain line breaks. The output then
    /// starts with a <c>#</c> comment that names the attributes reflowed.</para>
    /// <para>With vertical attributes, every attribute and namespace declaration is on its own line; the output
    /// reads back as the same document.</para></summary>
    public static string Write(SmlDocument doc, SmlWriterOptions options) => new SmlWriter(doc, options).Write();

    /// <summary>Writes the document with a profile: the options overload with Profile and Reflow set.</summary>
    public static string Write(SmlDocument doc, SmlProfile profile, bool reflow = false) =>
        Write(doc, new SmlWriterOptions { Profile = profile, Reflow = reflow });

    private string Write()
    {
        _out.Add("@sml " + SmlReader.Version);
        if (_doc.Standalone is not null)
            _out.Add("@standalone " + _doc.Standalone);
        if (_unit is not null)
            _out.Add("@indent " + (_unit == "\t" ? "tab" : _unit.Length.ToString()));
        _out.AddRange(_nodeDeclarations.Select(d => $"@node {d.Element} {d.Alias}"));
        Items(_doc.Prolog, 0, _unit is not null, "");
        Element(_doc.Root, 0, "", _unit is not null);
        Items(_doc.Epilog, 0, _unit is not null, "");
        if (_reflowed.Count > 0)
            _out.Insert(0, "# Readable conversion (SML §3.2), not strict: one item per line in " + string.Join(", ", _reflowed));
        return string.Join('\n', _out) + "\n";
    }

    // ---------------------------------------------------------------------------------------------
    // Profiles

    /// <summary>The value to write for an attribute, reflowed if requested, and its layout, if any.</summary>
    private (string Value, Layout? Layout) Effective(ElementNode element, string name, string value)
    {
        if (_profile is null)
            return (value, null);
        Layout? layout = _profile.FindLayout(element.Name, element.AttributeNames[name]);
        if (_reflow && layout is { } l && ValueLayout.Reflow(value, l) is { } reflowed)
            return (reflowed, layout);
        return (value, layout);
    }

    /// <summary>Whether a profile writes the value quoted with a continuation line per item.</summary>
    private static bool Segmented(string value, Layout? layout) =>
        layout is { } l && BlockTag(value) is null && ValueLayout.Segments(value, l) is not null;

    /// <summary>Chooses the aliases to use: those of attributes written as blocks, where the element and attribute
    /// names resolve on the root (§8.5), so that one declaration there serves the whole document.</summary>
    private void PlanAliases(SmlProfile profile)
    {
        ElementNode root = _doc.Root;
        var stack = new Stack<ElementNode>([root]);
        var used = new HashSet<SmlProfile.Alias>();
        var lexical = new Dictionary<SmlProfile.Alias, (string Element, string Attribute)>();
        while (stack.Count > 0)
        {
            ElementNode element = stack.Pop();
            foreach (var (name, value) in element.Attributes)
            {
                SmlProfile.Alias? alias = profile.FindAlias(element.Name, element.AttributeNames[name]);
                if (alias is null || BlockTag(Effective(element, name, value).Value) is null)
                    continue;
                if (lexical.TryGetValue(alias, out var declared))
                {
                    if (declared.Attribute == name)
                        _aliasOf[(element.Name, name)] = alias.Name;
                    continue;
                }
                string? elementName = QNameOnRoot(root, element.Name);
                if (elementName is null || !AttributeResolvesOnRoot(root, name, element.AttributeNames[name]))
                    continue;
                lexical[alias] = (elementName, name);
                used.Add(alias);
                _aliasOf[(element.Name, name)] = alias.Name;
            }
            foreach (ElementNode child in element.Children.OfType<ElementNode>().Reverse())
                stack.Push(child);
        }
        foreach (SmlProfile.Alias alias in profile.Aliases.Where(used.Contains))
            _aliasDeclarations.Add((lexical[alias].Element, alias.Name, lexical[alias].Attribute));
    }

    /// <summary>Chooses the profile's element aliases to use (§8.7). An alias stands for a lexical name, so it is
    /// declared for the name of the first element with its expanded name, and used for the elements with that
    /// expanded name and that lexical name. It is not used at all if an element of the document is named like it,
    /// since that element could then be opened only with @element.</summary>
    private void PlanNodeAliases(SmlProfile profile)
    {
        if (profile.Nodes.Count == 0)
            return;
        var elements = new List<ElementNode>();
        var stack = new Stack<ElementNode>([_doc.Root]);
        while (stack.Count > 0)
        {
            ElementNode element = stack.Pop();
            elements.Add(element);
            foreach (ElementNode child in element.Children.OfType<ElementNode>().Reverse())
                stack.Push(child);
        }
        var names = elements.Select(e => e.QualifiedName).ToHashSet(StringComparer.Ordinal);
        foreach (SmlProfile.NodeAlias alias in profile.Nodes)
        {
            if (names.Contains(alias.Name) || elements.Find(e => e.Name == alias.Element) is not { } first)
                continue;
            _nodeDeclarations.Add((first.QualifiedName, alias.Name));
            _nodeAliasOf[(alias.Element, first.QualifiedName)] = alias.Name;
        }
    }

    /// <summary>A QName for an element's expanded name under the root's namespace declarations, or null.</summary>
    private static string? QNameOnRoot(ElementNode root, ExpandedName name)
    {
        root.NamespaceDeclarations.TryGetValue("", out string? defaultNs);
        if (string.IsNullOrEmpty(defaultNs))
            defaultNs = null;
        if (name.Namespace == defaultNs)
            return name.Local;
        if (name.Namespace is null)
            return null;
        string? prefix = root.NamespaceDeclarations
            .Where(d => d.Key.Length > 0 && d.Value == name.Namespace)
            .Select(d => d.Key)
            .Order(Comparer<string>.Create(XmlRules.CompareCodePoints))
            .FirstOrDefault();
        return prefix is null ? null : prefix + ":" + name.Local;
    }

    /// <summary>Whether an attribute's lexical name means the same expanded name on the root.</summary>
    private static bool AttributeResolvesOnRoot(ElementNode root, string lexical, ExpandedName name)
    {
        int colon = lexical.IndexOf(':');
        if (colon < 0)
            return true;
        string prefix = lexical[..colon];
        if (prefix == "xml")
            return true;
        return root.NamespaceDeclarations.TryGetValue(prefix, out string? uri) && uri == name.Namespace;
    }

    /// <summary>Writes an attribute: with a profile, as a block with its alias, or with a continuation line per
    /// item, where those apply; otherwise in canonical form.</summary>
    private void Attribute(ElementNode element, string name, string value, int depth)
    {
        string ind = Indentation(depth);
        var (written, layout) = Effective(element, name, value);
        if (written != value)
            _reflowed.Add(name);
        if (_profile is not null && BlockTag(written) is { } tag)
        {
            string head = _aliasOf.TryGetValue((element.Name, name), out string? alias) ? alias : "." + name;
            Block(ind + head, written, tag, depth);
            return;
        }
        if (layout is { } l && ValueLayout.Segments(written, l) is { } segments)
        {
            string head = ind + "." + name + " ";
            foreach (string segment in segments)
            {
                List<string> units = EscapeUnits(segment);
                IEnumerable<string> parts = XmlRules.CodePointLength(head) + 2 + units.Sum(XmlRules.CodePointLength) <= LineLimit
                    ? [string.Concat(units)]
                    : SplitSegments(units);
                foreach (string part in parts)
                {
                    _out.Add(head + "\"" + part + "\"");
                    head = ind + "  ";
                }
            }
            return;
        }
        Value(ind + "." + name, written, depth);
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

    /// <summary>Whether content nodes are laid out (§6.4): LF, the indentation, and the step before each non-text
    /// child, and LF and the indentation before the end.</summary>
    private static bool MatchesLayout(List<SmlNode> nodes, string indentation, string step)
    {
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
            string want = "\n" + indentation + (i == 2 * k ? "" : step);
            if (nodes[i] is not TextNode text || text.Data != want)
                return false;
        }
        return true;
    }

    /// <summary>Whether an element is laid out with every step equal to unit (used to choose @indent, rule 9).</summary>
    private static bool MatchesPrediction(ElementNode element, int depth, string unit) =>
        MatchesLayout(ContentNodes(element), string.Concat(Enumerable.Repeat(unit, depth)), unit);

    /// <summary>The step that an element's first whitespace suggests, if it is a possible @indent value (§6.4).</summary>
    private static string? StepFrom(List<SmlNode> nodes, string indentation)
    {
        if (nodes.Count > 0 && nodes[0] is TextNode first && first.Data.StartsWith("\n" + indentation, StringComparison.Ordinal))
        {
            string step = first.Data[(1 + indentation.Length)..];
            if (IndentCandidates.Contains(step))
                return step;
        }
        return null;
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

    /// <summary>The marker to write a value that contains LF and something else as a block (canonical rule 7.2), or
    /// null. A value of line breaks only would be a block of blank lines; it is clearer quoted.</summary>
    public static string? BlockTag(string value) =>
        value.Contains('\n') && value.AsSpan().ContainsAnyExcept('\n') ? BlockMarker(value) : null;

    /// <summary>The marker to write a value as a block's payload (§7): the shortest run of three or more quotation
    /// marks that no payload line equals, or null if a block cannot reproduce the value. The empty value has no
    /// payload lines.</summary>
    public static string? BlockMarker(string value)
    {
        if (value.Contains('\r') || value.EnumerateRunes().Any(XmlRules.NeedsEscape))
            return null;
        string[] lines = value.Length == 0 ? [] : value.Split('\n');
        if (lines.Any(l => l != l.TrimEnd(' ', '\t')))
            return null;
        if (SmlReader.DedentBlock(lines) != value)
            return null;
        var stripped = lines.Select(XmlRules.StripSpacesAndTabs).ToHashSet(StringComparer.Ordinal);
        string marker = "\"\"\"";
        while (stripped.Contains(marker))
            marker += '"';
        return marker;
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
            Block(prefix, value, tag, depth);
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

    /// <summary>Writes prefix and a value as a block (§7) opened and closed by marker; depth is the statement's.</summary>
    private void Block(string prefix, string value, string marker, int depth)
    {
        string ind = Indentation(depth), inner = ind + "  ";
        _out.Add(prefix + " " + marker);
        _out.AddRange(value.Split('\n').Select(line => line.Length == 0 ? "" : inner + line));
        _out.Add(ind + marker);
    }

    /// <summary>Whether a comment can be one line of an @comments block (§9): non-empty, one line, no space or tab
    /// at either end, and none of the characters §4 says to escape.</summary>
    public static bool CommentRunOk(string data) =>
        data.Length > 0 && !data.AsSpan().ContainsAny('\n', '\r') && data[0] is not (' ' or '\t')
        && data[^1] is not (' ' or '\t') && !data.EnumerateRunes().Any(XmlRules.NeedsEscape);

    /// <summary>The block value that <see cref="SmlReader.LayOutBlock"/> turns into data, or null if the data does
    /// not have that layout.</summary>
    public static string? LaidOutValue(string data, string indentation, string unit)
    {
        string outer = indentation;
        if (!data.StartsWith('\n') || !data.EndsWith(outer, StringComparison.Ordinal) || data.Length < 1 + outer.Length)
            return null;
        string middle = data[1..^outer.Length];
        string value;
        if (middle.Length == 0)
        {
            value = "";
        }
        else
        {
            if (!middle.EndsWith('\n'))
                return null;
            string inner = outer + unit;
            var lines = new List<string>();
            foreach (string line in middle[..^1].Split('\n'))
            {
                if (line.StartsWith(inner, StringComparison.Ordinal))
                    lines.Add(line[inner.Length..]);
                else if (line.Length == 0)
                    lines.Add("");
                else
                    return null;
            }
            value = string.Join('\n', lines);
        }
        if (BlockMarker(value) is null || SmlReader.LayOutBlock(value, indentation, unit) != data)
            return null;
        return value;
    }

    /// <summary>Writes content statements at depth; a run of two or more comments that <see cref="CommentRunOk"/>
    /// accepts is one @comments block (canonical rule 11).</summary>
    private void Items(List<SmlNode> nodes, int depth, bool laidOut, string indentation)
    {
        string ind = Indentation(depth);
        for (int i = 0; i < nodes.Count;)
        {
            int j = i;
            while (j < nodes.Count && nodes[j] is CommentNode run && CommentRunOk(run.Data))
                j++;
            if (j - i >= 2)
            {
                var lines = nodes.GetRange(i, j - i).Select(n => ((CommentNode)n).Data).ToList();
                string marker = BlockMarker(string.Join('\n', lines))!;
                _out.Add(ind + "@comments " + marker);
                _out.AddRange(lines.Select(line => ind + "  " + line));
                _out.Add(ind + marker);
                i = j;
                continue;
            }
            switch (nodes[i])
            {
                case TextNode text:
                    Value(ind + "=", text.Data, depth);
                    break;
                case ElementNode child:
                    Element(child, depth, indentation, laidOut);
                    break;
                default:
                    Misc(nodes[i], depth, laidOut, indentation);
                    break;
            }
            i++;
        }
    }

    /// <summary>Writes a comment; in a laid-out position a block has predicted edges (§6.4), so a block is used only
    /// for data in that layout, and other multi-line data is quoted.</summary>
    private void Comment(string data, int depth, bool laidOut, string indentation)
    {
        string ind = Indentation(depth);
        if (!laidOut || data.Length == 0
            || (RawOk(data) && XmlRules.CodePointLength(ind) + 9 + XmlRules.CodePointLength(data) <= LineLimit))
        {
            Value(ind + "@comment", data, depth, allowBlock: !laidOut);
            return;
        }
        string? value = LaidOutValue(data, indentation, _unit!);
        if (value is null || !value.AsSpan().ContainsAnyExcept('\n'))
        {
            Value(ind + "@comment", data, depth, allowBlock: false);
            return;
        }
        Block(ind + "@comment", value, BlockMarker(value)!, depth);
    }

    /// <summary>Writes prefix and an element's only text in the compact form, and returns true; or returns false
    /// when the element must use the normal form: in a laid-out position a compact block has predicted edges
    /// (§6.4), so text that needs a literal block is written as an = statement instead (rule 6).</summary>
    private bool CompactText(string prefix, string data, int depth, string indentation, bool laidOut)
    {
        if (_unit is null || !laidOut || data.Length == 0
            || (RawOk(data) && XmlRules.CodePointLength(prefix) + 1 + XmlRules.CodePointLength(data) <= LineLimit))
        {
            Value(prefix, data, depth);
            return true;
        }
        string? value = LaidOutValue(data, indentation, _unit);
        if (value is not null && value.AsSpan().ContainsAnyExcept('\n'))
        {
            Block(prefix, value, BlockMarker(value)!, depth);
            return true;
        }
        if (BlockTag(data) is not null)
            return false;
        Value(prefix, data, depth, allowBlock: false);
        return true;
    }

    private void Misc(SmlNode item, int depth, bool laidOut = false, string indentation = "")
    {
        string ind = Indentation(depth);
        switch (item)
        {
            case CommentNode comment:
                Comment(comment.Data, depth, laidOut, indentation);
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

    /// <summary>Writes an element; indentation is its own indentation in the XML (§6.4), and laidOutPosition says
    /// whether whitespace before it is predicted.</summary>
    private void Element(ElementNode element, int depth, string indentation, bool laidOutPosition)
    {
        string ind = Indentation(depth), inner = Indentation(depth + 1);
        List<SmlNode> nodes = ContentNodes(element);
        bool noIndent = false, laidOut = false;
        string? step = _unit, local = null;
        if (_unit is not null && nodes.Any(n => n is not TextNode))
        {
            laidOut = MatchesLayout(nodes, indentation, _unit);
            if (!laidOut && StepFrom(nodes, indentation) is { } candidate && candidate != _unit
                && MatchesLayout(nodes, indentation, candidate))
            {
                (laidOut, step, local) = (true, candidate, candidate);
            }
            if (laidOut)
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
        var attributeEntries = attributes.Select(a =>
        {
            var (value, layout) = Effective(element, a.Key, a.Value);
            return (Name: a.Key, Value: value, IsNamespace: false, Layout: layout);
        });
        var entries = declarations
            .Select(d => (Name: d.Key.Length == 0 ? "xmlns" : "xmlns:" + d.Key, d.Value, IsNamespace: true, Layout: (Layout?)null))
            .Concat(attributeEntries)
            .ToList();
        string elementName = _nodeAliasOf.GetValueOrDefault((element.Name, element.QualifiedName)) ?? element.QualifiedName;
        bool isEnd = elementName == "end";
        bool inline = !isEnd && !_vertical && entries.All(e =>
            RawInlineOk(e.Value) && (!e.IsNamespace || e.Value.Contains(':')) && !Segmented(e.Value, e.Layout));
        string opener = elementName;
        if (entries.Count > 0 && inline)
        {
            opener += "(" + string.Join(", ", entries.Select(e => e.Name + "=" + e.Value)) + ")";
            inline = XmlRules.CodePointLength(ind + opener) <= LineLimit;
        }
        bool declaresAliases = depth == 0 && _aliasDeclarations.Count > 0;
        if ((inline || entries.Count == 0) && !isEnd && !declaresAliases)
        {
            if (nodes.Count == 0)
            {
                _out.Add(ind + opener + "/");
                return;
            }
            if (nodes is [TextNode only] && CompactText(ind + opener + " =", only.Data, depth, indentation, laidOutPosition))
                return;
        }
        if (isEnd)
            _out.Add(ind + "@element end");
        else
            _out.Add(ind + (inline || entries.Count == 0 ? opener : elementName));
        bool vertical = entries.Count > 0 && !(inline && !isEnd);
        if (vertical)
        {
            foreach (var (prefix, uri) in declarations)
            {
                string token = RawOk(uri) && uri.Contains(':') && !uri.Contains(' ') ? uri : Quote(uri);
                _out.Add(inner + "@ns " + (prefix.Length == 0 ? "" : prefix + " ") + token);
            }
        }
        if (declaresAliases)
            _out.AddRange(_aliasDeclarations.Select(d => $"{inner}@block {d.Element} .{d.Attribute} {d.Alias}"));
        if (vertical)
        {
            foreach (var (name, value) in attributes)
                Attribute(element, name, value, depth + 1);
        }
        if (local is not null)
            _out.Add(inner + "@indent " + (local == "\t" ? "tab" : local.Length.ToString()));
        if (noIndent)
            _out.Add(inner + "@noindent");
        Items(nodes, depth + 1, laidOut, indentation + (step ?? ""));
        _out.Add(ind + "end");
    }
}
