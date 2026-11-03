namespace OptimaxTools.SMLReadWrite;

/// <summary>The kinds of structured attribute value that a layout divides into items (specification §3.2).</summary>
public enum LayoutKind
{
    /// <summary>SVG path data: an item per command.</summary>
    Path,

    /// <summary>Numbers separated by whitespace or commas: an item per Count numbers.</summary>
    Numbers,

    /// <summary>CSS declarations: an item per declaration, ending with its ';'.</summary>
    Declarations,

    /// <summary>A function list such as an SVG transform: an item per function.</summary>
    Functions,

    /// <summary>A ';'-separated list such as an animation's values: an item per entry, ending with its ';'.</summary>
    List,
}

/// <summary>A layout: its kind, and for <see cref="LayoutKind.Numbers"/> the count of numbers per item.</summary>
public readonly record struct Layout(LayoutKind Kind, int Count = 0)
{
    public override string ToString() => Kind == LayoutKind.Numbers ? $"numbers {Count}" : Kind.ToString().ToLowerInvariant();
}

/// <summary>Divides attribute values into items (§3.2): for writing them exactly with a continuation line per item,
/// or for the readable conversion, which puts each item on its own line.</summary>
public static class ValueLayout
{
    private const string PathCommands = "MmZzLlHhVvCcSsQqTtAa";

    /// <summary>The items of a value as (start, end) index pairs, or null if the value does not have the layout's
    /// form. Items never begin or end with XML whitespace, and only separators lie between them.</summary>
    public static List<(int Start, int End)>? Items(string value, Layout layout) => layout.Kind switch
    {
        LayoutKind.Path => PathItems(value),
        LayoutKind.Numbers => NumberItems(value, layout.Count),
        LayoutKind.Declarations => DeclarationItems(value),
        LayoutKind.Functions => FunctionItems(value),
        LayoutKind.List => ListItems(value),
        _ => null,
    };

    /// <summary>The value divided before each item after the first, or null if it has fewer than two items.
    /// The segments joined give the value again, exactly.</summary>
    public static List<string>? Segments(string value, Layout layout)
    {
        List<(int Start, int End)>? items = Items(value, layout);
        if (items is null || items.Count < 2)
            return null;
        var segments = new List<string>(items.Count);
        for (int k = 0; k < items.Count; k++)
        {
            int start = k == 0 ? 0 : items[k].Start;
            int end = k == items.Count - 1 ? value.Length : items[k + 1].Start;
            segments.Add(value[start..end]);
        }
        return segments;
    }

    /// <summary>The value with its items joined by LF, one item per line (§3.2), or null if the layout does not
    /// apply: the value does not have the layout's form, has fewer than two items, or an item contains TAB, LF,
    /// or CR. Path commands and numbers are also spaced (<see cref="SpacePathCommand"/>,
    /// <see cref="SpaceNumbers"/>).</summary>
    public static string? Reflow(string value, Layout layout)
    {
        List<(int Start, int End)>? items = Items(value, layout);
        if (items is null || items.Count < 2)
            return null;
        var texts = items.Select(item => value[item.Start..item.End]).ToList();
        if (texts.Any(text => text.AsSpan().ContainsAny('\t', '\n', '\r')))
            return null;
        if (layout.Kind == LayoutKind.Path)
            texts = texts.ConvertAll(SpacePathCommand);
        else if (layout.Kind == LayoutKind.Numbers)
            texts = texts.ConvertAll(SpaceNumbers);
        return string.Join('\n', texts);
    }

    /// <summary>A path command spaced for reading (§3.2): a space after its letter, after each comma, and between
    /// two parameters written together, wherever there is none and more follows. "M10,20" becomes "M 10, 20", and
    /// "a.5.5 0 01-1 0" becomes "a .5 .5 0 0 1 -1 0". Parameters are read as path data reads them: numbers each as
    /// long as possible, and an arc's two flags as single characters, so a space only goes where the path already
    /// has a boundary.</summary>
    public static string SpacePathCommand(string command) =>
        SpaceParameters(command, 1, arc: command[0] is 'a' or 'A')
            ?? SpaceLetterAndCommas(command);    // not path parameters after all: space only the safe places

    /// <summary>Numbers spaced for reading (§3.2), as path parameters are: a space after each comma and between two
    /// numbers written together, wherever there is none and more follows. "10,20" becomes "10, 20", and "1-2"
    /// becomes "1 -2". Text that is not numbers is returned unchanged.</summary>
    public static string SpaceNumbers(string numbers) => SpaceParameters(numbers, 0, arc: false) ?? numbers;

    /// <summary>The text with its parameters from position start on spaced, or null if after start it has anything
    /// but parameters, spaces, and single commas.</summary>
    private static string? SpaceParameters(string text, int start, bool arc)
    {
        List<(int Start, int End)>? parameters = Parameters(text, start, arc);
        if (parameters is null)
            return null;
        var result = new System.Text.StringBuilder(text.Length + 16);
        result.Append(text, 0, start);
        int previous = start;
        foreach (var (from, to) in parameters)
        {
            result.Append(text, previous, from - previous);    // the separator: spaces and at most one comma
            if (result.Length > 0 && result[^1] != ' ')
                result.Append(' ');
            result.Append(text, from, to - from);
            previous = to;
        }
        result.Append(text, previous, text.Length - previous);
        return result.ToString();
    }

    /// <summary>The parameters of text from position start on, or null if it has anything but parameters, spaces,
    /// and single commas there. Numbers are each as long as possible; with arc, the 4th and 5th of every 7
    /// parameters are an arc command's flags: one character, 0 or 1.</summary>
    private static List<(int Start, int End)>? Parameters(string text, int start, bool arc)
    {
        var parameters = new List<(int Start, int End)>();
        int i = start;
        while (true)
        {
            int commas = 0;
            for (; i < text.Length && text[i] is ' ' or ','; i++)
                commas += text[i] == ',' ? 1 : 0;
            if (commas > 1)
                return null;
            if (i == text.Length)
                return parameters;
            int end = arc && parameters.Count % 7 is 3 or 4
                ? (text[i] is '0' or '1' ? i + 1 : i)
                : ScanNumber(text, i);
            if (end == i)
                return null;
            parameters.Add((i, end));
            i = end;
        }
    }

    /// <summary>A command with a space after its letter and after each comma, where there is none and more follows.</summary>
    private static string SpaceLetterAndCommas(string command)
    {
        var result = new System.Text.StringBuilder(command.Length + 8);
        for (int i = 0; i < command.Length; i++)
        {
            result.Append(command[i]);
            bool spaceAfter = i == 0 || command[i] == ',';
            if (spaceAfter && i + 1 < command.Length && command[i + 1] != ' ')
                result.Append(' ');
        }
        return result.ToString();
    }

    private static bool IsWhite(char c) => c is ' ' or '\t' or '\n' or '\r';

    private static int SkipWhite(string s, int i)
    {
        while (i < s.Length && IsWhite(s[i]))
            i++;
        return i;
    }

    /// <summary>The end of [start, end) without its trailing whitespace.</summary>
    private static int TrimEnd(string s, int start, int end)
    {
        while (end > start && IsWhite(s[end - 1]))
            end--;
        return end;
    }

    private static List<(int, int)>? PathItems(string v)
    {
        foreach (char c in v)
        {
            bool numeric = char.IsAsciiDigit(c) || c is '.' or '+' or '-' or 'e' or 'E' or ',';
            if (!numeric && !IsWhite(c) && !PathCommands.Contains(c))
                return null;
        }
        int first = SkipWhite(v, 0);
        if (first == v.Length || !PathCommands.Contains(v[first]))
            return null;
        var items = new List<(int, int)>();
        int start = first;
        for (int i = first + 1; i < v.Length; i++)
        {
            if (PathCommands.Contains(v[i]))
            {
                items.Add((start, TrimEnd(v, start, i)));
                start = i;
            }
        }
        items.Add((start, TrimEnd(v, start, v.Length)));
        return items;
    }

    /// <summary>The end of the number at i (SVG number syntax), or i if there is none.</summary>
    private static int ScanNumber(string s, int i)
    {
        int j = i;
        if (j < s.Length && s[j] is '+' or '-')
            j++;
        int digits = 0;
        while (j < s.Length && char.IsAsciiDigit(s[j]))
            (j, digits) = (j + 1, digits + 1);
        if (j < s.Length && s[j] == '.')
        {
            j++;
            while (j < s.Length && char.IsAsciiDigit(s[j]))
                (j, digits) = (j + 1, digits + 1);
        }
        if (digits == 0)
            return i;
        if (j < s.Length && s[j] is 'e' or 'E')
        {
            int k = j + 1;
            if (k < s.Length && s[k] is '+' or '-')
                k++;
            int exponent = k;
            while (k < s.Length && char.IsAsciiDigit(s[k]))
                k++;
            if (k > exponent)
                j = k;
        }
        return j;
    }

    private static List<(int, int)>? NumberItems(string v, int count)
    {
        if (count < 1)
            return null;
        var numbers = new List<(int Start, int End)>();
        int i = SkipWhite(v, 0);
        while (i < v.Length)
        {
            int end = ScanNumber(v, i);
            if (end == i)
                return null;
            numbers.Add((i, end));
            i = SkipWhite(v, end);
            if (i < v.Length && v[i] == ',')
            {
                i = SkipWhite(v, i + 1);
                if (i == v.Length)
                    return null;
            }
        }
        if (numbers.Count == 0)
            return null;
        var items = new List<(int, int)>();
        for (int k = 0; k < numbers.Count; k += count)
            items.Add((numbers[k].Start, numbers[Math.Min(k + count, numbers.Count) - 1].End));
        return items;
    }

    private static List<(int, int)>? DeclarationItems(string v)
    {
        var items = new List<(int, int)>();
        int depth = 0, start = -1;
        char quote = '\0';
        for (int i = 0; i < v.Length; i++)
        {
            char c = v[i];
            if (start < 0)
            {
                if (IsWhite(c))
                    continue;
                start = i;
            }
            if (c == '\\')
            {
                i++;
                continue;
            }
            if (quote != '\0')
            {
                if (c == quote)
                    quote = '\0';
                continue;
            }
            if (c == '/' && i + 1 < v.Length && v[i + 1] == '*')
            {
                int close = v.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (close < 0)
                    return null;
                i = close + 1;
                continue;
            }
            switch (c)
            {
                case '"' or '\'':
                    quote = c;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    if (--depth < 0)
                        return null;
                    break;
                case ';' when depth == 0:
                    items.Add((start, i + 1));
                    start = -1;
                    break;
            }
        }
        if (quote != '\0' || depth != 0)
            return null;
        if (start >= 0)
            items.Add((start, TrimEnd(v, start, v.Length)));
        return items.Count > 0 ? items : null;
    }

    private static List<(int, int)>? FunctionItems(string v)
    {
        var items = new List<(int, int)>();
        int i = SkipWhite(v, 0);
        while (i < v.Length)
        {
            int start = i;
            if (!char.IsAsciiLetter(v[i]))
                return null;
            while (i < v.Length && (char.IsAsciiLetterOrDigit(v[i]) || v[i] == '-'))
                i++;
            i = SkipWhite(v, i);
            if (i == v.Length || v[i] != '(')
                return null;
            int depth = 0;
            for (; i < v.Length; i++)
            {
                if (v[i] == '(')
                    depth++;
                else if (v[i] == ')' && --depth == 0)
                    break;
            }
            if (i == v.Length)
                return null;
            items.Add((start, i + 1));
            i = SkipWhite(v, i + 1);
            if (i < v.Length && v[i] == ',')
            {
                i = SkipWhite(v, i + 1);
                if (i == v.Length)
                    return null;
            }
        }
        return items.Count > 0 ? items : null;
    }

    private static List<(int, int)>? ListItems(string v)
    {
        var items = new List<(int, int)>();
        int start = -1;
        for (int i = 0; i < v.Length; i++)
        {
            if (start < 0)
            {
                if (IsWhite(v[i]))
                    continue;
                start = i;
            }
            if (v[i] == ';')
            {
                items.Add((start, i + 1));
                start = -1;
            }
        }
        if (start >= 0)
            items.Add((start, TrimEnd(v, start, v.Length)));
        return items.Count > 0 ? items : null;
    }
}
