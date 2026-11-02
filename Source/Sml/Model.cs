namespace Sml;

/// <summary>A node of the SML information model (specification §2).</summary>
public abstract class SmlNode
{
}

public sealed class TextNode(string data) : SmlNode
{
    public string Data { get; set; } = data;
}

public sealed class CommentNode(string data) : SmlNode
{
    public string Data { get; set; } = data;
}

public sealed class ProcessingInstructionNode(string target, string data) : SmlNode
{
    public string Target { get; } = target;

    public string Data { get; set; } = data;
}

/// <summary>A DOCTYPE without an internal subset, kept as opaque metadata (§11.1).</summary>
public sealed class DoctypeNode(string name, string? publicId, string? systemId) : SmlNode
{
    public string Name { get; } = name;

    public string? PublicId { get; } = publicId;

    public string? SystemId { get; } = systemId;
}

/// <summary>An expanded name; Namespace is null for "no namespace".</summary>
public readonly record struct ExpandedName(string? Namespace, string Local);

public sealed class ElementNode(string qualifiedName) : SmlNode
{
    /// <summary>The lexical name as written, e.g. "svg:path".</summary>
    public string QualifiedName { get; } = qualifiedName;

    public ExpandedName Name { get; set; }

    /// <summary>Declarations made on this element, in source order; the key "" is the default namespace.</summary>
    public OrderedDictionary<string, string> NamespaceDeclarations { get; } = new(StringComparer.Ordinal);

    /// <summary>Attributes by lexical name, in source order.</summary>
    public OrderedDictionary<string, string> Attributes { get; } = new(StringComparer.Ordinal);

    /// <summary>The expanded name of each attribute, by lexical name.</summary>
    public Dictionary<string, ExpandedName> AttributeNames { get; } = new(StringComparer.Ordinal);

    public List<SmlNode> Children { get; } = [];
}

public sealed class SmlDocument
{
    /// <summary>Comments, processing instructions, and the DOCTYPE before the root, in order.</summary>
    public List<SmlNode> Prolog { get; } = [];

    public ElementNode Root { get; set; } = null!;

    /// <summary>Comments and processing instructions after the root, in order.</summary>
    public List<SmlNode> Epilog { get; } = [];

    /// <summary>"yes", "no", or null when the XML declaration has no standalone flag.</summary>
    public string? Standalone { get; set; }
}
