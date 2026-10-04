using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Serialization;
using CamelGraph.TestSupport.StandIns;
using Xunit;

namespace CamelGraph.Integration.Tests;

/// <summary>
/// Saved graphs that hold the search and selection-set nodes as they were before the audit still open: the ids those nodes had before
/// they gained inputs (an exists mode, a list of values and a scope on Search.ByProperty; a mode and a folder on the set creators; includeCount
/// on SelectionSet.Info) resolve to the node as it is now, and the merged nodes (Search.HasProperty, HasCategory, InItems) stay registered
/// as retired nodes. The Navisworks assembly cannot be loaded on this build agent, so the definitions are the stand-ins built from the
/// source (see <see cref="NavisworksStandInTests"/>); a typed-in value and a wire saved under the old id must survive the load.
/// </summary>
public class SearchAndSetsEvolutionTests
{
    private const string Doc = "Autodesk.Navisworks.Api.Document";

    private static readonly Lazy<(NodeRegistry Registry, StandInCatalogue StandIns)> Rig =
        new Lazy<(NodeRegistry, StandInCatalogue)>(() =>
        {
            var registry = Pipeline.CreateRegistry();
            var standIns = StandInCatalogue.Create(NodeCatalogue.Load(), SourceScan.ReadNavisworks(), registry);
            standIns.RegisterInto(registry);
            return (registry, standIns);
        });

    private static NodeRegistry Registry => Rig.Value.Registry;

    public static IEnumerable<object[]> EarlierIds()
    {
        yield return new object[] { "CamelGraph.Navisworks.SearchNodes.ByProperty@string,string,object,string,string," + Doc, "Search.ByProperty", false };
        yield return new object[] { "CamelGraph.Navisworks.SelectionSetNodes.Create@string,System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>," + Doc, "SelectionSet.Create", false };
        yield return new object[] { "CamelGraph.Navisworks.SelectionSetNodes.CreateFromSearch@string,string,string,object," + Doc, "SelectionSet.CreateFromSearch", false };
        yield return new object[] { "CamelGraph.Navisworks.SelectionExtraNodes.Info@Autodesk.Navisworks.Api.SelectionSet," + Doc, "SelectionSet.Info", false };
    }

    [Theory]
    [MemberData(nameof(EarlierIds))]
    public void AnEarlierIdResolvesToTheNodeAsItIsNow(string earlierId, string name, bool retired)
    {
        Assert.True(Registry.TryGetDefinition(earlierId, out var definition), "no definition for the earlier id " + earlierId);
        Assert.Equal(name, definition!.Name);
        Assert.Equal(retired, definition.IsDeprecated);
        Assert.NotEqual(earlierId, definition.Id);
    }

    [Theory]
    [InlineData("Search.HasProperty", "CamelGraph.Navisworks.SearchNodes.HasProperty@string,string,string," + Doc)]
    [InlineData("Search.HasCategory", "CamelGraph.Navisworks.SearchNodes.HasCategory@string,string," + Doc)]
    [InlineData("Search.InItems", "CamelGraph.Navisworks.SearchNodes.InItems@System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>,string,string,object,string," + Doc)]
    public void TheMergedSearchNodesAreRetiredNotRemoved(string name, string id)
    {
        Assert.True(Registry.TryGetDefinition(id, out var definition), "the retired node " + name + " no longer loads from " + id);
        Assert.Equal(name, definition!.Name);
        Assert.True(definition.IsDeprecated);
        Assert.Equal("Search.ByProperty", definition.Replacement);
    }

    [Fact]
    public void ASavedGraphWithTheEarlierSearchByPropertyIdKeepsItsTypedValuesAndItsWire()
    {
        var registry = Registry;
        var current = registry.Definitions.Single(d => d.Name == "Search.ByProperty");
        var graph = new GraphModel();
        var search = registry.CreateZeroTouchNode(current.Id)!;
        var sink = registry.CreateZeroTouchNode(registry.Definitions.Single(d => d.Name == "Selection.SetCurrent").Id)!;
        graph.AddNode(search);
        graph.AddNode(sink);
        search.InPorts.Single(p => p.Name == "categoryName").SetUserValue("Element");
        search.InPorts.Single(p => p.Name == "propertyName").SetUserValue("Category");
        search.InPorts.Single(p => p.Name == "mode").SetUserValue(">=");
        Assert.True(graph.Connect(search.OutPorts[0], sink.InPorts[0]).Success);

        var serializer = new GraphSerializer(registry);
        var earlier = "CamelGraph.Navisworks.SearchNodes.ByProperty@string,string,object,string,string," + Doc;
        var json = serializer.Serialize(graph).Replace("\"" + current.Id + "\"", "\"" + earlier + "\"");
        Assert.Contains(earlier, json);

        var loaded = serializer.Deserialize(json);

        Assert.Empty(serializer.LoadWarnings);
        Assert.Empty(loaded.Nodes.OfType<MissingNodeModel>());
        var loadedSearch = loaded.Nodes.OfType<ZeroTouchNodeModel>().Single(n => n.Name == "Search.ByProperty");
        Assert.Equal(current.Id, loadedSearch.Definition.Id);
        Assert.Equal("Element", loadedSearch.InPorts.Single(p => p.Name == "categoryName").UserValue);
        Assert.Equal(">=", loadedSearch.InPorts.Single(p => p.Name == "mode").UserValue);
        Assert.NotNull(loaded.FindConnectionsFrom(loadedSearch.OutPorts[0]).FirstOrDefault());
    }

    [Fact]
    public void ASavedGraphWithAnEarlierSetCreatorIdKeepsTheNameItWasGiven()
    {
        var registry = Registry;
        var serializer = new GraphSerializer(registry);
        foreach (var (name, earlier, typed) in new[]
                 {
                     ("SelectionSet.Create", "CamelGraph.Navisworks.SelectionSetNodes.Create@string,System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>," + Doc, "Walls"),
                     ("SelectionSet.CreateFromSearch", "CamelGraph.Navisworks.SelectionSetNodes.CreateFromSearch@string,string,string,object," + Doc, "Level 2"),
                 })
        {
            var current = registry.Definitions.Single(d => d.Name == name);
            var graph = new GraphModel();
            var node = registry.CreateZeroTouchNode(current.Id)!;
            graph.AddNode(node);
            node.InPorts.Single(p => p.Name == "name").SetUserValue(typed);

            var json = serializer.Serialize(graph).Replace("\"" + current.Id + "\"", "\"" + earlier + "\"");
            var loaded = serializer.Deserialize(json);

            Assert.Empty(loaded.Nodes.OfType<MissingNodeModel>());
            var back = loaded.Nodes.OfType<ZeroTouchNodeModel>().Single();
            Assert.Equal(current.Id, back.Definition.Id);
            Assert.Equal(typed, back.InPorts.Single(p => p.Name == "name").UserValue);
        }
    }

    [Fact]
    public void TheNewSearchInputsAreOptionalSoAnOldWiringStillRuns()
    {
        var search = Registry.Definitions.Single(d => d.Name == "Search.ByProperty");

        Assert.True(search.Inputs.Single(i => i.Name == "value").HasDefault);
        Assert.True(search.Inputs.Single(i => i.Name == "within").HasDefault);
        Assert.True(search.Inputs.Single(i => i.Name == "within").MultiInput);
        Assert.Contains("exists", search.Inputs.Single(i => i.Name == "mode").Choices!);

        var create = Registry.Definitions.Single(d => d.Name == "SelectionSet.CreateFromSearch");
        Assert.True(create.Inputs.Single(i => i.Name == "folder").HasDefault);
        Assert.True(create.Inputs.Single(i => i.Name == "mode").HasDefault);

        var info = Registry.Definitions.Single(d => d.Name == "SelectionSet.Info");
        Assert.True(info.Inputs.Single(i => i.Name == "includeCount").HasDefault);
        Assert.Equal(new[] { "name", "kind", "itemCount", "folder" }, info.Outputs.Select(o => o.Name));
    }

    [Fact]
    public void TheNewSetsFolderNodesAreInTheLibrary()
    {
        foreach (var name in new[] { "SelectionSets.InFolder", "SelectionSets.SortFolder", "SelectionSets.RenameFolder", "SelectionSets.DeleteFolder" })
        {
            var definition = Registry.Definitions.Single(d => d.Name == name);
            Assert.False(definition.IsDeprecated);
            Assert.Equal("Navisworks.SelectionSets", definition.Category);
        }
    }
}
