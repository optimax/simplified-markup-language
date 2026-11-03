using System.Text;
using System.Text.RegularExpressions;

namespace Sml;

/// <summary>Reads SML 1.0-SVG-4 (specification §§4–13) into the information model.</summary>
public sealed partial class SmlReader
{
    public const string Version = "1.0-SVG-4";

    /// <summary>Whether s is a block marker (§7): three or more quotation marks and nothing else.</summary>
    public static bool IsBlockMarker(string s) => s.Length >= 3 && s.All(c => c == '"');

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly string[] _lines;
    private readonly SmlDocument _doc = new();
    private readonly List<Frame> _stack = [];
    private readonly HashSet<string> _seenHeaders = new(StringComparer.Ordinal);
    private readonly HashSet<string> _optionalExtensions = new(StringComparer.Ordinal);
    private int _index;
    private string _phase = "prolog";
    private string? _indent;
    private readonly Dictionary<string, string> _nodes = new(StringComparer.Ordinal);
    private bool _seenStatement;
    private Pending? _pending;

    private SmlReader(string source)
    {
        if (source.StartsWith('﻿'))
            source = source[1..];
        _lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
    }

    public static SmlDocument Read(string source) => new SmlReader(source).Parse();

    public static SmlDocument Read(byte[] utf8)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(utf8);
        }
        catch (DecoderFallbackException e)
        {
            throw new SmlException("invalid UTF-8: " + e.Message);
        }
        return Read(text);
    }

    private enum ValueKind { Empty, Raw, Quoted, Block }

    private sealed class Frame(ElementNode element, string lexical, IReadOnlyDictionary<string, string> parentNs, int depth, int line)
    {
        public ElementNode Element { get; } = element;
        public string Lexical { get; } = lexical;
        public IReadOnlyDictionary<string, string> ParentNs { get; } = parentNs;
        public Dictionary<string, string> Ns { get; set; } = null!;
        public int Depth { get; } = depth;
        public int Line { get; } = line;
        public bool DeclarationOpen { get; set; } = true;
        public bool NamespacePhase { get; set; } = true;
        public List<(string Name, string Value, int Line)> InlineAttributes { get; } = [];
        public Dictionary<(ExpandedName Element, string Alias), string> Aliases { get; } = [];

        /// <summary>Element aliases declared in the element's declaration section, for its content (§8.7).</summary>
        public Dictionary<string, string> Nodes { get; } = new(StringComparer.Ordinal);

        /// <summary>The element alias the element was opened with, if any.</summary>
        public string? Alias { get; set; }

        public bool NoIndent { get; set; }
        public bool? LaidOut { get; set; }

        /// <summary>The element's own indentation (§6.4).</summary>
        public string Indentation { get; set; } = "";

        /// <summary>How much deeper its children are: the @indent unit, or the element's own @indent.</summary>
        public string? Step { get; set; }

        public bool LocalStep { get; set; }
    }

    /// <summary>A value that continuation lines (§6.5) may still extend.</summary>
    private sealed record Pending(Action<string> Append, Action? Finish = null);

    private static readonly Dictionary<string, string> BaseNamespaces = new(StringComparer.Ordinal) { ["xml"] = XmlRules.XmlNamespace };

    [GeneratedRegex(@"\G\{([0-9A-Fa-f]{1,6})\}")]
    private static partial Regex UnicodeEscape();

    [GeneratedRegex(@"\G@([^\s|""]+)")]
    private static partial Regex Directive();

    [GeneratedRegex(@"^[ \t]+([^ \t]+)(.*)$")]
    private static partial Regex PiOperands();

    // ---------------------------------------------------------------------------------------------
    // Driver

    private SmlDocument Parse()
    {
        while (_index < _lines.Length)
        {
            string raw = _lines[_index++];
            int ln = _index;
            string s = XmlRules.StripSpacesAndTabs(raw);
            if (_pending is not null)
            {
                if (s.StartsWith('"'))
                {
                    _pending.Append(ParseQuotedWhole(s, ln));
                    continue;
                }
                FinishPending();
            }
            if (s.Length == 0 || s[0] == '#')
                continue;
            if (s[0] == '"')
                throw new SmlException("continuation line without a preceding quoted value", ln);
            Statement(s, ln);
            _seenStatement = true;
        }
        FinishPending();
        if (_stack.Count > 0)
            throw new SmlException($"element '{_stack[^1].Lexical}' is not closed", _stack[^1].Line);
        if (_doc.Root is null)
            throw new SmlException("the document has no root element");
        return _doc;
    }

    private void FinishPending()
    {
        Pending? p = _pending;
        _pending = null;
        p?.Finish?.Invoke();
    }

    private void Statement(string s, int ln)
    {
        if (s == "end" || (s.StartsWith("end", StringComparison.Ordinal) && s.Length > 3 && s[3] is ' ' or '\t'))
        {
            End(s, ln);
            return;
        }
        switch (s[0])
        {
            case '.':
                Attribute(s, ln);
                return;
            case '=':
                Text(s, ln);
                return;
            case '@':
                DirectiveStatement(s, ln);
                return;
        }
        int nameEnd = XmlRules.ScanNCName(s, 0);
        if (nameEnd > 0)
        {
            int j = nameEnd;
            while (j < s.Length && s[j] is ' ' or '\t')
                j++;
            if (j < s.Length && IsBlockMarker(s[j..]))
            {
                AliasInvocation(s[..nameEnd], s[j..], ln);
                return;
            }
        }
        ElementStatement(s, ln);
    }

    // ---------------------------------------------------------------------------------------------
    // Values

    private static (string Value, int End) ParseQuoted(string s, int pos, int ln)
    {
        var sb = new StringBuilder();
        int i = pos + 1, n = s.Length;
        while (i < n)
        {
            char c = s[i];
            if (c == '"')
                return (sb.ToString(), i + 1);
            if (c == '\\')
            {
                char e = i + 1 < n ? s[i + 1] : '\0';
                string? simple = e switch { '\\' => "\\", '"' => "\"", 'n' => "\n", 'r' => "\r", 't' => "\t", _ => null };
                if (simple is not null)
                {
                    sb.Append(simple);
                    i += 2;
                    continue;
                }
                if (e == 'u')
                {
                    Match m = UnicodeEscape().Match(s, i + 2);
                    if (!m.Success)
                        throw new SmlException("malformed \\u{…} escape (1 to 6 hex digits)", ln);
                    int cp = Convert.ToInt32(m.Groups[1].Value, 16);
                    if (cp > 0x10FFFF || cp is >= 0xD800 and <= 0xDFFF)
                        throw new SmlException($"\\u{{{m.Groups[1].Value}}} is not a Unicode scalar value", ln);
                    if (!XmlRules.IsXmlChar(cp))
                        throw new SmlException($"\\u{{{m.Groups[1].Value}}} is not allowed in XML", ln);
                    sb.Append(char.ConvertFromUtf32(cp));
                    i = m.Index + m.Length;
                    continue;
                }
                throw new SmlException($"unknown escape \\{(e == '\0' ? "" : e.ToString())}", ln);
            }
            if (char.IsHighSurrogate(c) && i + 1 < n && char.IsLowSurrogate(s[i + 1]))
            {
                sb.Append(c).Append(s[i + 1]);
                i += 2;
                continue;
            }
            if (!XmlRules.IsXmlChar(char.IsSurrogate(c) ? -1 : c))
                throw new SmlException($"character U+{(int)c:X4} is not allowed in XML", ln);
            sb.Append(c);
            i++;
        }
        throw new SmlException("unterminated quoted string", ln);
    }

    private static string ParseQuotedWhole(string s, int ln)
    {
        (string value, int end) = ParseQuoted(s, 0, ln);
        if (end != s.Length)
            throw new SmlException("unexpected characters after the closing quote", ln);
        return value;
    }

    /// <summary>Parses the value part of a statement. For a block, the returned value is its marker.</summary>
    private static (ValueKind Kind, string Value) ParseValueTail(string rest, int ln, bool allowBlock = true)
    {
        if (rest.Length == 0)
            return (ValueKind.Empty, "");
        string v;
        if (rest[0] is ' ' or '\t')
            v = rest.TrimStart(' ', '\t');
        else if (IsBlockMarker(rest))
            v = rest;       // the space before a block marker is optional (§6.2)
        else
            throw new SmlException("expected a space before the value", ln);
        if (IsBlockMarker(v))
        {
            if (!allowBlock)
                throw new SmlException("this value cannot be a block", ln);
            return (ValueKind.Block, v);
        }
        if (v[0] == '"')
            return (ValueKind.Quoted, ParseQuotedWhole(v, ln));
        return (ValueKind.Raw, XmlRules.CheckChars(v, ln));
    }

    /// <summary>Reads a block's payload up to the line that repeats its marker (§7).</summary>
    private string ReadBlock(string marker, int ln)
    {
        var payload = new List<string>();
        while (_index < _lines.Length)
        {
            string raw = _lines[_index++];
            if (XmlRules.StripSpacesAndTabs(raw) == marker)
                return XmlRules.CheckChars(DedentBlock(payload), ln);
            payload.Add(raw);
        }
        throw new SmlException($"unterminated block (expected a line {marker})", ln);
    }

    private string ValueOf(ValueKind kind, string value, int ln) => kind == ValueKind.Block ? ReadBlock(value, ln) : value;

    /// <summary>The §7 dedentation algorithm.</summary>
    public static string DedentBlock(IReadOnlyList<string> lines)
    {
        var trimmed = lines.Select(l => l.TrimEnd(' ', '\t')).ToList();
        string? prefix = null;
        foreach (string line in trimmed)
        {
            if (line.Length == 0)
                continue;
            string lead = line[..(line.Length - line.TrimStart(' ', '\t').Length)];
            if (prefix is null)
            {
                prefix = lead;
                continue;
            }
            int k = 0;
            while (k < prefix.Length && k < lead.Length && prefix[k] == lead[k])
                k++;
            prefix = prefix[..k];
        }
        int n = prefix?.Length ?? 0;
        return string.Join('\n', trimmed.Select(l => l.Length == 0 ? "" : l[n..]));
    }

    /// <summary>Space-separated operands, each raw (no spaces) or quoted.</summary>
    private static List<(string Value, bool Quoted)> Tokens(string rest, int ln)
    {
        var result = new List<(string, bool)>();
        int i = 0, n = rest.Length;
        while (true)
        {
            while (i < n && rest[i] is ' ' or '\t')
                i++;
            if (i >= n)
                return result;
            if (rest[i] == '"')
            {
                (string value, int end) = ParseQuoted(rest, i, ln);
                result.Add((value, true));
                i = end;
            }
            else
            {
                int j = i;
                while (j < n && rest[j] is not (' ' or '\t'))
                    j++;
                result.Add((XmlRules.CheckChars(rest[i..j], ln), false));
                i = j;
            }
            if (i < n && rest[i] is not (' ' or '\t'))
                throw new SmlException("operands must be separated by spaces", ln);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Frames

    private void FixNamespaces(Frame f)
    {
        if (!f.NamespacePhase)
            return;
        var ns = new Dictionary<string, string>(f.ParentNs, StringComparer.Ordinal);
        foreach (var (prefix, uri) in f.Element.NamespaceDeclarations)
            ns[prefix] = uri;
        f.Ns = ns;
        f.NamespacePhase = false;
        f.Element.Name = XmlRules.Resolve(f.Lexical, ns, attribute: false, f.Line);
        foreach (var (name, value, line) in f.InlineAttributes)
            SetAttribute(f, name, value, line);
    }

    private void SetAttribute(Frame f, string qname, string value, int ln)
    {
        if (qname == "xmlns" || qname.StartsWith("xmlns:", StringComparison.Ordinal))
            throw new SmlException("'xmlns' declarations are written with @ns or inline, not as attributes", ln);
        ExpandedName expanded = XmlRules.Resolve(qname, f.Ns, attribute: true, ln);
        if (f.Element.AttributeNames.ContainsValue(expanded) || f.Element.Attributes.ContainsKey(qname))
            throw new SmlException($"duplicate attribute '{qname}'", ln);
        f.Element.Attributes.Add(qname, value);
        f.Element.AttributeNames.Add(qname, expanded);
    }

    private static void AddNamespaceDeclaration(Frame f, string prefix, string uri, bool quoted, int ln)
    {
        if (!quoted && !uri.Contains(':'))
            throw new SmlException($"a raw namespace URI must contain \":\"; quote '{uri}' if it is relative", ln);
        XmlRules.CheckNamespaceDeclaration(prefix, uri, ln);
        if (f.Element.NamespaceDeclarations.ContainsKey(prefix))
        {
            string what = prefix.Length == 0 ? "the default namespace" : $"prefix '{prefix}'";
            throw new SmlException($"{what} is declared twice on this element", ln);
        }
        f.Element.NamespaceDeclarations.Add(prefix, uri);
    }

    private Frame DeclarationFrame(int ln, string what)
    {
        if (_stack.Count == 0)
            throw new SmlException($"{what} outside an element", ln);
        Frame f = _stack[^1];
        if (!f.DeclarationOpen)
            throw new SmlException($"{what} after the element's content has begun", ln);
        FixNamespaces(f);
        return f;
    }

    private void CloseDeclarations(Frame f, bool isText)
    {
        if (!f.DeclarationOpen)
            return;
        f.DeclarationOpen = false;
        FixNamespaces(f);
        f.LaidOut = _indent is not null && !f.NoIndent && !isText;
    }

    /// <summary>Adds content to the current element, or to the prolog/epilog for comments and PIs.</summary>
    private void AddChild(SmlNode node, bool isText, int ln)
    {
        if (_stack.Count == 0)
        {
            if (isText || node is ElementNode)
            {
                if (node is ElementNode root && _phase == "prolog")
                {
                    _doc.Root = root;
                    _phase = "root";
                    return;
                }
                throw new SmlException("content outside the root element", ln);
            }
            (_phase == "prolog" ? _doc.Prolog : _doc.Epilog).Add(node);
            return;
        }
        Frame f = _stack[^1];
        CloseDeclarations(f, isText);
        if (isText && f.LaidOut == true)
            throw new SmlException("text in a laid-out element (§6.4); mark the element @noindent", ln);
        if (f.LaidOut == true)
            f.Element.Children.Add(new TextNode("\n" + f.Indentation + f.Step));
        f.Element.Children.Add(node);
    }


    private Frame OpenElement(string qname, List<(string Name, string Value, bool Quoted)> inline, string kind, int ln)
    {
        var element = new ElementNode(qname);
        IReadOnlyDictionary<string, string> parentNs = BaseNamespaces;
        if (_stack.Count > 0)
        {
            CloseDeclarations(_stack[^1], isText: false);
            parentNs = _stack[^1].Ns;
        }
        var f = new Frame(element, qname, parentNs, _stack.Count, ln);
        if (_stack.Count > 0)
            f.Indentation = _stack[^1].Indentation + (_stack[^1].Step ?? "");
        f.Step = _indent;
        foreach (var (name, value, quoted) in inline)
        {
            if (name == "xmlns")
                AddNamespaceDeclaration(f, "", value, quoted, ln);
            else if (name.StartsWith("xmlns:", StringComparison.Ordinal))
                AddNamespaceDeclaration(f, name[6..], value, quoted, ln);
            else
                f.InlineAttributes.Add((name, value, ln));
        }
        AddChild(element, isText: false, ln);
        if (kind == "open")
            _stack.Add(f);
        else
            FixNamespaces(f);
        return f;
    }

    // ---------------------------------------------------------------------------------------------
    // Statements

    private void End(string s, int ln)
    {
        if (_stack.Count == 0)
            throw new SmlException("'end' without an open element", ln);
        Frame f = _stack[^1];
        if (s != "end")
        {
            string name = s[3..].TrimStart(' ', '\t');
            if (!XmlRules.IsQName(name))
                throw new SmlException("'end' must be followed by nothing or a single element name", ln);
            if (name != f.Lexical && name != f.Alias)
                throw new SmlException($"end {name} does not match the open element '{f.Lexical}'", ln);
        }
        CloseDeclarations(f, isText: true);
        if (f.LaidOut == true)
            f.Element.Children.Add(new TextNode("\n" + f.Indentation));
        _stack.RemoveAt(_stack.Count - 1);
        if (_stack.Count == 0)
            _phase = "epilog";
    }

    private void Attribute(string s, int ln)
    {
        int end = XmlRules.ScanQName(s, 1);
        if (end == 1)
            throw new SmlException("expected an attribute name after \".\"", ln);
        string qname = s[1..end];
        Frame f = DeclarationFrame(ln, "attribute");
        var (kind, value) = ParseValueTail(s[end..], ln);
        SetAttribute(f, qname, ValueOf(kind, value, ln), ln);
        if (kind == ValueKind.Quoted)
            _pending = new Pending(more => f.Element.Attributes[qname] += more);
    }

    private void Text(string s, int ln)
    {
        if (_stack.Count == 0)
            throw new SmlException("text outside the root element", ln);
        var (kind, value) = ParseValueTail(s[1..], ln);
        var node = new TextNode(ValueOf(kind, value, ln));
        AddChild(node, isText: true, ln);
        if (kind == ValueKind.Quoted)
            _pending = new Pending(more => node.Data += more);
    }

    private void AliasInvocation(string alias, string marker, int ln)
    {
        Frame f = DeclarationFrame(ln, "alias invocation");
        string? target = null;
        for (int k = _stack.Count - 1; k >= 0 && target is null; k--)
            _stack[k].Aliases.TryGetValue((f.Element.Name, alias), out target);
        if (target is null)
            throw new SmlException($"no active alias '{alias}' for element '{f.Lexical}'", ln);
        SetAttribute(f, target, ReadBlock(marker, ln), ln);
    }

    private void ElementStatement(string s, int ln)
    {
        int pos = XmlRules.ScanQName(s, 0);
        if (pos == 0)
            throw new SmlException($"unrecognized statement '{s}'", ln);
        string qname = s[..pos];
        if (qname == "end")
            throw new SmlException("the element name 'end' is reserved; use '@element end'", ln);
        string? alias = null;
        if (NodeTarget(qname) is string target)
            (alias, qname) = (qname, target);
        var inline = new List<(string, string, bool)>();
        if (pos < s.Length && s[pos] == '(')
            (inline, pos) = ParseInlineList(s, pos, ln);
        string rest = s[pos..];
        if (rest.Length == 0)
        {
            OpenElement(qname, inline, "open", ln).Alias = alias;
            return;
        }
        if (rest == "/")
        {
            OpenElement(qname, inline, "empty", ln);
            return;
        }
        int k = 0;
        while (k < rest.Length && rest[k] is ' ' or '\t')
            k++;
        if (k == 0 || k >= rest.Length || rest[k] != '=')
            throw new SmlException($"unexpected '{rest}' after element name '{qname}'", ln);
        Frame f = OpenElement(qname, inline, "text", ln);
        var (kind, value) = ParseValueTail(rest[(k + 1)..], ln);
        if (kind == ValueKind.Empty)
            return;
        var node = new TextNode(ValueOf(kind, value, ln));
        if (kind == ValueKind.Block && _indent is not null && (_stack.Count == 0 || _stack[^1].LaidOut == true))
            node.Data = LayOutBlock(node.Data, f.Indentation, _indent);
        f.Element.Children.Add(node);
        if (kind == ValueKind.Quoted)
            _pending = new Pending(more => node.Data += more);
    }

    private static (List<(string Name, string Value, bool Quoted)> Entries, int End) ParseInlineList(string s, int pos, int ln)
    {
        var entries = new List<(string, string, bool)>();
        int i = pos + 1, n = s.Length;
        int Skip(int at)
        {
            while (at < n && s[at] is ' ' or '\t')
                at++;
            return at;
        }
        i = Skip(i);
        if (i < n && s[i] == ')')
            return (entries, i + 1);
        while (true)
        {
            int nameEnd = XmlRules.ScanQName(s, i);
            if (nameEnd == i)
                throw new SmlException("expected an attribute name in the inline list", ln);
            string name = s[i..nameEnd];
            i = Skip(nameEnd);
            if (i >= n || s[i] != '=')
                throw new SmlException($"expected '=' after inline attribute '{name}'", ln);
            i = Skip(i + 1);
            string value;
            bool quoted;
            if (i < n && s[i] == '"')
            {
                (value, i) = ParseQuoted(s, i, ln);
                quoted = true;
            }
            else
            {
                int start = i, depth = 0;
                for (; i < n; i++)
                {
                    char c = s[i];
                    if (c == '(')
                        depth++;
                    else if (c == ')' && depth-- == 0)
                        break;
                    else if (c == ',' && depth == 0)
                        break;
                }
                if (i >= n)
                    throw new SmlException("unclosed inline list or unbalanced parenthesis", ln);
                value = XmlRules.CheckChars(s[start..i].TrimEnd(' ', '\t'), ln);
                quoted = false;
            }
            entries.Add((name, value, quoted));
            i = Skip(i);
            if (i < n && s[i] == ',')
            {
                i = Skip(i + 1);
                continue;
            }
            if (i < n && s[i] == ')')
                return (entries, i + 1);
            throw new SmlException("expected ',' or ')' in the inline list", ln);
        }
    }

    private void DirectiveStatement(string s, int ln)
    {
        Match m = Directive().Match(s);
        if (!m.Success)
            throw new SmlException($"malformed directive '{s}'", ln);
        string name = m.Groups[1].Value;
        string rest = s[m.Length..];
        if (rest.Length > 0 && rest[0] is not (' ' or '\t') && !IsBlockMarker(rest))
            throw new SmlException($"expected a space after @{name}", ln);
        if (name.Contains(':'))
        {
            Extension(name, rest, ln);
            return;
        }
        switch (name)
        {
            case "sml": Sml(rest, ln); break;
            case "standalone": Standalone(rest, ln); break;
            case "indent": Indent(rest, ln); break;
            case "doctype": Doctype(rest, ln); break;
            case "use": Use(rest, ln); break;
            case "ns": Namespace(rest, ln); break;
            case "block": BlockDeclaration(rest, ln); break;
            case "node": NodeDeclaration(rest, ln); break;
            case "noindent": NoIndent(rest, ln); break;
            case "comment": Comment(rest, ln); break;
            case "comments": Comments(rest, ln); break;
            case "pi": ProcessingInstruction(rest, ln); break;
            case "element": ExplicitElement(rest, ln); break;
            default: throw new SmlException($"unknown directive @{name}", ln);
        }
    }

    private void Header(string name, int ln)
    {
        if (_phase != "prolog")
            throw new SmlException($"@{name} is only allowed before the root element", ln);
        if (!_seenHeaders.Add(name) && name != "use")
            throw new SmlException($"@{name} may appear only once", ln);
    }

    private void Sml(string rest, int ln)
    {
        if (_seenStatement)
            throw new SmlException("@sml must be the first statement", ln);
        Header("sml", ln);
        string version = XmlRules.StripSpacesAndTabs(rest);
        if (version is Version or "1.0")
            return;
        if (version.StartsWith("1.0-SVG-", StringComparison.Ordinal))
            throw new SmlException($"unsupported draft revision {version}; this reader implements {Version}", ln);
        throw new SmlException($"unsupported SML version '{version}'", ln);
    }

    private void Standalone(string rest, int ln)
    {
        Header("standalone", ln);
        string value = XmlRules.StripSpacesAndTabs(rest);
        if (value is not ("yes" or "no") || rest.Length == 0 || rest[0] is not (' ' or '\t'))
            throw new SmlException("@standalone takes yes or no", ln);
        _doc.Standalone = value;
    }

    private void Indent(string rest, int ln)
    {
        string value = XmlRules.StripSpacesAndTabs(rest);
        string unit;
        if (value == "tab")
            unit = "\t";
        else if (value == "0" || (value.Length is 1 or 2 && value[0] is >= '1' and <= '9' && value.All(char.IsAsciiDigit)
                                  && int.Parse(value) <= 16))
            unit = new string(' ', int.Parse(value));
        else
            throw new SmlException("@indent takes tab or a width from 0 to 16", ln);
        if (_stack.Count > 0)
        {
            // In an element: how much deeper its children are (§6.4).
            Frame f = DeclarationFrame(ln, "@indent");
            if (_indent is null)
                throw new SmlException("@indent in an element needs the @indent header", ln);
            if (f.LocalStep)
                throw new SmlException("@indent may appear only once per element", ln);
            (f.Step, f.LocalStep) = (unit, true);
            return;
        }
        Header("indent", ln);
        _indent = unit;
    }

    private void Doctype(string rest, int ln)
    {
        Header("doctype", ln);
        var t = Tokens(rest, ln);
        if (t.Count == 0 || t[0].Quoted)
            throw new SmlException("@doctype needs a name", ln);
        DoctypeNode dt;
        if (t.Count == 1)
            dt = new DoctypeNode(t[0].Value, null, null);
        else if (t.Count == 4 && t[1] is ("PUBLIC", false) && t[2].Quoted && t[3].Quoted)
            dt = new DoctypeNode(t[0].Value, t[2].Value, t[3].Value);
        else if (t.Count == 3 && t[1] is ("SYSTEM", false) && t[2].Quoted)
            dt = new DoctypeNode(t[0].Value, null, t[2].Value);
        else
            throw new SmlException("@doctype takes: name [PUBLIC \"pubid\" \"sysid\" | SYSTEM \"sysid\"]", ln);
        XmlRules.CheckDoctype(dt, ln);
        _doc.Prolog.Add(dt);
    }

    private void Use(string rest, int ln)
    {
        Header("use", ln);
        var t = Tokens(rest, ln).Select(x => x.Value).ToList();
        bool versionOk = t.Count >= 2 && t[1].Split('.') is [var major, var minor]
            && major.Length > 0 && minor.Length > 0 && major.All(char.IsAsciiDigit) && minor.All(char.IsAsciiDigit);
        if (t.Count is not (2 or 3) || !XmlRules.IsNCName(t[0]) || !versionOk)
            throw new SmlException("@use takes: identifier major.minor [optional]", ln);
        if (t.Count == 3 && t[2] != "optional")
            throw new SmlException("the third @use operand can only be 'optional'", ln);
        if (_optionalExtensions.Contains(t[0]))
            throw new SmlException($"extension '{t[0]}' is declared twice", ln);
        if (t.Count == 2)
            throw new SmlException($"required extension '{t[0]}' is not implemented by this reader", ln);
        _optionalExtensions.Add(t[0]);
    }

    private void Extension(string name, string rest, int ln)
    {
        string ident = name[..name.IndexOf(':')];
        if (!_optionalExtensions.Contains(ident))
            throw new SmlException($"directive @{name} belongs to an undeclared extension", ln);
        var (kind, value) = ParseValueTail(rest, ln);
        ValueOf(kind, value, ln);       // optional extension: read and skip
        if (kind == ValueKind.Quoted)
            _pending = new Pending(_ => { });
    }

    private void Namespace(string rest, int ln)
    {
        if (_stack.Count == 0)
            throw new SmlException("@ns outside an element", ln);
        Frame f = _stack[^1];
        if (!f.DeclarationOpen)
            throw new SmlException("@ns after the element's content has begun", ln);
        if (!f.NamespacePhase)
            throw new SmlException("@ns must come before every other declaration statement (§5.1)", ln);
        var t = Tokens(rest, ln);
        if (t.Count == 1)
            AddNamespaceDeclaration(f, "", t[0].Value, t[0].Quoted, ln);
        else if (t.Count == 2 && !t[0].Quoted)
            AddNamespaceDeclaration(f, t[0].Value, t[1].Value, t[1].Quoted, ln);
        else
            throw new SmlException("@ns takes one or two operands: [prefix] URI", ln);
    }

    private void BlockDeclaration(string rest, int ln)
    {
        Frame f = DeclarationFrame(ln, "@block");
        string[] t = XmlRules.StripSpacesAndTabs(rest).Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (t.Length != 3 || !t[1].StartsWith('.'))
            throw new SmlException("@block takes: element-qname .attribute-qname alias", ln);
        string attribute = t[1][1..], alias = t[2];
        ExpandedName element = XmlRules.Resolve(t[0], f.Ns, attribute: false, ln);
        if (!XmlRules.IsNCName(alias) || alias == "end")
            throw new SmlException($"invalid alias name '{alias}'", ln);
        if (attribute == "xmlns" || attribute.StartsWith("xmlns:", StringComparison.Ordinal))
            throw new SmlException("an alias cannot target a namespace declaration", ln);
        XmlRules.Resolve(attribute, f.Ns, attribute: true, ln);
        if (!f.Aliases.TryAdd((element, alias), attribute))
            throw new SmlException($"alias '{alias}' is declared twice for this element in one scope", ln);
    }

    /// <summary>The element name that an element alias in scope gives a name (§8.7), or null. The nearest scope
    /// wins.</summary>
    private string? NodeTarget(string name)
    {
        for (int k = _stack.Count - 1; k >= 0; k--)
        {
            if (_stack[k].Nodes.TryGetValue(name, out string? target))
                return target;
        }
        return _nodes.GetValueOrDefault(name);
    }

    private void NodeDeclaration(string rest, int ln)
    {
        string[] t = XmlRules.StripSpacesAndTabs(rest).Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (t.Length != 2)
            throw new SmlException("@node takes: element-qname alias", ln);
        if (!XmlRules.IsQName(t[0]))
            throw new SmlException($"invalid element name '{t[0]}' in @node", ln);
        if (!XmlRules.IsNCName(t[1]) || t[1] == "end")
            throw new SmlException($"invalid element alias '{t[1]}'", ln);
        Dictionary<string, string> scope;
        if (_stack.Count > 0)
            scope = DeclarationFrame(ln, "@node").Nodes;
        else if (_phase == "prolog")
            scope = _nodes;
        else
            throw new SmlException("@node is only allowed before the root element or in a declaration section", ln);
        if (!scope.TryAdd(t[1], t[0]))
            throw new SmlException($"element alias '{t[1]}' is declared twice in one scope", ln);
    }

    private void NoIndent(string rest, int ln)
    {
        Frame f = DeclarationFrame(ln, "@noindent");
        if (XmlRules.StripSpacesAndTabs(rest).Length > 0)
            throw new SmlException("@noindent takes no operands", ln);
        if (f.NoIndent)
            throw new SmlException("@noindent may appear only once per element", ln);
        f.NoIndent = true;
    }

    /// <summary>The data of a comment block, or of the block of a compact text element, in a laid-out position
    /// (§6.4): LF, each payload line indented one unit deeper than the comment or element (empty lines stay empty),
    /// LF, and the comment's or element's own indentation.</summary>
    public static string LayOutBlock(string value, string indentation, string unit)
    {
        if (value.Length == 0)
            return "\n" + indentation;
        string inner = indentation + unit;
        return "\n" + string.Join('\n', value.Split('\n').Select(line => line.Length == 0 ? "" : inner + line))
            + "\n" + indentation;
    }

    /// <summary>The indentation of a comment just added, if its position is laid out (§6.4), else null.</summary>
    private string? LaidOutIndentation()
    {
        if (_indent is null)
            return null;
        if (_stack.Count == 0)
            return "";
        Frame f = _stack[^1];
        return f.LaidOut == true ? f.Indentation + f.Step : null;
    }

    private void Comment(string rest, int ln)
    {
        var (kind, value) = ParseValueTail(rest, ln);
        var node = new CommentNode(ValueOf(kind, value, ln));
        AddChild(node, isText: false, ln);
        if (kind == ValueKind.Block && LaidOutIndentation() is { } indentation)
            node.Data = LayOutBlock(node.Data, indentation, _indent!);
        if (kind == ValueKind.Quoted)
            _pending = new Pending(more => node.Data += more, () => XmlRules.CheckComment(node.Data, ln));
        else
            XmlRules.CheckComment(node.Data, ln);
    }

    private void Comments(string rest, int ln)
    {
        var (kind, value) = ParseValueTail(rest, ln);
        if (kind != ValueKind.Block)
            throw new SmlException("@comments takes a block: @comments \"\"\", one comment per line", ln);
        string data = ReadBlock(value, ln);
        string[] lines = data.Split('\n');
        if (data.Length == 0 || lines.Contains(""))
            throw new SmlException("@comments cannot have blank lines; each line is one comment", ln);
        foreach (string line in lines)
        {
            XmlRules.CheckComment(line, ln);
            AddChild(new CommentNode(line), isText: false, ln);
        }
    }

    private void ProcessingInstruction(string rest, int ln)
    {
        Match m = PiOperands().Match(rest);
        if (!m.Success)
            throw new SmlException("@pi needs a target", ln);
        string target = m.Groups[1].Value;
        var (kind, value) = ParseValueTail(m.Groups[2].Value, ln, allowBlock: false);
        var node = new ProcessingInstructionNode(target, value);
        AddChild(node, isText: false, ln);
        if (kind == ValueKind.Quoted)
            _pending = new Pending(more => node.Data += more, () => XmlRules.CheckProcessingInstruction(node.Target, node.Data, ln));
        else
            XmlRules.CheckProcessingInstruction(target, value, ln);
    }

    private void ExplicitElement(string rest, int ln)
    {
        string qname = XmlRules.StripSpacesAndTabs(rest);
        if (rest.Length == 0 || rest[0] is not (' ' or '\t') || !XmlRules.IsQName(qname))
            throw new SmlException("@element takes a single element name", ln);
        OpenElement(qname, [], "open", ln);
    }
}
