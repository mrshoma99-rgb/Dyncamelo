using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes;

/// <summary>
/// XML nodes that read a file. The file is decoded the way its own XML declaration says (<c>encoding="windows-1252"</c>,
/// UTF-16, ...) and then converted exactly like <see cref="XmlNodes.Parse"/>.
/// </summary>
[NodeCategory("Data")]
public static class XmlFileNodes
{
    private static readonly Regex DeclaredEncoding = new Regex(
        "^\\s*<\\?xml[^>]*?\\bencoding\\s*=\\s*[\"']([^\"']+)[\"']",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// Reads an XML file into dictionaries, lists and strings, the same shape XML.Parse produces, honouring the encoding the file
    /// declares.
    /// </summary>
    /// <param name="path">Path to the XML file. A relative path starts in the graph's folder.</param>
    /// <returns>The parsed value (dictionary of dictionaries/lists/strings), see XML.Parse.</returns>
    [NodeName("XML.ReadFromFile")]
    [return: NodeName("value")]
    [NodeDescription("Reads an XML file into dictionaries, lists and strings, in the same shape as XML.Parse (attributes as \"@name\", repeated elements as lists, mixed text as \"#text\"). The encoding the file declares is honoured - UTF-8, UTF-16, Windows-1252 / ISO-8859-1 - so the accents of an MSP or P6 export survive; reading the file with Text.ReadFromFile and then XML.Parse would decode it as UTF-8 first. A relative path starts in the graph's folder.")]
    [NodeSearchTags("xml", "read", "load", "import", "file", "parse", "markup", "schedule", "msp", "p6", "encoding")]
    public static object? ReadFromFile([NodePath(NodePathMode.Open, Filter = FileFilters.Xml)] string path)
    {
        const string node = "XML.ReadFromFile";
        var file = FileNodes.RequireExistingFile(path, node);
        var bytes = FileErrors.Run(node, file, false, () => File.ReadAllBytes(file));
        var text = Decode(bytes, node, file);

        XDocument document;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = null, MaxCharactersFromEntities = 1000000 };
            using (var reader = XmlReader.Create(new StringReader(text), settings))
            {
                document = XDocument.Load(reader);
            }
        }
        catch (XmlException ex)
        {
            throw new FormatException(node + ": the file '" + file + "' is not valid XML. " + ex.Message, ex);
        }

        // One conversion for both nodes: the file is re-serialized (entities are already expanded) and handed to XML.Parse.
        return XmlNodes.Parse(document.ToString(SaveOptions.DisableFormatting));
    }

    /// <summary>The text of an XML file's bytes: a byte-order mark first, then the encoding of the XML declaration, then UTF-8.</summary>
    internal static string Decode(byte[] bytes, string nodeName, string file)
    {
        if (HasByteOrderMark(bytes))
        {
            return TextFile.Decode(bytes, FileEncoding.Auto);
        }

        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 200));
        var match = DeclaredEncoding.Match(head);
        if (!match.Success)
        {
            return TextFile.Decode(bytes, FileEncoding.Auto); // XML without a declaration is UTF-8; an invalid file is read as Windows-1252
        }

        var name = match.Groups[1].Value.Trim();
        switch (name.ToLowerInvariant().Replace("_", "-"))
        {
            case "utf-8":
            case "utf8":
                return TextFile.Decode(bytes, FileEncoding.Utf8);
            case "utf-16":
            case "utf16":
            case "utf-16le":
                return TextFile.Decode(bytes, FileEncoding.Utf16);
            case "windows-1252":
            case "cp1252":
            case "iso-8859-1":
            case "iso8859-1":
            case "latin1":
            case "latin-1":
            case "us-ascii":
            case "ascii":
                return TextFile.Decode(bytes, FileEncoding.Windows1252);
        }

        try
        {
            return Encoding.GetEncoding(name).GetString(bytes);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException(
                nodeName + ": the file '" + file + "' says it is encoded as '" + name + "', which this computer cannot read. " +
                "Save the file as UTF-8 or Windows-1252, or change the encoding in its first line.",
                ex);
        }
    }

    private static bool HasByteOrderMark(byte[] bytes)
    {
        return (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ||
               (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)));
    }
}
