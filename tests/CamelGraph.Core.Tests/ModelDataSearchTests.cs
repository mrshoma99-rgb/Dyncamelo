using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Nodes;
using CamelGraph.Core.Tests.Fixtures;
using Xunit;

namespace CamelGraph.Core.Tests;

public static class DataSearchFixtures
{
    public static string Read(
        object element,
        [NodeTabChoice("element")] string tab,
        [NodePropertyChoice("element", "tab")] string property) => tab + "/" + property;

    public static string Plain(string text) => text;

    public static string Find(
        [NodeTabChoice(NodeDataSource.Selection)] string tab,
        [NodePropertyChoice(NodeDataSource.Selection, "tab")] string property,
        object value) => tab + "/" + property + "/" + value;

    public static string Ancestors(
        object element,
        [NodeTabChoice("element", IncludeAncestors = true)] string tab) => tab;
}

/// <summary>
/// The search button next to a tab or property input: it reads the element on the node's own element input, only when asked,
/// and never anything else.
/// </summary>
public class ModelDataSearchTests : IDisposable
{
    private static readonly List<NodeDefinition> Definitions = AssemblyNodeLoader.LoadType(typeof(DataSearchFixtures));

    private sealed class RecordingCatalog : IModelPropertyCatalog
    {
        public List<(object? Source, ModelDataChoice Choice, string? Tab)> Calls { get; } = new List<(object?, ModelDataChoice, string?)>();

        public ModelDataListing List(object? source, ModelDataChoice choice, string? tab)
        {
            Calls.Add((source, choice, tab));
            var names = choice.Kind == ModelDataKind.Tab
                ? new[] { "Item", "Element", "TimeLiner" }
                : new[] { "Name", "Type" };
            return ModelDataListing.Union(new[] { names.AsEnumerable() }, 1);
        }
    }

    private readonly IModelPropertyCatalog? _before = ModelPropertyHost.Current;
    private readonly RecordingCatalog _catalog = new RecordingCatalog();

    public ModelDataSearchTests()
    {
        ModelPropertyHost.Current = _catalog;
    }

    public void Dispose()
    {
        ModelPropertyHost.Current = _before;
    }

    private static ZeroTouchNodeModel Node(string method) => new ZeroTouchNodeModel(Definitions.Single(d => d.Method.Name == method));

    private static PortModel In(NodeModel node, string name) => node.InPorts.Single(p => p.Name == name);

    [Fact]
    public void OnlyTheNameInputsGetASearch()
    {
        var read = Node("Read");

        Assert.Null(In(read, "element").DataChoice);
        var tab = In(read, "tab").DataChoice!;
        Assert.Equal(ModelDataKind.Tab, tab.Kind);
        Assert.Equal("element", tab.From);
        Assert.False(tab.IncludeAncestors);
        var property = In(read, "property").DataChoice!;
        Assert.Equal(ModelDataKind.Property, property.Kind);
        Assert.Equal("element", property.From);
        Assert.Equal("tab", property.Tab);
        Assert.Null(In(Node("Plain"), "text").DataChoice);
        Assert.True(In(Node("Ancestors"), "tab").DataChoice!.IncludeAncestors);
    }

    [Fact]
    public void TheEditorStaysATextBoxSoTheNameCanAlwaysBeTyped()
    {
        var read = Node("Read");

        Assert.Equal(PortEditorKind.Text, PortEditors.Resolve(In(read, "tab")));
        Assert.Equal(PortEditorKind.Text, PortEditors.Resolve(In(read, "property")));
        PortEditors.SetText(In(read, "tab"), "Element");
        Assert.Equal("Element", PortEditors.GetText(In(read, "tab")));
    }

    [Fact]
    public void NothingIsReadUntilTheButtonIsPressed()
    {
        var graph = new GraphModel();
        var read = Node("Read");
        graph.AddNode(read);
        In(read, "element").SetUserValue("nw:0:1/2");
        PortEditors.SetText(In(read, "tab"), "Element");

        new GraphEngine().Run(graph);

        Assert.Empty(_catalog.Calls);
    }

    [Fact]
    public void ThePropertySearchReadsThePickedElementOfTheSameNodeAndTheChosenTab()
    {
        var read = Node("Read");
        new GraphModel().AddNode(read);
        In(read, "element").SetUserValue("nw:0:1/2");
        PortEditors.SetText(In(read, "tab"), "Element");

        var listing = ModelDataScope.Search(In(read, "property"));

        var call = Assert.Single(_catalog.Calls);
        Assert.Equal("nw:0:1/2", call.Source);
        Assert.Equal(ModelDataKind.Property, call.Choice.Kind);
        Assert.Equal("Element", call.Tab);
        Assert.Equal(new[] { "Name", "Type" }, listing.Names.ToArray());
        Assert.Equal(string.Empty, listing.Problem);
    }

    [Fact]
    public void TheTabSearchReadsAWiredElementFromTheLastRun()
    {
        var graph = new GraphModel();
        var element = ZT.Value(graph, "the element");
        var read = Node("Read");
        graph.AddNode(read);
        ZT.Wire(graph, element, 0, read, 0);

        var before = ModelDataScope.Search(In(read, "tab"));
        Assert.Empty(_catalog.Calls);                      // not run yet: nothing to read, and it says so
        Assert.Contains("has not been computed", before.Problem);

        new GraphEngine().Run(graph);
        var listing = ModelDataScope.Search(In(read, "tab"));

        Assert.Equal("the element", Assert.Single(_catalog.Calls).Source);
        Assert.Equal(new[] { "Element", "Item", "TimeLiner" }, listing.Names.ToArray());
    }

    [Fact]
    public void WithoutAnElementThereIsNothingToSearchAndTheCatalogIsNotAsked()
    {
        var read = Node("Read");
        new GraphModel().AddNode(read);

        var tabs = ModelDataScope.Search(In(read, "tab"));
        PortEditors.SetText(In(read, "tab"), "Element");
        var properties = ModelDataScope.Search(In(read, "property"));

        Assert.Empty(_catalog.Calls);
        Assert.Contains("Pick an element", tabs.Problem);
        Assert.Contains("Pick an element", properties.Problem);
        Assert.Empty(tabs.Names);
    }

    [Fact]
    public void ThePropertySearchNeedsATabFirst()
    {
        var read = Node("Read");
        new GraphModel().AddNode(read);
        In(read, "element").SetUserValue("nw:0:1");

        var listing = ModelDataScope.Search(In(read, "property"));

        Assert.Empty(_catalog.Calls);
        Assert.Contains("Choose the tab first", listing.Problem);
    }

    [Fact]
    public void ThePropertySearchFollowsAWiredTab()
    {
        var graph = new GraphModel();
        var tab = ZT.Value(graph, "Item");
        var read = Node("Read");
        graph.AddNode(read);
        ZT.Wire(graph, tab, 0, read, 1);
        In(read, "element").SetUserValue("nw:0:1");
        new GraphEngine().Run(graph);

        ModelDataScope.Search(In(read, "property"));

        Assert.Equal("Item", Assert.Single(_catalog.Calls).Tab);
    }

    [Fact]
    public void ANodeThatSearchesTheWholeModelOffersTheTabsAndPropertiesOfTheSelectionOnly()
    {
        var find = Node("Find");
        new GraphModel().AddNode(find);
        var tab = In(find, "tab").DataChoice!;

        Assert.True(tab.FromSelection);
        Assert.True(In(find, "property").DataChoice!.FromSelection);
        Assert.Null(ModelDataScope.ScopePort(In(find, "tab")));     // there is no element input to read
        Assert.Null(In(find, "value").DataChoice);

        var tabs = ModelDataScope.Search(In(find, "tab"));
        Assert.Same(ModelSelectionSource.Current, Assert.Single(_catalog.Calls).Source);
        Assert.Equal(new[] { "Element", "Item", "TimeLiner" }, tabs.Names.ToArray());

        PortEditors.SetText(In(find, "tab"), "Element");
        ModelDataScope.Search(In(find, "property"));
        Assert.Equal(2, _catalog.Calls.Count);
        Assert.Same(ModelSelectionSource.Current, _catalog.Calls[1].Source);
        Assert.Equal("Element", _catalog.Calls[1].Tab);
    }

    [Fact]
    public void TheSelectionSearchStillWantsATabBeforeProperties()
    {
        var find = Node("Find");
        new GraphModel().AddNode(find);

        var listing = ModelDataScope.Search(In(find, "property"));

        Assert.Empty(_catalog.Calls);
        Assert.Contains("Choose the tab first", listing.Problem);
    }

    [Fact]
    public void WithoutAHostCatalogTheSearchExplainsInsteadOfFailing()
    {
        ModelPropertyHost.Current = null;
        var read = Node("Read");
        new GraphModel().AddNode(read);
        In(read, "element").SetUserValue("nw:0:1");

        var listing = ModelDataScope.Search(In(read, "tab"));

        Assert.Contains("Navisworks only", listing.Problem);
    }

    [Fact]
    public void ACatalogThatThrowsIsReportedNotPropagated()
    {
        ModelPropertyHost.Current = new ThrowingCatalog();
        var read = Node("Read");
        new GraphModel().AddNode(read);
        In(read, "element").SetUserValue("nw:0:1");

        var listing = ModelDataScope.Search(In(read, "tab"));

        Assert.Contains("could not be read", listing.Problem);
        Assert.Contains("boom", listing.Problem);
    }

    private sealed class ThrowingCatalog : IModelPropertyCatalog
    {
        public ModelDataListing List(object? source, ModelDataChoice choice, string? tab) => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void TheListingJoinsSortsAndDeduplicatesWithoutCaseDifferences()
    {
        var listing = ModelDataListing.Union(
            new[]
            {
                new[] { "Item", "element", "", "Layer" }.AsEnumerable(),
                new[] { "Element", "Item", "TimeLiner" }.AsEnumerable(),
            },
            total: 250);

        Assert.Equal(new[] { "element", "Item", "Layer", "TimeLiner" }, listing.Names.ToArray());
        Assert.Equal(2, listing.Scanned);
        Assert.Equal(250, listing.Total);
    }

    [Fact]
    public void ElementsAreCountedInAValueTheCatalogMayCap()
    {
        Assert.Equal(0, ModelDataScope.CountOf(null));
        Assert.Equal(1, ModelDataScope.CountOf(new object()));
        Assert.Equal(3, ModelDataScope.CountOf("nw:0:1;0:2;0:3"));
        Assert.Equal(0, ModelDataScope.CountOf("just text"));
        Assert.Equal(4, ModelDataScope.CountOf(new object[] { new object(), new object[] { new object(), new object() }, "nw:0:9" }));
    }
}
