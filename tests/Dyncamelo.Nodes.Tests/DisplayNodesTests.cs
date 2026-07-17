using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Loader;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// Watch List / Watch Image display nodes and the XML-doc-driven port
/// descriptions surfaced by the loader.
/// </summary>
public class DisplayNodesTests
{
    private static EvaluationContext Context => new EvaluationContext();

    // ------------------------------------------------------------ Watch List

    [Fact]
    public void WatchList_List_ProducesIndexedEntries()
    {
        var node = new WatchListNode();
        var result = node.Evaluate(new object?[] { new List<object?> { "a", "b", "c" } }, Context);

        Assert.Equal(3, node.Entries.Count);
        Assert.Equal("0", node.Entries[0].Index);
        Assert.True(node.Entries[0].HasIndex);
        Assert.Equal("a", node.Entries[0].Text);
        Assert.Equal("2", node.Entries[2].Index);
        Assert.Equal("c", node.Entries[2].Text);
        Assert.Equal("0 : a\n1 : b\n2 : c", node.FormattedValue);
        Assert.IsType<List<object?>>(result[0]);
    }

    [Fact]
    public void WatchList_Scalar_ProducesSingleEntryWithoutIndex()
    {
        var node = new WatchListNode();
        node.Evaluate(new object?[] { 42.0 }, Context);

        var entry = Assert.Single(node.Entries);
        Assert.Equal(string.Empty, entry.Index);
        Assert.False(entry.HasIndex);
        Assert.Equal("42", entry.Text);
    }

    // ----------------------------------------------------------- Watch Image

    [Fact]
    public void WatchImage_ExistingFile_ExposesPathAndPassesThrough()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        try
        {
            var node = new WatchImageNode();
            int versionBefore = node.ImageVersion;
            var result = node.Evaluate(new object?[] { path }, Context);

            Assert.Equal(path, node.ImagePath);
            Assert.True(node.HasImage);
            Assert.Equal(Path.GetFileName(path), node.FileName);
            Assert.Equal(versionBefore + 1, node.ImageVersion);
            Assert.Equal(path, result[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WatchImage_MissingFileOrNull_HasNoImage()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");

        var node = new WatchImageNode();
        node.Evaluate(new object?[] { missing }, Context);
        Assert.Equal(missing, node.ImagePath);
        Assert.False(node.HasImage);

        node.Evaluate(new object?[] { null }, Context);
        Assert.Equal(string.Empty, node.ImagePath);
        Assert.Equal(string.Empty, node.FileName);
        Assert.False(node.HasImage);
    }

    [Fact]
    public void WatchImage_IsRegisteredAndRoundTripsViewSize()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);

        var node = registry.CreateNode(WatchImageNode.TypeName);
        Assert.IsType<WatchImageNode>(node);

        var image = (WatchImageNode)node!;
        image.ViewWidth = 300;
        image.ViewHeight = 200;
        var data = new Newtonsoft.Json.Linq.JObject();
        image.SerializeData(data);

        var restored = new WatchImageNode();
        restored.DeserializeData(data);
        Assert.Equal(300, restored.ViewWidth);
        Assert.Equal(200, restored.ViewHeight);
    }

    // ---------------------------------------- XML-doc port descriptions

    [Fact]
    public void Loader_ReadsInputDescriptions_FromXmlDocumentation()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);

        // List.Sort's parameter is documented as "The list to sort." and its
        // IList<object> parameter exercises the generic doc-id encoding.
        var sort = registry.Definitions.Single(d => d.Name == "List.Sort");
        var input = Assert.Single(sort.Inputs);
        Assert.Equal("The list to sort.", input.Description);

        // The single output picks up the <returns> text.
        Assert.Equal("A new sorted list.", Assert.Single(sort.Outputs).Description);
    }

    [Fact]
    public void Loader_XmlDocs_CoverNearlyAllZeroTouchInputs()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);

        var inputs = registry.Definitions.SelectMany(d => d.Inputs).ToList();
        Assert.NotEmpty(inputs);

        // CS1591/CS1573 keep the pack fully documented, so effectively every
        // input port must carry a description; tolerate a tiny remainder so a
        // future doc-id encoding edge case degrades this to a soft signal
        // instead of hiding behind an exact count.
        int described = inputs.Count(i => i.Description.Length > 0);
        Assert.True(
            described >= inputs.Count * 95 / 100,
            $"Only {described}/{inputs.Count} zero-touch inputs carry an XML-doc description.");
    }
}
