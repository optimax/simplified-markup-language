namespace SmlXml;

/// <summary>Expands wildcard arguments. Windows shells pass patterns such as *.svg to programs unexpanded, so
/// smlxml expands them itself: * and ? in any part of the path, and ** as a whole part for any number of
/// folders. Names are compared case-insensitively on Windows and macOS. Hidden files and folders, including
/// names starting with ".", match only a pattern part that itself starts with ".".</summary>
public static class FileExpander
{
    private static readonly bool IgnoreCase = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public static bool IsPattern(string path) => path.AsSpan().IndexOfAny('*', '?') >= 0;

    public static StringComparer PathComparer { get; } = IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Returns the files that match a pattern, sorted, in the same relative or absolute form as the
    /// pattern; an empty list if none match.</summary>
    public static List<string> Expand(string pattern)
    {
        string root = Path.GetPathRoot(pattern) ?? "";
        string[] parts = pattern[root.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var current = new List<string> { root };
        for (int i = 0; i < parts.Length; i++)
        {
            bool last = i == parts.Length - 1;
            string part = parts[i];
            var next = new List<string>();
            foreach (string folder in current)
            {
                if (part == "**")
                    next.AddRange(last ? Entries(folder, files: true, "*", recursive: true) : [folder, .. Entries(folder, files: false, "*", recursive: true)]);
                else if (IsPattern(part))
                    next.AddRange(Entries(folder, files: last, part, recursive: false));
                else if (last ? File.Exists(Join(folder, part)) : Directory.Exists(Join(folder, part)))
                    next.Add(Join(folder, part));
            }
            current = next.Distinct(PathComparer).ToList();
        }
        if (parts.Length == 0)
            return [];
        current.Sort(PathComparer);
        return current;
    }

    private static string Join(string folder, string name) => folder.Length == 0 ? name : Path.Join(folder, name);

    private static IEnumerable<string> Entries(string folder, bool files, string part, bool recursive)
    {
        string directory = folder.Length == 0 ? "." : folder;
        if (!Directory.Exists(directory))
            return [];
        bool showHidden = part.StartsWith('.');
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            MatchType = MatchType.Simple,
            AttributesToSkip = showHidden ? FileAttributes.System : FileAttributes.Hidden | FileAttributes.System,
        };
        IEnumerable<string> found = files
            ? Directory.EnumerateFiles(directory, "*", options)
            : Directory.EnumerateDirectories(directory, "*", options);
        return found
            .Where(path => recursive ? !IsUnderHiddenName(Path.GetRelativePath(directory, path)) : Matches(Path.GetFileName(path), part, showHidden))
            .Select(path => folder.Length == 0 ? Path.GetRelativePath(".", path) : path);
    }

    private static bool IsUnderHiddenName(string relative) =>
        relative.Split(Separators, StringSplitOptions.RemoveEmptyEntries).Any(name => name.StartsWith('.'));

    /// <summary>Matches a single name against a pattern part with * (any run of characters) and ? (one character).</summary>
    public static bool Matches(string name, string pattern, bool showHidden = false)
    {
        if (name.StartsWith('.') && !showHidden)
            return false;
        int n = 0, p = 0, star = -1, starName = 0;
        while (n < name.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || Same(pattern[p], name[n])))
            {
                n++;
                p++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                starName = n;
            }
            else if (star >= 0)
            {
                p = star + 1;
                n = ++starName;
            }
            else
            {
                return false;
            }
        }
        while (p < pattern.Length && pattern[p] == '*')
            p++;
        return p == pattern.Length;
    }

    private static bool Same(char a, char b) => a == b || (IgnoreCase && char.ToUpperInvariant(a) == char.ToUpperInvariant(b));
}
