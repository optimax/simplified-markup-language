namespace Sml;

/// <summary>The strict profile's document equality (§3), including prefixes and declaration locations.</summary>
public static class DocumentComparer
{
    /// <summary>Describes the first difference between two documents, or returns null if they are equal.</summary>
    public static string? FirstDifference(SmlDocument a, SmlDocument b)
    {
        if (a.Standalone != b.Standalone)
            return $"standalone: {a.Standalone ?? "(none)"} != {b.Standalone ?? "(none)"}";
        return Sequence("prolog", a.Prolog, b.Prolog, "")
            ?? ElementDifference(a.Root, b.Root, "")
            ?? Sequence("epilog", a.Epilog, b.Epilog, "");
    }

    private static string? Sequence(string what, IReadOnlyList<SmlNode> a, IReadOnlyList<SmlNode> b, string path)
    {
        for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
        {
            string? diff = NodeDifference(a[i], b[i], $"{path}/{what}[{i}]");
            if (diff is not null)
                return diff;
        }
        return a.Count == b.Count ? null : $"{path}/{what}: {a.Count} nodes != {b.Count} nodes";
    }

    private static string? NodeDifference(SmlNode a, SmlNode b, string path) => (a, b) switch
    {
        (ElementNode x, ElementNode y) => ElementDifference(x, y, path),
        (TextNode x, TextNode y) => x.Data == y.Data ? null : $"{path}: text '{x.Data}' != '{y.Data}'",
        (CommentNode x, CommentNode y) => x.Data == y.Data ? null : $"{path}: comment '{x.Data}' != '{y.Data}'",
        (ProcessingInstructionNode x, ProcessingInstructionNode y) => x.Target == y.Target && x.Data == y.Data
            ? null : $"{path}: PI '{x.Target} {x.Data}' != '{y.Target} {y.Data}'",
        (DoctypeNode x, DoctypeNode y) => x.Name == y.Name && x.PublicId == y.PublicId && x.SystemId == y.SystemId
            ? null : $"{path}: DOCTYPE differs",
        _ => $"{path}: {a.GetType().Name} != {b.GetType().Name}",
    };

    private static string? ElementDifference(ElementNode a, ElementNode b, string path)
    {
        path = $"{path}/{a.QualifiedName}";
        if (a.QualifiedName != b.QualifiedName || a.Name != b.Name)
            return $"{path}: element {a.QualifiedName} {a.Name} != {b.QualifiedName} {b.Name}";
        string Declarations(ElementNode e) =>
            string.Join(' ', e.NamespaceDeclarations.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => $"{d.Key}={d.Value}"));
        if (Declarations(a) != Declarations(b))
            return $"{path}: namespace declarations [{Declarations(a)}] != [{Declarations(b)}]";
        string Attributes(ElementNode e) =>
            string.Join('\u0001', e.Attributes.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => $"{x.Key}\u0002{e.AttributeNames[x.Key]}\u0002{x.Value}"));
        if (Attributes(a) != Attributes(b))
            return $"{path}: attributes differ";
        return Sequence("content", SmlWriter.ContentNodes(a), SmlWriter.ContentNodes(b), path);
    }
}
