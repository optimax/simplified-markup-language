// Created by Andrew Wozniewicz with Claude
// Copyright (C) 2026 Optimax Technologies, Inc.
// Distributed under MIT License.
//
using System.Reflection;
using System.Text;
using OptimaxTools.SMLReadWrite;

namespace SmlXml;

/// <summary>The smlxml command line: converts .sml files to XML and any other file (XML, SVG) to SML. It handles
/// arguments, wildcards, files, and messages; the conversion itself is OptimaxTools.SMLReadWrite's.</summary>
public static class CommandLine
{
    public const int Success = 0;
    public const int ConversionFailed = 1;
    public const int UsageError = 2;

    private const string Usage = """
        Usage: smlxml [options] <file>...

        Converts each file beside the original:
          an .sml file to XML      drawing.sml -> drawing.svg (if the root is an SVG <svg>), else drawing.xml
          any other file to SML    drawing.svg -> drawing.sml

        Files may contain wildcards: * and ? in any part of the path, and ** for any number of
        folders. Examples: smlxml *.svg    smlxml drawings\*.sml    smlxml art\**\*.svg

        Options:
          -o, --output <path>  Write to this file instead (one input file only). With *, write one output per
                               input: * stands for the input's name without its extension, and without a
                               folder each output goes beside its input. Example: smlxml -o *.out.svg *.sml
          -f, --force          Overwrite an existing output file.
          -c, --clean <name>   Write readable SML with a profile (XML to SML only): indentation and line
              --tidy <name>    breaks tidied whatever the input's were, one item per line (such as one path
                               command per line), multi-line text and comments tidied, and the profile's
                               aliases. Whitespace changes, so the XML written back differs, pretty-printed
                               with two spaces. A name without folder or extension is a profile beside
                               smlxml: smlxml -c svg uses svg.smlprofile.
              --verbose        Write every attribute and namespace declaration on its own line, never in a
                               name(...) list (XML to SML only).
              --sort-attributes
                               Write attributes in alphabetical order (the order of Canonical XML) instead
                               of the order of the XML input (XML to SML only).
          -h, --help           Show this help.
          -v, --version        Show the version.

        Exit codes: 0 success, 1 a conversion failed or a pattern matched no file,
        2 invalid command line or profile (including no arguments).
        """;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        WriteBanner(stdout);
        if (args.Length == 0)
        {
            stdout.WriteLine(Usage);
            return UsageError;
        }
        var inputs = new List<string>();
        string? output = null, profileName = null;
        bool force = false, verbose = false, sortAttributes = false;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-h" or "--help" or "/?":
                    stdout.WriteLine(Usage);
                    return Success;
                case "-v" or "--version":
                    return Success;     // the banner already shows the version
                case "-f" or "--force":
                    force = true;
                    break;
                case "-o" or "--output":
                    if (i + 1 >= args.Length)
                        return Fail(stderr, $"{arg} needs a path");
                    output = args[++i];
                    break;
                case "-c" or "--clean" or "--tidy":
                    if (i + 1 >= args.Length)
                        return Fail(stderr, $"{arg} needs a profile name or file");
                    profileName = args[++i];
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--sort-attributes":
                    sortAttributes = true;
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
        bool outputPattern = output is not null && output.Contains('*');
        if (output is not null && output.Contains('?'))
            return Fail(stderr, "an --output pattern can only use *");
        if (outputPattern && (Path.GetDirectoryName(output) ?? "").Contains('*'))
            return Fail(stderr, "an --output pattern can use * only in the file name");
        SmlProfile? profile = null;
        if (profileName is not null)
        {
            string? path = FindProfile(profileName);
            if (path is null)
                return Fail(stderr, $"profile '{profileName}' not found");
            try
            {
                profile = SmlProfile.Parse(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (SmlException e)
            {
                stderr.WriteLine(e.Line is { } line ? $"{path}({line}): error: {e.Reason}" : $"{path}: error: {e.Reason}");
                return UsageError;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"{path}: error: {e.Message}");
                return UsageError;
            }
        }

        // Expand every pattern before converting anything, so files written by this run never become inputs.
        // A name that exists literally is used as is (on Linux, * and ? are legal in file names).
        var files = new List<string>();
        var writer = new SmlWriterOptions { Clean = profile, VerticalAttributes = verbose, SortAttributes = sortAttributes };
        var run = new RunContext(force, writer, new HashSet<string>(FileExpander.PathComparer),
            new Dictionary<string, string>(FileExpander.PathComparer), stdout, stderr);
        int result = Success;
        foreach (string input in inputs)
        {
            List<string> matches = FileExpander.IsPattern(input) && !File.Exists(input) ? FileExpander.Expand(input) : [input];
            if (matches.Count == 0)
            {
                stderr.WriteLine($"{input}: error: no files match");
                result = ConversionFailed;
            }
            foreach (string match in matches)
            {
                if (run.Inputs.Add(Path.GetFullPath(match)))
                    files.Add(match);
            }
        }
        if (output is not null && !outputPattern && files.Count > 1)
            return Fail(stderr, "--output can only be used with one input file, unless it contains *");

        foreach (string file in files)
        {
            string? target = outputPattern ? ApplyOutputPattern(output!, file) : output;
            if (!Convert(file, target, run))
                result = ConversionFailed;
        }
        return result;
    }

    /// <summary>The state of one run: the options, the full paths of all inputs, and of each output written so far
    /// with its input.</summary>
    private sealed record RunContext(bool Force, SmlWriterOptions Writer, HashSet<string> Inputs,
        Dictionary<string, string> Outputs, TextWriter Out, TextWriter Err);

    /// <summary>The file of a --clean argument: an existing file, or for a plain name (no folder or extension),
    /// name.smlprofile beside smlxml. Null if there is none.</summary>
    private static string? FindProfile(string name)
    {
        if (File.Exists(name))
            return name;
        if (Path.GetFileName(name) != name || Path.HasExtension(name))
            return null;
        string builtIn = Path.Combine(AppContext.BaseDirectory, name + ".smlprofile");
        return File.Exists(builtIn) ? builtIn : null;
    }

    /// <summary>Applies an --output pattern to one input: each * becomes the input's file name without its
    /// extension. A pattern without a folder puts the output beside the input.</summary>
    private static string ApplyOutputPattern(string pattern, string input)
    {
        string name = Path.GetFileName(pattern).Replace("*", Path.GetFileNameWithoutExtension(input));
        string? folder = Path.GetDirectoryName(pattern);
        return string.IsNullOrEmpty(folder) ? Path.Join(Path.GetDirectoryName(input), name) : Path.Join(folder, name);
    }

    private static int Fail(TextWriter stderr, string message)
    {
        stderr.WriteLine($"smlxml: {message}");
        stderr.WriteLine("Try 'smlxml --help'.");
        return UsageError;
    }

    /// <summary>The banner printed at the start of every run: version, copyright, license, and a blank line.
    /// The version and copyright come from the assembly (Version and Copyright in SmlXml.csproj).</summary>
    private static void WriteBanner(TextWriter stdout)
    {
        stdout.WriteLine($"smlxml {VersionText()} (SML {SmlReader.Version})");
        stdout.WriteLine(typeof(CommandLine).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright);
        stdout.WriteLine("Freely distributed under MIT license.");
        stdout.WriteLine();
    }

    private static string VersionText() =>
        typeof(CommandLine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "1.0.0";

    /// <summary>Converts one file to output, or to its default name when output is null. Errors are reported as
    /// "file(line): error: message". An output that is also an input of the run is refused, so that a pattern
    /// such as * cannot convert a.svg to a.sml and a.sml back to a.svg; so is an output already written by the
    /// run, so that two inputs (a.svg and a.xml) cannot overwrite each other's a.sml.</summary>
    private static bool Convert(string input, string? output, RunContext run)
    {
        try
        {
            if (!File.Exists(input))
            {
                run.Err.WriteLine($"{input}: error: file not found");
                return false;
            }
            byte[] data = File.ReadAllBytes(input);
            string text;
            if (SmlConverter.IsSmlFileName(input))
            {
                (text, string extension) = SmlConverter.SmlToXml(data);
                output ??= Path.ChangeExtension(input, extension);
            }
            else
            {
                text = SmlConverter.XmlToSml(data, run.Writer);
                output ??= Path.ChangeExtension(input, ".sml");
            }
            string fullOutput = Path.GetFullPath(output);
            if (fullOutput == Path.GetFullPath(input))
            {
                run.Err.WriteLine($"{input}: error: the output would overwrite the input");
                return false;
            }
            if (run.Inputs.Contains(fullOutput))
            {
                run.Err.WriteLine($"{input}: error: {output} is also an input of this run; convert them separately");
                return false;
            }
            if (run.Outputs.TryGetValue(fullOutput, out string? earlier))
            {
                run.Err.WriteLine($"{input}: error: {output} is already the output of {earlier} in this run");
                return false;
            }
            if (File.Exists(output) && !run.Force)
            {
                run.Err.WriteLine($"{input}: error: {output} already exists; use --force to overwrite it");
                return false;
            }
            WriteAtomically(output, text);
            run.Outputs[fullOutput] = input;
            run.Out.WriteLine($"{input} -> {output}");
            return true;
        }
        catch (SmlException e)
        {
            run.Err.WriteLine(e.Line is { } line ? $"{input}({line}): error: {e.Reason}" : $"{input}: error: {e.Reason}");
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            run.Err.WriteLine($"{input}: error: {e.Message}");
            return false;
        }
    }

    /// <summary>Writes to a temporary file beside the target, then renames it, so a failure never leaves a
    /// partial output file. A missing output folder (from --output) is created.</summary>
    private static void WriteAtomically(string path, string text)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        string temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, text, SmlConverter.Encoding);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
