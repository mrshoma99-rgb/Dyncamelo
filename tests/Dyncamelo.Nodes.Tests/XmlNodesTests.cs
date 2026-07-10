using System;
using System.Collections.Generic;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

public class XmlNodesTests
{
    private static Dictionary<string, object?> AsDict(object? value) =>
        Assert.IsType<Dictionary<string, object?>>(value);

    // --------------------------------------------------------------- Shape

    [Fact]
    public void Parse_WrapsResultInRootElementName()
    {
        var value = XmlNodes.Parse("<root><a>1</a></root>");

        var root = AsDict(value);
        var pair = Assert.Single(root);
        Assert.Equal("root", pair.Key);
    }

    [Fact]
    public void Parse_AttributesArePrefixedWithAt()
    {
        var value = XmlNodes.Parse("<task id=\"7\" name=\"Dig\"/>");

        var task = AsDict(AsDict(value)["task"]);
        Assert.Equal("7", task["@id"]);
        Assert.Equal("Dig", task["@name"]);
    }

    [Fact]
    public void Parse_TextOnlyElement_BecomesString()
    {
        var value = XmlNodes.Parse("<name>  Pump 42  </name>");

        Assert.Equal("Pump 42", AsDict(value)["name"]);
    }

    [Fact]
    public void Parse_EmptyElements_BecomeNull()
    {
        var value = XmlNodes.Parse("<r><a/><b></b><c>   </c></r>");

        var r = AsDict(AsDict(value)["r"]);
        Assert.Null(r["a"]);
        Assert.Null(r["b"]);
        Assert.Null(r["c"]);
    }

    [Fact]
    public void Parse_RepeatedElements_BecomeLists()
    {
        var value = XmlNodes.Parse("<tasks><task>Dig</task><task>Pour</task><task>Cure</task></tasks>");

        var tasks = AsDict(AsDict(value)["tasks"]);
        var list = Assert.IsType<List<object?>>(tasks["task"]);
        Assert.Equal(new object?[] { "Dig", "Pour", "Cure" }, list);
    }

    [Fact]
    public void Parse_SingleChildStaysScalar_NotAOneElementList()
    {
        var value = XmlNodes.Parse("<tasks><task>Dig</task></tasks>");

        var tasks = AsDict(AsDict(value)["tasks"]);
        Assert.Equal("Dig", tasks["task"]);
    }

    [Fact]
    public void Parse_MixedContent_TextGoesUnderHashText()
    {
        var value = XmlNodes.Parse("<p kind=\"note\">before<b>bold</b></p>");

        var p = AsDict(AsDict(value)["p"]);
        Assert.Equal("note", p["@kind"]);
        Assert.Equal("bold", p["b"]);
        Assert.Equal("before", p["#text"]);
    }

    [Fact]
    public void Parse_NestedStructure_MirrorsJsonParseShape()
    {
        var value = XmlNodes.Parse(
            "<project name=\"P1\">" +
            "<task id=\"1\">Dig</task>" +
            "<task id=\"2\">Pour</task>" +
            "<owner/>" +
            "</project>");

        var project = AsDict(AsDict(value)["project"]);
        Assert.Equal("P1", project["@name"]);
        Assert.Null(project["owner"]);

        var tasks = Assert.IsType<List<object?>>(project["task"]);
        var task0 = AsDict(tasks[0]);
        Assert.Equal("1", task0["@id"]);
        Assert.Equal("Dig", task0["#text"]);
    }

    // ------------------------------------------------------- Text handling

    [Fact]
    public void Parse_CdataIsTreatedAsText()
    {
        var value = XmlNodes.Parse("<code><![CDATA[a < b && c > d]]></code>");

        Assert.Equal("a < b && c > d", AsDict(value)["code"]);
    }

    [Fact]
    public void Parse_CommentsAndProcessingInstructionsAreIgnored()
    {
        var value = XmlNodes.Parse("<?xml version=\"1.0\"?><a><!-- note --><?pi data?>text</a>");

        Assert.Equal("text", AsDict(value)["a"]);
    }

    [Fact]
    public void Parse_EntitiesAreDecoded()
    {
        var value = XmlNodes.Parse("<a>x &lt; y &amp; z</a>");

        Assert.Equal("x < y & z", AsDict(value)["a"]);
    }

    // ---------------------------------------------------------- Namespaces

    [Fact]
    public void Parse_KeepsNamespacePrefixesInNames()
    {
        var value = XmlNodes.Parse("<r xmlns:ns=\"urn:x\"><ns:item code=\"1\"/></r>");

        var r = AsDict(AsDict(value)["r"]);
        Assert.Equal("urn:x", r["@xmlns:ns"]);
        var item = AsDict(r["ns:item"]);
        Assert.Equal("1", item["@code"]);
    }

    [Fact]
    public void Parse_DefaultNamespace_LeavesNamesUnprefixed()
    {
        var value = XmlNodes.Parse("<r xmlns=\"urn:x\"><item>v</item></r>");

        var r = AsDict(AsDict(value)["r"]);
        Assert.Equal("urn:x", r["@xmlns"]);
        Assert.Equal("v", r["item"]);
    }

    // --------------------------------------------------------------- Errors

    [Fact]
    public void Parse_InvalidXml_ThrowsFormatException()
    {
        var ex = Assert.Throws<FormatException>(() => XmlNodes.Parse("<a><b></a>"));
        Assert.Contains("not valid XML", ex.Message);
    }

    [Fact]
    public void Parse_EmptyInput_ThrowsHelpfulMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => XmlNodes.Parse("  "));
        Assert.Contains("XML.Parse", ex.Message);
    }
}
