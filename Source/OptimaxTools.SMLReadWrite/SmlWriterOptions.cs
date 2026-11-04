// Created by Andrew Wozniewicz with Claude
// Copyright (C) 2026 Optimax Technologies, Inc.
// Distributed under MIT License.
//
namespace OptimaxTools.SMLReadWrite;

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

    /// <summary>Writes namespace declarations and attributes in the order of Canonical XML instead of document order:
    /// the default namespace, then prefixes, then attributes without a namespace by local name, then namespaced ones
    /// by namespace URI and local name, all in ascending code-point order. XML gives attribute order no meaning, but
    /// the SML reads back with the attributes in that order.</summary>
    public bool SortAttributes { get; init; }
}
