using System;
using System.Linq;
using Dyncamelo.Core.Loader;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// Loader-level checks for the v0.3 WS-A data-plumbing nodes: they register
/// with the expected names, categories, output ports and port defaults.
/// </summary>
public class DataPlumbingRegistrationTests
{
    private static NodeRegistry CreateRegistry()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        return registry;
    }

    private static NodeDefinition Definition(NodeRegistry registry, string name) =>
        registry.Definitions.Single(d => d.Name == name);

    [Fact]
    public void RegisterAll_ImportsEveryDataPlumbingNode()
    {
        var registry = CreateRegistry();
        var names = registry.Definitions.Select(d => d.Name).ToList();

        foreach (var expected in new[]
        {
            "Excel.ReadFromFile", "Excel.WriteToFile",
            "Table.JoinByKey", "XML.Parse", "Snapshot.Diff",
            "BoundingBox.Contains", "Point.Translate",
        })
        {
            Assert.Contains(expected, names);
        }
    }

    [Fact]
    public void DataPlumbingNodes_LandInTheExpectedCategories()
    {
        var registry = CreateRegistry();

        Assert.Equal("File", Definition(registry, "Excel.ReadFromFile").Category);
        Assert.Equal("File", Definition(registry, "Excel.WriteToFile").Category);
        Assert.Equal("List", Definition(registry, "Table.JoinByKey").Category);
        Assert.Equal("File", Definition(registry, "XML.Parse").Category);
        Assert.Equal("File", Definition(registry, "Snapshot.Diff").Category);
        Assert.Equal("Geometry", Definition(registry, "BoundingBox.Contains").Category);
        Assert.Equal("Geometry", Definition(registry, "Point.Translate").Category);
    }

    [Fact]
    public void MultiReturnNodes_ExposeTheDocumentedOutputPorts()
    {
        var registry = CreateRegistry();

        Assert.Equal(
            new[] { "rows", "headers", "sheetNames" },
            Definition(registry, "Excel.ReadFromFile").Outputs.Select(o => o.Name));
        Assert.Equal(
            new[] { "matchedRows", "unmatchedKeys" },
            Definition(registry, "Table.JoinByKey").Outputs.Select(o => o.Name));
        Assert.Equal(
            new[] { "addedKeys", "removedKeys", "changedKeys" },
            Definition(registry, "Snapshot.Diff").Outputs.Select(o => o.Name));
    }

    [Fact]
    public void ExcelNodes_OptionalPortsCarryTheDocumentedDefaults()
    {
        var registry = CreateRegistry();

        var read = Definition(registry, "Excel.ReadFromFile");
        var sheet = read.Inputs.Single(i => i.Name == "sheet");
        Assert.True(sheet.HasDefault);
        Assert.Equal("", sheet.DefaultValue);
        var hasHeaders = read.Inputs.Single(i => i.Name == "hasHeaders");
        Assert.True(hasHeaders.HasDefault);
        Assert.Equal(true, hasHeaders.DefaultValue);

        var write = Definition(registry, "Excel.WriteToFile");
        var headers = write.Inputs.Single(i => i.Name == "headers");
        Assert.True(headers.HasDefault);
        Assert.Null(headers.DefaultValue);
        var sheetName = write.Inputs.Single(i => i.Name == "sheet");
        Assert.True(sheetName.HasDefault);
        Assert.Equal("Sheet1", sheetName.DefaultValue);
        var append = write.Inputs.Single(i => i.Name == "append");
        Assert.True(append.HasDefault);
        Assert.Equal(false, append.DefaultValue);
    }

    [Fact]
    public void SingleReturnNodes_UseTheDocumentedOutputNames()
    {
        var registry = CreateRegistry();

        Assert.Equal("path", Definition(registry, "Excel.WriteToFile").Outputs.Single().Name);
        Assert.Equal("value", Definition(registry, "XML.Parse").Outputs.Single().Name);
        Assert.Equal("contains", Definition(registry, "BoundingBox.Contains").Outputs.Single().Name);
        Assert.Equal("point", Definition(registry, "Point.Translate").Outputs.Single().Name);
    }
}
