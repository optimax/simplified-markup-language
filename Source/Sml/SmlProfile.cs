namespace Sml;

/// <summary>An SML profile: aliases and layouts that an SML writer applies when it converts XML (see
/// <see cref="SmlWriter.Write(SmlDocument, SmlProfile, bool)"/>). A profile is writer configuration, not part of
/// any document: the SML written with it declares every alias it uses, so it reads without the profile.</summary>
/// <remarks>
/// A profile is UTF-8 text with one statement per line; blank lines and lines starting with <c>#</c> are ignored.
/// <list type="bullet">
/// <item><c>@ns [prefix] URI</c> binds a prefix, or the default namespace, for the names in the profile. All
/// <c>@ns</c> lines come first.</item>
/// <item><c>@block element-qname alias .attribute-qname</c> declares an alias, exactly as in SML (§8.1). The writer
/// invokes it, and declares it on the root element, wherever it writes that attribute of that element as a
/// block.</item>
/// <item><c>@node element-qname alias</c> declares an element alias, as in SML (§8.7). The writer declares it before
/// the root element and opens those elements with the alias, unless an element of the document is named like the
/// alias.</item>
/// <item><c>@layout element-qname .attribute-qname kind</c> divides the attribute's values into items (§3.2), where
/// kind is <c>path</c>, <c>numbers N</c>, <c>declarations</c>, <c>functions</c>, or <c>list</c>. The element
/// <c>*</c> means any element without a layout of its own for that attribute.</item>
/// </list>
/// </remarks>
public sealed class SmlProfile
{
    /// <summary>An alias for an attribute of an element.</summary>
    public sealed record Alias(ExpandedName Element, string Name, ExpandedName Attribute);

    /// <summary>An alias for the name of an element.</summary>
    public sealed record NodeAlias(ExpandedName Element, string Name);

    /// <summary>A layout for an attribute of an element, or of any element when Element is null.</summary>
    public sealed record LayoutRule(ExpandedName? Element, ExpandedName Attribute, Layout Layout);

    private readonly List<Alias> _aliases = [];
    private readonly List<NodeAlias> _nodes = [];
    private readonly List<LayoutRule> _layouts = [];

    private SmlProfile()
    {
    }

    public IReadOnlyList<Alias> Aliases => _aliases;

    public IReadOnlyList<NodeAlias> Nodes => _nodes;

    public IReadOnlyList<LayoutRule> Layouts => _layouts;

    /// <summary>Parses a profile; throws <see cref="SmlException"/> with the line number of an error.</summary>
    public static SmlProfile Parse(string text)
    {
        if (text.StartsWith('﻿'))
            text = text[1..];
        var profile = new SmlProfile();
        var ns = new Dictionary<string, string>(StringComparer.Ordinal) { ["xml"] = XmlRules.XmlNamespace };
        bool namespacePhase = true;
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            int ln = index + 1;
            string line = XmlRules.CheckChars(XmlRules.StripSpacesAndTabs(lines[index]), ln);
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] t = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            switch (t[0])
            {
                case "@ns":
                    if (!namespacePhase)
                        throw new SmlException("@ns must come before every other statement", ln);
                    profile.Namespace(t, ns, ln);
                    break;
                case "@block":
                    namespacePhase = false;
                    profile.Block(t, ns, ln);
                    break;
                case "@node":
                    namespacePhase = false;
                    profile.Node(t, ns, ln);
                    break;
                case "@layout":
                    namespacePhase = false;
                    profile.LayoutStatement(t, ns, ln);
                    break;
                default:
                    throw new SmlException($"unknown profile statement '{t[0]}'; expected @ns, @block, @node, or @layout", ln);
            }
        }
        return profile;
    }

    /// <summary>The layout for an attribute of an element: the element's own, else the one for any element.</summary>
    public Layout? FindLayout(ExpandedName element, ExpandedName attribute) =>
        (_layouts.Find(r => r.Element == element && r.Attribute == attribute)
            ?? _layouts.Find(r => r.Element is null && r.Attribute == attribute))?.Layout;

    /// <summary>The alias for an attribute of an element, if the profile declares one.</summary>
    public Alias? FindAlias(ExpandedName element, ExpandedName attribute) =>
        _aliases.Find(a => a.Element == element && a.Attribute == attribute);

    private void Namespace(string[] t, Dictionary<string, string> ns, int ln)
    {
        if (t.Length is not (2 or 3))
            throw new SmlException("@ns takes one or two operands: [prefix] URI", ln);
        string prefix = t.Length == 3 ? t[1] : "", uri = t[^1];
        XmlRules.CheckNamespaceDeclaration(prefix, uri, ln);
        if (prefix == "xml" || !ns.TryAdd(prefix, uri))
        {
            string what = prefix.Length == 0 ? "the default namespace" : $"prefix '{prefix}'";
            throw new SmlException($"{what} is declared twice", ln);
        }
    }

    private static ExpandedName AttributeName(string operand, Dictionary<string, string> ns, int ln)
    {
        if (!operand.StartsWith('.'))
            throw new SmlException($"expected an attribute name starting with '.', found '{operand}'", ln);
        string name = operand[1..];
        if (name == "xmlns" || name.StartsWith("xmlns:", StringComparison.Ordinal))
            throw new SmlException("a profile cannot name a namespace declaration", ln);
        return XmlRules.Resolve(name, ns, attribute: true, ln);
    }

    private void Block(string[] t, Dictionary<string, string> ns, int ln)
    {
        if (t.Length != 4)
            throw new SmlException("@block takes: element-qname alias .attribute-qname", ln);
        ExpandedName element = XmlRules.Resolve(t[1], ns, attribute: false, ln);
        string alias = t[2];
        if (!XmlRules.IsNCName(alias) || alias == "end")
            throw new SmlException($"invalid alias name '{alias}'", ln);
        ExpandedName attribute = AttributeName(t[3], ns, ln);
        if (_aliases.Exists(a => a.Element == element && a.Name == alias))
            throw new SmlException($"alias '{alias}' is declared twice for element '{t[1]}'", ln);
        if (_aliases.Exists(a => a.Element == element && a.Attribute == attribute))
            throw new SmlException($"attribute '{t[3][1..]}' of element '{t[1]}' already has an alias", ln);
        _aliases.Add(new Alias(element, alias, attribute));
    }

    private void Node(string[] t, Dictionary<string, string> ns, int ln)
    {
        if (t.Length != 3)
            throw new SmlException("@node takes: element-qname alias", ln);
        ExpandedName element = XmlRules.Resolve(t[1], ns, attribute: false, ln);
        string alias = t[2];
        if (!XmlRules.IsNCName(alias) || alias == "end")
            throw new SmlException($"invalid element alias '{alias}'", ln);
        if (_nodes.Exists(n => n.Name == alias))
            throw new SmlException($"element alias '{alias}' is declared twice", ln);
        if (_nodes.Exists(n => n.Element == element))
            throw new SmlException($"element '{t[1]}' already has an alias", ln);
        _nodes.Add(new NodeAlias(element, alias));
    }

    private void LayoutStatement(string[] t, Dictionary<string, string> ns, int ln)
    {
        if (t.Length < 4)
            throw new SmlException("@layout takes: element-qname .attribute-qname kind", ln);
        ExpandedName? element = t[1] == "*" ? null : XmlRules.Resolve(t[1], ns, attribute: false, ln);
        ExpandedName attribute = AttributeName(t[2], ns, ln);
        Layout layout = (t[3], t.Length) switch
        {
            ("path", 4) => new Layout(LayoutKind.Path),
            ("declarations", 4) => new Layout(LayoutKind.Declarations),
            ("functions", 4) => new Layout(LayoutKind.Functions),
            ("list", 4) => new Layout(LayoutKind.List),
            ("numbers", 5) when int.TryParse(t[4], out int n) && n is >= 1 and <= 1000 && t[4] == n.ToString() =>
                new Layout(LayoutKind.Numbers, n),
            ("numbers", _) => throw new SmlException("the numbers layout takes a count from 1 to 1000: numbers N", ln),
            _ => throw new SmlException(
                $"unknown layout '{string.Join(' ', t[3..])}'; expected path, numbers N, declarations, functions, or list", ln),
        };
        if (_layouts.Exists(r => r.Element == element && r.Attribute == attribute))
            throw new SmlException($"attribute '{t[2][1..]}' of element '{t[1]}' already has a layout", ln);
        _layouts.Add(new LayoutRule(element, attribute, layout));
    }
}
