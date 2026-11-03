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
        const string lf = "g\n  .a 1\n  = \"\"\"\n    x\n    y\n    \"\"\"\nend\n";
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
        Assert.Equal("x:end", ((ElementNode)Root("@indent none\ng(xmlns:x=urn:x)\n  x:end/\nend\n").Children[0]).QualifiedName);

    [Fact]
    public void CP07_CheckedEnd()
    {
        Root("g\n  h\n  end h\nend g\n");
        Rejects("g\n  h\n  end g\nend\n", "does not match");
        Rejects("g\nend g h\n");
    }

    [Fact]
    public void CP08_CP09_BlockMarkers()
    {
        Assert.Equal("end path\nend", Root("g\n  .a \"\"\"\n    end path\n    end\n    \"\"\"\nend\n").Attributes["a"]);
        Assert.Equal("x\n\"\"\"", Root("g\n  .a \"\"\"\"\n    x\n    \"\"\"\n    \"\"\"\"\nend\n").Attributes["a"]);
        Rejects("g\n  .a \"\"\"\"\n    x\n  \"\"\"\nend\n", "unterminated block");
        Assert.Equal("", Root("g\n  .a \"\"\nend\n").Attributes["a"]);
        Rejects("g\n  .a \"\"\"x\nend\n");
    }

    [Fact]
    public void CP10_ValuesBeginningWithBar()
    {
        Assert.Equal("|x", Root("g(a=|x)/\n").Attributes["a"]);
        Assert.Equal("|x", ((ProcessingInstructionNode)SmlReader.Read("@pi t |x\ng/\n").Prolog[0]).Data);
        Assert.Equal("|x:y", Root("g\n  @ns |x:y\nend\n").NamespaceDeclarations[""]);
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
        ElementNode g = Root("g\n  .a \"one \"\n    \"two\"\n  = \"t1\"\n    \"t2\"\n  @rem \"c1\"\n    \"c2\"\nend\n");
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
        Assert.Equal("a\n\n  b\n\nc", Root("g\n  .a \"\"\"\n    a\n\n      b\n\n    c\n    \"\"\"\nend\n").Attributes["a"]);
        Assert.Equal("\ta\n  b", Root("g\n  .a \"\"\"\n\ta\n  b\n\"\"\"\nend\n").Attributes["a"]);
        Assert.Equal("", Root("g\n  .a \"\"\"\n  \"\"\"\nend\n").Attributes["a"]);
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
        Rejects("@rem x\n@sml 1.0-SVG-1\ng/\n", "first statement");
        Rejects("@sml 1.0-SVG-2\ng/\n", "unsupported draft");
        Root("# note\n@sml 1.0-SVG-1\ng/\n");
        Root("@sml 1.0\ng/\n");
        Root("@sml\t1.0  tidy\ng/\n");
        Same("@sml 1.0-SVG-1 tidy\ng(a=1)/\n", "g(a=1)/\n");
        foreach (string header in new[] { "@sml 1.0-SVG-1 neat", "@sml 1.0-SVG-1 tidy tidy", "@sml" })
            Rejects(header + "\ng/\n", "@sml takes a version");
        Rejects("@sml tidy\ng/\n", "unsupported SML version");
        Rejects("@sml 1.0-SVG-2 tidy\ng/\n", "unsupported draft");
    }

    [Fact]
    public void CP21_CP22_Extensions()
    {
        Rejects("@use geo 1.0\ng/\n", "not implemented");
        Same("@use geo 1.0 optional\n@geo:grid 10\ng\n  .a 1\n  @geo:hint \"\"\"\n    x\n    \"\"\"\n  .b 2\nend\n",
             "g\n  .a 1\n  .b 2\nend\n");
    }

    [Fact]
    public void CP23_CanonicalFormIsUnique()
    {
        string[] variants =
        [
            "svg(xmlns=http://www.w3.org/2000/svg, width=10, d=x)\n  path(d=M0 0)/\nend\n",
            "svg\n  @ns http://www.w3.org/2000/svg\n  .d \"x\"\n  .width 10\n  path\n    .d M0 0\n  end\nend svg\n",
            "# c\nsvg\n  @ns \"http://www.w3.org/2000/svg\"\n  @block path .d p\n  .width \"1\"\n    \"0\"\n  .d x\n  path\n    p \"\"\"\n      M0 0\n      \"\"\"\n  end\nend\n",
        ];
        Assert.Single(variants.Select(v => SmlWriter.Write(SmlReader.Read(v))).Distinct());
    }

    [Fact]
    public void CP24_CanonicalWriterIsIdempotent()
    {
        const string source = "@indent 2\nsvg(xmlns=http://www.w3.org/2000/svg)\n  text\n    @noindent\n    tspan = A\n    = \" B\"\n  end\n  style = \"\"\"\"\n    a\n    \"\"\"\n    end\n    \"\"\"\"\nend\n";
        string once = SmlWriter.Write(SmlReader.Read(source));
        Assert.Equal(once, SmlWriter.Write(SmlReader.Read(once)));
    }

    [Fact]
    public void SV30_Standalone()
    {
        // standalone="no" is the same as no flag (XML 1.0 §2.9): it is read as none and never written.
        foreach (string declaration in new[] { "<?xml version=\"1.0\" standalone=\"no\"?>", "<?xml version=\"1.0\"?>", "" })
        {
            SmlDocument doc = XmlImporter.Read(Encoding.UTF8.GetBytes(declaration + "<g/>"));
            Assert.Null(doc.Standalone);
            Assert.Equal("@sml 1.0-SVG-1\ng/\n", SmlWriter.Write(doc));
            Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<g/>\n", XmlExporter.Write(doc));
        }
        Same("@standalone no\ng/\n", "g/\n");
        SmlDocument yes = XmlImporter.Read(Encoding.UTF8.GetBytes("<?xml version=\"1.0\" standalone=\"yes\"?><g/>"));
        Assert.Equal("@sml 1.0-SVG-1\n@standalone yes\ng/\n", SmlWriter.Write(yes));
        Assert.Contains("standalone=\"yes\"", XmlExporter.Write(SmlReader.Read("@standalone yes\ng/\n")));
        Rejects("@standalone\ng/\n", "yes or no");
    }

    [Fact]
    public void SV34_SV37_AliasesOnlyWithMarker()
    {
        ElementNode svg = Root($"@indent none\nsvg\n  @ns {Svg}\n  @block path .d data\n  path\n    data \"\"\"\n      M 0 0\n      \"\"\"\n  end\nend\n");
        ElementNode path = (ElementNode)svg.Children[0];
        Assert.Equal(new ExpandedName(Svg, "path"), path.Name);
        Assert.Equal("M 0 0", path.Attributes["d"]);
        ElementNode g = Root("@indent none\ng\n  @block g .d data\n  data \"\"\"\n    v\n    \"\"\"\n  data\n    = child\n  end\nend\n");
        Assert.Equal("v", g.Attributes["d"]);
        Assert.Equal("data", ((ElementNode)g.Children[0]).QualifiedName);
        Rejects("g\n  data \"\"\"\n    v\n    \"\"\"\nend\n", "no active alias");
        foreach (string declaration in new[] { "@block g data .d", "@block g .d", "@block g .d data x" })
            Rejects($"g\n  {declaration}\nend\n", "@block takes: element-qname .attribute-qname alias");
    }

    [Fact]
    public void SV45_SV56_VerbatimBlocks()
    {
        foreach (string value in new[] { " a\n b", "a \nb\t", "a\n", "\na", "\n  x\n  " })
            Assert.Equal("\"\"\"", SmlWriter.BlockTag(value));
        foreach (string value in new[] { "a\r\nb", "a\n\u2028", "\n\n", "\n \t\n" })
            Assert.Null(SmlWriter.BlockTag(value));

        // Trailing whitespace is data; only the closing marker's indentation comes off each line.
        Assert.Equal("x  \n\ty\t", Root("g\n  .a \"\"\"\n    x  \n    \ty\t\n    \"\"\"\nend\n").Attributes["a"]);
        Assert.Equal("  x\n  y", Root("g\n  .a \"\"\"\n      x\n      y\n    \"\"\"\nend\n").Attributes["a"]);
        // A line of spaces and tabs that does not begin with that indentation is empty; one that does keeps the rest.
        Assert.Equal("x\n\n\n  ", Root("g\n  .a \"\"\"\n    x\n\n  \n      \n    \"\"\"\nend\n").Attributes["a"]);
        Rejects("g\n  .a \"\"\"\n    x\n   y\n    \"\"\"\nend\n", "closing marker's indentation");
        Rejects("g\n  .a \"\"\"\n    x\n\ty\n    \"\"\"\nend\n", "closing marker's indentation");

        // The writer copies such text exactly: Inkscape-style CSS indented with tabs, with trailing whitespace.
        SmlDocument doc = XmlImporter.Read(Encoding.UTF8.GetBytes("<style>\n\t\ta {\t \n\t\t}\n    </style>"));
        string sml = SmlWriter.Write(doc);
        Assert.Equal("@sml 1.0-SVG-1\n@indent none\nstyle = \"\"\"\n\n  \t\ta {\t \n  \t\t}\n      \n  \"\"\"\n", sml);
        Assert.Null(DocumentComparer.FirstDifference(doc, SmlReader.Read(sml)));
    }

    [Fact]
    public void SV53_SV55_LinesAreTrimmed()
    {
        ElementNode g = Root($"g  \n  @ns {Svg}   \n  .x   value   \n  .q \"y\"   \n  .d \"\"\"  \n    v\n    \"\"\"\nend\n");
        Assert.Equal(Svg, g.NamespaceDeclarations[""]);
        Assert.Equal("value", g.Attributes["x"]);
        Assert.Equal("y", g.Attributes["q"]);
        Assert.Equal("v", g.Attributes["d"]);
    }

    [Fact]
    public void SV58_SV31_ProcessingInstructionAndCommentConstraints()
    {
        Rejects("@pi t \" x\"\ng/\n", "PI data");
        Rejects("g\n  @rem \"a\\rb\"\nend\n", "CR");
        Rejects("@pi a:b x\ng/\n", "NCName");
    }

    [Fact]
    public void SV60_SV64_IndentModel()
    {
        ElementNode svg = Root("@indent 2\nsvg\n  g\n    rect/\n  end\nend\n");
        Assert.Equal("\n  ", ((TextNode)svg.Children[0]).Data);
        Assert.Equal("\n    ", ((TextNode)((ElementNode)svg.Children[1]).Children[0]).Data);
        Rejects("@indent 2\ng\n  h/\n  = x\nend\n", "laid-out");
        Root("@indent 0\ng/\n");
        Rejects("@indent 01\ng/\n");
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
        Same("g\n  .d\"\"\"\n    v\n    \"\"\"\nend\n", "g\n  .d \"\"\"\n    v\n    \"\"\"\nend\n");
        Same("g\n  @block g .y x\n  x\"\"\"\n    v\n    \"\"\"\nend\n", "g\n  .y \"\"\"\n    v\n    \"\"\"\nend\n");
        ElementNode bars = Root("@indent none\ng(a=|x, b=|)\n  .c |x y\n  @rem |c\n  @pi t |p\n  = |\nend\n");
        Assert.Equal(("|x", "|", "|x y"), (bars.Attributes["a"], bars.Attributes["b"], bars.Attributes["c"]));
        Assert.Equal("|c", ((CommentNode)bars.Children[0]).Data);
        Assert.Equal("|p", ((ProcessingInstructionNode)bars.Children[1]).Data);
        Assert.Equal("|", ((TextNode)bars.Children[2]).Data);
        Assert.Equal("@sml 1.0-SVG-1\ng(a=|x) = |\n", SmlWriter.Write(SmlReader.Read("g(a=|x)\n  = |\nend\n")));
        Rejects("g\n  .d|x\nend\n", "expected a space");
        Rejects("g\n  x |\nend\n", "unexpected ' |' after element name");
    }

    private static List<string> Comments(ElementNode element) =>
        element.Children.OfType<CommentNode>().Select(c => c.Data).ToList();

    [Fact]
    public void SV77_CommentRuns()
    {
        // @comment is a synonym of @rem.
        Same("g\n  @comment \" a\"\n  @comment\"\"\"\n    b\n    c\n    \"\"\"\nend\n", "g\n  @rem \" a\"\n  @rem\"\"\"\n    b\n    c\n    \"\"\"\nend\n");
        Assert.Equal(["a", "# b", "end"], Comments(Root("g\n  @comments \"\"\"\n    a\n    # b\n    end\n    \"\"\"\n  h/\nend\n")));
        ElementNode laid = Root("@indent 2\ng\n  @comments \"\"\"\n    a\n    b\n    \"\"\"\nend\n");
        Assert.Equal(["\n  ", "a", "\n  ", "b", "\n"],
            laid.Children.Select(c => c is CommentNode comment ? comment.Data : ((TextNode)c).Data).ToList());
        Assert.Equal(["p", "q"], SmlReader.Read("@comments \"\"\"\n  p\n  q\n  \"\"\"\ng/\n").Prolog.Cast<CommentNode>().Select(c => c.Data).ToList());
        Rejects("g\n  @comments \"\"\"\n    a\n\n    b\n    \"\"\"\nend\n", "blank lines");
        Rejects("g\n  @comments \"\"\"\n  \"\"\"\nend\n", "blank lines");
        Rejects("g\n  @comments a\nend\n", "takes a block");
        Rejects("g\n  @comments \"\"\"\n    a--b\n    \"\"\"\nend\n", "'--'");
        Rejects("g\n  @comments \"\"\"\n    x\n    \"\"\"\n  .a 1\nend\n", "after the element");
        Rejects("g\n  @comments \"\"\"\n    a\n     \t\n    b\n    \"\"\"\nend\n", "blank lines");
        // Each line is trimmed like any comment, so a comment written with spaces at its ends joins the run.
        Assert.Equal(["p", "q"], SmlReader.Read("@comments \"\"\"\n  p  \n  \tq\n  \"\"\"\ng/\n").Prolog.Cast<CommentNode>().Select(c => c.Data).ToList());
        SmlDocument doc = SmlReader.Read("g\n  @rem a\n  @rem \" b\"\n  @rem c\n  @rem d\n  @rem \"\\\"\\\"\\\"\"\nend\n");
        Assert.Equal(["a", "b", "c", "d", "\"\"\""], Comments(doc.Root));
        string sml = SmlWriter.Write(doc);
        Assert.Contains("  @comments \"\"\"\"\n    a\n    b\n    c\n    d\n    \"\"\"\n    \"\"\"\"\n", sml);
        Assert.Null(DocumentComparer.FirstDifference(doc, SmlReader.Read(sml)));
    }

    [Fact]
    public void SV78_CommentsAreNormalized()
    {
        // A comment loses the whitespace at its ends, and a multi-line one the indentation its lines share (§9), from
        // XML and from SML alike.
        SmlDocument doc = XmlImporter.Read(Encoding.UTF8.GetBytes("<!--\n  top\n-->\n<g>\n  <!--\n    Usage:\n      a\n\n      b\n  -->\n"
            + "  <h>\n    <!-- deep -->\n  </h>\n  <!-- Starts at once\n       and goes on -->\n  <!--   ends with a dash -   -->\n  <!--\t-->\n</g>"));
        Assert.Equal("top", ((CommentNode)doc.Prolog[0]).Data);
        Assert.Equal(["Usage:\n  a\n\n  b", "Starts at once\nand goes on", "ends with a dash -", ""], Comments(doc.Root));
        string sml = SmlWriter.Write(doc);
        Assert.Equal("@sml 1.0-SVG-1\n@rem top\ng\n  @rem \"\"\"\n    Usage:\n      a\n\n      b\n    \"\"\"\n  h\n    @rem deep\n"
            + "  end\n  @rem \"\"\"\n    Starts at once\n    and goes on\n    \"\"\"\n  @rem ends with a dash -\n  @rem\nend\n", sml);
        Assert.Null(DocumentComparer.FirstDifference(doc, SmlReader.Read(sml)));

        // XML output: a one-line comment has a space on each side, and a multi-line one is pretty-printed, its lines one
        // step deeper than the comment.
        string xml = XmlExporter.Write(doc);
        Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!-- top -->\n<g>\n  <!--\n    Usage:\n      a\n\n      b\n  -->\n"
            + "  <h>\n    <!-- deep -->\n  </h>\n  <!--\n    Starts at once\n    and goes on\n  -->\n"
            + "  <!-- ends with a dash - -->\n  <!---->\n</g>\n", xml);
        Assert.Null(DocumentComparer.FirstDifference(doc, XmlImporter.Read(Encoding.UTF8.GetBytes(xml))));

        // The step is the document's: four spaces, a tab.
        foreach (string unit in new[] { "    ", "\t" })
        {
            string source = $"<g>\n{unit}<h>\n{unit}{unit}<!--\n{unit}{unit}{unit}a\n{unit}{unit}{unit}  b\n{unit}{unit}-->\n{unit}</h>\n</g>";
            Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + source + "\n", XmlExporter.Write(XmlImporter.Read(Encoding.UTF8.GetBytes(source))));
        }

        // In SML every line takes part, whatever the value's form; a block is literal wherever it is.
        ElementNode g = Root("g\n  @rem \" x \"\n  @rem \"\\n  y\\n\"\n  @comment \"\"\"\n\n    z\n\n    \"\"\"\n"
            + "  @rem \"\"\"\n      a\n        b\n    \"\"\"\n  @rem \"\\t\"\n  @rem \" p\\n q\"\nend\n");
        Assert.Equal(["x", "y", "z", "a\n  b", "", "p\nq"], Comments(g));
        Assert.Equal(["a"], Comments(Root("@indent none\ng\n  @rem \"\"\"\n    a\n    \"\"\"\nend\n")));
        Assert.Equal(["x-"], Comments(Root("g\n  @rem \"x-\"\nend\n")));
        Rejects("g\n  @rem \"a--b\"\nend\n", "'--'");
    }

    [Fact]
    public void SV79_Indent0()
    {
        ElementNode g = Root("@indent 0\ng\n  h/\n  k\n    m/\n  end\nend\n");
        Assert.Equal(["\n", "\n", "\n"], g.Children.OfType<TextNode>().Select(t => t.Data).ToList());
        Assert.Equal(["\n", "\n"], g.Children.OfType<ElementNode>().ElementAt(1).Children.OfType<TextNode>().Select(t => t.Data).ToList());
        SmlDocument doc = XmlImporter.Read(Encoding.UTF8.GetBytes("<g>\n<h/>\n<k>\n<m/>\n</k>\n<!--\nline\n-->\n</g>"));
        string sml = SmlWriter.Write(doc);
        Assert.Equal("@sml 1.0-SVG-1\n@indent 0\ng\n  h/\n  k\n    m/\n  end\n  @rem line\nend\n", sml);
        Assert.Null(DocumentComparer.FirstDifference(doc, SmlReader.Read(sml)));
    }

    [Fact]
    public void SV80_LineBreaksAreQuoted()
    {
        Assert.Null(SmlWriter.BlockTag("\n"));
        Assert.Null(SmlWriter.BlockTag("\n\n"));
        Assert.Equal("\"\"\"", SmlWriter.BlockTag("a\n"));
        SmlDocument doc = XmlImporter.Read(Encoding.UTF8.GetBytes("<g a=\"&#10;\"><t>\n</t><h/>\n\n<h/><!--\n--></g>"));
        string sml = SmlWriter.Write(doc);
        foreach (string fragment in new[] { ".a \"\\n\"", "t = \"\\n\"", "= \"\\n\\n\"", "  @rem\n" })
            Assert.Contains(fragment, sml);
        Assert.DoesNotContain("\"\"\"", sml);
        Assert.Null(DocumentComparer.FirstDifference(doc, SmlReader.Read(sml)));
        Assert.Equal("\n", Root("g\n  = \"\"\"\n\n\n  \"\"\"\nend\n").Children.OfType<TextNode>().First().Data);
    }

    private static List<string> Texts(ElementNode element) =>
        element.Children.OfType<TextNode>().Select(t => t.Data).ToList();

    [Fact]
    public void SV81_LocalIndent()
    {
        const string source = "@indent 2\ng\n  h\n    @indent 4\n    a/\n    b\n      c/\n    end\n  end\n  k\n    @indent tab\n    m/\n  end\nend\n";
        SmlDocument doc = SmlReader.Read(source);
        ElementNode h = doc.Root.Children.OfType<ElementNode>().ElementAt(0);
        Assert.Equal(["\n      ", "\n      ", "\n  "], Texts(h));
        Assert.Equal(["\n        ", "\n      "], Texts(h.Children.OfType<ElementNode>().ElementAt(1)));
        Assert.Equal(["\n  \t", "\n  "], Texts(doc.Root.Children.OfType<ElementNode>().ElementAt(1)));
        Assert.Equal("@sml 1.0-SVG-1\n" + source.Replace("@indent 2\n", ""), SmlWriter.Write(doc));
        Rejects("@indent none\ng\n  @indent 4\n  h/\nend\n", "not allowed under @indent none");
        Rejects("@indent 2\ng\n  @indent none\nend\n", "width from 0 to 16");
        Rejects("@indent 2\ng\n  @indent 4\n  @indent 2\nend\n", "only once");
        Rejects("@indent 2\ng\n  h/\n  @indent 4\nend\n", "after the element");
        Rejects("@indent 2\ng\n  @indent 3x\nend\n", "width from 0 to 16");
        Rejects("@indent 2\ng/\n@indent 4\n", "only allowed before the root");
    }

    [Fact]
    public void SV82_LaidOutText()
    {
        const string source = "@indent 2\ng\n  p = \"\"\"\n    one\n\n    two\n    \"\"\"\n  q\n    @indent 4\n    r = \"\"\"\n      deep\n      \"\"\"\n  end\nend\n";
        SmlDocument doc = SmlReader.Read(source);
        List<ElementNode> elements = doc.Root.Children.OfType<ElementNode>().ToList();
        Assert.Equal("\n    one\n\n    two\n  ", ((TextNode)elements[0].Children[0]).Data);
        Assert.Equal("\n        deep\n      ", ((TextNode)elements[1].Children.OfType<ElementNode>().First().Children[0]).Data);
        Assert.Equal("@sml 1.0-SVG-1\n" + source.Replace("@indent 2\n", ""), SmlWriter.Write(doc));

        // The normal form, @noindent parents, mixed content, and documents without @indent stay literal.
        foreach (string literal in new[]
        {
            "@indent 2\ng\n  p\n    = \"\"\"\n      a\n      b\n      \"\"\"\n  end\nend\n",
            "@indent 2\ng\n  @noindent\n  p = \"\"\"\n    a\n    b\n    \"\"\"\nend\n",
            "@indent 2\ng\n  = x\n  p = \"\"\"\n    a\n    b\n    \"\"\"\nend\n",
            "@indent none\ng\n  p = \"\"\"\n    a\n    b\n    \"\"\"\nend\n",
        })
            Assert.Equal("a\nb", ((TextNode)Root(literal).Children.OfType<ElementNode>().First().Children[0]).Data);

        // In a laid-out position, text without the layout uses the normal form, whose block is literal; text a block
        // cannot hold, such as line breaks only, is quoted.
        SmlDocument xml = XmlImporter.Read(Encoding.UTF8.GetBytes("<g>\n  <p>a\nb</p>\n  <p>\n  flush\n  </p>\n  <p>\n    x\n  </p>\n</g>"));
        string sml = SmlWriter.Write(xml);
        Assert.Contains("  p\n    = \"\"\"\n      a\n      b\n      \"\"\"\n  end\n", sml);
        Assert.Contains("  p\n    = \"\"\"\n\n        flush\n        \n      \"\"\"\n  end\n", sml);
        Assert.Contains("  p = \"\\n\\n\"\n", SmlWriter.Write(XmlImporter.Read(Encoding.UTF8.GetBytes("<g>\n  <p>\n\n</p>\n</g>"))));
        Assert.Contains("  p = \"\"\"\n    x\n    \"\"\"\n", sml);
        Assert.Null(DocumentComparer.FirstDifference(xml, SmlReader.Read(sml)));
    }

    [Fact]
    public void SV83_NodeAliases()
    {
        const string source = "@indent none\n@node g group\n@node p para\nsvg(xmlns=http://www.w3.org/2000/svg)\n  group(id=a)\n    rect/\n  end group\n"
            + "  group/\n  g = plain\n  foreignObject\n    @node div box\n    box(xmlns=http://www.w3.org/1999/xhtml)\n"
            + "      para = Hello\n      @element para\n      end para\n    end div\n  end\n  box/\nend\n";
        SmlDocument doc = SmlReader.Read(source);
        SmlDocument xml = XmlImporter.Read(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><g id=\"a\"><rect/></g><g/><g>plain</g>"
            + "<foreignObject><div xmlns=\"http://www.w3.org/1999/xhtml\"><p>Hello</p><para/></div></foreignObject><box/></svg>"));
        Assert.Null(DocumentComparer.FirstDifference(doc, xml));
        Assert.DoesNotContain("@node", SmlWriter.Write(doc));

        // The alias stands for the name as written: its prefix is resolved where the element is opened.
        ElementNode info = Root("@node d:info info\nsvg\n  info(xmlns:d=urn:d)/\nend\n").Children.OfType<ElementNode>().First();
        Assert.Equal(("d:info", "urn:d", "info"), (info.QualifiedName, info.Name.Namespace, info.Name.Local));

        // Shadowing in a nested scope; aliases do not chain.
        Assert.Equal(["p", "h"], Root("@node g x\nsvg\n  @node p x\n  x/\n  h\n    x/\n  end\nend\n").Children.OfType<ElementNode>().Select(e => e.QualifiedName));
        Assert.Equal(["x"], Root("svg\n  @node g x\n  @node x y\n  y/\nend\n").Children.OfType<ElementNode>().Select(e => e.QualifiedName));

        Rejects("@node g\nsvg/\n", "takes");
        Rejects("@node g end\nsvg/\n", "invalid element alias");
        Rejects("@node g a:b\nsvg/\n", "invalid element alias");
        Rejects("@node 1x y\nsvg/\n", "invalid element name");
        Rejects("@node g x\n@node p x\nsvg/\n", "twice");
        Rejects("svg\n  @node g x\n  @node p x\nend\n", "twice");
        Rejects("svg/\n@node g x\n", "only allowed before the root");
        Rejects("svg\n  a/\n  @node g x\nend\n", "after the element");
        Rejects("@node g group\ngroup\nend p\n", "does not match");
    }

    [Fact]
    public void SV84_NestedIndentation()
    {
        // Pretty-printed XML nested one level deeper than its root, as SVG copied out of HTML: the root is exact with
        // its own step, and everything below it is laid out.
        SmlDocument doc = XmlImporter.Read(Encoding.UTF8.GetBytes("<svg xmlns=\"urn:s\">\n    <g>\n      <rect/>\n    </g>\n    <rect/>\n  </svg>"));
        string sml = SmlWriter.Write(doc);
        Assert.Equal("@sml 1.0-SVG-1\nsvg(xmlns=urn:s)\n  @indent 4\n  = \"\\n    \"\n  g\n    rect/\n  end\n"
            + "  = \"\\n    \"\n  rect/\n  = \"\\n  \"\nend\n", sml);
        Assert.Null(DocumentComparer.FirstDifference(doc, SmlReader.Read(sml)));
        // An exact element takes its own step only if that lays out more of its child elements.
        Assert.DoesNotContain("@indent 4", SmlWriter.Write(XmlImporter.Read(Encoding.UTF8.GetBytes("<g>\n  <h>\n      <rect/>\n    <rect/></h>\n</g>"))));
        // An exact element's own step may be zero spaces.
        SmlDocument zero = XmlImporter.Read(Encoding.UTF8.GetBytes("<g>\n  <a>\n    <b/>\n  </a>\n  <c>\n    <d/>\n  </c>\n  <h>\n  <i>\n  <j/>\n  </i>\n    </h>\n</g>"));
        string zeroSml = SmlWriter.Write(zero);
        Assert.Contains("  h\n    @indent 0\n    = \"\\n  \"\n    i\n      @indent 0\n      j/\n    end\n", zeroSml);
        Assert.Null(DocumentComparer.FirstDifference(zero, SmlReader.Read(zeroSml)));
        // A uniformly indented document needs no per-element steps.
        foreach (var (xml, unit) in new[] { ("<g>\n    <h>\n        <i/>\n    </h>\n</g>", "@indent 4"), ("<g>\n\t<h>\n\t\t<i/>\n\t</h>\n</g>", "@indent tab") })
        {
            string output = SmlWriter.Write(XmlImporter.Read(Encoding.UTF8.GetBytes(xml)));
            Assert.Contains(unit + "\n", output);
            Assert.Single(output.Split('\n'), line => line.Contains("@indent"));
        }
    }

    [Fact]
    public void SupplementaryCharactersInNamesAndCodePointOrdering()
    {
        // A name using a character outside the BMP (U+10000) and attribute ordering by code point, not UTF-16 unit.
        string sml = SmlWriter.Write(SmlReader.Read("g\n  .\U00010000 1\n  .\uFFFD 2\nend\n"));
        Assert.True(sml.IndexOf('\uFFFD') < sml.IndexOf("\U00010000", StringComparison.Ordinal));
    }
}
