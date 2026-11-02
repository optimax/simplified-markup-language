using SmlXml;

namespace Sml.Tests;

/// <summary>Tests of the smlxml command line, run in a temporary folder.</summary>
public sealed class CommandLineTests : IDisposable
{
    private const string SvgXml = "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1\"/></svg>";
    private readonly string _dir = Directory.CreateTempSubdirectory("smlxml-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathOf(string name) => Path.Combine(_dir, name);

    private (int Code, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = CommandLine.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void ConvertsXmlToSmlBesideTheOriginal()
    {
        File.WriteAllText(PathOf("drawing.svg"), SvgXml);
        var (code, output, _) = Run(PathOf("drawing.svg"));
        Assert.Equal(CommandLine.Success, code);
        Assert.Contains("drawing.sml", output);
        Assert.Equal("@sml 1.0-SVG-3\nsvg(xmlns=http://www.w3.org/2000/svg)\n  rect(width=1)/\nend\n", File.ReadAllText(PathOf("drawing.sml")));
    }

    [Fact]
    public void ConvertsSmlToSvgOrXmlByRootElement()
    {
        File.WriteAllText(PathOf("drawing.sml"), "svg(xmlns=http://www.w3.org/2000/svg)\n  rect/\nend\n");
        File.WriteAllText(PathOf("data.sml"), "config\n  item = 1\nend\n");
        Assert.Equal(CommandLine.Success, Run(PathOf("drawing.sml"), PathOf("data.sml")).Code);
        Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<svg xmlns=\"http://www.w3.org/2000/svg\"><rect/></svg>\n",
            File.ReadAllText(PathOf("drawing.svg")));
        Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<config><item>1</item></config>\n", File.ReadAllText(PathOf("data.xml")));
    }

    [Fact]
    public void NeverOverwritesWithoutForce()
    {
        File.WriteAllText(PathOf("drawing.svg"), SvgXml);
        File.WriteAllText(PathOf("drawing.sml"), "# hand-edited\n");
        var (code, _, error) = Run(PathOf("drawing.svg"));
        Assert.Equal(CommandLine.ConversionFailed, code);
        Assert.Contains("already exists; use --force", error);
        Assert.Equal("# hand-edited\n", File.ReadAllText(PathOf("drawing.sml")));
        Assert.Equal(CommandLine.Success, Run("--force", PathOf("drawing.svg")).Code);
        Assert.StartsWith("@sml 1.0-SVG-3", File.ReadAllText(PathOf("drawing.sml")));
    }

    [Fact]
    public void WritesToAnExplicitOutputPath()
    {
        File.WriteAllText(PathOf("drawing.svg"), SvgXml);
        Assert.Equal(CommandLine.Success, Run("-o", PathOf("other.sml"), PathOf("drawing.svg")).Code);
        Assert.True(File.Exists(PathOf("other.sml")));
        Assert.False(File.Exists(PathOf("drawing.sml")));
    }

    [Fact]
    public void ReportsErrorsWithFileAndLineAndLeavesNoOutput()
    {
        File.WriteAllText(PathOf("bad.sml"), "g\n  .a 1\n  @ns urn:x\nend\n");
        var (code, _, error) = Run(PathOf("bad.sml"));
        Assert.Equal(CommandLine.ConversionFailed, code);
        Assert.Contains($"{PathOf("bad.sml")}(3): error: @ns must come", error);
        Assert.Equal([PathOf("bad.sml")], Directory.GetFiles(_dir));
    }

    [Fact]
    public void ContinuesWithTheNextFileAfterAFailure()
    {
        File.WriteAllText(PathOf("bad.svg"), "<svg><g></svg>");
        File.WriteAllText(PathOf("good.svg"), SvgXml);
        var (code, _, error) = Run(PathOf("bad.svg"), PathOf("good.svg"));
        Assert.Equal(CommandLine.ConversionFailed, code);
        Assert.Contains("bad.svg(1): error: malformed XML", error);
        Assert.True(File.Exists(PathOf("good.sml")));
    }

    [Theory]
    [InlineData]
    [InlineData("--bogus", "x.svg")]
    [InlineData("-o")]
    [InlineData("-o", "out.sml", "a.svg", "b.svg")]
    public void RejectsInvalidCommandLines(params string[] args) => Assert.Equal(CommandLine.UsageError, Run(args).Code);

    [Fact]
    public void RefusesToOverwriteTheInputAndReportsMissingFiles()
    {
        File.WriteAllText(PathOf("drawing.svg"), SvgXml);
        Assert.Contains("would overwrite the input", Run("-o", PathOf("drawing.svg"), PathOf("drawing.svg")).Err);
        Assert.Contains("file not found", Run(PathOf("missing.svg")).Err);
    }

    [Fact]
    public void ShowsHelpAndVersion()
    {
        Assert.Contains("Usage: smlxml", Run("--help").Out);
        Assert.Contains("SML 1.0-SVG-3", Run("--version").Out);
        Assert.Equal(Run("--version"), Run("-v"));
    }

    private const string Banner =
        "smlxml 1.0.0 (SML 1.0-SVG-3)\nCopyright (c) 2026 Optimax Technologies, Inc.\nFreely distributed under MIT license.\n\n";

    [Fact]
    public void VersionPrintsExactlyTheBanner() => Assert.Equal(Banner, Run("-v").Out.ReplaceLineEndings("\n"));

    [Fact]
    public void EveryRunStartsWithTheBanner()
    {
        File.WriteAllText(PathOf("drawing.svg"), SvgXml);
        File.WriteAllText(PathOf("bad.sml"), "g\n  .a 1\n  @ns urn:x\nend\n");
        string[][] runs = [[PathOf("drawing.svg")], ["--help"], [], ["--bogus"], [PathOf("bad.sml")], [PathOf("*.nothing")]];
        foreach (string[] args in runs)
            Assert.StartsWith(Banner, Run(args).Out.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ShowsUsageWhenRunWithoutArguments()
    {
        var (code, output, error) = Run();
        Assert.Equal(CommandLine.UsageError, code);
        Assert.Contains("Usage: smlxml", output);
        Assert.Contains("wildcards", output);
        Assert.Empty(error);
    }

    private void Create(params string[] relativePaths)
    {
        foreach (string relative in relativePaths)
        {
            string path = PathOf(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, relative.EndsWith(".sml", StringComparison.Ordinal) ? "svg(xmlns=http://www.w3.org/2000/svg)/\n" : SvgXml);
        }
    }

    private string[] Existing(string extension) =>
        Directory.GetFiles(_dir, "*" + extension, SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(_dir, f).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void ExpandsStarAndQuestionMarkInFileNames()
    {
        Create("a1.svg", "a22.svg", "b.svg", "c.xml");
        Assert.Equal(CommandLine.Success, Run(PathOf("a?.svg")).Code);
        Assert.Equal(["a1.sml"], Existing(".sml"));
        Assert.Equal(CommandLine.Success, Run("--force", PathOf("*.svg"), PathOf("*.xml")).Code);
        Assert.Equal(["a1.sml", "a22.sml", "b.sml", "c.sml"], Existing(".sml"));
    }

    [Fact]
    public void ExpandsWildcardsInFolderNamesAndDoubleStarAcrossFolders()
    {
        Create("f1/a.svg", "f2/a.svg", "g/a.svg", "top.svg", "deep/x/y/z.svg");
        Assert.Equal(CommandLine.Success, Run(Path.Join(_dir, "f*", "a.svg")).Code);
        Assert.Equal(["f1/a.sml", "f2/a.sml"], Existing(".sml"));
        Assert.Equal(CommandLine.ConversionFailed, Run(Path.Join(_dir, "**", "*.svg")).Code);   // f1 and f2 already converted
        Assert.Equal(["deep/x/y/z.sml", "f1/a.sml", "f2/a.sml", "g/a.sml", "top.sml"], Existing(".sml"));
    }

    [Fact]
    public void ReportsPatternsThatMatchNothingAndStillConvertTheRest()
    {
        Create("a.svg");
        var (code, _, error) = Run(PathOf("*.nothing"), PathOf("a.svg"));
        Assert.Equal(CommandLine.ConversionFailed, code);
        Assert.Contains("*.nothing: error: no files match", error);
        Assert.Equal(["a.sml"], Existing(".sml"));
    }

    [Fact]
    public void ConvertsAFileMatchedTwiceOnlyOnce()
    {
        Create("a.svg");
        Assert.Equal(CommandLine.Success, Run(PathOf("*.svg"), PathOf("a.svg"), PathOf("?.svg")).Code);
    }

    [Fact]
    public void SkipsHiddenFilesUnlessThePatternStartsWithADot()
    {
        Create(".hidden.svg", "shown.svg");
        Assert.Equal(CommandLine.Success, Run(PathOf("*.svg")).Code);
        Assert.Equal(["shown.sml"], Existing(".sml"));
        Assert.Equal(CommandLine.Success, Run(PathOf(".h*.svg")).Code);
        Assert.Equal([".hidden.sml", "shown.sml"], Existing(".sml"));
    }

    [Fact]
    public void RefusesOutputsThatAreAlsoInputsOfTheSameRun()
    {
        Create("a.svg", "a.sml");
        string sml = File.ReadAllText(PathOf("a.sml"));
        var (code, _, error) = Run("--force", PathOf("*"));
        Assert.Equal(CommandLine.ConversionFailed, code);
        Assert.Contains("is also an input of this run", error);
        Assert.Equal(sml, File.ReadAllText(PathOf("a.sml")));
        Assert.Equal(SvgXml, File.ReadAllText(PathOf("a.svg")));
    }

    [Fact]
    public void AllowsOutputOnlyWhenAPatternMatchesOneFile()
    {
        Create("only.svg", "x1.xml", "x2.xml");
        Assert.Equal(CommandLine.Success, Run("-o", PathOf("out.sml"), PathOf("o*.svg")).Code);
        Assert.Equal(CommandLine.UsageError, Run("-o", PathOf("out2.sml"), PathOf("x*.xml")).Code);
    }

    private void CreateSvg(string relative, string corpusFixture)
    {
        string source = Path.Combine(CorpusFolder(), corpusFixture + ".svg");
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf(relative))!);
        File.Copy(source, PathOf(relative));
    }

    private static string CorpusFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "Tests", "svg-roundtrip", "corpus");
            if (Directory.Exists(candidate))
                return candidate;
        }
        throw new DirectoryNotFoundException("Tests/svg-roundtrip/corpus");
    }

    private void AssertSameDocument(string original, string roundTripped) =>
        Assert.Null(DocumentComparer.FirstDifference(
            XmlImporter.Read(File.ReadAllBytes(PathOf(original))), XmlImporter.Read(File.ReadAllBytes(PathOf(roundTripped)))));

    [Fact]
    public void OutputPatternRoundTripsEverySvgBesideTheOriginal()
    {
        CreateSvg("a.svg", "07-definitions-references");
        CreateSvg("b.svg", "66-doctype-public");
        CreateSvg("c.svg", "60a-indent-2");
        Assert.Equal(CommandLine.Success, Run(PathOf("*.svg")).Code);
        var (code, output, error) = Run("-o", PathOf("*.out.svg"), PathOf("*.sml"));
        Assert.Equal(CommandLine.Success, code);
        Assert.Empty(error);
        Assert.Contains("a.sml -> ", output);
        foreach (string name in new[] { "a", "b", "c" })
            AssertSameDocument(name + ".svg", name + ".out.svg");
    }

    [Fact]
    public void OutputPatternWithoutAFolderWritesBesideEachInput()
    {
        CreateSvg("one/x.y.svg", "01-basic-shapes");
        CreateSvg("two/z.svg", "05-namespaces");
        Assert.Equal(CommandLine.Success, Run(Path.Join(_dir, "*", "*.svg")).Code);
        Assert.Equal(CommandLine.Success, Run("-o", "*.out.svg", Path.Join(_dir, "*", "*.sml")).Code);
        AssertSameDocument("one/x.y.svg", "one/x.y.out.svg");
        AssertSameDocument("two/z.svg", "two/z.out.svg");
    }

    [Fact]
    public void OutputPatternWithAFolderCreatesItAndConvertsXmlInputs()
    {
        CreateSvg("p.xml", "14-metadata-vendor");
        CreateSvg("q.xml", "19-prolog-epilog");
        Assert.Equal(CommandLine.Success, Run("-o", Path.Join(_dir, "converted", "*.sml"), PathOf("*.xml")).Code);
        Assert.Equal(["converted/p.sml", "converted/q.sml"], Existing(".sml"));
    }

    [Theory]
    [InlineData("out/*/x.svg")]
    [InlineData("a?.svg")]
    [InlineData("literal.svg")]
    public void RejectsInvalidOutputPatterns(string output)
    {
        Create("a.sml", "b.sml");
        Assert.Equal(CommandLine.UsageError, Run("-o", output, PathOf("*.sml")).Code);
    }

    [Fact]
    public void RefusesTwoInputsWithTheSameOutput()
    {
        File.WriteAllText(PathOf("a.svg"), SvgXml);
        File.WriteAllText(PathOf("a.xml"), SvgXml);
        var (code, _, error) = Run("--force", PathOf("a.*"));
        Assert.Equal(CommandLine.ConversionFailed, code);
        Assert.Contains("is already the output of", error);
        Assert.Equal(["a.sml"], Existing(".sml"));
    }

    [Fact]
    public void PrefersAnExistingFileWhoseNameContainsWildcardCharacters()
    {
        if (OperatingSystem.IsWindows())
            return;     // * and ? cannot appear in Windows file names
        Create("a*b.svg", "aXb.svg");
        Assert.Equal(CommandLine.Success, Run(PathOf("a*b.svg")).Code);
        Assert.Equal(["a*b.sml"], Existing(".sml"));
    }

    private const string PathXml = "<svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M 0 0 L 1 1\"/></svg>";

    [Fact]
    public void WritesWithTheBuiltInSvgProfileExactlyOrReflowed()
    {
        File.WriteAllText(PathOf("drawing.svg"), PathXml);
        Assert.Equal(CommandLine.Success, Run("--profile", "svg", PathOf("drawing.svg")).Code);
        Assert.Contains("    .d \"M 0 0 \"\n      \"L 1 1\"\n", File.ReadAllText(PathOf("drawing.sml")));
        Assert.Equal(CommandLine.Success, Run("-p", "svg", "-r", "-f", PathOf("drawing.svg")).Code);
        string reflowed = File.ReadAllText(PathOf("drawing.sml"));
        Assert.StartsWith("# Readable conversion", reflowed);
        Assert.Contains("  @block path d .d\n", reflowed);
        Assert.Contains("    d |\n      M 0 0\n      L 1 1\n    end\n", reflowed);
    }

    [Fact]
    public void ReadsAProfileFromAFileAndIgnoresItForSmlInput()
    {
        File.WriteAllText(PathOf("mine.smlprofile"), "@layout * .v numbers 1\n");
        File.WriteAllText(PathOf("data.xml"), "<r v=\"1 2\"/>");
        File.WriteAllText(PathOf("other.sml"), "r(v=3)/\n");
        Assert.Equal(CommandLine.Success, Run("-p", PathOf("mine.smlprofile"), PathOf("data.xml"), PathOf("other.sml")).Code);
        Assert.Equal("@sml 1.0-SVG-3\nr\n  .v \"1 \"\n    \"2\"\nend\n", File.ReadAllText(PathOf("data.sml")));
        Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<r v=\"3\"/>\n", File.ReadAllText(PathOf("other.xml")));
    }

    [Fact]
    public void RejectsMissingOrInvalidProfilesBeforeConvertingAnything()
    {
        File.WriteAllText(PathOf("drawing.svg"), PathXml);
        File.WriteAllText(PathOf("bad.smlprofile"), "# fine\n@layout path .d curves\n");
        var (code, _, error) = Run("-p", PathOf("bad.smlprofile"), PathOf("drawing.svg"));
        Assert.Equal(CommandLine.UsageError, code);
        Assert.Contains($"{PathOf("bad.smlprofile")}(2): error: unknown layout 'curves'", error);
        Assert.Contains("profile 'nonesuch' not found", Run("-p", "nonesuch", PathOf("drawing.svg")).Err);
        Assert.Contains("profile 'x.smlprofile' not found", Run("-p", "x.smlprofile", PathOf("drawing.svg")).Err);
        Assert.Contains("--reflow needs --profile", Run("--reflow", PathOf("drawing.svg")).Err);
        Assert.Equal(CommandLine.UsageError, Run("-p").Code);
        Assert.False(File.Exists(PathOf("drawing.sml")));
    }

    [Fact]
    public void VerboseWritesEveryAttributeOnItsOwnLine()
    {
        File.WriteAllText(PathOf("drawing.svg"), SvgXml);
        Assert.Equal(CommandLine.Success, Run("--verbose", PathOf("drawing.svg")).Code);
        Assert.Equal("@sml 1.0-SVG-3\nsvg\n  @ns http://www.w3.org/2000/svg\n  rect\n    .width 1\n  end\nend\n",
            File.ReadAllText(PathOf("drawing.sml")));
        File.WriteAllText(PathOf("path.svg"), PathXml);
        Assert.Equal(CommandLine.Success, Run("--verbose", "-p", "svg", "-r", PathOf("path.svg")).Code);
        Assert.Contains("svg\n  @ns http://www.w3.org/2000/svg\n  @block path d .d\n  path\n    d |\n", File.ReadAllText(PathOf("path.sml")));
        Assert.Contains("--verbose", Run("--help").Out);
    }
}

public class FileExpanderTests
{
    [Theory]
    [InlineData("a.svg", "*.svg", true)]
    [InlineData("a.svgx", "*.svg", false)]
    [InlineData("a.svg", "*", true)]
    [InlineData("abc", "a*b*c", true)]
    [InlineData("acb", "a*b*c", false)]
    [InlineData("ab", "a?", true)]
    [InlineData("a", "a?", false)]
    [InlineData("aaab", "*a*b", true)]
    [InlineData(".x.svg", "*.svg", false)]
    [InlineData("x.svg", "", false)]
    public void MatchesNames(string name, string pattern, bool expected) => Assert.Equal(expected, FileExpander.Matches(name, pattern));

    [Fact]
    public void ComparesCaseAccordingToThePlatform() =>
        Assert.Equal(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), FileExpander.Matches("A.SVG", "*.svg"));

    [Fact]
    public void RecognizesPatterns()
    {
        Assert.True(FileExpander.IsPattern("art/*.svg"));
        Assert.True(FileExpander.IsPattern("a?.svg"));
        Assert.False(FileExpander.IsPattern("art/a.svg"));
    }
}
