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
    }
}
