// Created by Andrew Wozniewicz with Claude
// Copyright (C) 2026 Optimax Technologies, Inc.
// Distributed under MIT License.
//
using System.Text;

namespace OptimaxTools.SMLReadWrite;

/// <summary>Converts between SML and XML in one call, for applications such as smlxml or an editor. The text it
/// returns is meant to be saved as UTF-8 without a byte order mark (<see cref="Encoding"/>); reading, writing, and
/// naming files is left to the application.</summary>
public static class SmlConverter
{
    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    /// <summary>UTF-8 without a byte order mark: the encoding of SML (§4) and of all XML output (§11).</summary>
    public static Encoding Encoding { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Whether a file name is an SML file's, by its extension .sml (in any case).</summary>
    public static bool IsSmlFileName(string path) =>
        Path.GetExtension(path).Equals(".sml", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the document's root is the SVG <c>svg</c> element.</summary>
    public static bool IsSvg(SmlDocument document) => document.Root.Name == new ExpandedName(SvgNamespace, "svg");

    /// <summary>The extension for a document written as XML: .svg for SVG, otherwise .xml.</summary>
    public static string XmlExtension(SmlDocument document) => IsSvg(document) ? ".svg" : ".xml";

    /// <summary>Converts XML, in any encoding its declaration names, to SML: canonical by default, or with the
    /// options' departures, such as cleaning with a profile.</summary>
    /// <exception cref="SmlException">The XML is malformed or outside the supported subset (§3).</exception>
    public static string XmlToSml(byte[] xml, SmlWriterOptions? options = null) =>
        SmlWriter.Write(XmlImporter.Read(xml), options ?? new SmlWriterOptions());

    /// <summary>Converts SML in UTF-8 to XML, and gives the extension for it (<see cref="XmlExtension"/>).</summary>
    /// <exception cref="SmlException">The SML is invalid.</exception>
    public static (string Xml, string Extension) SmlToXml(byte[] sml) => SmlToXml(SmlReader.Read(sml));

    /// <summary>Converts SML text to XML, and gives the extension for it (<see cref="XmlExtension"/>).</summary>
    /// <exception cref="SmlException">The SML is invalid.</exception>
    public static (string Xml, string Extension) SmlToXml(string sml) => SmlToXml(SmlReader.Read(sml));

    private static (string Xml, string Extension) SmlToXml(SmlDocument document) =>
        (XmlExporter.Write(document), XmlExtension(document));
}
