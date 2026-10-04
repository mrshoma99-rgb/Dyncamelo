using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes;

/// <summary>
/// XML nodes. XML.Parse converts markup into the same dictionary/list shape
/// that JSON.Parse produces, so downstream Dictionary.* / List.* nodes work
/// identically on both formats (MSP/P6 XML schedules, BCF internals,
/// search-set XML, ...).
/// </summary>
[NodeCategory("Data")]
public static class XmlNodes
{
    /// <summary>
    /// Parses XML text into dictionaries, lists and strings — the same shape
    /// JSON.Parse produces. Rules: the result is a dictionary keyed by the
    /// root element's name; every element with attributes or child elements
    /// becomes a dictionary (attributes keyed "@name", children keyed by
    /// element name, repeated element names collected into a list); an
    /// element with only text becomes that text (string); an empty element
    /// becomes null; text mixed with attributes/children is stored under
    /// "#text". All values stay strings — XML carries no type information.
    /// Comments and processing instructions are ignored. An element that occurs once
    /// is a dictionary or a string and the same element occurring twice is a list, so
    /// the names listed in <paramref name="listElements"/> are always wrapped in a list.
    /// </summary>
    /// <param name="xml">The XML text to parse.</param>
    /// <param name="listElements">Names of elements that must always come as a list, even when the file holds only one of them; separated by commas or semicolons, or * for every element. Empty (default) keeps the rule that only repeated elements are lists.</param>
    /// <returns>The parsed value (dictionary of dictionaries/lists/strings).</returns>
    [NodeName("XML.Parse")]
    [NodeAliases("CamelGraph.Nodes.XmlNodes.Parse@string")]
    [return: NodeName("value")]
    [NodeDescription("Parses XML into dictionaries, lists and strings (attributes as \"@name\", repeated elements as lists, mixed text as \"#text\"). An element that occurs once is a dictionary or a text, the same element occurring twice is a list, so a graph tried on a file with several items can break on a file with one: name such elements in listElements (for example Task, Resource; or * for all) and they always come as a list. Dictionary.ValueAtPath reads nested results either way. DOCTYPE declarations are accepted but an external entity is never read from disk or the network.")]
    [NodeSearchTags("xml", "parse", "deserialize", "decode", "markup", "schedule", "msp", "p6")]
    public static object? Parse(string xml, string listElements = "")
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            throw new ArgumentException(
                "XML.Parse requires XML text, e.g. \"<root attr=\\\"1\\\">...</root>\".", nameof(xml));
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            throw new FormatException("XML.Parse: the input is not valid XML. " + ex.Message, ex);
        }

        var listNames = ListNames(listElements);
        var root = document.Root!;
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [NameOf(root)] = ConvertElement(root, listNames),
        };
    }

    // ------------------------------------------------------------------
    // Helpers (not imported as nodes: non-public).
    // ------------------------------------------------------------------

    // The element names the user wants as lists even when they occur once; null = none, an empty-named "*" entry = all.
    private static HashSet<string>? ListNames(string? listElements)
    {
        if (string.IsNullOrWhiteSpace(listElements))
        {
            return null;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var piece in listElements!.Split(',', ';', '\n', '\r'))
        {
            var trimmed = piece.Trim();
            if (trimmed.Length > 0)
            {
                names.Add(trimmed);
            }
        }

        return names.Count == 0 ? null : names;
    }

    private static object? ConvertElement(XElement element, HashSet<string>? listNames)
    {
        var attributes = element.Attributes().ToList();
        var children = element.Elements().ToList();
        var text = TextOf(element);

        if (attributes.Count == 0 && children.Count == 0)
        {
            // Leaf element: its (trimmed) text, or null when empty.
            return text.Length == 0 ? null : text;
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var attribute in attributes)
        {
            result["@" + NameOf(attribute)] = attribute.Value;
        }

        foreach (var child in children)
        {
            var name = NameOf(child);
            var value = ConvertElement(child, listNames);
            if (listNames != null && !result.ContainsKey(name) &&
                (listNames.Contains("*") || listNames.Contains(name) || listNames.Contains(child.Name.LocalName)))
            {
                result[name] = new List<object?> { value };
            }
            else if (result.TryGetValue(name, out var existing))
            {
                if (existing is List<object?> siblings)
                {
                    siblings.Add(value);
                }
                else
                {
                    result[name] = new List<object?> { existing, value };
                }
            }
            else
            {
                result[name] = value;
            }
        }

        if (text.Length > 0)
        {
            result["#text"] = text;
        }

        return result;
    }

    /// <summary>Concatenated, trimmed text content (text nodes and CDATA sections).</summary>
    private static string TextOf(XElement element)
    {
        StringBuilder? builder = null;
        foreach (var node in element.Nodes())
        {
            if (node is XText textNode) // XCData derives from XText
            {
                builder = builder ?? new StringBuilder();
                builder.Append(textNode.Value);
            }
        }

        return builder == null ? string.Empty : builder.ToString().Trim();
    }

    /// <summary>Element name as written in the source ("item" or "ns:item").</summary>
    private static string NameOf(XElement element)
    {
        var prefix = element.GetPrefixOfNamespace(element.Name.Namespace);
        return string.IsNullOrEmpty(prefix) ? element.Name.LocalName : prefix + ":" + element.Name.LocalName;
    }

    /// <summary>Attribute name as written in the source ("attr", "ns:attr", "xmlns" or "xmlns:ns").</summary>
    private static string NameOf(XAttribute attribute)
    {
        if (attribute.IsNamespaceDeclaration)
        {
            return attribute.Name.LocalName == "xmlns" ? "xmlns" : "xmlns:" + attribute.Name.LocalName;
        }

        var prefix = attribute.Parent?.GetPrefixOfNamespace(attribute.Name.Namespace);
        return string.IsNullOrEmpty(prefix) ? attribute.Name.LocalName : prefix + ":" + attribute.Name.LocalName;
    }
}
