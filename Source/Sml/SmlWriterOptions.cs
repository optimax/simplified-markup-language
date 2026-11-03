namespace Sml;

/// <summary>How <see cref="SmlWriter"/> departs from the canonical form (§14.2). The default options give the
/// canonical form.</summary>
public sealed record SmlWriterOptions
{
    /// <summary>A profile to clean the SML with (§3.2), or null. Its layouts put one item of a value per line and
    /// tidy the text of code elements, which changes the document's whitespace; its aliases shorten blocks and element
    /// names.</summary>
    public SmlProfile? Clean { get; init; }

    /// <summary>Writes every attribute and namespace declaration on its own line (<c>.name value</c>,
    /// <c>@ns</c>), never in an inline list (§6.3). Elements without either keep their compact forms.</summary>
    public bool VerticalAttributes { get; init; }
}
