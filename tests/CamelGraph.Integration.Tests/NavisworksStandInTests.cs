using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Serialization;
using CamelGraph.Nodes;
using CamelGraph.TestSupport.StandIns;
using Xunit;

namespace CamelGraph.Integration.Tests;

/// <summary>
/// The Navisworks node stand-ins that the documentation screenshots are drawn with (tests/Shared/StandIns): a dynamic assembly built
/// from docs/camelgraph-nodes.json, so graphs made of Navisworks nodes load, and look, on a machine without Navisworks.
/// </summary>
public class NavisworksStandInTests
{
    private static readonly Lazy<(NodeRegistry Registry, StandInCatalogue StandIns, NodeCatalogue Catalogue)> Rig =
        new Lazy<(NodeRegistry, StandInCatalogue, NodeCatalogue)>(() =>
        {
            var registry = Pipeline.CreateRegistry();
            var catalogue = NodeCatalogue.Load();
            var standIns = StandInCatalogue.Create(catalogue, SourceScan.ReadNavisworks(), registry);
            standIns.RegisterInto(registry);
            return (registry, standIns, catalogue);
        });

    private static NodeRegistry Registry => Rig.Value.Registry;

    private static StandInCatalogue StandIns => Rig.Value.StandIns;

    private static NodeCatalogue Catalogue => Rig.Value.Catalogue;

    [Fact]
    public void TheStandInsAreBuiltWithoutProblems()
    {
        Assert.Empty(StandIns.Problems);
        Assert.NotEmpty(StandIns.Definitions);
    }

    [Fact]
    public void OnlyTheNavisworksNodesNeedAStandIn()
    {
        // Everything else is registered by NodeRegistry.CreateDefault and NodeLibrary.RegisterAll.
        Assert.All(StandIns.Definitions, d => Assert.Equal("CamelGraph.Navisworks", d.AssemblyName));
        Assert.All(StandIns.Definitions.Where(d => !d.IsDeprecated), d => Assert.Contains(Catalogue.Nodes, n => n.Id == d.Id));
        var interactive = StandIns.InteractiveNodes.Select(n => n.Id).ToList();
        Assert.Equal(new[] { "CapturedSelection" }, interactive);
    }

    [Fact]
    public void EveryCatalogueNodeHasExactlyOneDefinitionWithTheSamePorts()
    {
        var standInIds = StandIns.Definitions.Select(d => d.Id).ToList();
        Assert.Equal(standInIds.Count, standInIds.Distinct(StringComparer.Ordinal).Count());

        foreach (var node in Catalogue.Nodes.Where(n => n.Id.Length > 0))
        {
            if (node.Interactive)
            {
                var created = Registry.CreateNode(node.Id);
                Assert.True(created != null, node.Name + ": no node type '" + node.Id + "' is registered.");
                Assert.Equal(node.Name, created!.Name);
                Assert.Equal(node.Category, created.Category);
                continue;
            }

            Assert.True(Registry.TryGetDefinition(node.Id, out var definition), node.Name + ": no definition '" + node.Id + "' is registered.");
            Assert.Equal(1, Registry.Definitions.Count(d => d.Id == node.Id));
            Assert.Equal(node.Name, definition!.Name);
            Assert.Equal(node.Assembly, definition.AssemblyName);
            Assert.Equal(node.Category, definition.Category);
            Assert.Equal(node.Inputs.Select(i => i.Name.TrimStart('@')), definition.Inputs.Select(i => i.Name)); // the catalogue keeps the '@' of a verbatim identifier (Math.Pow's @base)
            Assert.Equal(node.Outputs.Select(o => o.Name), definition.Outputs.Select(o => o.Name));
            for (var i = 0; i < node.Inputs.Count; i++)
            {
                var label = node.Name + " input '" + node.Inputs[i].Name + "'";
                Assert.True(node.Inputs[i].HasDefault == definition.Inputs[i].HasDefault, label + ": optional differs.");
                Assert.True(node.Inputs[i].MultiInput == definition.Inputs[i].MultiInput, label + ": multi-input differs.");
                AssertSameKind(label, node.Inputs[i].Type, definition.Inputs[i].Type);
            }

            for (var i = 0; i < node.Outputs.Count; i++)
            {
                // A multi-output node's ports are untyped (object) and take their colour from [PortKinds]; the catalogue says the same in its own words.
                if (definition.MultiReturnKeys == null)
                {
                    AssertSameKind(node.Name + " output '" + node.Outputs[i].Name + "'", node.Outputs[i].Type, definition.Outputs[i].Type);
                }
                else
                {
                    var hinted = PortKinds.FromTypeName(node.Outputs[i].Type);
                    Assert.True(
                        PortKinds.TryParse(definition.Outputs[i].Kind, out var kind) ? kind.Family == hinted.Family : hinted.Family == PortFamily.Any,
                        node.Name + " output '" + node.Outputs[i].Name + "': the socket kind differs from the catalogue's " + node.Outputs[i].Type + ".");
                }
            }

            // Names are unique across the library (the quick search and the library list go by them).
            Assert.Equal(1, Registry.Definitions.Count(d => d.Name == node.Name));
        }
    }

    private static void AssertSameKind(string label, string catalogueType, Type type)
    {
        var expected = PortKinds.FromTypeName(catalogueType);
        var actual = PortKinds.FromType(type);
        var sameShape = expected.Depth == actual.Depth
            || (expected.Depth != PortDepth.List && expected.Depth != PortDepth.Nested && actual.Depth != PortDepth.List && actual.Depth != PortDepth.Nested);
        // PortKinds.FromType does not see CamelGraph's own CamelGraphTable as data (it strips the "CamelGraph" prefix before the lookup); the real definitions are the same.
        // An enum (Navisworks' Units) is an integer family for a definition and an item for the catalogue's name lookup, in the real assembly too.
        var sameFamily = expected.Family == actual.Family || type.IsEnum || (type.Name == "CamelGraphTable" && expected.Family == PortFamily.Data);
        Assert.True(
            sameFamily && sameShape,
            label + ": the catalogue says " + catalogueType + " (" + expected + "), the definition has " + type.Name + " (" + actual + ").");
    }

    [Fact]
    public void EveryNavisworksIdIsOneTheSourceHas()
    {
        // The ids come from the generator, the types from the source: both must say the same about every node.
        foreach (var node in Catalogue.Nodes.Where(n => n.IsZeroTouch && n.Assembly == "CamelGraph.Navisworks"))
        {
            Assert.True(NavisworksSourceIndex.Resolve(node.Id).Count > 0, node.Name + ": the id '" + node.Id + "' is not the signature of any method in src/CamelGraph.Navisworks.");
        }
    }

    [Fact]
    public void TheDefaultsAreTheCatalogueDefaults()
    {
        var search = Registry.Definitions.Single(d => d.Name == "Search.ByProperty");
        Assert.Equal("equals", search.Inputs.Single(i => i.Name == "mode").DefaultValue);
        Assert.Equal("Self", search.Inputs.Single(i => i.Name == "resolveTo").DefaultValue);
        Assert.True(search.Inputs.Single(i => i.Name == "document").HasDefault);
        Assert.Null(search.Inputs.Single(i => i.Name == "document").DefaultValue);
        Assert.False(search.Inputs.Single(i => i.Name == "categoryName").HasDefault);
    }

    [Fact]
    public void TheSearchDropDownsOfTheRealNodesAreThere()
    {
        var search = Registry.Definitions.Single(d => d.Name == "Search.ByProperty");
        Assert.Equal(new[] { "equals", "contains", "wildcard", ">", ">=", "<", "<=", "exists" }, search.Inputs.Single(i => i.Name == "mode").Choices);
        Assert.Equal(new[] { "Self", "File", "Layer", "FirstObject", "LastObject", "LastUnique", "Geometry" }, search.Inputs.Single(i => i.Name == "resolveTo").Choices);
        Assert.Null(search.Inputs.Single(i => i.Name == "value").Choices);
    }

    [Fact]
    public void TheChoicesRangesAndNameSearchesOfTheSourceAreOnThePorts()
    {
        var scan = SourceScan.ReadNavisworks();
        var checkedChoices = 0;
        var checkedRanges = 0;
        var checkedSearches = 0;
        foreach (var definition in StandIns.Definitions.Where(d => !d.IsDeprecated))
        {
            var method = scan.Find(definition.Id.Split('@')[0], definition.Inputs.Select(i => i.Name));
            Assert.True(method != null, "No source method found for " + definition.Id);
            for (var i = 0; i < definition.Inputs.Count; i++)
            {
                var port = definition.Inputs[i];
                var source = method!.Parameters[i];
                var choices = source.Attribute("NodeChoices");
                if (choices != null)
                {
                    Assert.Equal(choices.Strings(), port.Choices);
                    checkedChoices++;
                }

                if (source.Attribute("NodeRange") != null)
                {
                    Assert.True(port.Range != null, definition.Name + "." + port.Name + ": the [NodeRange] is missing (" + string.Join(", ", source.Attribute("NodeRange")!.Positional) + ")");
                    checkedRanges++;
                }

                if (source.Attribute("NodeTabChoice") != null || source.Attribute("NodePropertyChoice") != null)
                {
                    Assert.True(port.DataChoice != null, definition.Name + "." + port.Name + ": the name search is missing");
                    checkedSearches++;
                }

                if (source.Attribute("PortKinds") != null)
                {
                    Assert.NotEqual(string.Empty, port.Kind);
                }

                if (source.Attribute("NodeChoices") == null && source.Attribute("NodeChoicesFromEnum") == null && !port.Type.IsEnum)
                {
                    Assert.Null(port.Choices);
                }
            }
        }

        Assert.True(checkedChoices >= 15 && checkedRanges >= 15 && checkedSearches >= 10, "choices " + checkedChoices + ", ranges " + checkedRanges + ", searches " + checkedSearches);
    }

    [Fact]
    public void MultiOutputNodesHaveTheirKeysAndKinds()
    {
        var definition = Registry.Definitions.First(d => d.Assembly() == "CamelGraph.Navisworks" && d.MultiReturnKeys != null && d.MultiReturnKeys.Count > 1);
        Assert.Equal(definition.MultiReturnKeys, definition.Outputs.Select(o => o.Name).ToList());
        Assert.All(definition.Outputs, o => Assert.Equal(typeof(object), o.Type));
    }

    [Theory]
    [InlineData("Clash.GroupResults", "results,groupName,moveExisting,document", "test,group,added,moved,skipped")]
    [InlineData("Clash.GroupResultsByStatus", "test,document", "test,groupCount")]
    [InlineData("Clash.GroupResultsByGridIntersection", "test,document", "test,groupCount")]
    [InlineData("Clash.GroupResultsBySameItem", "test,useItem1,document", "test,groupCount")]
    [InlineData("Clash.GroupResultsByProximity", "test,radius,document", "test,groupCount")]
    [InlineData("Clash.GroupResultsByLevel", "test,levelNames,levelElevations,document", "test,groupCount")]
    [InlineData("Viewpoints.FromClashResults", "results,folderName,document", "viewpoints")]
    [InlineData("SelectionSets.BulkByPropertyValues", "categoryName,propertyName,folderName,document", "selectionSets,values")]
    public void TheClashGroupingAndFolderFillingNodesKeepTheirSockets(string name, string inputs, string outputs)
    {
        // These nodes were rewritten to stop searching a whole tree or folder per item; a wired graph must not notice.
        var definition = Registry.Definitions.Single(d => d.Name == name);

        Assert.Equal(inputs.Split(','), definition.Inputs.Select(i => i.Name));
        Assert.Equal(outputs.Split(','), definition.Outputs.Select(o => o.Name));
    }

    [Fact]
    public void TheUnitsNodesGetTheirDropDown()
    {
        var convert = Registry.Definitions.Single(d => d.Name == "Units.Convert");
        Assert.True(convert.Inputs.Single(i => i.Name == "fromUnits").Type.IsEnum);
        Assert.Contains("Millimeters", convert.Inputs.Single(i => i.Name == "fromUnits").Choices!);
    }

    [Fact]
    public void RetiredNodesAreKeptSoOldGraphsStillOpen()
    {
        var retired = StandIns.RetiredDefinitions.ToList();
        Assert.Contains(retired, d => d.Name == "Search.ByPropertyContains");
        Assert.Contains(retired, d => d.Name == "Search.ByPropertyValue");
        Assert.All(retired, d => Assert.True(d.IsDeprecated));
        Assert.All(retired, d => Assert.False(Catalogue.Nodes.Any(n => n.Id == d.Id)));
    }

    [Fact]
    public void AnEarlierIdOfANodeStillResolves()
    {
        // [NodeAliases]: the id Search.ByPropertyContains had before the optional resolveTo input was added.
        Assert.True(Registry.TryGetDefinition("CamelGraph.Navisworks.SearchNodes.ByPropertyContains@string,string,string,Autodesk.Navisworks.Api.Document", out var definition));
        Assert.Equal("Search.ByPropertyContains", definition!.Name);
    }

    [Fact]
    public void ACapturedSelectionNodeCanBeCreatedAndSavedWithTheRealTypeTag()
    {
        var node = Registry.CreateNode("CapturedSelection");
        Assert.NotNull(node);
        Assert.Equal("Captured Selection", node!.Name);
        Assert.Equal("Navisworks.Selection", node.Category);
        Assert.Equal("items", node.OutPorts.Single().Name);
        Assert.Equal("CapturedSelectionNode", node.GetType().Name);
    }

    public static IEnumerable<object[]> SampleFiles()
    {
        return Directory.EnumerateFiles(SampleGraphFileTests.SamplesDirectory(), "*.dyc")
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new object[] { Path.GetFileName(p) });
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void EverySampleGraphResolvesEveryNodeWithTheStandIns(string fileName)
    {
        var serializer = new GraphSerializer(Registry);
        var graph = serializer.LoadFromFile(Path.Combine(SampleGraphFileTests.SamplesDirectory(), fileName));
        Assert.True(serializer.LoadWarnings.Count == 0, fileName + ": " + string.Join("; ", serializer.LoadWarnings));
        var unresolved = graph.Nodes.OfType<MissingNodeModel>().Select(n => n.Name + ": " + n.Reason).ToList();
        Assert.True(unresolved.Count == 0, fileName + " has unresolved nodes: " + string.Join("; ", unresolved));
        Assert.NotEmpty(graph.Nodes);

        // Nothing was dropped on the way in: every connector of the file is a connection of the graph.
        var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(SampleGraphFileTests.SamplesDirectory(), fileName)));
        Assert.Equal(((Newtonsoft.Json.Linq.JArray)json["Connectors"]!).Count, graph.Connections.Count);
    }

    public static IEnumerable<object[]> HowToGraphFiles()
    {
        var folder = Path.Combine(Path.GetDirectoryName(SampleGraphFileTests.SamplesDirectory())!, "docs", "wiki-src", "graphs");
        if (!Directory.Exists(folder))
        {
            return new List<object[]>();
        }

        return Directory.EnumerateFiles(folder, "*.dyc")
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new object[] { Path.GetFileName(p) })
            .ToList();
    }

    [Theory]
    [MemberData(nameof(HowToGraphFiles))]
    public void EveryHowToGraphResolvesEveryNodeWithTheStandIns(string fileName)
    {
        var path = Path.Combine(Path.GetDirectoryName(SampleGraphFileTests.SamplesDirectory())!, "docs", "wiki-src", "graphs", fileName);
        var serializer = new GraphSerializer(Registry);
        var graph = serializer.LoadFromFile(path);
        var unresolved = graph.Nodes.OfType<MissingNodeModel>().Select(n => n.Name + ": " + n.Reason).ToList();
        Assert.True(unresolved.Count == 0, fileName + " has unresolved nodes: " + string.Join("; ", unresolved));
        Assert.True(serializer.LoadWarnings.Count == 0, fileName + ": " + string.Join("; ", serializer.LoadWarnings));
        Assert.NotEmpty(graph.Nodes);

        var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
        Assert.Equal(((Newtonsoft.Json.Linq.JArray)json["Connectors"]!).Count, graph.Connections.Count);
    }

    [Fact]
    public void TheStandInsRoundTripThroughSaveAndLoad()
    {
        var graph = new GraphModel { Name = "stand-in round trip" };
        var search = new ZeroTouchNodeModel(Registry.Definitions.Single(d => d.Name == "Search.ByProperty"));
        var colour = new ZeroTouchNodeModel(Registry.Definitions.Single(d => d.Name == "Appearance.OverrideColor"));
        graph.AddNode(search);
        graph.AddNode(colour);
        var result = graph.Connect(search.OutPorts.Single(p => p.Name == "items"), colour.InPorts.Single(p => p.Name == "items"));
        Assert.True(result.Success, result.Message);

        var reloaded = Pipeline.SaveLoad(graph, Registry);
        Assert.Equal(2, reloaded.Nodes.Count);
        Assert.Empty(reloaded.Nodes.OfType<MissingNodeModel>());
        Assert.Single(reloaded.Connections);
    }
}

internal static class DefinitionExtensions
{
    public static string Assembly(this NodeDefinition definition) => definition.AssemblyName;
}
