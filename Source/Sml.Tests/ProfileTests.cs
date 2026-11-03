using System.Text;

namespace Sml.Tests;

/// <summary>Tests of SML profiles: value layouts, the profile format, and the profile-aware writer, including both
/// modes over every fixture and the real-world corpus (Tests/svg-roundtrip/corpus and Tests/svg-corpus).</summary>
public class ProfileTests
{
    private static readonly SmlProfile Svg =
        SmlProfile.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "svg.smlprofile"), Encoding.UTF8));

    /// <summary>The svg profile with more element aliases (§8.7), including names that elements of the corpus have.</summary>
    private static readonly SmlProfile SvgWithNodes = SmlProfile.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "svg.smlprofile"), Encoding.UTF8)
        + "\n@node svg drawing\n@node path shape\n@node linearGradient gradient\n@node title desc\n");

    internal static readonly string TestsFolder = FindTestsFolder();

    private static string FindTestsFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "Tests");
            if (Directory.Exists(Path.Combine(candidate, "svg-corpus")))
                return candidate;
        }
        throw new DirectoryNotFoundException("Tests/svg-corpus not found above " + AppContext.BaseDirectory);
    }

    // ---------------------------------------------------------------------------------------------
    // Layouts

    private static readonly Layout PathData = new(LayoutKind.Path);
    private static readonly Layout Pairs = new(LayoutKind.Numbers, 2);
    private static readonly Layout Declarations = new(LayoutKind.Declarations);
    private static readonly Layout Functions = new(LayoutKind.Functions);
    private static readonly Layout List = new(LayoutKind.List);

    private static string[]? ItemTexts(string value, Layout layout) =>
        ValueLayout.Items(value, layout)?.Select(item => value[item.Start..item.End]).ToArray();

    public static TheoryData<string, Layout, string[]> LayoutCases() => new()
    {
        { "M 20 60 L 60 20 Z", PathData, ["M 20 60", "L 60 20", "Z"] },
        { "M10-20l5.5.5e2-3z", PathData, ["M10-20", "l5.5.5e2-3", "z"] },
        { "  M0,0\n\t\tC1,2 3,4 5,6  ", PathData, ["M0,0", "C1,2 3,4 5,6"] },
        { "0,0 5,10 10,0", Pairs, ["0,0", "5,10", "10,0"] },
        { "1 2 3 4 5", Pairs, ["1 2", "3 4", "5"] },
        { "1,2,3,4", Pairs, ["1,2", "3,4"] },
        { "1-2-3-4", Pairs, ["1-2", "-3-4"] },
        { "1 0 0 0 0 0 1 0 0 0", new Layout(LayoutKind.Numbers, 5), ["1 0 0 0 0", "0 1 0 0 0"] },
        { "fill: none; stroke: black", Declarations, ["fill: none;", "stroke: black"] },
        { "font-family: 'a;b'; fill: red;", Declarations, ["font-family: 'a;b';", "fill: red;"] },
        { "fill: url(data:a;b); /* x;y */ c: d", Declarations, ["fill: url(data:a;b);", "/* x;y */ c: d"] },
        { "translate(10,20) rotate(45)", Functions, ["translate(10,20)", "rotate(45)"] },
        { "translate(1,2),scale(3)", Functions, ["translate(1,2)", "scale(3)"] },
        { "0;10;0", List, ["0;", "10;", "0"] },
        { " 0 ; 1 ", List, ["0 ;", "1"] },
    };

    [Theory]
    [MemberData(nameof(LayoutCases))]
    public void DividesValuesIntoItems(string value, Layout layout, string[] expected)
    {
        Assert.Equal(expected, ItemTexts(value, layout));
        Assert.Equal(value, string.Concat(ValueLayout.Segments(value, layout)!));
    }

    [Theory]
    [InlineData("M 0 0 X 1", "path")]
    [InlineData("10 10 L 1 1", "path")]
    [InlineData("", "path")]
    [InlineData("1,,2", "pairs")]
    [InlineData("1,2,", "pairs")]
    [InlineData("1e", "pairs")]
    [InlineData("a b", "pairs")]
    [InlineData("content: 'open", "declarations")]
    [InlineData("fill: url(a", "declarations")]
    [InlineData("translate 10", "functions")]
    [InlineData("rotate(45", "functions")]
    [InlineData("10 rotate(45)", "functions")]
    public void RejectsValuesWithoutTheLayoutsForm(string value, string kind)
    {
        Layout layout = kind switch
        {
            "path" => PathData,
            "pairs" => Pairs,
            "declarations" => Declarations,
            _ => Functions,
        };
        Assert.Null(ValueLayout.Items(value, layout));
        Assert.Null(ValueLayout.Reflow(value, layout));
    }

    [Fact]
    public void ReflowsOneItemPerLineOnlyWhenThatIsClean()
    {
        Assert.Equal("M 20 60\nL 60 20\nZ", ValueLayout.Reflow("M 20 60 L 60 20 Z", PathData));
        Assert.Equal("1, 2\n3, 4", ValueLayout.Reflow("1,2,3,4", Pairs));
        Assert.Equal("M 0 0\nL 1 1", ValueLayout.Reflow("M0 0L1 1", PathData));
        Assert.Equal("M 10, 20\nL 30, -40\nc 1, 2 3, 4 5, 6\nz", ValueLayout.Reflow("M10,20L30,-40c1,2 3,4 5,6z", PathData));
        Assert.Equal("M 0 0,\nL 1, 1", ValueLayout.Reflow("M 0 0,L 1, 1", PathData));
        Assert.Equal("a .5 .5 0 0 0 -1 0\nZ", ValueLayout.Reflow("a.5.5 0 0 0-1 0Z", PathData));
        Assert.Equal("10, 20\n30, 40", ValueLayout.Reflow("10,20 30,40", Pairs));
        Assert.Equal("1 -2\n-3.5 .5", ValueLayout.Reflow("1-2-3.5.5", Pairs));
        Assert.Equal("1 0 0 0 0\n0 1 0 0 0", ValueLayout.Reflow("1 0 0 0 0 0 1 0 0 0", new Layout(LayoutKind.Numbers, 5)));
        Assert.Null(ValueLayout.Reflow("M 0 0", PathData));             // a single item
        Assert.Null(ValueLayout.Reflow("M 0\t0 L 1 1", PathData));      // TAB inside an item
        Assert.Null(ValueLayout.Segments("rotate(45)", Functions));
    }

    [Theory]
    [InlineData("a.75.75 0 0 0-.75-.75", "a .75 .75 0 0 0 -.75 -.75")]
    [InlineData("M10,20", "M 10, 20")]
    [InlineData("M10-20", "M 10 -20")]
    [InlineData("c1e-5-2.5.5 1E2,3", "c 1e-5 -2.5 .5 1E2, 3")]
    [InlineData("a1 1 0 011 1", "a 1 1 0 0 1 1 1")]      // arc flags are single characters
    [InlineData("a.75.75 0 01-.69-.35", "a .75 .75 0 0 1 -.69 -.35")]
    [InlineData("A1 1 0 1 0 5 5 2 2 0 0110 10", "A 1 1 0 1 0 5 5 2 2 0 0 1 10 10")]
    [InlineData("a1 1 0 2 1 1 1", "a 1 1 0 2 1 1 1")]   // not a flag: only the letter and commas are spaced
    [InlineData("M1,,2", "M 1, , 2")]                  // two commas: not path parameters; letter and commas spaced
    [InlineData("M 0 0,", "M 0 0,")]
    [InlineData("z", "z")]
    [InlineData("L1e 2", "L 1e 2")]                     // not path numbers: only the letter and commas are spaced
    public void SpacesPathCommands(string command, string expected) =>
        Assert.Equal(expected, ValueLayout.SpacePathCommand(command));

    [Theory]
    [InlineData("10,20", "10, 20")]
    [InlineData("10, 20", "10, 20")]
    [InlineData("1-2", "1 -2")]
    [InlineData(".5.5", ".5 .5")]
    [InlineData("1e2,-3", "1e2, -3")]
    [InlineData("1 0 0 0 0", "1 0 0 0 0")]
    [InlineData("a,b", "a,b")]                          // not numbers: unchanged
    public void SpacesNumbers(string numbers, string expected) =>
        Assert.Equal(expected, ValueLayout.SpaceNumbers(numbers));

    // ---------------------------------------------------------------------------------------------
    // Profile format

    [Fact]
    public void ParsesTheSvgProfile()
    {
        const string svg = "http://www.w3.org/2000/svg";
        Assert.Contains(Svg.Aliases, a => a == new SmlProfile.Alias(new(svg, "path"), "data", new(null, "d")));
        Assert.Equal(new Layout(LayoutKind.Path), Svg.FindLayout(new(svg, "path"), new(null, "d")));
        Assert.Equal(new Layout(LayoutKind.Numbers, 2), Svg.FindLayout(new(svg, "polygon"), new(null, "points")));
        Assert.Equal(new Layout(LayoutKind.Declarations), Svg.FindLayout(new(svg, "rect"), new(null, "style")));
        Assert.Equal(new Layout(LayoutKind.Declarations), Svg.FindLayout(new("urn:other", "p"), new(null, "style")));
        Assert.Null(Svg.FindLayout(new(svg, "rect"), new(null, "d")));
        Assert.Null(Svg.FindAlias(new(svg, "rect"), new(null, "d")));
    }

    [Fact]
    public void AnElementsOwnLayoutWinsOverTheOneForAnyElement()
    {
        SmlProfile profile = SmlProfile.Parse("@layout * .values list\n@layout m .values numbers 5\n");
        Assert.Equal(new Layout(LayoutKind.Numbers, 5), profile.FindLayout(new(null, "m"), new(null, "values")));
        Assert.Equal(new Layout(LayoutKind.List), profile.FindLayout(new(null, "a"), new(null, "values")));
    }

    [Theory]
    [InlineData("@ns urn:a\n@ns urn:b\n", 2, "the default namespace is declared twice")]
    [InlineData("@block p .d d\n@ns urn:a\n", 2, "@ns must come before every other statement")]
    [InlineData("@block s:path .d d\n", 1, "prefix 's' is not bound")]
    [InlineData("@block path .d d\n@block path .e d\n", 2, "alias 'd' is declared twice")]
    [InlineData("@block path .d d\n@block path .d data\n", 2, "already has an alias")]
    [InlineData("@block path .d end\n", 1, "invalid alias name 'end'")]
    [InlineData("@block path d d\n", 1, "expected an attribute name starting with '.'")]
    [InlineData("@block path d\n", 1, "@block takes")]
    [InlineData("@block path data .d\n", 1, "expected an attribute name starting with '.'")]
    [InlineData("@layout path .d path\n@layout path .d list\n", 2, "already has a layout")]
    [InlineData("@layout path .d numbers 0\n", 1, "numbers layout takes a count")]
    [InlineData("@layout path .d numbers\n", 1, "numbers layout takes a count")]
    [InlineData("@layout path .d curves\n", 1, "unknown layout 'curves'")]
    [InlineData("# comment\n\n.d x\n", 3, "unknown profile statement")]
    [InlineData("@block path .xmlns d\n", 1, "cannot name a namespace declaration")]
    [InlineData("@node g\n", 1, "@node takes")]
    [InlineData("@node g end\n", 1, "invalid element alias 'end'")]
    [InlineData("@node g a:b\n", 1, "invalid element alias 'a:b'")]
    [InlineData("@node g x\n@node p x\n", 2, "element alias 'x' is declared twice")]
    [InlineData("@node g x\n@node g y\n", 2, "element 'g' already has an alias")]
    [InlineData("@node s:g x\n", 1, "prefix 's' is not bound")]
    public void ReportsProfileErrorsWithTheirLine(string text, int line, string message)
    {
        SmlException e = Assert.Throws<SmlException>(() => SmlProfile.Parse(text));
        Assert.Equal(line, e.Line);
        Assert.Contains(message, e.Reason);
    }

    // ---------------------------------------------------------------------------------------------
    // Writer

    private static SmlDocument Xml(string xml) => XmlImporter.Read(Encoding.UTF8.GetBytes(xml));

    [Fact]
    public void WritesExactlyWithContinuationLinesPerItem()
    {
        SmlDocument doc = Xml("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0 0 L 1 1 Z' fill='red'/></svg>");
        Assert.Equal("""
            @sml 1.0-SVG-4
            svg(xmlns=http://www.w3.org/2000/svg)
              path
                .d "M 0 0 "
                  "L 1 1 "
                  "Z"
                .fill red
              end
            end

            """.ReplaceLineEndings("\n"), SmlWriter.Write(doc, Svg));
    }

    [Fact]
    public void ReflowsIntoAliasBlocksDeclaredOnTheRoot()
    {
        SmlDocument doc = Xml("<svg xmlns='http://www.w3.org/2000/svg'><path d='M 0 0 L 1 1 Z' fill='red'/></svg>");
        Assert.Equal(""""
            # Readable conversion (SML §3.2), not strict: one item per line in d
            @sml 1.0-SVG-4
            svg(xmlns=http://www.w3.org/2000/svg)
              @block path .d data
              path
                data """
                  M 0 0
                  L 1 1
                  Z
                """
                .fill red
              end
            end

            """".ReplaceLineEndings("\n"), SmlWriter.Write(doc, Svg, reflow: true));
    }

    [Fact]
    public void UsesAliasesForExactBlocksWithTheRootsPrefix()
    {
        SmlDocument doc = Xml("<s:svg xmlns:s='http://www.w3.org/2000/svg'><s:polygon points='0,0&#10;1,1'/></s:svg>");
        string sml = SmlWriter.Write(doc, Svg);
        Assert.Contains("  @block s:polygon .points points\n", sml);
        Assert.Contains("    points \"\"\"\n      0,0\n      1,1\n    \"\"\"\n", sml);
        Assert.Null(DocumentComparer.FirstDifference(doc, SmlReader.Read(sml)));
    }

    [Fact]
    public void WritesNoAliasWhenTheRootCannotDeclareIt()
    {
        SmlDocument doc = Xml("<doc><svg xmlns='http://www.w3.org/2000/svg'><path d='M 0 0&#10;L 1 1'/></svg></doc>");
        string sml = SmlWriter.Write(doc, Svg);
        Assert.DoesNotContain("@block", sml);
        Assert.Contains("      .d \"\"\"\n", sml);
        Assert.Null(DocumentComparer.FirstDifference(doc, SmlReader.Read(sml)));
    }

    [Fact]
    public void WritesElementAliasesBeforeTheRoot()
    {
        SmlDocument doc = Xml("<svg xmlns='http://www.w3.org/2000/svg'><g id='a'><path d='M 0 0'/></g><g/><rect/></svg>");
        SmlProfile profile = SmlProfile.Parse("@ns http://www.w3.org/2000/svg\n@node g group\n@node circle round\n@node svg drawing\n");
        string sml = SmlWriter.Write(doc, profile);
        Assert.Equal("""
            @sml 1.0-SVG-4
            @node g group
            @node svg drawing
            drawing(xmlns=http://www.w3.org/2000/svg)
              group(id=a)
                path(d=M 0 0)/
              end
              group/
              rect/
            end

            """.ReplaceLineEndings("\n"), sml);
        Assert.Null(DocumentComparer.FirstDifference(doc, SmlReader.Read(sml)));
    }

    [Fact]
    public void ElementAliasesFollowTheLexicalNameAndAvoidRealNames()
    {
        SmlProfile profile = SmlProfile.Parse("@ns http://www.w3.org/2000/svg\n@node g group\n@node rect box\n");
        // Declared for the first element's prefix; a g with another prefix or of another namespace keeps its name,
        // and no alias is used that an element of the document is named like.
        SmlDocument doc = Xml("<s:svg xmlns:s='http://www.w3.org/2000/svg'><s:g/><g xmlns='http://www.w3.org/2000/svg'/>"
            + "<g xmlns='urn:x'/><s:rect/><box/></s:svg>");
        string sml = SmlWriter.Write(doc, profile);
        Assert.StartsWith("@sml 1.0-SVG-4\n@node s:g group\ns:svg", sml);
        Assert.Contains("  group/\n  g(xmlns=http://www.w3.org/2000/svg)/\n  g(xmlns=urn:x)/\n  s:rect/\n  box/\n", sml);
        Assert.DoesNotContain("@node s:rect", sml);
        Assert.Null(DocumentComparer.FirstDifference(doc, SmlReader.Read(sml)));
    }

    [Fact]
    public void AnElementAliasOpensAnElementNamedEnd()
    {
        SmlDocument doc = Xml("<doc><end/><end>x</end></doc>");
        string sml = SmlWriter.Write(doc, SmlProfile.Parse("@node end finish\n"));
        Assert.Equal("@sml 1.0-SVG-4\n@node end finish\ndoc\n  finish/\n  finish = x\nend\n", sml);
        Assert.Null(DocumentComparer.FirstDifference(doc, SmlReader.Read(sml)));
    }

    public static TheoryData<string> SvgFiles()
    {
        var files = Directory.GetFiles(Path.Combine(TestsFolder, "svg-roundtrip", "corpus"), "*.svg")
            .Concat(Directory.GetFiles(Path.Combine(TestsFolder, "svg-corpus"), "*.svg", SearchOption.AllDirectories))
            .Select(f => Path.GetRelativePath(TestsFolder, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);
        return new TheoryData<string>(files);
    }

    /// <summary>The document read from a test file, or null if it is outside the supported subset.</summary>
    private static SmlDocument? Load(string file)
    {
        try
        {
            return XmlImporter.Read(File.ReadAllBytes(Path.Combine(TestsFolder, file)));
        }
        catch (UnsupportedXmlException)
        {
            return null;
        }
    }

    [Theory]
    [MemberData(nameof(SvgFiles))]
    public void ProfileOutputReadsBackAsTheSameDocument(string file)
    {
        if (Load(file) is not { } doc)
            return;
        string sml = SmlWriter.Write(doc, Svg);
        SmlDocument back = SmlReader.Read(sml);
        Assert.Null(DocumentComparer.FirstDifference(doc, back));
        Assert.Equal(sml, SmlWriter.Write(back, Svg));
        Assert.Equal(SmlWriter.Write(doc), SmlWriter.Write(doc, SmlProfile.Parse("")));
    }

    [Theory]
    [MemberData(nameof(SvgFiles))]
    public void ElementAliasOutputReadsBackAsTheSameDocument(string file)
    {
        if (Load(file) is not { } doc)
            return;
        foreach (bool reflow in new[] { false, true })
        {
            string sml = SmlWriter.Write(doc, SvgWithNodes, reflow);
            SmlDocument back = SmlReader.Read(sml);
            if (!reflow)
            {
                Assert.Null(DocumentComparer.FirstDifference(doc, back));
                Assert.Equal(sml, SmlWriter.Write(back, SvgWithNodes));
            }
            // Element aliases change only how elements are opened.
            Assert.Equal(SmlWriter.Write(SmlReader.Read(SmlWriter.Write(doc, Svg, reflow))), SmlWriter.Write(back));
        }
    }

    [Theory]
    [MemberData(nameof(SvgFiles))]
    public void ReflowChangesOnlyTheWhitespaceOfValuesWithALayout(string file)
    {
        if (Load(file) is not { } doc)
            return;
        string sml = SmlWriter.Write(doc, Svg, reflow: true);
        SmlDocument back = SmlReader.Read(sml);

        // The expected document: each value with a layout reflowed, which changes only separators.
        SmlDocument expected = Load(file)!;
        bool changed = false;
        var stack = new Stack<ElementNode>([expected.Root]);
        while (stack.Count > 0)
        {
            ElementNode element = stack.Pop();
            foreach (string name in element.Attributes.Keys.ToList())
            {
                string value = element.Attributes[name];
                if (Svg.FindLayout(element.Name, element.AttributeNames[name]) is { } layout
                    && ValueLayout.Reflow(value, layout) is { } reflowed && reflowed != value)
                {
                    Assert.Equal(StripSeparators(value), StripSeparators(reflowed));
                    element.Attributes[name] = reflowed;
                    changed = true;
                }
            }
            foreach (ElementNode child in element.Children.OfType<ElementNode>())
                stack.Push(child);
        }
        Assert.Null(DocumentComparer.FirstDifference(expected, back));
        Assert.Equal(changed, sml.StartsWith("# Readable conversion", StringComparison.Ordinal));

        // Written again, exactly, the reflowed document gives the same SML without the comment.
        string body = changed ? sml[(sml.IndexOf('\n') + 1)..] : sml;
        Assert.Equal(body, SmlWriter.Write(back, Svg));
        SmlDocument fromXml = XmlImporter.Read(Encoding.UTF8.GetBytes(XmlExporter.Write(back)));
        Assert.Null(DocumentComparer.FirstDifference(back, fromXml));
    }

    private static string StripSeparators(string value) =>
        string.Concat(value.Where(c => c is not (' ' or '\t' or '\n' or '\r' or ',')));
}
