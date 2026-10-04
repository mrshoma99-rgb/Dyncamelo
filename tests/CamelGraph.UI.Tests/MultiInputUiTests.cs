using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.UI.Services;
using CamelGraph.UI.ViewModels;
using CamelGraph.UI.Views;
using Xunit;

namespace CamelGraph.UI.Tests;

/// <summary>A node with one multi-input socket.</summary>
public sealed class MultiSinkNode : NodeModel
{
    public MultiSinkNode()
    {
        Name = "Sink";
        Category = "Test";
        AddMultiInput("items", typeof(IList<object>));
        AddOutput("result", typeof(IList<object>));
    }

    public override string NodeType => "TestMultiSink";

    public override object?[] Evaluate(object?[] inputs, EvaluationContext context) => new object?[] { inputs[0] };
}

/// <summary>Multi-input sockets as the editor draws and edits them.</summary>
public class MultiInputUiTests
{
    private sealed class Rig
    {
        public GraphEditorViewModel Vm = null!;
        public MultiSinkNode Sink = null!;
        public List<SumNode> Sources = new List<SumNode>();

        public NodeViewModel SinkVm => Vm.Items.OfType<NodeViewModel>().Single(n => n.Model == Sink);

        public ConnectorViewModel Pill => SinkVm.Inputs[0];

        public List<ConnectionViewModel> Wires => Vm.Connections.Where(c => c.Target == Pill).OrderBy(c => c.Slot).ToList();

        public List<NodeModel> Order => Vm.Graph.FindConnectionsInto(Sink.InPorts[0]).Select(w => w.SourceNode).ToList();
    }

    private static Rig Build(int wires)
    {
        var registry = NodeRegistry.CreateDefault();
        var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
        var rig = new Rig { Vm = new GraphEditorViewModel(registry, new StubDialogs(), settings) };
        rig.Sink = new MultiSinkNode { X = 400, Y = 100 };
        rig.Vm.Graph.AddNode(rig.Sink);
        for (var i = 0; i < wires; i++)
        {
            var source = new SumNode { X = 0, Y = i * 120 };
            rig.Vm.Graph.AddNode(source);
            rig.Sources.Add(source);
            Assert.True(rig.Vm.Graph.Connect(source.OutPorts[0], rig.Sink.InPorts[0]).Success);
        }

        rig.Vm.History.Clear();
        return rig;
    }

    [Fact]
    public void WireCountAndPillHeightFollowTheWires()
    {
        StaHost.Run(() =>
        {
            var rig = Build(1);
            Assert.True(rig.Pill.IsMultiInput);
            Assert.Equal(1, rig.Pill.WireCount);
            Assert.Equal(14d, rig.Pill.PillHeight);

            for (var i = 0; i < 2; i++)
            {
                var source = new SumNode();
                rig.Vm.Graph.AddNode(source);
                rig.Vm.Graph.Connect(source.OutPorts[0], rig.Sink.InPorts[0]);
            }

            Assert.Equal(3, rig.Pill.WireCount);
            Assert.Equal(32d, rig.Pill.PillHeight);       // (3 - 1) * 9 + 14
            Assert.Equal(32d, rig.Pill.SocketHeight);
            Assert.Contains("3 connected", rig.Pill.ToolTip);

            rig.Vm.Graph.Disconnect(rig.Vm.Graph.Connections.First());
            Assert.Equal(2, rig.Pill.WireCount);
            Assert.Equal(23d, rig.Pill.PillHeight);
        });
    }

    [Fact]
    public void AnOrdinaryInputIsNeverAPill()
    {
        StaHost.Run(() =>
        {
            var rig = Build(2);
            var sum = rig.Sources[0];
            var connector = rig.Vm.Items.OfType<NodeViewModel>().Single(n => n.Model == sum).Inputs[0];

            Assert.False(connector.IsMultiInput);
            Assert.Equal(14d, connector.SocketHeight);
            Assert.Equal(0, connector.WireCount);
        });
    }

    [Fact]
    public void WiresFanOutAlongThePillAndASingleWireLandsOnTheAnchor()
    {
        StaHost.Run(() =>
        {
            var rig = Build(3);
            rig.Pill.Anchor = new Point(100, 200);

            var wires = rig.Wires;
            Assert.Equal(new[] { 191d, 200d, 209d }, wires.Select(w => w.TargetAnchor.Y).ToArray());
            Assert.All(wires, w => Assert.Equal(100d, w.TargetAnchor.X));

            rig.Vm.Graph.Disconnect(rig.Vm.Graph.Connections[1]);
            rig.Vm.Graph.Disconnect(rig.Vm.Graph.Connections[1]);
            Assert.Equal(new Point(100, 200), rig.Wires.Single().TargetAnchor);
        });
    }

    [Fact]
    public void ARowMakesRoomForItsPill()
    {
        StaHost.Run(() =>
        {
            var rig = Build(1);
            var row = rig.SinkVm.Rows.First(r => r.Connector == rig.Pill);
            Assert.Equal(rig.Vm.RowBaseHeight, row.RowMinHeight);

            for (var i = 0; i < 4; i++)
            {
                var source = new SumNode();
                rig.Vm.Graph.AddNode(source);
                rig.Vm.Graph.Connect(source.OutPorts[0], rig.Sink.InPorts[0]);
            }

            row = rig.SinkVm.Rows.First(r => r.Connector == rig.Pill);
            Assert.Equal(rig.Pill.PillHeight + 4d, row.RowMinHeight);      // 5 wires: 50 + 4
            Assert.True(row.RowMinHeight > rig.Vm.RowBaseHeight);
        });
    }

    [Fact]
    public void NearestSlotIsPickedFromThePointer()
    {
        StaHost.Run(() =>
        {
            var rig = Build(4);
            rig.Pill.Anchor = new Point(0, 100);

            Assert.Equal(0, rig.Pill.SlotAt(60));
            Assert.Equal(0, rig.Pill.SlotAt(87));
            Assert.Equal(1, rig.Pill.SlotAt(96));
            Assert.Equal(2, rig.Pill.SlotAt(105));
            Assert.Equal(3, rig.Pill.SlotAt(500));
        });
    }

    [Fact]
    public void DraggingFromThePillTakesTheWireUnderThePointer()
    {
        StaHost.Run(() =>
        {
            var rig = Build(3);
            rig.Pill.Anchor = new Point(100, 200);
            rig.Vm.PointerLocation = () => new Point(100, 209);        // the third slot

            rig.Vm.StartConnectionCommand.Execute(rig.Pill);

            Assert.NotNull(rig.Vm.MovingLink);
            Assert.Same(rig.Sources[2], rig.Vm.MovingLink!.Model.SourceNode);
            Assert.True(rig.Vm.MovingLink.IsHidden);
            Assert.Equal(2, rig.Wires.Count(w => !w.IsHidden));
        });
    }

    [Fact]
    public void DroppingAPickedUpWireBackOnThePillMovesItToTheSlotItLandedOn()
    {
        StaHost.Run(() =>
        {
            var rig = Build(3);
            rig.Pill.Anchor = new Point(100, 200);
            rig.Vm.PointerLocation = () => new Point(100, 209);
            rig.Vm.StartConnectionCommand.Execute(rig.Pill);

            rig.Vm.PointerLocation = () => new Point(100, 190);        // the first slot
            rig.Vm.CreateConnectionCommand.Execute(rig.Pill);

            Assert.Equal(new NodeModel[] { rig.Sources[2], rig.Sources[0], rig.Sources[1] }, rig.Order);
            Assert.Equal(1, rig.Vm.History.UndoCount);

            rig.Vm.UndoCommand.Execute(null);
            Assert.Equal(new NodeModel[] { rig.Sources[0], rig.Sources[1], rig.Sources[2] }, rig.Order);
            rig.Vm.RedoCommand.Execute(null);
            Assert.Equal(new NodeModel[] { rig.Sources[2], rig.Sources[0], rig.Sources[1] }, rig.Order);
        });
    }

    [Fact]
    public void AWireDroppedOnAnotherMultiInputIsAppendedThere()
    {
        StaHost.Run(() =>
        {
            var rig = Build(2);
            var other = new MultiSinkNode { X = 400, Y = 400 };
            rig.Vm.Graph.AddNode(other);
            var otherPill = rig.Vm.Items.OfType<NodeViewModel>().Single(n => n.Model == other).Inputs[0];
            rig.Pill.Anchor = new Point(100, 200);
            rig.Vm.PointerLocation = () => new Point(100, 195);
            rig.Vm.StartConnectionCommand.Execute(rig.Pill);

            rig.Vm.CreateConnectionCommand.Execute(otherPill);

            Assert.Equal(new NodeModel[] { rig.Sources[1] }, rig.Order);
            Assert.Equal(new NodeModel[] { rig.Sources[0] }, rig.Vm.Graph.FindConnectionsInto(other.InPorts[0]).Select(w => w.SourceNode).ToList());
        });
    }

    [Fact]
    public void DisconnectingThePillRemovesEveryWireInOneUndoStep()
    {
        StaHost.Run(() =>
        {
            var rig = Build(3);

            rig.Vm.DisconnectConnectorCommand.Execute(rig.Pill);
            Assert.Empty(rig.Vm.Graph.Connections);
            Assert.False(rig.Pill.IsConnected);
            Assert.Equal(1, rig.Vm.History.UndoCount);

            rig.Vm.UndoCommand.Execute(null);
            Assert.Equal(new NodeModel[] { rig.Sources[0], rig.Sources[1], rig.Sources[2] }, rig.Order);
            Assert.Equal(3, rig.Pill.WireCount);
        });
    }

    [Fact]
    public void SelectedWiresMoveEarlierAndLaterAndStaySelected()
    {
        StaHost.Run(() =>
        {
            var rig = Build(3);
            rig.Vm.SelectedConnections.Add(rig.Wires[1]);

            rig.Vm.MoveSelectedWiresEarlierCommand.Execute(null);
            Assert.Equal(new NodeModel[] { rig.Sources[1], rig.Sources[0], rig.Sources[2] }, rig.Order);
            Assert.Equal(rig.Sources[1], rig.Vm.SelectedConnections.Single().Model.SourceNode);
            Assert.Equal(1, rig.Vm.History.UndoCount);

            rig.Vm.MoveSelectedWiresLaterCommand.Execute(null);
            rig.Vm.MoveSelectedWiresLaterCommand.Execute(null);
            Assert.Equal(new NodeModel[] { rig.Sources[0], rig.Sources[2], rig.Sources[1] }, rig.Order);
            Assert.Equal(rig.Sources[1], rig.Vm.SelectedConnections.Single().Model.SourceNode);

            rig.Vm.MoveSelectedWiresLaterCommand.Execute(null);            // already last: nothing moves
            Assert.Equal(new NodeModel[] { rig.Sources[0], rig.Sources[2], rig.Sources[1] }, rig.Order);
        });
    }

    [Fact]
    public void MovingAWireOnAnOrdinaryInputSaysWhatToSelect()
    {
        StaHost.Run(() =>
        {
            var rig = Build(1);
            var sum = new SumNode();
            rig.Vm.Graph.AddNode(sum);
            rig.Vm.Graph.Connect(rig.Sources[0].OutPorts[0], sum.InPorts[0]);
            rig.Vm.SelectedConnections.Add(rig.Vm.Connections.Single(c => c.Model.TargetNode == sum));

            rig.Vm.MoveSelectedWiresEarlierCommand.Execute(null);

            Assert.Contains("multi-input", rig.Vm.StatusMessage);
        });
    }

    [Fact]
    public void ARunCombinesTheWiresInTheirOrder()
    {
        StaHost.Run(() =>
        {
            var rig = Build(3);
            for (var i = 0; i < 3; i++)
            {
                rig.Sources[i].InPorts[0].SetUserValue((double)(i + 1) * 10d);
            }

            new GraphEngine().Run(rig.Vm.Graph);
            var combined = rig.Sink.OutPorts[0].Value as IList<object?>;
            Assert.NotNull(combined);
            var first = Convert.ToDouble(combined![0]);

            rig.Vm.PointerLocation = () => new Point(0, 0);
            var moved = CamelGraph.Core.Editing.GraphOps.MoveWire(rig.Vm.Graph, rig.Vm.Graph.FindConnectionsInto(rig.Sink.InPorts[0])[2], 0);
            Assert.NotNull(moved);
            new GraphEngine().Run(rig.Vm.Graph);

            var reordered = (IList<object?>)rig.Sink.OutPorts[0].Value!;
            Assert.NotEqual(first, Convert.ToDouble(reordered[0]));
            Assert.Equal(3, reordered.Count);
        });
    }
}

/// <summary>The pill and its fanned-out wires in a real window.</summary>
public class MultiInputWindowTests
{
    private static System.Collections.Generic.List<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var found = new System.Collections.Generic.List<T>();
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                found.Add(match);
            }

            found.AddRange(Descendants<T>(child));
        }

        return found;
    }

    [Fact]
    public void ThePillIsAsTallAsItsWiresAndEachWireLandsOnItsOwnSlot()
    {
        GraphEditorViewModel vm = null!;
        System.Windows.Window window = null!;
        MultiSinkNode sink = null!;
        StaHost.Run(() =>
        {
            var registry = NodeRegistry.CreateDefault();
            var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
            vm = new GraphEditorViewModel(registry, new StubDialogs(), settings);
            sink = new MultiSinkNode { X = 500, Y = 200 };
            vm.Graph.AddNode(sink);
            for (var i = 0; i < 3; i++)
            {
                var source = new SumNode { X = 50, Y = i * 130 };
                vm.Graph.AddNode(source);
                vm.Graph.Connect(source.OutPorts[0], sink.InPorts[0]);
            }

            window = new System.Windows.Window
            {
                Width = 1400,
                Height = 900,
                Content = new CamelGraphEditorControl { ViewModel = vm },
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStyle = System.Windows.WindowStyle.None,
            };
            window.Show();
        });
        StaHost.Flush();
        try
        {
            StaHost.Run(() =>
            {
                var pill = vm.Items.OfType<NodeViewModel>().Single(n => n.Model == sink).Inputs[0];
                var socket = Descendants<System.Windows.Controls.Control>(window)
                    .First(c => c.Name == "PART_Connector" && c.DataContext == pill);
                Assert.InRange(socket.ActualHeight, 31d, 33d);                    // the pill: (3 - 1) * 9 + 14

                var wires = Descendants<DycWire>(window).Where(w => w.DataContext is ConnectionViewModel c && c.Target == pill).ToList();
                Assert.Equal(3, wires.Count);
                var ends = wires.OrderBy(w => ((ConnectionViewModel)w.DataContext).Slot).Select(w => w.Target).ToList();
                Assert.Equal(9d, ends[1].Y - ends[0].Y, 1);
                Assert.Equal(9d, ends[2].Y - ends[1].Y, 1);
                Assert.All(ends, e => Assert.Equal(pill.Anchor.X, e.X, 1));
                Assert.Equal(pill.Anchor.Y, ends[1].Y, 1);                        // the middle wire lands on the centre
            });
        }
        finally
        {
            StaHost.Run(() => window.Close());
        }
    }
}
