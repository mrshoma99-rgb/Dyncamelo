using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Nodes;
using Dyncamelo.UI.Services;
using Dyncamelo.UI.ViewModels;
using Dyncamelo.UI.Views;
using Xunit;

namespace Dyncamelo.UI.Tests;

/// <summary>The wiring gestures as the editor view model runs them (no window needed).</summary>
public class WiringGestureTests
{
    private static GraphEditorViewModel NewEditor()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterNodeType("TestSum", () => new SumNode());
        var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
        return new GraphEditorViewModel(registry, new StubDialogs(), settings);
    }

    private static SumNode AddSum(GraphEditorViewModel vm, double x, double y = 0)
    {
        var node = new SumNode { X = x, Y = y };
        vm.Graph.AddNode(node);
        return node;
    }

    private static NodeViewModel Vm(GraphEditorViewModel vm, NodeModel model) =>
        vm.Items.OfType<NodeViewModel>().Single(n => n.Model == model);

    private static void Select(GraphEditorViewModel vm, params NodeModel[] nodes)
    {
        vm.SelectedItems.Clear();
        foreach (var node in nodes)
        {
            vm.SelectedItems.Add(Vm(vm, node));
        }
    }

    private static void Wire(GraphEditorViewModel vm, NodeModel from, NodeModel to) =>
        Assert.True(vm.Graph.Connect(from.OutPorts[0], to.InPorts[0]).Success);

    [Fact]
    public void DeleteAndReconnectKeepsTheDataFlowingAndUndoesAsOneStep()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var a = AddSum(vm, 0);
            var b = AddSum(vm, 300);
            var c = AddSum(vm, 600);
            Wire(vm, a, b);
            Wire(vm, b, c);
            vm.History.Clear();
            Select(vm, b);

            vm.DeleteAndReconnectCommand.Execute(null);

            Assert.DoesNotContain(b, vm.Graph.Nodes);
            Assert.Same(a, vm.Graph.FindConnectionInto(c.InPorts[0])!.SourceNode);
            Assert.Equal(1, vm.History.UndoCount);

            vm.UndoCommand.Execute(null);
            Assert.Contains(b, vm.Graph.Nodes);
            Assert.Equal(2, vm.Graph.Connections.Count);
        });
    }

    [Fact]
    public void PlainDeleteOfARerouteKeepsTheWire()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var a = AddSum(vm, 0);
            var c = AddSum(vm, 600);
            var reroute = new RerouteNode { X = 300 };
            vm.Graph.AddNode(reroute);
            Assert.True(vm.Graph.Connect(a.OutPorts[0], reroute.InPorts[0]).Success);
            Assert.True(vm.Graph.Connect(reroute.OutPorts[0], c.InPorts[0]).Success);
            Select(vm, reroute);

            vm.DeleteSelectionCommand.Execute(null);

            Assert.DoesNotContain(reroute, vm.Graph.Nodes);
            Assert.Same(a, vm.Graph.FindConnectionInto(c.InPorts[0])!.SourceNode);
        });
    }

    [Fact]
    public void AutoConnectChainsTheSelection()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var a = AddSum(vm, 0);
            var b = AddSum(vm, 300);
            var c = AddSum(vm, 600);
            vm.History.Clear();
            Select(vm, c, a, b);

            vm.AutoConnectCommand.Execute(null);

            Assert.Equal(2, vm.Graph.Connections.Count);
            Assert.Equal(1, vm.History.UndoCount);
            vm.UndoCommand.Execute(null);
            Assert.Empty(vm.Graph.Connections);
        });
    }

    [Fact]
    public void MutingSelectedWiresIsUndoableAndShowsOnTheWire()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var a = AddSum(vm, 0);
            var b = AddSum(vm, 300);
            Wire(vm, a, b);
            var wire = vm.Connections.Single();
            var changed = new List<string?>();
            wire.PropertyChanged += (s, e) => changed.Add(e.PropertyName);
            vm.History.Clear();
            vm.SelectedConnections.Add(wire);

            vm.MuteSelectedWiresCommand.Execute(null);
            Assert.True(wire.IsMuted);
            Assert.Contains(nameof(ConnectionViewModel.IsMuted), changed);
            Assert.Equal(1, vm.History.UndoCount);

            vm.UndoCommand.Execute(null);
            Assert.False(wire.IsMuted);
            vm.RedoCommand.Execute(null);
            Assert.True(wire.IsMuted);
        });
    }

    [Fact]
    public void MuteShortcutOnAWireSelectionTogglesTheWires()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var a = AddSum(vm, 0);
            var b = AddSum(vm, 300);
            Wire(vm, a, b);
            vm.SelectedConnections.Add(vm.Connections.Single());

            vm.ToggleMuteSelectedCommand.Execute(null);

            Assert.True(vm.Graph.Connections.Single().IsMuted);
            Assert.False(a.IsMuted);
            Assert.False(b.IsMuted);
        });
    }

    [Fact]
    public void RerouteOnSelectedWireSplitsItAndUndoes()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var a = AddSum(vm, 0);
            var b = AddSum(vm, 300);
            Wire(vm, a, b);
            vm.History.Clear();
            vm.SelectedConnections.Add(vm.Connections.Single());

            vm.RerouteSelectedWiresCommand.Execute(null);

            var reroute = vm.Graph.Nodes.OfType<RerouteNode>().Single();
            Assert.Same(a, vm.Graph.FindConnectionInto(reroute.InPorts[0])!.SourceNode);
            Assert.Same(reroute, vm.Graph.FindConnectionInto(b.InPorts[0])!.SourceNode);
            Assert.Equal(1, vm.History.UndoCount);

            vm.UndoCommand.Execute(null);
            Assert.Empty(vm.Graph.Nodes.OfType<RerouteNode>());
            Assert.Same(a, vm.Graph.FindConnectionInto(b.InPorts[0])!.SourceNode);
        });
    }

    [Fact]
    public void SelectingAlongLinksFollowsWiresAndKinds()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var a = AddSum(vm, 0);
            var b = AddSum(vm, 300);
            var c = AddSum(vm, 600);
            Wire(vm, a, b);
            Wire(vm, b, c);

            Select(vm, b);
            vm.SelectDownstreamCommand.Execute(null);
            Assert.Equal(new[] { b, c }.OrderBy(n => n.X), vm.SelectedItems.OfType<NodeViewModel>().Select(n => n.Model).OrderBy(n => n.X));

            Select(vm, b);
            vm.SelectUpstreamCommand.Execute(null);
            Assert.Equal(new[] { a, b }.OrderBy(n => n.X), vm.SelectedItems.OfType<NodeViewModel>().Select(n => n.Model).OrderBy(n => n.X));

            Select(vm, a);
            vm.SelectSimilarCommand.Execute(null);
            Assert.Equal(3, vm.SelectedItems.Count);
        });
    }
}

public class WireDropSearchTests
{
    private static GraphEditorViewModel NewEditor()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterNodeType("TestSum", () => new SumNode());
        var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
        return new GraphEditorViewModel(registry, new StubDialogs(), settings);
    }

    private static NodeViewModel AddSum(GraphEditorViewModel vm, double x)
    {
        var node = new SumNode { X = x };
        vm.Graph.AddNode(node);
        return vm.Items.OfType<NodeViewModel>().Single(n => n.Model == node);
    }

    [Fact]
    public void DraggingFromAnOutputOffersOnlyNodesThatCanTakeIt()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var a = AddSum(vm, 0);
            var source = a.Outputs[0];

            vm.OpenQuickSearch(new Point(600, 100), source);

            Assert.True(vm.IsQuickSearchOpen);
            Assert.True(vm.HasQuickSearchContext);
            Assert.Contains("accept", vm.QuickSearchContext);
            Assert.Contains(vm.QuickSearchResults, r => r.Id == "TestSum");
            // Input nodes have no sockets to receive a wire.
            Assert.DoesNotContain(vm.QuickSearchResults, r => r.Id == NumberInputNode.TypeName);
        });
    }

    [Fact]
    public void DraggingFromAnInputOffersNodesThatProduceForIt()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var a = AddSum(vm, 400);
            var source = a.Inputs[0];

            vm.OpenQuickSearch(new Point(300, 100), source);

            Assert.Contains("produce", vm.QuickSearchContext);
            Assert.Contains(vm.QuickSearchResults, r => r.Id == NumberInputNode.TypeName);
            Assert.Contains(vm.QuickSearchResults, r => r.Id == "TestSum");
        });
    }

    [Fact]
    public void ChoosingAResultAddsAndConnectsInOneUndoStep()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var a = AddSum(vm, 0);
            vm.History.Clear();
            vm.OpenQuickSearch(new Point(600, 100), a.Outputs[0]);
            var entry = vm.QuickSearchResults.First(r => r.Id == "TestSum");

            var added = vm.CommitQuickSearch(entry);

            Assert.NotNull(added);
            Assert.Equal(2, vm.Graph.Nodes.Count);
            Assert.Same(a.Model, vm.Graph.FindConnectionInto(added!.Model.InPorts[0])!.SourceNode);
            Assert.Equal(600, added.Model.X);
            Assert.Equal(1, vm.History.UndoCount);
            Assert.False(vm.HasQuickSearchContext);

            vm.UndoCommand.Execute(null);
            Assert.Single(vm.Graph.Nodes);
            Assert.Empty(vm.Graph.Connections);
        });
    }

    [Fact]
    public void ANodeFeedingAnInputSitsLeftOfTheDropPoint()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var a = AddSum(vm, 800);
            vm.OpenQuickSearch(new Point(500, 100), a.Inputs[1]);
            var entry = vm.QuickSearchResults.First(r => r.Id == NumberInputNode.TypeName);

            var added = vm.CommitQuickSearch(entry)!;

            Assert.True(added.Model.X < 500);
            Assert.Same(added.Model, vm.Graph.FindConnectionInto(a.Model.InPorts[1])!.SourceNode);
        });
    }

    [Fact]
    public void ReleasingAWireOnEmptyCanvasOpensTheFilteredSearch()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var a = AddSum(vm, 0);
            vm.PendingConnection.Source = a.Outputs[0];
            vm.PendingConnection.Target = null;
            vm.PendingConnection.TargetLocation = new Point(700, 300);
            vm.History.Clear();

            vm.CreateConnectionCommand.Execute(null);

            Assert.True(vm.IsQuickSearchOpen);
            Assert.True(vm.HasQuickSearchContext);
            Assert.Equal(0, vm.History.UndoCount);

            vm.CloseQuickSearch();
            Assert.False(vm.HasQuickSearchContext);
            Assert.Empty(vm.Graph.Connections);
        });
    }

    [Fact]
    public void ReleasingAWireOnASocketStillConnects()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var a = AddSum(vm, 0);
            var b = AddSum(vm, 400);
            vm.PendingConnection.Source = a.Outputs[0];
            vm.PendingConnection.Target = b.Inputs[0];

            vm.CreateConnectionCommand.Execute(b.Inputs[0]);

            Assert.False(vm.IsQuickSearchOpen);
            Assert.Single(vm.Graph.Connections);
        });
    }
}

public class ShortcutRouterTests
{
    [Fact]
    public void CatalogueShortcutsMapToTheirCommands()
    {
        var router = new ShortcutRouter();
        Assert.Equal("node.collapse", router.Find(Key.H, ModifierKeys.None)!.Id);
        Assert.Equal("node.hideunused", router.Find(Key.H, ModifierKeys.Control)!.Id);
        Assert.Equal("edit.redo", router.Find(Key.Z, ModifierKeys.Control | ModifierKeys.Shift)!.Id);
        Assert.Equal("edit.deletereconnect", router.Find(Key.Delete, ModifierKeys.Control)!.Id);
        Assert.Equal("edit.delete", router.Find(Key.Delete, ModifierKeys.None)!.Id);
        Assert.Null(router.Find(Key.Q, ModifierKeys.None));
    }

    [Fact]
    public void EveryCatalogueShortcutIsRoutable()
    {
        var router = new ShortcutRouter();
        foreach (var pair in Shortcuts.All())
        {
            Assert.True(Enum.TryParse<Key>(pair.Key.Key, out var key) || pair.Key.Key.Length == 1 && char.IsDigit(pair.Key.Key[0]),
                "unknown key name in catalogue: " + pair.Key);
            if (Enum.TryParse<Key>(pair.Key.Key, out key))
            {
                var mods = (pair.Key.Ctrl ? ModifierKeys.Control : 0) | (pair.Key.Shift ? ModifierKeys.Shift : 0) | (pair.Key.Alt ? ModifierKeys.Alt : 0);
                Assert.Equal(pair.Value.Id, router.Find(key, mods)!.Id);
            }
        }
    }

    private sealed class Probe : ICommand
    {
        public int Runs;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => Runs++;
    }

    [Fact]
    public void CanvasKeysDoNotFireWhileTypingOrWithoutCanvasFocus()
    {
        var router = new ShortcutRouter();
        var probe = new Probe();
        Func<string, ICommand?> resolve = _ => probe;

        Assert.False(router.TryDispatch(Key.H, ModifierKeys.None, typing: true, canvasFocused: true, resolve, null));
        Assert.False(router.TryDispatch(Key.H, ModifierKeys.None, typing: false, canvasFocused: false, resolve, null));
        Assert.Equal(0, probe.Runs);

        Assert.True(router.TryDispatch(Key.H, ModifierKeys.None, typing: false, canvasFocused: true, resolve, null));
        // A chorded canvas command works from anywhere in the control except a text box.
        Assert.True(router.TryDispatch(Key.D, ModifierKeys.Control, typing: false, canvasFocused: false, resolve, null));
        Assert.Equal(2, probe.Runs);
    }

    [Fact]
    public void GlobalKeysFireEvenWhileTyping()
    {
        var router = new ShortcutRouter();
        var probe = new Probe();
        Assert.True(router.TryDispatch(Key.S, ModifierKeys.Control, typing: true, canvasFocused: false, _ => probe, null));
        Assert.Equal(1, probe.Runs);
    }

    [Fact]
    public void SkippedCommandsAreLeftAlone()
    {
        var router = new ShortcutRouter(new[] { "graph.addnode" });
        Assert.Null(router.Find(Key.Space, ModifierKeys.None));
    }
}
