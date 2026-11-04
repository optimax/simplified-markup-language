// Created by Andrew Wozniewicz with Claude
// Copyright (C) 2026 Optimax Technologies, Inc.
// Distributed under MIT License.
//
using System.Text;

namespace Sml.Tests;

/// <summary>Tests of SmlConverter, the one-call entry point of OptimaxTools.SMLReadWrite for applications.</summary>
public class ConverterTests
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\">\n  <rect x=\"1\" y=\"2\"/>\n</svg>\n";

    [Fact]
    public void ConvertsXmlToSmlAndBack()
    {
        string sml = SmlConverter.XmlToSml(Encoding.UTF8.GetBytes(Svg));
        Assert.Equal("@sml 1.0-SVG-1\nsvg(xmlns=http://www.w3.org/2000/svg)\n  rect(x=1, y=2)/\nend\n", sml);
        var (xml, extension) = SmlConverter.SmlToXml(sml);
        Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + Svg, xml);
        Assert.Equal(".svg", extension);
        Assert.Equal((xml, extension), SmlConverter.SmlToXml(SmlConverter.Encoding.GetBytes(sml)));
    }

    [Fact]
    public void AppliesWriterOptions()
    {
        string sml = SmlConverter.XmlToSml(Encoding.UTF8.GetBytes("<g b=\"1\" a=\"2\"/>"), new SmlWriterOptions { SortAttributes = true });
        Assert.Contains("g(a=2, b=1)/", sml);
    }

    [Fact]
    public void NamesOutputsAndReportsErrors()
    {
        Assert.Equal(".xml", SmlConverter.SmlToXml("config\n  item = 1\nend\n").Extension);
        Assert.True(SmlConverter.IsSmlFileName("drawing.SML"));
        Assert.False(SmlConverter.IsSmlFileName("drawing.svg"));
        Assert.Empty(SmlConverter.Encoding.GetPreamble());
        Assert.ThrowsAny<SmlException>(() => SmlConverter.XmlToSml(Encoding.UTF8.GetBytes("<g>")));
        SmlException e = Assert.Throws<SmlException>(() => SmlConverter.SmlToXml("g\n  .a\n  .a 1\nend\n"));
        Assert.Equal(3, e.Line);
    }
}
