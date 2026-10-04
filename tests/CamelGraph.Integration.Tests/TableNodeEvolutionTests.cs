using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Serialization;
using CamelGraph.Nodes;
using Xunit;

namespace CamelGraph.Integration.Tests;

/// <summary>
/// Table nodes whose column inputs became lists (Sort, Distinct, GroupBy, Join), whose Join got a second output and whose writers
/// stopped replicating over a list of tables: a graph saved under the old definition id must still load, keep its wires and its
/// typed text, and give the same result.
/// </summary>
public class TableNodeEvolutionTests : IDisposable
{
    private const string Table = "CamelGraph.Nodes.CamelGraphTable";
    private const string ObjectList = "System.Collections.Generic.IList<object>";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "camelgraph-tables-" + Guid.NewGuid().ToString("N"));

    public TableNodeEvolutionTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Csv(string name, params string[] lines)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        return path;
    }

    private static ZeroTouchNodeModel ReadCsv(NodeRegistry registry, GraphModel graph, string path, string name)
    {
        var node = Pipeline.ZeroTouch(registry, "Table.FromCsvFile", name);
        graph.AddNode(node);
        node.InPorts.First(p => p.Name == "path").SetUserValue(path);
        return node;
    }

    private static string DefinitionId(NodeRegistry registry, string name) => registry.Definitions.Single(d => d.Name == name).Id;

    /// <summary>Saves the graph, puts the pre-audit definition id into the file where the new one is, loads it and runs it.</summary>
    private static GraphModel LoadAsSavedBefore(GraphModel graph, NodeRegistry registry, string newId, string oldId, out RunResult result)
    {
        var serializer = new GraphSerializer(registry);
        var json = serializer.Serialize(graph);
        Assert.Contains("\"" + newId + "\"", json);
        var loaded = serializer.Deserialize(json.Replace("\"" + newId + "\"", "\"" + oldId + "\""));

        Assert.Empty(serializer.LoadWarnings);
        Assert.DoesNotContain(loaded.Nodes, n => n is MissingNodeModel);
        result = new GraphEngine().Run(loaded);
        return loaded;
    }

    private static string Lines(object? text) => ((string)text!).Replace("\r\n", "\n");

    [Fact]
    public void ASortSavedWithATextColumnsInputStillLoadsAndSortsByTheTypedText()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "sort" };
        var read = ReadCsv(registry, graph, Csv("elements.csv", "Level,Category,Length", "L01,Wall,3000", "L02,Door,900", "L01,Wall,4500"), "Read");
        var sort = Pipeline.ZeroTouch(registry, "Table.Sort", "Sort");
        var text = Pipeline.ZeroTouch(registry, "Table.ToText", "Text");
        graph.AddNode(sort);
        graph.AddNode(text);
        Pipeline.Connect(graph, read, "table", sort, "table");
        Pipeline.Connect(graph, sort, "table", text, "table");
        sort.InPorts.First(p => p.Name == "columns").SetUserValue("Level, -Length");
        text.InPorts.First(p => p.Name == "format").SetUserValue("csv");

        var loaded = LoadAsSavedBefore(
            graph, registry, DefinitionId(registry, "Table.Sort"), "CamelGraph.Nodes.TableToolkitNodes.Sort@" + Table + ",string,bool", out var result);

        Assert.True(result.Success);
        Assert.Equal("Level,Category,Length\nL01,Wall,4500\nL01,Wall,3000\nL02,Door,900\n", Lines(Pipeline.Output(loaded, "Text")));
    }

    [Fact]
    public void AGroupBySavedWithATextByInputStillLoadsAndGroups()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "group" };
        var read = ReadCsv(registry, graph, Csv("elements.csv", "Level,Category,Length", "L01,Wall,3000", "L02,Door,900", "L01,Wall,4500"), "Read");
        var group = Pipeline.ZeroTouch(registry, "Table.GroupBy", "Group");
        var text = Pipeline.ZeroTouch(registry, "Table.ToText", "Text");
        graph.AddNode(group);
        graph.AddNode(text);
        Pipeline.Connect(graph, read, "table", group, "table");
        Pipeline.Connect(graph, group, "table", text, "table");
        group.InPorts.First(p => p.Name == "by").SetUserValue("Level");
        group.InPorts.First(p => p.Name == "aggregations").SetUserValue("sum:Length");
        text.InPorts.First(p => p.Name == "format").SetUserValue("csv");

        var loaded = LoadAsSavedBefore(
            graph, registry, DefinitionId(registry, "Table.GroupBy"), "CamelGraph.Nodes.TableToolkitNodes.GroupBy@" + Table + ",string," + ObjectList, out var result);

        Assert.True(result.Success);
        Assert.Equal("Level,sum(Length)\nL01,7500\nL02,900\n", Lines(Pipeline.Output(loaded, "Text")));
    }

    [Fact]
    public void ADistinctSavedWithATextColumnsInputStillLoads()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "distinct" };
        var read = ReadCsv(registry, graph, Csv("elements.csv", "Level,Category", "L01,Wall", "L02,Door", "L01,Pipe"), "Read");
        var distinct = Pipeline.ZeroTouch(registry, "Table.Distinct", "Distinct");
        var text = Pipeline.ZeroTouch(registry, "Table.ToText", "Text");
        graph.AddNode(distinct);
        graph.AddNode(text);
        Pipeline.Connect(graph, read, "table", distinct, "table");
        Pipeline.Connect(graph, distinct, "table", text, "table");
        distinct.InPorts.First(p => p.Name == "columns").SetUserValue("Level");
        text.InPorts.First(p => p.Name == "format").SetUserValue("csv");

        var loaded = LoadAsSavedBefore(
            graph, registry, DefinitionId(registry, "Table.Distinct"), "CamelGraph.Nodes.TableToolkitNodes.Distinct@" + Table + ",string", out var result);

        Assert.True(result.Success);
        Assert.Equal("Level,Category\nL01,Wall\nL02,Door\n", Lines(Pipeline.Output(loaded, "Text")));
    }

    [Fact]
    public void AJoinSavedWithTextKeysStillLoadsKeepsItsTableWireAndJoins()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "join" };
        var left = ReadCsv(registry, graph, Csv("model.csv", "GUID,Name", "5f8c1b9e-0000-4a7b-9c3d-0123456789ab,Wall 1", "5F8C1B9E-0000-4A7B-9C3D-0123456789AC,Wall 2", "5f8c1b9e-0000-4a7b-9c3d-0123456789ad,Wall 3"), "Left");
        var right = ReadCsv(registry, graph, Csv("excel.csv", "GUID,Fire", "5F8C1B9E-0000-4A7B-9C3D-0123456789AB,EI30", "5f8c1b9e-0000-4a7b-9c3d-0123456789ac,EI60"), "Right");
        var join = Pipeline.ZeroTouch(registry, "Table.Join", "Join");
        var text = Pipeline.ZeroTouch(registry, "Table.ToText", "Text");
        graph.AddNode(join);
        graph.AddNode(text);
        Pipeline.Connect(graph, left, "table", join, "left");
        Pipeline.Connect(graph, right, "table", join, "right");
        Pipeline.Connect(graph, join, "table", text, "table");
        join.InPorts.First(p => p.Name == "leftKey").SetUserValue("GUID");
        join.InPorts.First(p => p.Name == "kind").SetUserValue("left");
        text.InPorts.First(p => p.Name == "format").SetUserValue("csv");

        var oldId = "CamelGraph.Nodes.TableToolkitNodes.Join@" + Table + "," + Table + ",string,string,string";
        var loaded = LoadAsSavedBefore(graph, registry, DefinitionId(registry, "Table.Join"), oldId, out var result);

        Assert.True(result.Success);
        // the file was written when keys were compared exactly; today a GUID in capitals on one side still finds its partner
        Assert.Equal("GUID,Name,Fire\n5f8c1b9e-0000-4a7b-9c3d-0123456789ab,Wall 1,EI30\n5F8C1B9E-0000-4A7B-9C3D-0123456789AC,Wall 2,EI60\n5f8c1b9e-0000-4a7b-9c3d-0123456789ad,Wall 3,\n", Lines(Pipeline.Output(loaded, "Text")));
        Assert.Equal(3, ((CamelGraphTable)Pipeline.Output(loaded, "Join", "table")!).ColumnCount);
    }
}
