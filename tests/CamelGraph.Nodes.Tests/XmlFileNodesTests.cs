using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CamelGraph.Core.Files;
using CamelGraph.Core.Loader;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>XML.ReadFromFile (SYS-33): the file is decoded the way its declaration says, then converted like XML.Parse.</summary>
[Collection("GraphContext")]
public class XmlFileNodesTests : IDisposable
{
    private readonly string _directory;

    public XmlFileNodesTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "CamelGraphXmlFile", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    private string Save(string name, byte[] bytes)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static Dictionary<string, object?> Root(object? value, string name) =>
        Assert.IsType<Dictionary<string, object?>>(Assert.IsType<Dictionary<string, object?>>(value)[name]);

    private static byte[] Cp1252(string text) => TextFile.Encode(text, FileEncoding.Windows1252, withMark: false);

    [Fact]
    public void AFileThatDeclaresWindows1252_IsDecodedAsWindows1252()
    {
        var xml = "<?xml version=\"1.0\" encoding=\"windows-1252\"?><Project><Name>Müller €</Name><Task id=\"1\"/><Task id=\"2\"/></Project>";
        var path = Save("msp.xml", Cp1252(xml));

        var root = Root(XmlFileNodes.ReadFromFile(path), "Project");

        Assert.Equal("Müller €", root["Name"]);
        Assert.Equal(2, Assert.IsType<List<object?>>(root["Task"]).Count);

        // What the old two-node recipe did: decode as UTF-8 first, which spoils the accents before the parser sees them.
        var old = XmlNodes.Parse(File.ReadAllText(path, new UTF8Encoding(false)).Replace("windows-1252", "utf-8"));
        Assert.NotEqual("Müller €", ((Dictionary<string, object?>)((Dictionary<string, object?>)old!)["Project"]!)["Name"]);
    }

    [Theory]
    [InlineData("iso-8859-1")]
    [InlineData("ISO-8859-1")]
    [InlineData("latin1")]
    public void LatinOneNamesAreUnderstood(string declared)
    {
        var xml = "<?xml version=\"1.0\" encoding=\"" + declared + "\"?><a>café</a>";
        Assert.Equal("café", ((Dictionary<string, object?>)XmlFileNodes.ReadFromFile(Save("l1.xml", Cp1252(xml)))!)["a"]);
    }

    [Fact]
    public void Utf8AndUtf16Files_AreReadWithOrWithoutAMark()
    {
        var xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><a>é中</a>";
        Assert.Equal("é中", ((Dictionary<string, object?>)XmlFileNodes.ReadFromFile(Save("u8.xml", new UTF8Encoding(false).GetBytes(xml)))!)["a"]);
        Assert.Equal("é中", ((Dictionary<string, object?>)XmlFileNodes.ReadFromFile(Save("u8bom.xml", new UTF8Encoding(true).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(xml)).ToArray()))!)["a"]);

        var utf16 = "<?xml version=\"1.0\" encoding=\"utf-16\"?><a>é中</a>";
        Assert.Equal("é中", ((Dictionary<string, object?>)XmlFileNodes.ReadFromFile(Save("u16.xml", Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(utf16)).ToArray()))!)["a"]);

        var noDeclaration = Save("plain.xml", new UTF8Encoding(false).GetBytes("<a>é</a>"));
        Assert.Equal("é", ((Dictionary<string, object?>)XmlFileNodes.ReadFromFile(noDeclaration)!)["a"]);
    }

    [Fact]
    public void ItGivesTheSameShapeAsXmlParse()
    {
        var xml = "<r a=\"1\"><x>one</x><x>two</x><e/><t>text<k>v</k></t></r>";
        var path = Save("shape.xml", new UTF8Encoding(false).GetBytes(xml));

        var fromFile = XmlFileNodes.ReadFromFile(path);
        var parsed = XmlNodes.Parse(xml);

        Assert.Equal(CamelGraph.Core.Types.TypeCoercion.FormatValue(parsed), CamelGraph.Core.Types.TypeCoercion.FormatValue(fromFile));
    }

    [Fact]
    public void ABrokenOrUnknownFile_IsExplainedWithItsPath()
    {
        var broken = Save("broken.xml", new UTF8Encoding(false).GetBytes("<a><b></a>"));
        var ex = Assert.Throws<FormatException>(() => XmlFileNodes.ReadFromFile(broken));
        Assert.Contains("XML.ReadFromFile", ex.Message);
        Assert.Contains(broken, ex.Message);
        Assert.Contains("not valid XML", ex.Message);

        var unknown = Save("odd.xml", new UTF8Encoding(false).GetBytes("<?xml version=\"1.0\" encoding=\"x-no-such-page\"?><a/>"));
        var odd = Assert.Throws<FormatException>(() => XmlFileNodes.ReadFromFile(unknown));
        Assert.Contains("x-no-such-page", odd.Message);
        Assert.Contains("UTF-8", odd.Message);

        Assert.Throws<FileNotFoundException>(() => XmlFileNodes.ReadFromFile(Path.Combine(_directory, "nope.xml")));
        Assert.Contains("File Path", Assert.Throws<ArgumentException>(() => XmlFileNodes.ReadFromFile(" ")).Message);
    }

    [Fact]
    public void ARelativePathStartsInTheGraphFolder_AndTheNodeIsRegisteredInData()
    {
        Save("rel.xml", new UTF8Encoding(false).GetBytes("<a>1</a>"));
        using (GraphContext.Use(_directory))
        {
            Assert.Equal("1", ((Dictionary<string, object?>)XmlFileNodes.ReadFromFile("rel.xml")!)["a"]);
        }

        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var definition = registry.Definitions.Single(d => d.Name == "XML.ReadFromFile");
        Assert.Equal("Data", definition.Category);
        Assert.Equal(NodePathMode.Open, definition.Inputs.Single().PathMode);
        Assert.Contains("*.xml", definition.Inputs.Single().PathFilter);
    }
}
