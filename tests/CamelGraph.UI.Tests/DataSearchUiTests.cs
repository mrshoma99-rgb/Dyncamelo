using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.UI.Services;
using CamelGraph.UI.ViewModels;
using CamelGraph.UI.Views;
using Xunit;

namespace CamelGraph.UI.Tests;

public static class DataSearchFixtures
{
    public static string Read(
        ModelItem item,
        [NodeTabChoice("item")] string tab,
        [NodePropertyChoice("item", "tab")] string property) => tab + "/" + property;

    public static string Plain(string text) => text;

    public static string Find(
        [NodeTabChoice(NodeDataSource.Selection)] string tab,
        [NodePropertyChoice(NodeDataSource.Selection, "tab")] string property,
        object value) => tab + "/" + property + "/" + value;
}

/// <summary>The small search button next to a tab / property input: what it shows, what it reads, and that it stays out of the way.</summary>
public class DataSearchUiTests
{
    private static readonly List<NodeDefinition> Definitions = AssemblyNodeLoader.LoadType(typeof(DataSearchFixtures));

    private sealed class RecordingCatalog : IModelPropertyCatalog
    {
        public List<(object? Source, string? Tab)> Calls { get; } = new List<(object?, string?)>();

        public ModelDataListing List(object? source, ModelDataChoice choice, string? tab)
        {
            Calls.Add((source, tab));
            var names = choice.Kind == ModelDataKind.Tab
                ? new[] { "Item", "Element", "TimeLiner" }
                : new[] { "Name", "Type", "Layer" };
            return ModelDataListing.Union(new[] { names.AsEnumerable() }, 1);
        }
    }

    private static (GraphEditorViewModel Vm, NodeViewModel Node) Rig(string method)
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterDefinitions(Definitions);
        var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
        var vm = new GraphEditorViewModel(registry, new StubDialogs(), settings);
        vm.Graph.AddNode(new ZeroTouchNodeModel(Definitions.Single(d => d.Method.Name == method)));
        return (vm, vm.Items.OfType<NodeViewModel>().Single());
    }

    private static ConnectorViewModel Input(NodeViewModel node, string name) => node.Inputs.Single(c => c.Port.Name == name);

    [Fact]
    public void OnlyTheNameInputsOfAnElementNodeHaveTheButton()
    {
        StaHost.Run(() =>
        {
            var (_, node) = Rig("Read");
            Assert.False(Input(node, "item").HasDataSearch);
            Assert.True(Input(node, "tab").HasDataSearch);
            Assert.True(Input(node, "property").HasDataSearch);
            Assert.Contains("tabs of the element on 'item'", Input(node, "tab").DataSearchToolTip);
            Assert.Contains("'tab' tab", Input(node, "property").DataSearchToolTip);

            var (_, plain) = Rig("Plain");
            Assert.False(Input(plain, "text").HasDataSearch);
        });
    }

    [Fact]
    public void PressingTheButtonListsTheNodesOwnElementAndChoosingFillsTheBox()
    {
        StaHost.Run(() =>
        {
            var previousPicker = ModelPickerHost.Current;
            var previousCatalog = ModelPropertyHost.Current;
            var catalog = new RecordingCatalog();
            ModelPickerHost.Current = new FakePicker { Selected = 1 };
            ModelPropertyHost.Current = catalog;
            try
            {
                var (_, node) = Rig("Read");
                var item = Input(node, "item");
                var tab = Input(node, "tab");
                var property = Input(node, "property");
                item.CaptureModelCommand.Execute(null);
                Assert.Empty(catalog.Calls);                         // picking the element reads no properties

                tab.SearchDataCommand.Execute(null);

                Assert.Equal("nw:0:1", Assert.Single(catalog.Calls).Source);
                Assert.True(tab.IsDataListOpen);
                Assert.Equal(new[] { "Element", "Item", "TimeLiner" }, tab.DataItems.ToArray());
                Assert.Equal("3 tabs on the element", tab.DataStatus);

                tab.ChooseDataCommand.Execute("Item");
                Assert.False(tab.IsDataListOpen);
                Assert.Equal("Item", tab.TextValue);

                property.SearchDataCommand.Execute(null);
                Assert.Equal("Item", catalog.Calls.Last().Tab);       // the properties of the chosen tab, of the same element
                Assert.Equal(new[] { "Layer", "Name", "Type" }, property.DataItems.ToArray());
            }
            finally
            {
                ModelPickerHost.Current = previousPicker;
                ModelPropertyHost.Current = previousCatalog;
            }
        });
    }

    [Fact]
    public void WhatIsTypedNarrowsTheListUnlessItAlreadyIsAName()
    {
        StaHost.Run(() =>
        {
            var previousCatalog = ModelPropertyHost.Current;
            ModelPropertyHost.Current = new RecordingCatalog();
            try
            {
                var (_, node) = Rig("Read");
                Input(node, "item").Port.SetUserValue("nw:0:1");
                var tab = Input(node, "tab");

                tab.TextValue = "ti";
                tab.SearchDataCommand.Execute(null);
                Assert.Equal(new[] { "TimeLiner" }, tab.DataItems.ToArray());
                Assert.Contains("1 of 3 tabs contain 'ti'", tab.DataStatus);

                tab.TextValue = "item";                              // already one of the names: the others are wanted too
                tab.SearchDataCommand.Execute(null);
                Assert.Equal(3, tab.DataItems.Count);

                tab.TextValue = "zzz";                               // matches nothing: everything is listed rather than nothing
                tab.SearchDataCommand.Execute(null);
                Assert.Equal(3, tab.DataItems.Count);
            }
            finally
            {
                ModelPropertyHost.Current = previousCatalog;
            }
        });
    }

    [Fact]
    public void ASearchNodeOffersTheTabsOfTheSelectionAndSaysSo()
    {
        StaHost.Run(() =>
        {
            var previousCatalog = ModelPropertyHost.Current;
            var catalog = new RecordingCatalog();
            ModelPropertyHost.Current = catalog;
            try
            {
                var (_, node) = Rig("Find");
                var tab = Input(node, "tab");
                var property = Input(node, "property");
                Assert.True(tab.HasDataSearch);
                Assert.True(property.HasDataSearch);
                Assert.False(Input(node, "value").HasDataSearch);
                Assert.Contains("selected in Navisworks", tab.DataSearchToolTip);
                Assert.Contains("the model is not searched", tab.DataSearchToolTip);

                tab.SearchDataCommand.Execute(null);

                Assert.Same(ModelSelectionSource.Current, Assert.Single(catalog.Calls).Source);
                Assert.Equal(new[] { "Element", "Item", "TimeLiner" }, tab.DataItems.ToArray());
                tab.ChooseDataCommand.Execute("Element");
                property.SearchDataCommand.Execute(null);
                Assert.Equal("Element", catalog.Calls.Last().Tab);
                Assert.Equal(new[] { "Layer", "Name", "Type" }, property.DataItems.ToArray());
            }
            finally
            {
                ModelPropertyHost.Current = previousCatalog;
            }
        });
    }

    [Fact]
    public void WithoutAnElementTheListSaysWhatToDoAndNothingIsRead()
    {
        StaHost.Run(() =>
        {
            var previousCatalog = ModelPropertyHost.Current;
            var catalog = new RecordingCatalog();
            ModelPropertyHost.Current = catalog;
            try
            {
                var (_, node) = Rig("Read");
                var tab = Input(node, "tab");

                tab.SearchDataCommand.Execute(null);

                Assert.Empty(catalog.Calls);
                Assert.Empty(tab.DataItems);
                Assert.True(tab.IsDataListOpen);
                Assert.Contains("Pick an element on 'item'", tab.DataStatus);
            }
            finally
            {
                ModelPropertyHost.Current = previousCatalog;
            }
        });
    }

    [Fact]
    public void TheButtonIsOnTheNodeAndNothingIsReadByShowingIt()
    {
        var previousCatalog = ModelPropertyHost.Current;
        var catalog = new RecordingCatalog();
        ModelPropertyHost.Current = catalog;
        Window? window = null;
        try
        {
            StaHost.Run(() =>
            {
                var (vm, node) = Rig("Read");
                var control = new CamelGraphEditorControl { ViewModel = vm, Width = 900, Height = 500 };
                window = new Window
                {
                    Width = 900,
                    Height = 500,
                    Content = control,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStyle = WindowStyle.None,
                };
                window.Show();
            });
            StaHost.Flush();
            StaHost.Flush();

            StaHost.Run(() =>
            {
                var buttons = Descendants<Button>(window!).Where(b => b.ToolTip is string tip && tip.StartsWith("Choose from the ", StringComparison.Ordinal)).ToList();
                Assert.Equal(2, buttons.Count);                     // tab and property, not the element input
                Assert.All(buttons, b => Assert.True(b.ActualWidth > 0 && b.ActualHeight > 0, "the search button has no size"));
                Assert.Empty(catalog.Calls);
            });
        }
        finally
        {
            ModelPropertyHost.Current = previousCatalog;
            if (window != null)
            {
                StaHost.Run(() => window.Close());
            }
        }
    }

    [Fact]
    public void ThePlayersFormGetsTheButtonToo()
    {
        var previousCatalog = ModelPropertyHost.Current;
        var catalog = new RecordingCatalog();
        ModelPropertyHost.Current = catalog;
        var root = Path.Combine(Path.GetTempPath(), "dyc-data-search-" + Guid.NewGuid().ToString("N"));
        var scripts = Path.Combine(root, "scripts");
        Directory.CreateDirectory(scripts);
        Window? window = null;
        try
        {
            StaHost.Run(() =>
            {
                var registry = NodeRegistry.CreateDefault();
                registry.RegisterDefinitions(Definitions);
                var graph = new GraphModel { Name = "Reader" };
                var node = new ZeroTouchNodeModel(Definitions.Single(d => d.Method.Name == "Read"));
                graph.AddNode(node);
                node.InPorts.Single(p => p.Name == "tab").PlayerExposed = true;
                node.InPorts.Single(p => p.Name == "property").PlayerExposed = true;
                new CamelGraph.Core.Serialization.GraphSerializer(registry).SaveToFile(graph, Path.Combine(scripts, "reader.dyc"));

                var settings = new UiSettingsService(Path.Combine(root, "settings.json"));
                settings.SetPlayerFolders(new[] { scripts });
                var player = new PlayerViewModel(registry, new StubDialogs(), settings) { CancelPoll = () => false };
                player.Refresh();
                window = new Window
                {
                    Width = 420,
                    Height = 700,
                    Content = new PlayerControl { ViewModel = player },
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStyle = WindowStyle.None,
                };
                window.Show();
                player.SelectedScript = player.Scripts.Single();
                player.CloseList();
            });
            StaHost.Flush();
            StaHost.Flush();

            StaHost.Run(() =>
            {
                var buttons = Descendants<Button>(window!).Where(b => b.ToolTip is string tip && tip.StartsWith("Choose from the ", StringComparison.Ordinal)).ToList();
                Assert.Equal(2, buttons.Count);
                Assert.All(buttons, b => Assert.Equal(28d, b.ActualHeight));
                Assert.Empty(catalog.Calls);
            });
        }
        finally
        {
            ModelPropertyHost.Current = previousCatalog;
            if (window != null)
            {
                StaHost.Run(() => window.Close());
            }

            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var inner in Descendants<T>(child))
            {
                yield return inner;
            }
        }
    }
}
