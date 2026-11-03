namespace Sml;

/// <summary>How <see cref="SmlWriter"/> departs from the canonical form (§14.2). The default options give the
/// canonical form.</summary>
public sealed record SmlWriterOptions
{
    /// <summary>A profile whose aliases and layouts the writer applies, or null.</summary>
    public SmlProfile? Profile { get; init; }

    /// <summary>With a profile: the readable reflow of §3.2, one item per line. This changes the document.</summary>
    public bool Reflow { get; init; }

    /// <summary>Writes every attribute and namespace declaration on its own line (<c>.name value</c>,
    /// <c>@ns</c>), never in an inline list (§6.3). Elements without either keep their compact forms.</summary>
    public bool VerticalAttributes { get; init; }
}
