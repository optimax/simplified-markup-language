namespace Sml;

/// <summary>A document is malformed or cannot be represented.</summary>
public class SmlException(string reason, int? line = null)
    : Exception(line is null ? reason : $"line {line}: {reason}")
{
    public string Reason { get; } = reason;

    public int? Line { get; } = line;
}

/// <summary>XML input is malformed or violates Namespaces in XML.</summary>
public class XmlInputException(string reason, int? line = null) : SmlException(reason, line);

/// <summary>XML input contains information outside the supported subset (UNSUPPORTED_XML_INFORMATION).</summary>
public sealed class UnsupportedXmlException(string reason, int? line = null) : XmlInputException(reason, line);
