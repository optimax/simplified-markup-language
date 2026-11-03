using System.Globalization;
using System.Text;

namespace Sml;

/// <summary>XML 1.0 (Fifth Edition) name and character rules, and the validation shared by the SML reader
/// and the XML importer. Names are scanned by code point (Rune) so that characters outside the BMP work.</summary>
public static class XmlRules
{
    public const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";
    public const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";
    private const string PubidPunctuation = " \r\n-'()+,./:=?;!*#@$_%";

    public static bool IsXmlChar(int cp) =>
        cp is 0x9 or 0xA or 0xD or (>= 0x20 and <= 0xD7FF) or (>= 0xE000 and <= 0xFFFD) or (>= 0x10000 and <= 0x10FFFF);

    private static bool IsNameStartChar(int cp) =>
        cp is (>= 'A' and <= 'Z') or '_' or (>= 'a' and <= 'z') or (>= 0xC0 and <= 0xD6) or (>= 0xD8 and <= 0xF6)
            or (>= 0xF8 and <= 0x2FF) or (>= 0x370 and <= 0x37D) or (>= 0x37F and <= 0x1FFF) or (>= 0x200C and <= 0x200D)
            or (>= 0x2070 and <= 0x218F) or (>= 0x2C00 and <= 0x2FEF) or (>= 0x3001 and <= 0xD7FF)
            or (>= 0xF900 and <= 0xFDCF) or (>= 0xFDF0 and <= 0xFFFD) or (>= 0x10000 and <= 0xEFFFF);

    private static bool IsNameChar(int cp) =>
        IsNameStartChar(cp) || cp is '-' or '.' or (>= '0' and <= '9') or 0xB7 or (>= 0x300 and <= 0x36F)
            or (>= 0x203F and <= 0x2040);

    /// <summary>Returns the end of the longest NCName starting at pos, or pos if there is none.</summary>
    public static int ScanNCName(string s, int pos)
    {
        int i = pos;
        bool first = true;
        while (i < s.Length && Rune.DecodeFromUtf16(s.AsSpan(i), out Rune r, out int used) == System.Buffers.OperationStatus.Done)
        {
            if (!(first ? IsNameStartChar(r.Value) : IsNameChar(r.Value)))
                break;
            first = false;
            i += used;
        }
        return i;
    }

    /// <summary>Returns the end of the longest QName (NCName, optionally ":" NCName) starting at pos, or pos.</summary>
    public static int ScanQName(string s, int pos)
    {
        int end = ScanNCName(s, pos);
        if (end > pos && end < s.Length && s[end] == ':')
        {
            int local = ScanNCName(s, end + 1);
            if (local > end + 1)
                return local;
        }
        return end;
    }

    public static bool IsNCName(string s) => s.Length > 0 && ScanNCName(s, 0) == s.Length;

    public static bool IsQName(string s) => s.Length > 0 && ScanQName(s, 0) == s.Length;

    public static bool IsName(string s)
    {
        int i = 0;
        foreach (Rune r in s.EnumerateRunes())
        {
            if (!(r.Value == ':' || (i == 0 ? IsNameStartChar(r.Value) : IsNameChar(r.Value))))
                return false;
            i++;
        }
        return i > 0;
    }

    public static string CheckChars(string s, int? line)
    {
        for (int i = 0; i < s.Length;)
        {
            if (Rune.DecodeFromUtf16(s.AsSpan(i), out Rune r, out int used) != System.Buffers.OperationStatus.Done)
                throw new SmlException($"character U+{(int)s[i]:X4} is not allowed in XML", line);
            if (!IsXmlChar(r.Value))
                throw new SmlException($"character U+{r.Value:X4} is not allowed in XML", line);
            i += used;
        }
        return s;
    }

    /// <summary>Characters writers write as \u{…} so that no data is invisible (§4).</summary>
    public static bool NeedsEscape(Rune r) =>
        r.Value is (>= 0x80 and <= 0x9F) or 0x2028 or 0x2029 || Rune.GetUnicodeCategory(r) == UnicodeCategory.Format;

    /// <summary>Length in code points, which is how the canonical form measures lines (§14.2).</summary>
    public static int CodePointLength(string s)
    {
        int n = 0;
        foreach (Rune _ in s.EnumerateRunes())
            n++;
        return n;
    }

    /// <summary>Orders strings by code point, as the canonical form requires (UTF-16 ordinal order differs).</summary>
    public static int CompareCodePoints(string? a, string? b)
    {
        if (ReferenceEquals(a, b))
            return 0;
        if (a is null)
            return -1;
        if (b is null)
            return 1;
        var ea = a.EnumerateRunes();
        var eb = b.EnumerateRunes();
        while (true)
        {
            bool ha = ea.MoveNext(), hb = eb.MoveNext();
            if (!ha || !hb)
                return ha == hb ? 0 : ha ? 1 : -1;
            int c = ea.Current.Value.CompareTo(eb.Current.Value);
            if (c != 0)
                return c;
        }
    }

    public static string StripSpacesAndTabs(string s) => s.Trim(' ', '\t');

    public static void CheckNamespaceDeclaration(string prefix, string uri, int? line)
    {
        if (prefix.Length > 0 && !IsNCName(prefix))
            throw new SmlException($"invalid namespace prefix '{prefix}'", line);
        if (prefix == "xmlns")
            throw new SmlException("the prefix 'xmlns' cannot be declared", line);
        if (prefix == "xml" && uri != XmlNamespace)
            throw new SmlException("the prefix 'xml' can only be bound to " + XmlNamespace, line);
        if (prefix != "xml" && uri == XmlNamespace)
            throw new SmlException(XmlNamespace + " can only be bound to the prefix 'xml'", line);
        if (uri == XmlnsNamespace)
            throw new SmlException(XmlnsNamespace + " cannot be bound", line);
        if (prefix.Length > 0 && uri.Length == 0)
            throw new SmlException($"prefix '{prefix}' cannot be undeclared under Namespaces in XML 1.0", line);
        CheckChars(uri, line);
    }

    /// <summary>Resolves a lexical name against in-scope bindings; the key "" is the default namespace.</summary>
    public static ExpandedName Resolve(string qname, IReadOnlyDictionary<string, string> ns, bool attribute, int? line)
    {
        if (!IsQName(qname))
            throw new SmlException($"invalid name '{qname}'", line);
        int colon = qname.IndexOf(':');
        if (colon >= 0)
        {
            string prefix = qname[..colon];
            if (!ns.TryGetValue(prefix, out string? uri) || uri.Length == 0)
                throw new SmlException($"prefix '{prefix}' is not bound", line);
            return new ExpandedName(uri, qname[(colon + 1)..]);
        }
        if (attribute)
            return new ExpandedName(null, qname);
        return new ExpandedName(ns.TryGetValue("", out string? d) && d.Length > 0 ? d : null, qname);
    }

    public static void CheckComment(string data, int? line)
    {
        CheckChars(data, line);
        if (data.Contains("--", StringComparison.Ordinal))
            throw new SmlException("an XML comment cannot contain '--'", line);
        if (data.Contains('\r'))
            throw new SmlException("an XML comment cannot contain CR (U+000D)", line);
    }

    /// <summary>A comment's data (§9): checked, then normalized by <see cref="CommentText"/>. SML comments always start
    /// on their own line.</summary>
    public static string CommentData(string data, int? line, bool ownLine = true)
    {
        CheckComment(data, line);
        return CommentText(data, ownLine);
    }

    /// <summary>A comment's normalized data (§9). The spaces, tabs, and line breaks at its end go. If its text starts on
    /// its own line (always so for SML, <paramref name="ownLine"/>), the blank lines before it go, and so does the
    /// indentation all its lines share; otherwise the first line loses its leading spaces and tabs, and the other
    /// lines the indentation they share. A line of spaces and tabs only that does not begin with the shared
    /// indentation becomes empty.</summary>
    public static string CommentText(string data, bool ownLine = false)
    {
        var lines = data.TrimEnd(' ', '\t', '\n').Split('\n').ToList();
        var first = new List<string>();
        if (ownLine || IsBlank(lines[0]))
        {
            while (lines.Count > 0 && IsBlank(lines[0]))
                lines.RemoveAt(0);
        }
        else
        {
            first.Add(lines[0].TrimStart(' ', '\t'));
            lines.RemoveAt(0);
        }
        string shared = SharedIndentation(lines);
        return string.Join('\n', first.Concat(lines.Select(l => l.StartsWith(shared, StringComparison.Ordinal) ? l[shared.Length..] : "")));
    }

    /// <summary>The leading spaces and tabs that all lines with a character other than space and tab share.</summary>
    public static string SharedIndentation(IEnumerable<string> lines)
    {
        string? prefix = null;
        foreach (string line in lines.Where(l => !IsBlank(l)))
        {
            string lead = line[..(line.Length - line.TrimStart(' ', '\t').Length)];
            int k = 0;
            while (prefix is not null && k < prefix.Length && k < lead.Length && prefix[k] == lead[k])
                k++;
            prefix = prefix is null ? lead : prefix[..k];
        }
        return prefix ?? "";
    }

    private static bool IsBlank(string line) => line.TrimStart(' ', '\t').Length == 0;

    public static void CheckProcessingInstruction(string target, string data, int? line)
    {
        if (!IsNCName(target))
            throw new SmlException($"invalid PI target '{target}' (PI targets are NCNames)", line);
        if (string.Equals(target, "xml", StringComparison.OrdinalIgnoreCase))
            throw new SmlException("the PI target 'xml' is reserved", line);
        CheckChars(data, line);
        if (data.Contains("?>", StringComparison.Ordinal) || data.Contains('\r'))
            throw new SmlException("PI data cannot contain '?>' or CR (U+000D)", line);
        if (data.Length > 0 && data[0] is ' ' or '\t' or '\n' or '\r')
            throw new SmlException("PI data cannot begin with whitespace", line);
    }

    public static void CheckDoctype(DoctypeNode dt, int? line)
    {
        if (!IsName(dt.Name))
            throw new SmlException($"invalid DOCTYPE name '{dt.Name}'", line);
        if (dt.PublicId is { } p)
        {
            foreach (char c in p)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || PubidPunctuation.Contains(c)) || c is '\r' or '\n')
                    throw new SmlException("invalid character in DOCTYPE public ID", line);
            }
            if (p != p.Trim(' ') || p.Contains("  ", StringComparison.Ordinal))
                throw new SmlException("DOCTYPE public ID is not normalized", line);
        }
        if (dt.SystemId is { } s)
        {
            CheckChars(s, line);
            if (s.Contains('"') && s.Contains('\''))
                throw new SmlException("DOCTYPE system ID cannot contain both quote characters", line);
            if (s.Contains('\r'))
                throw new SmlException("DOCTYPE system ID cannot contain CR (U+000D); XML cannot represent it", line);
        }
    }
}
