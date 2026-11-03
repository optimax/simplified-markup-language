using System.Text;

namespace OptimaxTools.SMLReadWrite;

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
    private readonly bool _vertical;
    private readonly bool _sortAttributes;

    /// <summary>The alias declarations written on the root: element QName, alias, and attribute QName.</summary>
    private readonly List<(string Element, string Alias, string Attribute)> _aliasDeclarations = [];

    /// <summary>The attributes written with an alias: element expanded name and attribute lexical name.</summary>
    private readonly Dictionary<(ExpandedName Element, string QName, string Attribute), string> _aliasOf = [];

    /// <summary>The element aliases declared before the root: element QName and alias.</summary>
    private readonly List<(string Element, string Alias)> _nodeDeclarations = [];

    /// <summary>The elements opened with an alias: expanded name and lexical name.</summary>
    private readonly Dictionary<(ExpandedName Name, string QName), string> _nodeAliasOf = [];

    /// <summary>The elements whose formatting whitespace cleaning replaces (§3.1).</summary>
    private readonly HashSet<ElementNode> _normalized = [];

    /// <summary>The elements whose multi-line text cleaning may tidy (§3.2): outside SVG text content,
    /// foreignObject, and xml:space="preserve".</summary>
    private readonly HashSet<ElementNode> _tidyable = [];

    /// <summary>When cleaning, the SVG style elements whose CSS is laid out (§3.2), even under xml:space="preserve",
    /// since whitespace in CSS never renders.</summary>
    private readonly HashSet<ElementNode> _styleSheets = [];

    /// <summary>The indentation unit of cleaned SML, whatever the input's indentation (§3.1).</summary>
    private const string CleanUnit = "  ";

    private SmlWriter(SmlDocument doc, SmlWriterOptions options)
    {
        _doc = doc;
        _profile = options.Clean;
        _vertical = options.VerticalAttributes;
        _sortAttributes = options.SortAttributes;
        if (_profile is not null)
        {
            FindNormalized(doc.Root, inText: false, foreign: false, preserve: false);
            PlanAliases(_profile);
            PlanNodeAliases(_profile);
        }
        _unit = _normalized.Count > 0 ? CleanUnit : ChooseIndent(doc);
    }

    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    /// <summary>Finds the elements whose formatting whitespace cleaning replaces with the prediction of @indent 2,
    /// and those whose text it may tidy (§3.1). Whitespace is replaced in content of elements, comments, or PIs
    /// separated only by whitespace, outside foreignObject. Under xml:space="preserve" it is replaced only in SVG
    /// elements outside text content, because SVG applies xml:space to text only. In SVG text content it is replaced
    /// only where that cannot change the rendered text: where every gap between the children, and for an element
    /// nested in text also each edge, already has a space or tab.</summary>
    private void FindNormalized(ElementNode element, bool inText, bool foreign, bool preserve)
    {
        bool svg = element.Name.Namespace == SvgNamespace;
        foreign |= svg && element.Name.Local == "foreignObject";
        bool textContent = svg && element.Name.Local is "text" or "tspan" or "textPath" or "tref" or "altGlyph";
        foreach (var (name, value) in element.Attributes)
        {
            if (element.AttributeNames[name] == new ExpandedName(XmlRules.XmlNamespace, "space"))
                preserve = value == "preserve";
        }
        bool rendered = inText || textContent;   // SVG renders this element's character data
        if (svg && element.Name.Local == "style" && !foreign && !rendered && IsCss(element))
            _styleSheets.Add(element);
        if (!foreign && (!preserve || svg && !rendered))
        {
            if (!preserve && !rendered)
                _tidyable.Add(element);
            List<SmlNode> nodes = ContentNodes(element);
            if (nodes.Any(n => n is not TextNode) && nodes.OfType<TextNode>().All(t => IsWhitespace(t.Data))
                && (!inText && !textContent || SpacedBetween(nodes, edges: inText)))
            {
                _normalized.Add(element);
            }
        }
        foreach (ElementNode child in element.Children.OfType<ElementNode>())
            FindNormalized(child, inText || textContent, foreign, preserve);
    }

    /// <summary>Whether a style element holds CSS: its type is absent, empty, or text/css.</summary>
    public static bool IsCss(ElementNode element)
    {
        foreach (var (name, value) in element.Attributes)
        {
            if (element.AttributeNames[name] == new ExpandedName(null, "type"))
                return value.Trim().Length == 0 || value.Trim().Equals("text/css", StringComparison.OrdinalIgnoreCase);
        }
        return true;
    }

    private static bool IsWhitespace(string text) => text.All(c => c is ' ' or '\t' or '\n' or '\r');

    /// <summary>Whether a text node holding a space or tab lies between every two non-text nodes, and, with edges,
    /// also before the first and after the last.</summary>
    private static bool SpacedBetween(List<SmlNode> nodes, bool edges)
    {
        static bool Spaced(SmlNode? node) => node is TextNode t && t.Data.AsSpan().ContainsAny(' ', '\t');
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is TextNode)
                continue;
            if ((i > 0 || edges) && !Spaced(i > 0 ? nodes[i - 1] : null))
                return false;
            if (edges && i == nodes.Count - 1)
                return false;
        }
        return !edges || Spaced(nodes[^1]);
    }

    /// <summary>Writes the canonical form of the document.</summary>
    public static string Write(SmlDocument doc) => new SmlWriter(doc, new SmlWriterOptions()).Write();

    /// <summary>Writes the document with options; anything but the default options gives output that is not
    /// canonical.
    /// <para>Cleaned with a profile, the readable conversion of §§3.1–3.2: formatting whitespace follows
    /// <c>@indent 2</c>, each value with a layout is written with one item per line, and multi-line text and comments
    /// are tidied, so the output reads back as a document that differs in whitespace. A value written as a block uses
    /// the profile's alias for its attribute, declared on the root element, and elements with an element alias are
    /// opened with it. A value with a layout that cannot be divided cleanly keeps its whitespace and is quoted with a
    /// continuation line per item (§6.5).</para>
    /// <para>With vertical attributes, every attribute and namespace declaration is on its own line; the output
    /// reads back as the same document.</para></summary>
    public static string Write(SmlDocument doc, SmlWriterOptions options) => new SmlWriter(doc, options).Write();

    /// <summary>Writes the document cleaned with a profile: the options overload with Clean set.</summary>
    public static string Write(SmlDocument doc, SmlProfile clean) => Write(doc, new SmlWriterOptions { Clean = clean });

    private string Write()
    {
        _out.Add("@sml " + SmlReader.Version + (_profile is not null ? " tidy" : ""));
        if (_doc.Standalone is not null)
            _out.Add("@standalone " + _doc.Standalone);
        if (_unit is null)
        {
            if (DefaultMatters(_doc))
                _out.Add("@indent none");
        }
        else if (_unit != SmlReader.DefaultUnit)
        {
            _out.Add("@indent " + (_unit == "\t" ? "tab" : _unit.Length.ToString()));
        }
        // Cleaning: every alias is declared before the root, right after the header (§§8.1, 8.7).
        _out.AddRange(_nodeDeclarations.Select(d => $"@node {d.Element} {d.Alias}"));
        _out.AddRange(_aliasDeclarations.Select(d => $"@block {d.Element} .{d.Attribute} {d.Alias}"));
        Items(_doc.Prolog, 0, _unit is not null, "");
        Element(_doc.Root, 0, "", _unit is not null);
        Items(_doc.Epilog, 0, _unit is not null, "");
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
        if (layout is { } l && ValueLayout.Reflow(value, l) is { } reflowed)
            return (reflowed, layout);
        return (value, layout);
    }

    /// <summary>Multi-line text tidied (§3.2): trailing spaces and tabs removed from each line, blank first and last
    /// lines dropped, runs of empty lines reduced to one, and the indentation that all other lines share removed.
    /// Null for text without LF, with a CR, or with nothing visible.</summary>
    public static string? TidyText(string text)
    {
        if (!text.Contains('\n') || text.Contains('\r') || !Visible(text))
            return null;
        var lines = text.Split('\n').Select(l => l.TrimEnd(' ', '\t')).ToList();
        while (lines[0].Length == 0)
            lines.RemoveAt(0);
        while (lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        lines = lines.Where((l, i) => l.Length > 0 || lines[i - 1].Length > 0).ToList();
        string prefix = XmlRules.SharedIndentation(lines);
        return string.Join('\n', lines.Select(l => l.Length == 0 ? "" : l[prefix.Length..]));
    }

    /// <summary>Whether a profile writes the value quoted with a continuation line per item.</summary>
    private static bool Segmented(string value, Layout? layout) =>
        layout is { } l && BlockTag(value) is null && ValueLayout.Segments(value, l) is not null;

    /// <summary>Chooses the aliases to use: those of attributes written as blocks. Each is declared before the root,
    /// where its names are resolved at each invocation (§8.1), with the element and attribute names of its first
    /// use, and used wherever an element and its attribute are written with those names.</summary>
    private void PlanAliases(SmlProfile profile)
    {
        var stack = new Stack<ElementNode>([_doc.Root]);
        var lexical = new Dictionary<SmlProfile.Alias, (string Element, string Attribute)>();
        while (stack.Count > 0)
        {
            ElementNode element = stack.Pop();
            foreach (var (name, value) in element.Attributes)
            {
                SmlProfile.Alias? alias = profile.FindAlias(element.Name, element.AttributeNames[name]);
                if (alias is null || BlockTag(Effective(element, name, value).Value) is null)
                    continue;
                if (!lexical.TryGetValue(alias, out var declared))
                    lexical[alias] = declared = (element.QualifiedName, name);
                if (declared == (element.QualifiedName, name))
                    _aliasOf[(element.Name, element.QualifiedName, name)] = alias.Name;
            }
            foreach (ElementNode child in element.Children.OfType<ElementNode>().Reverse())
                stack.Push(child);
        }
        foreach (SmlProfile.Alias alias in profile.Aliases.Where(lexical.ContainsKey))
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

    /// <summary>Writes an attribute: with a profile, as a block with its alias, or with a continuation line per
    /// item, where those apply; otherwise in canonical form.</summary>
    private void Attribute(ElementNode element, string name, string value, int depth)
    {
        string ind = Indentation(depth);
        var (written, layout) = Effective(element, name, value);
        if (_profile is not null && BlockTag(written) is { } tag)
        {
            string head = _aliasOf.TryGetValue((element.Name, element.QualifiedName, name), out string? alias) ? alias : "." + name;
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

    /// <summary>The step with which content nodes are laid out at an indentation (rule 9.1–9.2): the unit, or the step
    /// their first whitespace suggests; null if neither lays them out.</summary>
    private static string? LayoutStep(List<SmlNode> nodes, string indentation, string unit)
    {
        if (MatchesLayout(nodes, indentation, unit))
            return unit;
        string? candidate = StepFrom(nodes, indentation);
        return candidate is not null && candidate != unit && MatchesLayout(nodes, indentation, candidate) ? candidate : null;
    }

    /// <summary>How many child elements rule 9.1–9.2 lay out when they have this indentation.</summary>
    private static int LaidOutChildren(ElementNode element, string indentation, string unit) =>
        element.Children.OfType<ElementNode>().Count(child =>
        {
            List<SmlNode> nodes = ContentNodes(child);
            return nodes.Any(n => n is not TextNode) && LayoutStep(nodes, indentation, unit) is not null;
        });

    /// <summary>The writer's whitespace decisions for an element (rule 9). Step is the whitespace its children are
    /// indented by beyond its own indentation; Local is the step written as @indent, if any.</summary>
    private static (bool LaidOut, string? Step, string? Local, bool NoIndent) Plan(
        ElementNode element, List<SmlNode> nodes, string indentation, string? unit)
    {
        if (unit is null || nodes.All(n => n is TextNode))
            return (false, unit, null, false);
        if (LayoutStep(nodes, indentation, unit) is { } step)
            return (true, step, step != unit ? step : null, false);
        // Exact (rule 9.3). Its children's indentation still follows its first whitespace if that lays out more of them.
        string? local = StepFrom(nodes, indentation);
        if (local == unit || (local is not null
            && LaidOutChildren(element, indentation + local, unit) <= LaidOutChildren(element, indentation + unit, unit)))
        {
            local = null;
        }
        return (false, local ?? unit, local, nodes[0] is not TextNode);
    }

    /// <summary>Whether SML written without a unit would read differently under the default unit (§6.4): the
    /// document has an element with a non-text child, or multi-line text as the root's only content. Then the
    /// writer declares @indent none (rule 9).</summary>
    private static bool DefaultMatters(SmlDocument doc)
    {
        var stack = new Stack<ElementNode>([doc.Root]);
        while (stack.Count > 0)
        {
            ElementNode element = stack.Pop();
            if (ContentNodes(element).Any(n => n is not TextNode))
                return true;
            foreach (ElementNode child in element.Children.OfType<ElementNode>())
                stack.Push(child);
        }
        return ContentNodes(doc.Root).Any(n => n is TextNode t && t.Data.Contains('\n'));
    }

    /// <summary>The @indent unit (rule 9): the one with which the writer lays out the most elements, and then writes
    /// the fewest per-element @indent statements; ties go to the earlier candidate. Null if no element would be laid
    /// out.</summary>
    private static string? ChooseIndent(SmlDocument doc)
    {
        string? best = null;
        (int LaidOut, int Locals) bestScore = (0, 0);
        foreach (string unit in IndentCandidates)
        {
            int laidOut = 0, locals = 0;
            var stack = new Stack<(ElementNode, string)>();
            stack.Push((doc.Root, ""));
            while (stack.Count > 0)
            {
                var (element, indentation) = stack.Pop();
                var plan = Plan(element, ContentNodes(element), indentation, unit);
                laidOut += plan.LaidOut ? 1 : 0;
                locals += plan.Local is null ? 0 : 1;
                foreach (ElementNode child in element.Children.OfType<ElementNode>())
                    stack.Push((child, indentation + plan.Step));
            }
            if (laidOut > 0 && (best is null || laidOut > bestScore.LaidOut || (laidOut == bestScore.LaidOut && locals < bestScore.Locals)))
                (best, bestScore) = (unit, (laidOut, locals));
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
        if (value.Length == 0 || value[0] is '"' or ' ' or '\t' || value[^1] is ' ' or '\t')
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

    /// <summary>Whether a value has a character other than a space, tab, or line break: a block of whitespace only
    /// would show nothing, so it is clearer quoted (canonical rule 7.2).</summary>
    public static bool Visible(string value) => value.AsSpan().ContainsAnyExcept(' ', '\t', '\n');

    /// <summary>The marker to write a value that contains LF and a visible character as a block (canonical rule 7.2),
    /// or null.</summary>
    public static string? BlockTag(string value) => value.Contains('\n') && Visible(value) ? BlockMarker(value) : null;

    /// <summary>The marker to write a value as a block's payload (§7): the shortest run of three or more quotation
    /// marks that no payload line equals, or null if a block cannot reproduce the value. The empty value has no
    /// payload lines.</summary>
    public static string? BlockMarker(string value)
    {
        if (value.Contains('\r') || value.EnumerateRunes().Any(XmlRules.NeedsEscape))
            return null;
        string[] lines = value.Length == 0 ? [] : value.Split('\n');
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
        _out.Add(inner + marker);
    }

    /// <summary>Whether a comment can be one line of an @comments block (§9): non-empty, one line, and none of the
    /// characters §4 says to escape. Comment data never begins or ends with whitespace.</summary>
    public static bool CommentRunOk(string data) =>
        data.Length > 0 && !data.AsSpan().ContainsAny('\n', '\r') && !data.EnumerateRunes().Any(XmlRules.NeedsEscape);

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
                _out.Add(ind + "  " + marker);
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
                case CommentNode comment when _profile is not null && TidyText(comment.Data) is { } tidy:
                    // Cleaning: a multi-line comment is tidied (§3.2).
                    Comment(tidy, depth);
                    break;
                default:
                    Misc(nodes[i], depth);
                    break;
            }
            i++;
        }
    }

    /// <summary>Writes a comment like any other value (rule 7): a block is literal wherever it is (§9).</summary>
    private void Comment(string data, int depth) => Value(Indentation(depth) + "@rem", data, depth);

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
        if (value is not null && Visible(value))
        {
            Block(prefix, value, BlockMarker(value)!, depth);
            return true;
        }
        if (BlockTag(data) is not null)
            return false;
        Value(prefix, data, depth, allowBlock: false);
        return true;
    }

    private void Misc(SmlNode item, int depth)
    {
        string ind = Indentation(depth);
        switch (item)
        {
            case CommentNode comment:
                Comment(comment.Data, depth);
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
        // Cleaning: a style sheet is laid out as CSS, other code and text tidied (§3.2).
        string? code = null;
        if (_profile is not null && nodes is [TextNode text])
        {
            if (_styleSheets.Contains(element) && CssFormatter.Format(TidyText(text.Data) ?? text.Data) is { } sheet)
                code = sheet;
            else if (_tidyable.Contains(element))
                code = TidyText(text.Data);
        }
        var (laidOut, step, local, noIndent) = Plan(element, nodes, indentation, _unit);
        if (_normalized.Contains(element))
        {
            // Cleaning: the formatting whitespace becomes the prediction of @indent 2, whatever it was (§3.1).
            (laidOut, step, local, noIndent) = (true, CleanUnit, null, false);
        }
        if (laidOut)
            nodes = nodes.Where(n => n is not TextNode).ToList();
        // Rule 4: declarations, then attributes, each in document order; or in the order of Canonical XML.
        var declarations = element.NamespaceDeclarations.ToList();
        var attributes = element.Attributes.ToList();
        if (_sortAttributes)
        {
            var codePoints = Comparer<string>.Create(XmlRules.CompareCodePoints);
            declarations = declarations.OrderBy(d => d.Key.Length > 0).ThenBy(d => d.Key, codePoints).ToList();
            attributes = attributes
                .OrderBy(a => element.AttributeNames[a.Key].Namespace is not null)
                .ThenBy(a => element.AttributeNames[a.Key].Namespace ?? "", codePoints)
                .ThenBy(a => element.AttributeNames[a.Key].Local, codePoints)
                .ToList();
        }
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
        if ((inline || entries.Count == 0) && !isEnd)
        {
            if (nodes.Count == 0)
            {
                _out.Add(ind + opener + "/");
                return;
            }
            if (nodes is [TextNode only])
            {
                // Tidied text is written with the edges of a laid-out block (§6.4) where the compact block gets them.
                string data = code is null ? only.Data
                    : code.Contains('\n') && laidOutPosition && _unit is not null ? SmlReader.LayOutBlock(code, indentation, _unit) : code;
                if (CompactText(ind + opener + " =", data, depth, indentation, laidOutPosition))
                    return;
            }
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
        if (vertical)
        {
            foreach (var (name, value) in attributes)
                Attribute(element, name, value, depth + 1);
        }
        if (local is not null)
            _out.Add(inner + "@indent " + (local == "\t" ? "tab" : local.Length.ToString()));
        if (noIndent)
            _out.Add(inner + "@noindent");
        if (code is not null && nodes is [TextNode])
            nodes = [new TextNode(code)];
        Items(nodes, depth + 1, laidOut, indentation + (step ?? ""));
        _out.Add(ind + "end");
    }
}
