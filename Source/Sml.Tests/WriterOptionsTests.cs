using System.Text;

namespace Sml.Tests;

/// <summary>Tests of the writer's vertical-attribute option (smlxml --verbose).</summary>
public class WriterOptionsTests
{
    private static readonly SmlWriterOptions Vertical = new() { VerticalAttributes = true };

    private static SmlDocument Xml(string xml) => XmlImporter.Read(Encoding.UTF8.GetBytes(xml));

    [Fact]
    public void WritesEveryAttributeAndNamespaceDeclarationOnItsOwnLine()
    {
        SmlDocument doc = Xml("<svg xmlns='http://www.w3.org/2000/svg' xmlns:x='urn:x'>"
            + "<path d='M181 206.7q.1-1 1.1-1t1 1-1 1a1 1 0 0 1-1-1' fill='#fff' stroke='#000' stroke-width='.4'/>"
            + "<path d='M213.3 204.8q0-.9 1-1c1-.1 1 .5 1 1s-.4 1-1 1a1 1 0 0 1-1-1m15.8 1.9q.1-1 1-1t1.1 1q0 .9-1 1a1 1 0 0 1-1-1'"
            + " fill='#fff'/><text x='5'>Hi</text><title>T</title><g/></svg>");
        Assert.Equal("""
            @sml 1.0-SVG-1
            svg
              @ns http://www.w3.org/2000/svg
              @ns x urn:x
              path
                .d M181 206.7q.1-1 1.1-1t1 1-1 1a1 1 0 0 1-1-1
                .fill #fff
                .stroke #000
                .stroke-width .4
              end
              path
                .d "M213.3 204.8q0-.9 1-1c1-.1 1 .5 1 1s-.4 1-1 1a1 1 0 0 1-1-1m15.8 1.9q.1-1 "
                  "1-1t1.1 1q0 .9-1 1a1 1 0 0 1-1-1"
                .fill #fff
              end
              text
                .x 5
                = Hi
              end
              title = T
              g/
            end

            """.ReplaceLineEndings("\n"), SmlWriter.Write(doc, Vertical));
        Assert.Contains("path(d=M181 206.7", SmlWriter.Write(doc));
    }

    [Fact]
    public void CombinesWithAProfile()
    {
        SmlProfile profile = SmlProfile.Parse("@layout * .transform functions\n@block g .transform t\n");
        SmlDocument doc = Xml("<g transform='scale(2)'><g transform='translate(1,2) rotate(3)'/></g>");
        Assert.Equal(""""
            # Cleaned (SML §3.2), not exact: one item per line in transform
            @sml 1.0-SVG-1
            g
              @block g .transform t
              .transform scale(2)
              g
                t """
                  translate(1,2)
                  rotate(3)
                  """
              end
            end

            """".ReplaceLineEndings("\n"),
            SmlWriter.Write(doc, new SmlWriterOptions { Clean = profile, VerticalAttributes = true }));
    }

    [Theory]
    [MemberData(nameof(ProfileTests.SvgFiles), MemberType = typeof(ProfileTests))]
    public void VerticalOutputReadsBackAsTheSameDocument(string file)
    {
        SmlDocument doc;
        try
        {
            doc = XmlImporter.Read(File.ReadAllBytes(Path.Combine(ProfileTests.TestsFolder, file)));
        }
        catch (UnsupportedXmlException)
        {
            return;
        }
        string sml = SmlWriter.Write(doc, Vertical);
        SmlDocument back = SmlReader.Read(sml);
        Assert.Null(DocumentComparer.FirstDifference(doc, back));
        Assert.Equal(sml, SmlWriter.Write(back, Vertical));
    }
}
