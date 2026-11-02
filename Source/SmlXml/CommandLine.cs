using System.Reflection;
using System.Text;
using Sml;

namespace SmlXml;

/// <summary>The smlxml command line: converts .sml files to XML and any other file (XML, SVG) to SML.</summary>
public static class CommandLine
{
    public const int Success = 0;
    public const int ConversionFailed = 1;
    public const int UsageError = 2;

    private const string SvgNamespace = "http://www.w3.org/2000/svg";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private const string Usage = """
        Usage: smlxml [options] <file>...

        Converts each file beside the original:
          an .sml file to XML      drawing.sml -> drawing.svg (if the root is an SVG <svg>), else drawing.xml
          any other file to SML    drawing.svg -> drawing.sml

        Options:
          -o, --output <path>  Write to this file instead (one input file only).
          -f, --force          Overwrite an existing output file.
          -h, --help           Show this help.
              --version        Show the version.

        Exit codes: 0 success, 1 a conversion failed, 2 invalid command line.
        """;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var inputs = new List<string>();
        string? output = null;
        bool force = false;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-h" or "--help" or "/?":
                    stdout.WriteLine(Usage);
                    return Success;
                case "--version":
                    stdout.WriteLine($"smlxml {VersionText()} (SML {SmlReader.Version})");
                    return Success;
                case "-f" or "--force":
                    force = true;
                    break;
                case "-o" or "--output":
                    if (i + 1 >= args.Length)
                        return Fail(stderr, $"{arg} needs a path");
                    output = args[++i];
                    break;
                case "--":
                    inputs.AddRange(args[(i + 1)..]);
                    i = args.Length;
                    break;
                default:
                    if (arg.StartsWith('-') && arg.Length > 1)
                        return Fail(stderr, $"unknown option {arg}");
                    inputs.Add(arg);
                    break;
            }
        }
        if (inputs.Count == 0)
            return Fail(stderr, "no input file");
        if (output is not null && inputs.Count > 1)
            return Fail(stderr, "--output can only be used with one input file");

        int result = Success;
        foreach (string input in inputs)
        {
            if (!Convert(input, output, force, stdout, stderr))
                result = ConversionFailed;
        }
        return result;
    }

    private static int Fail(TextWriter stderr, string message)
    {
        stderr.WriteLine($"smlxml: {message}");
        stderr.WriteLine("Try 'smlxml --help'.");
        return UsageError;
    }

    private static string VersionText() =>
        typeof(CommandLine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "1.0.0";

    private static bool IsSml(string path) => Path.GetExtension(path).Equals(".sml", StringComparison.OrdinalIgnoreCase);

    /// <summary>Converts one file. Errors are reported as "file(line): error: message".</summary>
    private static bool Convert(string input, string? output, bool force, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            if (!File.Exists(input))
            {
                stderr.WriteLine($"{input}: error: file not found");
                return false;
            }
            byte[] data = File.ReadAllBytes(input);
            string text;
            if (IsSml(input))
            {
                SmlDocument doc = SmlReader.Read(data);
                output ??= Path.ChangeExtension(input, IsSvg(doc) ? ".svg" : ".xml");
                text = XmlExporter.Write(doc);
            }
            else
            {
                output ??= Path.ChangeExtension(input, ".sml");
                text = SmlWriter.Write(XmlImporter.Read(data));
            }
            if (Path.GetFullPath(output) == Path.GetFullPath(input))
            {
                stderr.WriteLine($"{input}: error: the output would overwrite the input");
                return false;
            }
            if (File.Exists(output) && !force)
            {
                stderr.WriteLine($"{input}: error: {output} already exists; use --force to overwrite it");
                return false;
            }
            WriteAtomically(output, text);
            stdout.WriteLine($"{input} -> {output}");
            return true;
        }
        catch (SmlException e)
        {
            stderr.WriteLine(e.Line is { } line ? $"{input}({line}): error: {e.Reason}" : $"{input}: error: {e.Reason}");
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"{input}: error: {e.Message}");
            return false;
        }
    }

    private static bool IsSvg(SmlDocument doc) => doc.Root.Name == new ExpandedName(SvgNamespace, "svg");

    /// <summary>Writes to a temporary file beside the target, then renames it, so a failure never leaves a
    /// partial output file.</summary>
    private static void WriteAtomically(string path, string text)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, text, Utf8NoBom);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
