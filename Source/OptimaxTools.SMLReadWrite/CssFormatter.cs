using System.Text;

namespace OptimaxTools.SMLReadWrite;

/// <summary>Lays out a CSS style sheet for reading (specification §3.2): each statement on its own line, a rule as
/// its selector and <c>{</c>, its declarations one per line and one level deeper, and <c>}</c>; nested blocks such as
/// <c>@media</c> are indented the same way. Only whitespace changes: line breaks between and inside statements become
/// the layout's, and the text of each statement, strings, and comments stay as they are.</summary>
public static class CssFormatter
{
    private const string Indent = "  ";

    private abstract record Item;

    private sealed record Statement(string Text) : Item;

    private sealed record Comment(string Text) : Item;

    private sealed record Rule(string Prelude, List<Item> Block) : Item;

    private sealed record Semicolon : Item;

    /// <summary>The style sheet laid out, or null if it is not well-formed enough to lay out safely: an unclosed
    /// string, comment, parenthesis, or block, a stray <c>}</c> or <c>)</c>, an HTML comment delimiter, or nothing but
    /// whitespace.</summary>
    public static string? Format(string css)
    {
        if (css.Contains("<!--", StringComparison.Ordinal) || css.Contains("-->", StringComparison.Ordinal))
            return null;
        int i = 0;
        List<Item>? items = Parse(css, ref i, nested: false);
        if (items is null || items.Count == 0)
            return null;
        var lines = new List<string>();
        Write(items, 0, lines);
        return string.Join('\n', lines);
    }

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    /// <summary>The items up to the end, or, nested, up to and including the closing brace.</summary>
    private static List<Item>? Parse(string css, ref int i, bool nested)
    {
        var items = new List<Item>();
        var text = new StringBuilder();
        var space = new StringBuilder();   // whitespace not yet copied into text
        int depth = 0;                       // parentheses

        void Flush(List<Item> into)
        {
            if (text.Length > 0)
                into.Add(new Statement(text.ToString()));
            text.Clear();
            space.Clear();
        }

        void Append(string s)
        {
            if (text.Length > 0 && space.Length > 0)
            {
                // A run of whitespace with a line break becomes one space; other runs stay as they are.
                string run = space.ToString();
                text.Append(run.AsSpan().ContainsAny('\n', '\r', '\f') ? " " : run);
            }
            space.Clear();
            text.Append(s);
        }

        while (i < css.Length)
        {
            char c = css[i];
            if (IsSpace(c))
            {
                space.Append(c);
                i++;
            }
            else if (c == '/' && i + 1 < css.Length && css[i + 1] == '*')
            {
                int end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0)
                    return null;
                string comment = css[i..(end + 2)];
                if (text.Length == 0)
                    items.Add(new Comment(comment));
                else
                    Append(comment);
                i = end + 2;
            }
            else if (c is '"' or '\'')
            {
                int j = i + 1;
                while (j < css.Length && css[j] != c)
                {
                    if (css[j] is '\n' or '\r' or '\f')
                        return null;
                    j += css[j] == '\\' ? 2 : 1;
                }
                if (j >= css.Length)
                    return null;
                Append(css[i..(j + 1)]);
                i = j + 1;
            }
            else if (c == '\\')
            {
                if (i + 1 >= css.Length)
                    return null;
                Append(css.Substring(i, 2));
                i += 2;
            }
            else if (c == '(' || c == ')')
            {
                depth += c == '(' ? 1 : -1;
                if (depth < 0)
                    return null;
                Append(c.ToString());
                i++;
            }
            else if (depth == 0 && c == ';')
            {
                if (text.Length == 0)
                    items.Add(new Semicolon());
                else
                {
                    Append(";");
                    Flush(items);
                }
                i++;
            }
            else if (depth == 0 && c == '{')
            {
                string prelude = text.ToString();
                text.Clear();
                space.Clear();
                i++;
                List<Item>? block = Parse(css, ref i, nested: true);
                if (block is null)
                    return null;
                items.Add(new Rule(prelude, block));
            }
            else if (depth == 0 && c == '}')
            {
                if (!nested)
                    return null;
                Flush(items);
                i++;
                return items;
            }
            else
            {
                Append(c.ToString());
                i++;
            }
        }
        if (nested || depth != 0)
            return null;
        Flush(items);
        return items;
    }

    private static void Write(List<Item> items, int level, List<string> lines)
    {
        string indent = string.Concat(Enumerable.Repeat(Indent, level));
        foreach (Item item in items)
        {
            switch (item)
            {
                case Statement s:
                    lines.Add(indent + s.Text);
                    break;
                case Comment c:
                    lines.Add(indent + c.Text);
                    break;
                case Semicolon:
                    if (lines.Count > 0)
                        lines[^1] += ";";
                    else
                        lines.Add(indent + ";");
                    break;
                case Rule r:
                    lines.Add(indent + (r.Prelude.Length > 0 ? r.Prelude + " {" : "{"));
                    Write(r.Block, level + 1, lines);
                    lines.Add(indent + "}");
                    break;
            }
        }
    }
}
