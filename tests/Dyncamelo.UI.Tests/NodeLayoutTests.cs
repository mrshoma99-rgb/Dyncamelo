using System;
using System.IO;
using System.Linq;
using System.Windows;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Nodes;
using Dyncamelo.UI.Services;
using Dyncamelo.UI.ViewModels;
using Dyncamelo.UI.Views;
using Xunit;

namespace Dyncamelo.UI.Tests;

/// <summary>Two-input, one-output test node.</summary>
public sealed class SumNode : NodeModel
{
    public SumNode()
    {
        Name = "Sum";
        Category = "Math";
        AddInput("a", typeof(double), 1.0);
        AddInput("b", typeof(double), 2.0);
        AddInput("c", typeof(double), 3.0);
        AddOutput("result", typeof(double));
    }

    public override string NodeType => "TestSum";

    public override object?[] Evaluate(object?[] inputs, EvaluationContext context) =>
        new object?[] { Convert.ToDouble(inputs[0] ?? 0d) + Convert.ToDouble(inputs[1] ?? 0d) };
}

internal sealed class StubDialogs : IDialogService
{
    public string? ShowOpenFile(string filter, string title) => null;

    public string? ShowSaveFile(string filter, string title, string defaultFileName) => null;

    public bool Confirm(string message, string title) => true;

    public void ShowError(string message, string title)
    {
    }

    public string? Prompt(string message, string title, string defaultValue) => null;

    public (int A, int R, int G, int B)? PickColor(int a, int r, int g, int b) => null;
}

/// <summary>
/// Spike S1 made permanent: the real editor control in a real window. Sockets
/// live outside Nodify's PART_Input/PART_Output panels, so these tests prove the
/// wire anchors still land on the node edges — at rest, after a move, when the
/// node collapses, at overview zoom, and in the classic layout.
/// </summary>
public class NodeLayoutTests
{
    private sealed class Rig : IDisposable
    {
        public GraphEditorViewModel Vm = null!;
        public Window Window = null!;
        public NodeViewModel A = null!;
        public NodeViewModel B = null!;
        public ConnectionViewModel Wire = null!;

        public void Dispose()
        {
            StaHost.Run(() => Window.Close());
        }
    }

    private static Rig Build()
    {
        var rig = new Rig();
        StaHost.Run(() =>
        {
            var registry = NodeRegistry.CreateDefault();
            registry.RegisterNodeType("TestSum", () => new SumNode());
            var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
            rig.Vm = new GraphEditorViewModel(registry, new StubDialogs(), settings);

            var a = new SumNode { X = 100, Y = 100 };
            var b = new SumNode { X = 500, Y = 160 };
            rig.Vm.Graph.AddNode(a);
            rig.Vm.Graph.AddNode(b);
            Assert.True(rig.Vm.Graph.Connect(a.OutPorts[0], b.InPorts[0]).Success);

            var control = new DyncameloEditorControl { ViewModel = rig.Vm };
            rig.Window = new Window
            {
                Width = 1400,
                Height = 900,
                Content = control,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStyle = WindowStyle.None,
            };
            rig.Window.Show();
        });
        StaHost.Flush();

        StaHost.Run(() =>
        {
            rig.A = rig.Vm.Items.OfType<NodeViewModel>().Single(n => n.Model.X == 100);
            rig.B = rig.Vm.Items.OfType<NodeViewModel>().Single(n => n.Model.X == 500);
            rig.Wire = rig.Vm.Connections.Single();
        });
        return rig;
    }

    private static void AssertOnEdge(NodeViewModel node, Point anchor, bool rightEdge, string what)
    {
        var size = node.Size;
        Assert.True(size.Width > 100 && size.Height > 30, what + ": node not measured, size " + size);
        var expectedX = rightEdge ? node.Location.X + size.Width - 7 : node.Location.X + 7;
        Assert.True(Math.Abs(anchor.X - expectedX) <= 3, what + ": anchor.X " + anchor.X + " expected ~" + expectedX);
        Assert.True(anchor.Y >= node.Location.Y - 1 && anchor.Y <= node.Location.Y + size.Height + 1,
            what + ": anchor.Y " + anchor.Y + " outside " + node.Location.Y + ".." + (node.Location.Y + size.Height));
    }

    [Fact]
    public void RowLayoutAnchorsSitOnTheNodeEdges()
    {
        using var rig = Build();
        StaHost.Run(() =>
        {
            Assert.True(rig.Vm.UseRowLayout);
            AssertOnEdge(rig.A, rig.Wire.Source.Anchor, rightEdge: true, "output");
            AssertOnEdge(rig.B, rig.Wire.Target.Anchor, rightEdge: false, "input");
            // Outputs are listed above inputs (Blender order), so the output sits higher than a's inputs would.
            Assert.True(rig.Wire.Source.Anchor.Y < rig.A.Location.Y + rig.A.Size.Height / 2);
        });
    }

    [Fact]
    public void AnchorsFollowAMovedNode()
    {
        using var rig = Build();
        StaHost.Run(() =>
        {
            var before = rig.Wire.Source.Anchor;
            rig.A.Model.X += 120;
            rig.A.Model.Y += 30;
        });
        StaHost.Flush();
        StaHost.Run(() =>
        {
            AssertOnEdge(rig.A, rig.Wire.Source.Anchor, rightEdge: true, "output after move");
        });
    }

    [Fact]
    public void CollapsingKeepsWiresAttachedToTheHeaderEdge()
    {
        using var rig = Build();
        StaHost.Run(() => rig.A.Model.Ui.Collapsed = true);
        StaHost.Flush();
        StaHost.Run(() =>
        {
            Assert.True(rig.A.Size.Height < 80, "collapsed node should be short, got " + rig.A.Size.Height);
            AssertOnEdge(rig.A, rig.Wire.Source.Anchor, rightEdge: true, "collapsed output");
            // Only the wired output stays as a zero-height row; unwired inputs are dropped.
            Assert.All(rig.A.Rows, r => Assert.True(r.ZeroHeight));
        });
        StaHost.Run(() => rig.A.Model.Ui.Collapsed = false);
        StaHost.Flush();
        StaHost.Run(() => AssertOnEdge(rig.A, rig.Wire.Source.Anchor, rightEdge: true, "re-expanded output"));
    }

    [Fact]
    public void OverviewZoomKeepsAnchorsValid()
    {
        using var rig = Build();
        StaHost.Run(() => rig.Vm.ViewportZoom = 0.2);
        StaHost.Flush();
        StaHost.Run(() =>
        {
            Assert.Equal(Dyncamelo.Core.Editing.LodLevel.Overview, rig.Vm.LodLevel);
            Assert.All(rig.A.Rows, r => Assert.Equal(0d, r.RowHeight));
            AssertOnEdge(rig.A, rig.Wire.Source.Anchor, rightEdge: true, "overview output");
            AssertOnEdge(rig.B, rig.Wire.Target.Anchor, rightEdge: false, "overview input");
        });
    }

    [Fact]
    public void ClassicLayoutStillWorks()
    {
        using var rig = Build();
        StaHost.Run(() => rig.Vm.ClassicNodeLayout = true);
        StaHost.Flush();
        StaHost.Run(() =>
        {
            Assert.False(rig.Vm.UseRowLayout);
            Assert.True(rig.A.Size.Width > 60);
            Assert.True(rig.Wire.Source.Anchor.X > rig.A.Location.X && rig.Wire.Source.Anchor.X < rig.A.Location.X + rig.A.Size.Width + 1);
            Assert.True(rig.Wire.Target.Anchor.X < rig.B.Location.X + rig.B.Size.Width);
        });
    }

    [Fact]
    public void HideUnusedDropsOptionalSocketsAndKeepsWiredOnes()
    {
        using var rig = Build();
        StaHost.Run(() =>
        {
            var inputs = rig.B.Rows.Count(r => r.Kind == Dyncamelo.Core.Editing.RowKind.Input);
            Assert.Equal(3, inputs);
            rig.B.Model.Ui.HideUnused = true;
        });
        StaHost.Flush();
        StaHost.Run(() =>
        {
            // a is wired, b and c are optional and unwired → hidden; the output is unwired → hidden.
            Assert.Equal(1, rig.B.Rows.Count(r => r.Kind == Dyncamelo.Core.Editing.RowKind.Input));
            Assert.Contains(rig.B.Rows, r => r.Kind == Dyncamelo.Core.Editing.RowKind.HiddenSummary && r.Count == 3);
            AssertOnEdge(rig.B, rig.Wire.Target.Anchor, rightEdge: false, "input after hide-unused");
        });
    }

    [Fact]
    public void RerouteNodeRendersAndConnects()
    {
        using var rig = Build();
        StaHost.Run(() =>
        {
            var graph = rig.Vm.Graph;
            var reroute = new RerouteNode { X = 300, Y = 300 };
            graph.AddNode(reroute);
            var sum = graph.Nodes.OfType<SumNode>().First();
            Assert.True(graph.Connect(sum.OutPorts[0], reroute.InPorts[0]).Success);
        });
        StaHost.Flush();
        StaHost.Run(() =>
        {
            var rr = rig.Vm.Items.OfType<NodeViewModel>().Single(n => n.IsReroute);
            var wire = rig.Vm.Connections.Single(c => c.Target.Node == rr);
            Assert.True(rr.Size.Width > 20 && rr.Size.Height > 10, "reroute size " + rr.Size);
            Assert.True(wire.Target.Anchor.X >= rr.Location.X && wire.Target.Anchor.X <= rr.Location.X + rr.Size.Width);
        });
    }
}
