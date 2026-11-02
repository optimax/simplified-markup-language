using System.Text;

namespace Sml.Tests;

/// <summary>Core reader and writer tests: C# ports of the specification's CP fixtures (§15.1) and of the
/// SML-only SV fixtures in Tests/svg-roundtrip/test_sml_core.py.</summary>
public class ReaderTests
{
    private const string Svg = "http://www.w3.org/2000/svg";

    private static ElementNode Root(string source) => SmlReader.Read(source).Root;

    private static void Rejects(string source, string? fragment = null)
    {
        var e = Assert.ThrowsAny<SmlException>(() => SmlReader.Read(source));
        if (fragment is not null)
            Assert.Contains(fragment, e.Message);
    }

    private static void Same(string a, string b) =>
        Assert.Null(DocumentComparer.FirstDifference(SmlReader.Read(a), SmlReader.Read(b)));

    [Fact]
    public void CP01_ByteOrderMarkIsIgnoredAndNeverWritten()
    {
        const string source = "svg(xmlns=http://www.w3.org/2000/svg, a=1)/\n";
        Same("\uFEFF" + source, source);
        Assert.Null(DocumentComparer.FirstDifference(SmlReader.Read(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(source)).ToArray()), SmlReader.Read(source)));
        Assert.False(SmlWriter.Write(SmlReader.Read("\uFEFF" + source)).StartsWith('\uFEFF'));
    }

    [Fact]
    public void CP02_LineEndings()
    {
        const string lf = "g\n  .a 1\n  = |\n    x\n    y\n  end\nend\n";
        Same(lf.Replace("\n", "\r\n"), lf);
        Same(lf.Replace("\n", "\r"), lf);
    }

    [Fact]
    public void CP03_UnicodeLineSeparatorsAreDataAndEscapedOnWrite()
    {
        Assert.Equal("x\u0085y\u2028z\u2029w", Root("g\n  .a x\u0085y\u2028z\u2029w\nend\n").Attributes["a"]);
        Assert.Contains("\\u{2028}", SmlWriter.Write(SmlReader.Read("g\n  .a x\u2028y\nend\n")));
    }

    [Theory]
    [InlineData("\"\\u{41}\"", "A")]
    [InlineData("\"\\u{1f600}\"", "\U0001F600")]
    [InlineData("\"\\u{01F600}\"", "\U0001F600")]
    public void CP04_UnicodeEscapes(string value, string expected) =>
        Assert.Equal(expected, Root($"g\n  .a {value}\nend\n").Attributes["a"]);

    [Theory]
    [InlineData(@"\u{}")]
    [InlineData(@"\u{001F600}")]
    [InlineData(@"\u{D800}")]
    [InlineData(@"\u{110000}")]
    [InlineData(@"\u{0}")]
    [InlineData(@"\u0041")]
    [InlineData(@"\x41")]
    public void CP04_CP05_InvalidEscapes(string escape) => Rejects($"g\n  .a \"{escape}\"\nend\n");

    [Theory]
    [InlineData("end()")]
    [InlineData("end/")]
    [InlineData("end(id=a)/")]
    [InlineData("end = x")]
    public void CP06_EndIsReserved(string statement) => Rejects($"g\n  {statement}\nend\n");

    [Fact]
    public void CP06_PrefixedEndIsAllowed() =>
        Assert.Equal("x:end", ((ElementNode)Root("g(xmlns:x=urn:x)\n  x:end/\nend\n").Children[0]).QualifiedName);

    [Fact]
    public void CP07_CheckedEnd()
    {
        Root("g\n  h\n  end h\nend g\n");
        Rejects("g\n  h\n  end g\nend\n", "does not match");
        Rejects("g\nend g h\n");
    }

    [Fact]
    public void CP08_CP09_BlockTerminators()
    {
        Assert.Equal("end path", Root("g\n  .a |\n    end path\n  end\nend\n").Attributes["a"]);
        Assert.Equal("x\nend", Root("g\n  .a |EOT\n    x\n    end\n  EOT\nend\n").Attributes["a"]);
        Rejects("g\n  .a |end\n    x\n  end\nend\n");
        Rejects("g\n  .a |1x\n    x\n  1x\nend\n");
    }

    [Fact]
    public void CP10_RawValuesCannotBeginWithBarOrQuote()
    {
        Rejects("g(a=|x)/\n");
        Rejects("@pi t |x\ng/\n");
        Rejects("g\n  @ns |x\nend\n");
        Assert.Equal("|x", Root("g(a=\"|x\")/\n").Attributes["a"]);
    }

    [Fact]
    public void CP11_CP12_InlineLists()
    {
        Assert.Equal("1", Root("a( x = 1 , y=2 )/\n").Attributes["x"]);
        ElementNode use = Root("use(transform=translate(10,20) rotate(45), fill=url(#a))/\n");
        Assert.Equal("translate(10,20) rotate(45)", use.Attributes["transform"]);
        Assert.Equal("url(#a)", use.Attributes["fill"]);
        Rejects("rect(a=(x)/\n");
        Rejects("rect(a=x)y)/\n");
    }

    [Fact]
    public void CP13_ContinuationLines()
    {
        ElementNode g = Root("g\n  .a \"one \"\n    \"two\"\n  = \"t1\"\n    \"t2\"\n  @comment \"c1\"\n    \"c2\"\nend\n");
        Assert.Equal("one two", g.Attributes["a"]);
        Assert.Equal("t1t2", ((TextNode)g.Children[0]).Data);
        Assert.Equal("c1c2", ((CommentNode)g.Children[1]).Data);
        Rejects("g\n  .a raw\n  \"x\"\nend\n", "continuation");
        Rejects("g\n  .a \"x\"\n\n  \"y\"\nend\n", "continuation");
        Rejects("g\n  @ns \"urn:x\"\n  \"y\"\nend\n", "continuation");
    }

    [Fact]
    public void CP14_BlockDedentation()
    {
        Assert.Equal("a\n\n  b\n\nc", Root("g\n  .a |\n    a\n\n      b\n   \t  \n    c\n  end\nend\n").Attributes["a"]);
        Assert.Equal("\ta\n  b", Root("g\n  .a |\n\ta\n  b\n  end\nend\n").Attributes["a"]);
        Assert.Equal("", Root("g\n  .a |\n  end\nend\n").Attributes["a"]);
    }

    [Fact]
    public void CP16_NamespacesMustComeFirst() => Rejects("g\n  .a 1\n  @ns urn:x\nend\n", "@ns must come");

    [Fact]
    public void CP17_CP18_CP19_NamespaceDeclarations()
    {
        Rejects("g\n  @ns s\nend\n", "must contain \":\"");
        Assert.Equal("relative", Root("g\n  @ns \"relative\"\nend\n").NamespaceDeclarations[""]);
        Rejects("g\n  .xmlns:p urn:x\nend\n");
        ElementNode x = Root("p:x(xmlns:p=urn:vendor, xmlns=urn:d)/\n");
        Assert.Equal(new ExpandedName("urn:vendor", "x"), x.Name);
        Rejects("g(xmlns:p=urn:a)\n  @ns p urn:b\nend\n", "declared twice");
        Root($"g\n  @ns xml {XmlRules.XmlNamespace}\nend\n");
        Rejects("g\n  @ns xml urn:other\nend\n");
        Rejects("g\n  @ns xmlns urn:x\nend\n");
    }

    [Fact]
    public void CP20_VersionHeader()
    {
        Rejects("@comment x\n@sml 1.0-SVG-3\ng/\n", "first statement");
        Rejects("@sml 1.0-SVG-2\ng/\n", "unsupported draft");
        Root("# note\n@sml 1.0-SVG-3\ng/\n");
        Root("@sml 1.0\ng/\n");
    }

    [Fact]
    public void CP21_CP22_Extensions()
    {
        Rejects("@use geo 1.0\ng/\n", "not implemented");
        Same("@use geo 1.0 optional\n@geo:grid 10\ng\n  .a 1\n  @geo:hint |\n    x\n  end\n  .b 2\nend\n",
             "g\n  .a 1\n  .b 2\nend\n");
    }

    [Fact]
    public void CP23_CanonicalFormIsUnique()
    {
        string[] variants =
        [
            "svg(xmlns=http://www.w3.org/2000/svg, width=10, d=x)\n  path(d=M0 0)/\nend\n",
            "svg\n  @ns http://www.w3.org/2000/svg\n  .d \"x\"\n  .width 10\n  path\n    .d M0 0\n  end\nend svg\n",
            "# c\nsvg\n  @ns \"http://www.w3.org/2000/svg\"\n  @block path p .d\n  .width \"1\"\n    \"0\"\n  .d x\n  path\n    p |\n      M0 0\n    end\n  end\nend\n",
        ];
        Assert.Single(variants.Select(v => SmlWriter.Write(SmlReader.Read(v))).Distinct());
    }

    [Fact]
    public void CP24_CanonicalWriterIsIdempotent()
    {
        const string source = "@indent 2\nsvg(xmlns=http://www.w3.org/2000/svg)\n  text\n    @noindent\n    tspan = A\n    = \" B\"\n  end\n  style = |EOT\n    a\n    end\n  EOT\nend\n";
        string once = SmlWriter.Write(SmlReader.Read(source));
        Assert.Equal(once, SmlWriter.Write(SmlReader.Read(once)));
    }

    [Fact]
    public void SV34_SV37_AliasesOnlyWithMarker()
    {
        ElementNode svg = Root($"svg\n  @ns {Svg}\n  @block path data .d\n  path\n    data |\n      M 0 0\n    end\n  end\nend\n");
        ElementNode path = (ElementNode)svg.Children[0];
        Assert.Equal(new ExpandedName(Svg, "path"), path.Name);
        Assert.Equal("M 0 0", path.Attributes["d"]);
        ElementNode g = Root("g\n  @block g data .d\n  data |\n    v\n  end\n  data\n    = child\n  end\nend\n");
        Assert.Equal("v", g.Attributes["d"]);
        Assert.Equal("data", ((ElementNode)g.Children[0]).QualifiedName);
        Rejects("g\n  data |\n    v\n  end\nend\n", "no active alias");
        Rejects("g\n  @block g data = .d\nend\n", "no longer valid");
    }

    [Fact]
    public void SV53_SV55_LinesAreTrimmed()
    {
        ElementNode g = Root($"g  \n  @ns {Svg}   \n  .x   value   \n  .q \"y\"   \n  .d |  \n    v\n  end\nend\n");
        Assert.Equal(Svg, g.NamespaceDeclarations[""]);
        Assert.Equal("value", g.Attributes["x"]);
        Assert.Equal("y", g.Attributes["q"]);
        Assert.Equal("v", g.Attributes["d"]);
    }

    [Fact]
    public void SV58_SV31_ProcessingInstructionAndCommentConstraints()
    {
        Rejects("@pi t \" x\"\ng/\n", "PI data");
        Rejects("g\n  @comment \"a\\rb\"\nend\n", "CR");
        Rejects("@pi a:b x\ng/\n", "NCName");
    }

    [Fact]
    public void SV60_SV64_IndentModel()
    {
        ElementNode svg = Root("@indent 2\nsvg\n  g\n    rect/\n  end\nend\n");
        Assert.Equal("\n  ", ((TextNode)svg.Children[0]).Data);
        Assert.Equal("\n    ", ((TextNode)((ElementNode)svg.Children[1]).Children[0]).Data);
        Rejects("@indent 2\ng\n  h/\n  = x\nend\n", "laid-out");
        Rejects("@indent 0\ng/\n");
    }

    [Fact]
    public void SV70_DoctypeIdentifiers()
    {
        Rejects("@doctype svg PUBLIC \"-//W3C//DTD  SVG\" \"x\"\nsvg/\n", "not normalized");
        Rejects("@doctype svg SYSTEM \"a\\\"b'c\"\nsvg/\n", "both quote");
        Rejects("@doctype svg SYSTEM \"a\\rb\"\nsvg/\n", "CR");
    }

    [Fact]
    public void SV71_SV72_BlockMarkers()
    {
        Same("g\n  .d|\n    v\n  end\nend\n", "g\n  .d |\n    v\n  end\nend\n");
        Assert.Equal("v", Root("g\n  .d|EOT\n    v\n  EOT\nend\n").Attributes["d"]);
        Rejects("g\n  .d |x y\nend\n", "invalid block marker");
    }

    [Fact]
    public void SupplementaryCharactersInNamesAndCodePointOrdering()
    {
        // A name using a character outside the BMP (U+10000) and attribute ordering by code point, not UTF-16 unit.
        string sml = SmlWriter.Write(SmlReader.Read("g\n  .\U00010000 1\n  .\uFFFD 2\nend\n"));
        Assert.True(sml.IndexOf('\uFFFD') < sml.IndexOf("\U00010000", StringComparison.Ordinal));
    }
}
