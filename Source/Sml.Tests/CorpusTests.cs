// Created by Andrew Wozniewicz with Claude
// Copyright (C) 2026 Optimax Technologies, Inc.
// Distributed under MIT License.
//
using System.Text;

namespace Sml.Tests;

/// <summary>Runs the shared fixture corpus (Tests/svg-roundtrip) through the C# implementation and compares its
/// canonical SML byte for byte with the Python prototype's reference output in results/*.sml.</summary>
public class CorpusTests
{
    private static readonly string RoundTrip = FindRoundTripFolder();

    private static string FindRoundTripFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "Tests", "svg-roundtrip");
            if (Directory.Exists(Path.Combine(candidate, "corpus")))
                return candidate;
        }
        throw new DirectoryNotFoundException("Tests/svg-roundtrip/corpus not found above " + AppContext.BaseDirectory);
    }

    public static TheoryData<string> Fixtures() =>
        new(Directory.GetFiles(Path.Combine(RoundTrip, "corpus"), "*.svg").Select(Path.GetFileNameWithoutExtension).Order()!);

    public static TheoryData<string> NegativeFixtures() =>
        new(Directory.GetFiles(Path.Combine(RoundTrip, "corpus-negative"), "*.svg").Select(Path.GetFileName).Order()!);

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void CanonicalSmlMatchesThePythonReference(string name)
    {
        SmlDocument doc = XmlImporter.Read(File.ReadAllBytes(Path.Combine(RoundTrip, "corpus", name + ".svg")));
        string expected = File.ReadAllText(Path.Combine(RoundTrip, "results", name + ".sml"), Encoding.UTF8);
        Assert.Equal(expected, SmlWriter.Write(doc));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void RoundTripsThroughSmlAndXml(string name)
    {
        SmlDocument original = XmlImporter.Read(File.ReadAllBytes(Path.Combine(RoundTrip, "corpus", name + ".svg")));
        string sml = SmlWriter.Write(original);
        SmlDocument fromSml = SmlReader.Read(sml);
        Assert.Null(DocumentComparer.FirstDifference(original, fromSml));
        SmlDocument fromXml = XmlImporter.Read(Encoding.UTF8.GetBytes(XmlExporter.Write(fromSml)));
        Assert.Null(DocumentComparer.FirstDifference(original, fromXml));
        Assert.Equal(sml, SmlWriter.Write(fromXml));
    }

    [Theory]
    [MemberData(nameof(NegativeFixtures))]
    public void RejectsUnsupportedOrMalformedXml(string file)
    {
        byte[] data = File.ReadAllBytes(Path.Combine(RoundTrip, "corpus-negative", file));
        Assert.ThrowsAny<XmlInputException>(() => XmlImporter.Read(data));
    }
}
