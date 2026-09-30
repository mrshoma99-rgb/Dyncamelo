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
    public void FreezeTogglesTheSelectedNodesAsOneUndoableStep()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var a = AddSum(vm, 0);
            var b = AddSum(vm, 300);
            vm.History.Clear();
            Select(vm, a, b);

            vm.ToggleFreezeSelectedCommand.Execute(null);
            Assert.True(a.IsFrozen && b.IsFrozen);
            Assert.True(Vm(vm, a).IsFrozen);
            Assert.False(Vm(vm, a).IsMuted);
            Assert.Equal(1, vm.History.UndoCount);

            vm.ToggleFreezeSelectedCommand.Execute(null);
            Assert.False(a.IsFrozen || b.IsFrozen);

            // The node's own menu item acts on that node alone, and freezing and muting are independent.
            Select(vm, b);
            Vm(vm, a).ToggleFreezeCommand.Execute(null);
            Assert.True(a.IsFrozen);
            Assert.False(b.IsFrozen);
            Vm(vm, a).ToggleMuteCommand.Execute(null);
            Assert.True(a.IsFrozen && a.IsMuted);
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

public class InsertOnWireTests
{
    private static GraphEditorViewModel NewEditor()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterNodeType("TestSum", () => new SumNode());
        var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
        return new GraphEditorViewModel(registry, new StubDialogs(), settings);
    }

    private static NodeViewModel AddSum(GraphEditorViewModel vm, double x, double y = 0)
    {
        var node = new SumNode { X = x, Y = y };
        vm.Graph.AddNode(node);
        var viewModel = vm.Items.OfType<NodeViewModel>().Single(n => n.Model == node);
        viewModel.Size = new Size(200, 90);
        return viewModel;
    }

    // a (x=0) -> c (x=600) with the wire drawn at y=50; b is a free node.
    private static (GraphEditorViewModel Vm, NodeViewModel A, NodeViewModel B, NodeViewModel C) Rig()
    {
        var vm = NewEditor();
        var a = AddSum(vm, 0);
        var c = AddSum(vm, 600);
        var b = AddSum(vm, 300, 400);
        Assert.True(vm.Graph.Connect(a.Model.OutPorts[0], c.Model.InPorts[0]).Success);
        a.Outputs[0].Anchor = new Point(200, 50);
        c.Inputs[0].Anchor = new Point(600, 50);
        return (vm, a, b, c);
    }

    [Fact]
    public void AFreeNodeOverAWireIsOfferedThatWire()
    {
        StaHost.Run(() =>
        {
            var (vm, _, b, _) = Rig();
            var over = vm.FindInsertTarget(b.Model, new Rect(300, 20, 200, 90), new Point(400, 50));
            Assert.Same(vm.Connections.Single(), over);

            var clear = vm.FindInsertTarget(b.Model, new Rect(300, 300, 200, 90), new Point(400, 340));
            Assert.Null(clear);
        });
    }

    [Fact]
    public void ANodeWithWiresOfItsOwnIsNeverOffered()
    {
        StaHost.Run(() =>
        {
            var (vm, _, b, c) = Rig();
            b.Model.X = 300;
            Assert.True(vm.Graph.Connect(b.Model.OutPorts[0], c.Model.InPorts[1]).Success);
            Assert.Null(vm.FindInsertTarget(b.Model, new Rect(300, 20, 200, 90), new Point(400, 50)));
        });
    }

    [Fact]
    public void HoveringHighlightsTheWireAndLeavingClearsIt()
    {
        StaHost.Run(() =>
        {
            var (vm, _, b, _) = Rig();
            var wire = vm.Connections.Single();

            vm.UpdateInsertCandidate(b, new Rect(300, 20, 200, 90), new Point(400, 50));
            Assert.True(wire.IsInsertTarget);
            Assert.Same(wire, vm.InsertCandidate);

            vm.UpdateInsertCandidate(b, new Rect(300, 300, 200, 90), new Point(400, 340));
            Assert.False(wire.IsInsertTarget);
            Assert.Null(vm.InsertCandidate);
        });
    }

    [Fact]
    public void InsertingSplicesTheNodeMakesRoomAndUndoesAsOneStep()
    {
        StaHost.Run(() =>
        {
            var (vm, a, b, c) = Rig();
            b.Model.X = 500; // 500..700 overlaps c at 600, so c must move
            b.Model.Y = 20;
            vm.History.Clear();

            Assert.True(vm.InsertNodeOnWire(b, vm.Connections.Single()));

            Assert.Same(a.Model, vm.Graph.FindConnectionInto(b.Model.InPorts[0])!.SourceNode);
            Assert.Same(b.Model, vm.Graph.FindConnectionInto(c.Model.InPorts[0])!.SourceNode);
            Assert.Equal(500 + 200 + GraphOps.InsertGap, c.Model.X, 6);
            Assert.Equal(1, vm.History.UndoCount);

            vm.UndoCommand.Execute(null);
            Assert.Equal(600, c.Model.X);
            Assert.Same(a.Model, vm.Graph.FindConnectionInto(c.Model.InPorts[0])!.SourceNode);
        });
    }

    [Fact]
    public void DroppingADraggedNodeOnAWireInsertsItInTheSameUndoStepAsTheMove()
    {
        StaHost.Run(() =>
        {
            var (vm, a, b, c) = Rig();
            vm.SelectedItems.Clear();
            vm.SelectedItems.Add(b);
            vm.History.Clear();

            vm.ItemsDragStartedCommand.Execute(null);
            b.Location = new Point(300, 10);
            vm.UpdateInsertCandidate(b, new Rect(308, 18, 184, 74), new Point(400, 50));
            vm.ItemsDragCompletedCommand.Execute(null);

            Assert.Same(b.Model, vm.Graph.FindConnectionInto(c.Model.InPorts[0])!.SourceNode);
            Assert.Equal(1, vm.History.UndoCount);
            Assert.Equal("Insert on wire", vm.History.UndoLabel);
            Assert.Null(vm.InsertCandidate);

            vm.UndoCommand.Execute(null);
            Assert.Equal(400, b.Model.Y);
            Assert.Same(a.Model, vm.Graph.FindConnectionInto(c.Model.InPorts[0])!.SourceNode);
        });
    }

    [Fact]
    public void DroppingALibraryNodeOnAWireInsertsIt()
    {
        StaHost.Run(() =>
        {
            var (vm, a, _, c) = Rig();
            vm.History.Clear();

            var added = vm.AddNodeOnWire("TestSum", new Point(400, 50));

            Assert.NotNull(added);
            Assert.Same(added!.Model, vm.Graph.FindConnectionInto(c.Model.InPorts[0])!.SourceNode);
            Assert.Same(a.Model, vm.Graph.FindConnectionInto(added.Model.InPorts[0])!.SourceNode);
            Assert.Equal(1, vm.History.UndoCount);
        });
    }

    [Fact]
    public void DroppingALibraryNodeAwayFromWiresJustAddsIt()
    {
        StaHost.Run(() =>
        {
            var (vm, _, _, _) = Rig();
            var before = vm.Graph.Connections.Count;
            var added = vm.AddNodeOnWire("TestSum", new Point(400, 600));
            Assert.NotNull(added);
            Assert.Equal(before, vm.Graph.Connections.Count);
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

/// <summary>A node with a variable number of inputs, like List.Create (the +/- buttons find these methods by name).</summary>
public sealed class GrowNode : NodeModel
{
    public GrowNode()
    {
        Name = "Grow";
        AddItemPort();
        AddOutput("out", typeof(object));
    }

    public override string NodeType => "TestGrow";

    public PortModel AddItemPort()
    {
        var port = AddInput("item" + InPorts.Count, typeof(object), null);
        MarkDirty();
        return port;
    }

    public bool RemoveItemPort()
    {
        if (InPorts.Count <= 1)
        {
            return false;
        }

        var last = InPorts[InPorts.Count - 1];
        var connection = Graph?.FindConnectionInto(last);
        if (connection != null)
        {
            Graph!.Disconnect(connection);
        }

        ((System.Collections.Generic.IList<PortModel>)InPorts).RemoveAt(InPorts.Count - 1);
        MarkDirty();
        return true;
    }

    public override object?[] Evaluate(object?[] inputs, Dyncamelo.Core.Execution.EvaluationContext context) => new object?[] { null };
}

public class PortCountUndoTests
{
    [Fact]
    public void AddingAndRemovingInputsUndoAndRedoWithTheirWires()
    {
        StaHost.Run(() =>
        {
            var registry = NodeRegistry.CreateDefault();
            registry.RegisterNodeType("TestSum", () => new SumNode());
            registry.RegisterNodeType("TestGrow", () => new GrowNode());
            var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
            var vm = new GraphEditorViewModel(registry, new StubDialogs(), settings);
            var source = new SumNode();
            var grow = new GrowNode();
            vm.Graph.AddNode(source);
            vm.Graph.AddNode(grow);
            var growVm = vm.Items.OfType<NodeViewModel>().Single(n => n.Model == grow);
            vm.History.Clear();

            growVm.AddPortCommand.Execute(null);
            var second = grow.InPorts[1];
            Assert.True(vm.Graph.Connect(source.OutPorts[0], second).Success);
            growVm.RemovePortCommand.Execute(null);
            Assert.Single(grow.InPorts);
            Assert.Empty(vm.Graph.Connections);

            vm.UndoCommand.Execute(null);   // the removal: same port object comes back
            Assert.Equal(2, grow.InPorts.Count);
            Assert.Same(second, grow.InPorts[1]);
            vm.UndoCommand.Execute(null);   // its wire returns, onto that port
            Assert.Same(source, vm.Graph.FindConnectionInto(second)!.SourceNode);
            Assert.Equal(2, growVm.Inputs.Count);

            vm.UndoCommand.Execute(null);   // the connect
            vm.UndoCommand.Execute(null);   // the add
            Assert.Single(grow.InPorts);
            Assert.Single(growVm.Inputs);

            for (var i = 0; i < 4; i++)
            {
                vm.RedoCommand.Execute(null);
            }

            Assert.Single(grow.InPorts);
            Assert.Empty(vm.Graph.Connections);
        });
    }
}

public class HelpOverlayTests
{
    [Fact]
    public void F1TogglesTheOverlayAndItListsTheCatalogue()
    {
        StaHost.Run(() =>
        {
            var registry = NodeRegistry.CreateDefault();
            var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
            var vm = new GraphEditorViewModel(registry, new StubDialogs(), settings);
            Assert.False(vm.IsHelpOpen);
            Assert.NotEmpty(vm.HelpSections);

            vm.ToggleHelpCommand.Execute(null);
            Assert.True(vm.IsHelpOpen);
            vm.CloseHelpCommand.Execute(null);
            Assert.False(vm.IsHelpOpen);

            var router = new ShortcutRouter();
            Assert.Equal("help.keys", router.Find(System.Windows.Input.Key.F1, System.Windows.Input.ModifierKeys.None)!.Id);
        });
    }
}

public class ArrangeTests
{
    [Fact]
    public void ArrangeAllLaysTheGraphOutAlongTheFlowAndUndoes()
    {
        StaHost.Run(() =>
        {
            var registry = NodeRegistry.CreateDefault();
            registry.RegisterNodeType("TestSum", () => new SumNode());
            var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
            var vm = new GraphEditorViewModel(registry, new StubDialogs(), settings);
            var nodes = Enumerable.Range(0, 5).Select(i => new SumNode { X = 10 * i, Y = 10 * i }).ToList();
            foreach (var node in nodes)
            {
                vm.Graph.AddNode(node);
            }

            Assert.True(vm.Graph.Connect(nodes[0].OutPorts[0], nodes[1].InPorts[0]).Success);
            Assert.True(vm.Graph.Connect(nodes[1].OutPorts[0], nodes[2].InPorts[0]).Success);
            Assert.True(vm.Graph.Connect(nodes[0].OutPorts[0], nodes[3].InPorts[0]).Success);
            Assert.True(vm.Graph.Connect(nodes[3].OutPorts[0], nodes[4].InPorts[0]).Success);
            var before = nodes.Select(n => (n.X, n.Y)).ToList();
            vm.History.Clear();

            vm.ArrangeAllCommand.Execute(null);

            Assert.True(nodes[0].X < nodes[1].X && nodes[1].X < nodes[2].X, "flow should run left to right");
            Assert.True(nodes[0].X < nodes[3].X && nodes[3].X < nodes[4].X);
            Assert.DoesNotContain("simple columns", vm.StatusMessage);
            Assert.Equal(1, vm.History.UndoCount);

            vm.UndoCommand.Execute(null);
            Assert.Equal(before, nodes.Select(n => (n.X, n.Y)).ToList());
        });
    }

    [Fact]
    public void TheBuiltInLayoutIsUsedWhenTheLayeredOneIsOff()
    {
        StaHost.Run(() =>
        {
            var registry = NodeRegistry.CreateDefault();
            var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
            var vm = new GraphEditorViewModel(registry, new StubDialogs(), settings) { UseLayeredArrange = false };
            for (var i = 0; i < 3; i++)
            {
                vm.Graph.AddNode(new SumNode { X = i, Y = i });
            }

            vm.ArrangeAllCommand.Execute(null);

            Assert.Contains("simple columns", vm.StatusMessage);
        });
    }
}

public class ParityCommandTests
{
    private static (GraphEditorViewModel Vm, string SettingsPath) NewEditor()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterNodeType("TestSum", () => new SumNode());
        var path = Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json");
        return (new GraphEditorViewModel(registry, new StubDialogs(), new UiSettingsService(path)), path);
    }

    private static NodeViewModel Add(GraphEditorViewModel vm, double x)
    {
        var node = new SumNode { X = x };
        vm.Graph.AddNode(node);
        return vm.Items.OfType<NodeViewModel>().Single(n => n.Model == node);
    }

    [Fact]
    public void CutRemovesTheSelectionInOneStepAndPasteBringsItBack()
    {
        StaHost.Run(() =>
        {
            var (vm, _) = NewEditor();
            var a = Add(vm, 0);
            vm.History.Clear();
            vm.SelectedItems.Add(a);

            vm.CutSelectionCommand.Execute(null);

            Assert.Empty(vm.Graph.Nodes);
            Assert.Equal(1, vm.History.UndoCount);
            vm.PasteCommand.Execute(null);
            Assert.Single(vm.Graph.Nodes);
        });
    }

    [Fact]
    public void SelectAllPicksEverything()
    {
        StaHost.Run(() =>
        {
            var (vm, _) = NewEditor();
            Add(vm, 0);
            Add(vm, 300);
            vm.Graph.Notes.Add(new NoteModel { Text = "n" });

            vm.SelectAllItemsCommand.Execute(null);

            Assert.Equal(3, vm.SelectedItems.Count);
        });
    }

    [Fact]
    public void ResetInputsAndWidthClearWhatTheUserSet()
    {
        StaHost.Run(() =>
        {
            var (vm, _) = NewEditor();
            var a = Add(vm, 0);
            a.Model.InPorts[0].SetUserValue(9d);
            a.Model.Ui.Width = 400;
            vm.SelectedItems.Add(a);
            vm.History.Clear();

            vm.ResetSelectedInputsCommand.Execute(null);
            vm.ResetSelectedWidthCommand.Execute(null);

            Assert.False(a.Model.InPorts[0].HasUserValue);
            Assert.Null(a.Model.Ui.Width);
            Assert.Equal(2, vm.History.UndoCount);
        });
    }

    [Fact]
    public void InsertIntoSelectedWireNeedsOneNodeAndOneWire()
    {
        StaHost.Run(() =>
        {
            var (vm, _) = NewEditor();
            var a = Add(vm, 0);
            var c = Add(vm, 600);
            var b = Add(vm, 300);
            Assert.True(vm.Graph.Connect(a.Model.OutPorts[0], c.Model.InPorts[0]).Success);
            vm.SelectedItems.Add(b);

            vm.InsertIntoSelectedWireCommand.Execute(null);
            Assert.Contains("one node and one wire", vm.StatusMessage);

            vm.SelectedConnections.Add(vm.Connections.Single());
            vm.InsertIntoSelectedWireCommand.Execute(null);
            Assert.Same(b.Model, vm.Graph.FindConnectionInto(c.Model.InPorts[0])!.SourceNode);
        });
    }

    [Fact]
    public void SwapLinksExchangesTwoSelectedWires()
    {
        StaHost.Run(() =>
        {
            var (vm, _) = NewEditor();
            var a = Add(vm, 0);
            var b = Add(vm, 0);
            var x = Add(vm, 300);
            var y = Add(vm, 300);
            Assert.True(vm.Graph.Connect(a.Model.OutPorts[0], x.Model.InPorts[0]).Success);
            Assert.True(vm.Graph.Connect(b.Model.OutPorts[0], y.Model.InPorts[0]).Success);
            foreach (var wire in vm.Connections.ToList())
            {
                vm.SelectedConnections.Add(wire);
            }

            vm.History.Clear();
            vm.SwapSelectedLinksCommand.Execute(null);

            Assert.Same(b.Model, vm.Graph.FindConnectionInto(x.Model.InPorts[0])!.SourceNode);
            Assert.Same(a.Model, vm.Graph.FindConnectionInto(y.Model.InPorts[0])!.SourceNode);
            Assert.Equal(1, vm.History.UndoCount);
        });
    }

    [Fact]
    public void TheMinimapShowsAutomaticallyOnlyForBigGraphsAndObeysTheToggle()
    {
        StaHost.Run(() =>
        {
            var (vm, path) = NewEditor();
            Add(vm, 0);
            Assert.Equal("auto", vm.MinimapMode);
            Assert.False(vm.IsMinimapVisible);

            for (var i = 1; i < GraphEditorViewModel.MinimapAutoNodeCount; i++)
            {
                Add(vm, i * 10);
            }

            Assert.True(vm.IsMinimapVisible);

            vm.ToggleMinimapCommand.Execute(null);
            Assert.False(vm.IsMinimapVisible);
            Assert.Equal("off", vm.MinimapMode);

            // The choice survives a restart.
            var again = new GraphEditorViewModel(NodeRegistry.CreateDefault(), new StubDialogs(), new UiSettingsService(path));
            Assert.Equal("off", again.MinimapMode);
        });
    }

    [Fact]
    public void PreferencesDriveTheViewAndResetRestoresTheDefaults()
    {
        StaHost.Run(() =>
        {
            var (vm, path) = NewEditor();
            var node = Add(vm, 0);
            var wireDetail = new System.Collections.Generic.List<string?>();
            vm.PropertyChanged += (s, e) => wireDetail.Add(e.PropertyName);

            Assert.Equal(22d, vm.RowBaseHeight);
            vm.NodeDensity = "compact";
            Assert.Equal(18d, vm.RowBaseHeight);
            Assert.All(node.Rows.Where(r => r.Kind == Dyncamelo.Core.Editing.RowKind.Input), r => Assert.Equal(18d, r.RowMinHeight));

            Assert.False(vm.WireLowDetail);
            vm.StraightWires = true;
            Assert.True(vm.WireLowDetail);

            vm.ScrubSpeed = "fast";
            Assert.True(vm.ScrubPixelsPerStep < 8d);
            vm.SnapToGrid = false;
            Assert.Equal(1u, vm.GridCellSize);
            Assert.Contains(nameof(GraphEditorViewModel.GridCellSize), wireDetail);

            // A node the user never touched follows the hide-unused preference.
            var visibleBefore = node.Rows.Count(r => r.Kind == Dyncamelo.Core.Editing.RowKind.Input);
            vm.HideUnusedByDefault = true;
            Assert.True(node.Rows.Count(r => r.Kind == Dyncamelo.Core.Editing.RowKind.Input) < visibleBefore);

            var settings = new UiSettingsService(path);
            Assert.Equal("compact", settings.GetString(SettingKeys.Density, "normal"));

            settings.ResetPreferences();
            Assert.Equal("normal", settings.GetString(SettingKeys.Density, "normal"));
        });
    }
}
