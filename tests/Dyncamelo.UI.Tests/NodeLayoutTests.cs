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

/// <summary>Named like Dyncamelo.Nodes' colour type so the port is classified as a colour.</summary>
public sealed class DyncameloColor
{
}

/// <summary>One input of every editor kind.</summary>
public sealed class EditorsNode : NodeModel
{
    public EditorsNode()
    {
        Name = "Editors";
        Category = "Test";
        AddInput("amount", typeof(double), 2.5);
        AddInput("flag", typeof(bool), false);
        AddInput("label", typeof(string), "hello");
        AddInput("tint", typeof(DyncameloColor), null);
        AddInput("outputPath", typeof(string), "");
        var few = AddInput("mode", typeof(string), "A");
        few.Choices = new[] { "A", "B" };
        var many = AddInput("kind", typeof(string), "One");
        many.Choices = new[] { "One", "Two", "Three", "Four", "Five" };
        AddOutput("result", typeof(double));
    }

    public override string NodeType => "TestEditors";

    public override object?[] Evaluate(object?[] inputs, EvaluationContext context) => new object?[] { 1d };
}

internal sealed class StubDialogs : IDialogService
{
    public string? ShowOpenFile(string filter, string title) => null;

    public string? ShowSaveFile(string filter, string title, string defaultFileName) => null;

    public bool Confirm(string message, string title) => true;

    public SaveChoice AskSaveChanges(string message, string title) => SaveChoice.DontSave;

    public void ShowError(string message, string title)
    {
    }

    public string? Prompt(string message, string title, string defaultValue) => null;

    public string? PickFolder(string title, string initialFolder) => null;
}

/// <summary>
/// Spike S1 made permanent: the real editor control in a real window. Sockets
/// live outside Nodify's PART_Input/PART_Output panels, so these tests prove the
/// wire anchors still land on the node edges — at rest, after a move, when the
/// node collapses and at overview zoom.
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
            registry.RegisterNodeType("TestEditors", () => new EditorsNode());
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
        // Sockets are centred on the visible card outline: 7px item margin + 2px state border.
        var expectedX = rightEdge ? node.Location.X + size.Width - 9 : node.Location.X + 9;
        Assert.True(Math.Abs(anchor.X - expectedX) <= 2.5, what + ": anchor.X " + anchor.X + " expected ~" + expectedX);
        Assert.True(anchor.Y >= node.Location.Y - 1 && anchor.Y <= node.Location.Y + size.Height + 1,
            what + ": anchor.Y " + anchor.Y + " outside " + node.Location.Y + ".." + (node.Location.Y + size.Height));
    }

    [Fact]
    public void RowLayoutAnchorsSitOnTheNodeEdges()
    {
        using var rig = Build();
        StaHost.Run(() =>
        {
            AssertOnEdge(rig.A, rig.Wire.Source.Anchor, rightEdge: true, "output");
            AssertOnEdge(rig.B, rig.Wire.Target.Anchor, rightEdge: false, "input");
            // Outputs are listed above inputs (Blender order), so the output sits higher than a's inputs would.
            Assert.True(rig.Wire.Source.Anchor.Y < rig.A.Location.Y + rig.A.Size.Height / 2);
        });
    }

    [Fact]
    public void TheLiveWireIsDrawnBetweenTheSockets()
    {
        using var rig = Build();
        StaHost.Run(() =>
        {
            var wire = FindDescendants<DycWire>(rig.Window).Single();
            var bounds = ThemeAndWireTests.Define(wire).Bounds;
            Assert.False(bounds.IsEmpty, "live wire has no geometry");
            Assert.True(Math.Abs(bounds.Left - rig.Wire.Source.Anchor.X) < 2 && Math.Abs(bounds.Right - rig.Wire.Target.Anchor.X) < 2,
                "wire spans " + bounds + " but sockets are at " + rig.Wire.Source.Anchor + " -> " + rig.Wire.Target.Anchor);
        });
    }

    private static System.Collections.Generic.List<T> FindDescendants<T>(DependencyObject root)
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

            found.AddRange(FindDescendants<T>(child));
        }

        return found;
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
    public void CollapsingKeepsEverySocketVisibleOnTheEdgesAsSlimRows()
    {
        using var rig = Build();
        var expanded = 0d;
        StaHost.Run(() => expanded = rig.A.Size.Height);
        StaHost.Run(() => rig.A.Model.Ui.Collapsed = true);
        StaHost.Flush();
        StaHost.Run(() =>
        {
            // One output and three inputs stay as slim socket-only rows: no labels, no editors, not zero-height.
            Assert.Equal(4, rig.A.Rows.Count);
            Assert.All(rig.A.Rows, r =>
            {
                Assert.True(r.Compact, "row " + r.Key + " should be a slim socket row");
                Assert.False(r.ZeroHeight);
                Assert.False(r.ShowContent);
                Assert.Equal(NodeRowViewModel.CompactRowHeight, r.RowHeight);
            });
            // The size includes the value-preview bubble under the node, so compare with the expanded node rather than a constant.
            Assert.True(rig.A.Size.Height < expanded, "collapsed node should be shorter than expanded (" + expanded + "), got " + rig.A.Size.Height);
            AssertOnEdge(rig.A, rig.Wire.Source.Anchor, rightEdge: true, "collapsed output");

            // Each input keeps its socket on the left edge, one under another — an unwired socket has no wire anchor, so look at the
            // socket controls themselves.
            var sockets = FindDescendants<Nodify.NodeInput>(rig.Window)
                .Where(i => i.DataContext is ConnectorViewModel c && rig.A.Inputs.Contains(c))
                .ToList();
            Assert.Equal(3, sockets.Count);
            Assert.All(sockets, i => Assert.True(i.IsVisible && i.ActualHeight > 0, "a collapsed node's socket must stay visible"));
            var positions = sockets.Select(i => i.TransformToAncestor(rig.Window).Transform(new Point(0, 0))).ToList();
            Assert.Equal(3, positions.Select(p => Math.Round(p.Y)).Distinct().Count());
            Assert.True(positions.Max(p => p.X) - positions.Min(p => p.X) < 1.5, "the input sockets share one column");
        });
        StaHost.Run(() => rig.A.Model.Ui.Collapsed = false);
        StaHost.Flush();
        StaHost.Run(() =>
        {
            Assert.All(rig.A.Rows.Where(r => r.Kind != Dyncamelo.Core.Editing.RowKind.Body), r => Assert.False(r.Compact));
            AssertOnEdge(rig.A, rig.Wire.Source.Anchor, rightEdge: true, "re-expanded output");
        });
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

    // ----- Phase 3: inline editors ---------------------------------------------------

    [Fact]
    public void UnwiredNumberInputsGetAScrubFieldAndWiredOnesDoNot()
    {
        using var rig = Build();
        StaHost.Run(() =>
        {
            // a: 3 unwired number inputs; b: input "a" is wired, so 2 remain.
            var fields = FindDescendants<ScrubNumberBox>(rig.Window);
            Assert.Equal(5, fields.Count);
        });
    }

    [Fact]
    public void EditingAFieldPinsTheValueAndUndoRestoresIt()
    {
        using var rig = Build();
        StaHost.Run(() =>
        {
            var port = rig.B.Model.InPorts[1]; // "b", default 2
            var field = FindDescendants<ScrubNumberBox>(rig.Window).First(f => f.Label == "b" && f.Value == 2d && ReferenceEquals(f.DataContext, rig.B.Rows.First(r => r.Connector?.Port == port).Connector));
            Assert.False(port.HasUserValue);

            Assert.True(field.CommitText("2*3+1"));
            Assert.Equal(7d, port.UserValue);
            Assert.True(rig.B.Rows.First(r => r.Connector?.Port == port).Connector!.IsModified);

            rig.Vm.UndoCommand.Execute(null);
            Assert.False(port.HasUserValue);
            Assert.Equal(2d, field.Value);
        });
    }

    [Fact]
    public void BadTextIsRejectedAndSteppingRespectsTheRange()
    {
        using var rig = Build();
        StaHost.Run(() =>
        {
            var field = FindDescendants<ScrubNumberBox>(rig.Window).First();
            var before = field.Value;
            Assert.False(field.CommitText("abc"));
            Assert.True(field.IsInvalid);
            Assert.Equal(before, field.Value);
            Assert.True(field.CommitText("4"));
            Assert.False(field.IsInvalid);
            field.StepValue(1);
            Assert.Equal(4.1, field.Value, 6); // default 1.0..3.0 magnitude => 0.01/0.1 steps; see ScrubMath
        });
    }

    [Fact]
    public void ConnectingAnInputHidesItsEditor()
    {
        using var rig = Build();
        var before = 0;
        StaHost.Run(() =>
        {
            before = FindDescendants<ScrubNumberBox>(rig.Window).Count;
            var graph = rig.Vm.Graph;
            var src = graph.Nodes.OfType<SumNode>().First(n => n.X == 100);
            Assert.True(graph.Connect(src.OutPorts[0], rig.B.Model.InPorts[1]).Success);
        });
        StaHost.Flush();
        StaHost.Run(() => Assert.Equal(before - 1, FindDescendants<ScrubNumberBox>(rig.Window).Count));
    }

    [Fact]
    public void EveryEditorKindIsRenderedForItsPort()
    {
        var rig = new Rig();
        StaHost.Run(() =>
        {
            var registry = NodeRegistry.CreateDefault();
            registry.RegisterNodeType("TestEditors", () => new EditorsNode());
            var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
            rig.Vm = new GraphEditorViewModel(registry, new StubDialogs(), settings);
            rig.Vm.Graph.AddNode(new EditorsNode { X = 100, Y = 100 });
            rig.Window = new Window { Width = 1400, Height = 900, Content = new DyncameloEditorControl { ViewModel = rig.Vm }, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
            rig.Window.Show();
        });
        StaHost.Flush();
        using (rig)
        {
            StaHost.Run(() =>
            {
                // Count by the port each control edits, so template internals (a ComboBox hosts its own TextBox) never confuse it.
                int Editors<T>(Func<Dyncamelo.UI.ViewModels.ConnectorViewModel, bool> forPort)
                    where T : FrameworkElement =>
                    FindDescendants<T>(rig.Window).Count(e => e.DataContext is Dyncamelo.UI.ViewModels.ConnectorViewModel c && forPort(c));

                Assert.Equal(1, Editors<ScrubNumberBox>(c => c.Port.Name == "amount"));
                Assert.Equal(1, Editors<System.Windows.Controls.CheckBox>(c => c.Port.Name == "flag"));
                Assert.Equal(1, Editors<System.Windows.Controls.TextBox>(c => c.Port.Name == "label"));
                Assert.Equal(1, Editors<System.Windows.Controls.TextBox>(c => c.Port.Name == "outputPath"));
                Assert.Equal(1, Editors<System.Windows.Controls.ListBox>(c => c.Port.Name == "mode"));
                Assert.Equal(1, Editors<System.Windows.Controls.ComboBox>(c => c.Port.Name == "kind"));
                Assert.Equal(1, Editors<ColorSwatchPicker>(c => c.Port.Name == "tint"));

                // The typed text must fit inside the box (a 6px vertical padding once cut the text in half).
                foreach (var box in FindDescendants<System.Windows.Controls.TextBox>(rig.Window)
                             .Where(t => t.DataContext is Dyncamelo.UI.ViewModels.ConnectorViewModel c && (c.Port.Name == "label" || c.Port.Name == "outputPath")))
                {
                    var host = box.Template.FindName("PART_ContentHost", box) as System.Windows.Controls.ScrollViewer;
                    Assert.NotNull(host);
                    var needed = box.FontSize * box.FontFamily.LineSpacing;
                    Assert.True(host!.ActualHeight >= needed - 0.5, "text area " + host.ActualHeight + "px is shorter than one line (" + needed + "px)");
                }
                Assert.Equal(0, Editors<System.Windows.Controls.ComboBox>(c => c.Port.Name == "mode"));
                // A ListBoxItem only selects on click when it can take focus (a non-focusable one silently ignores clicks).
                var segments = FindDescendants<System.Windows.Controls.ListBoxItem>(rig.Window).Where(i => i.DataContext is string).ToList();
                Assert.NotEmpty(segments);
                Assert.All(segments, i => Assert.True(i.Focusable, "segment '" + i.DataContext + "' cannot be clicked"));
                var node = rig.Vm.Items.OfType<NodeViewModel>().Single();
                var tint = node.Inputs.Single(c => c.Port.Name == "tint");
                Assert.Equal(Dyncamelo.Core.Editing.PortEditorKind.Colour, tint.EditorKind);
                Assert.Equal(Dyncamelo.Core.Editing.PortEditorKind.Path, node.Inputs.Single(c => c.Port.Name == "outputPath").EditorKind);
            });
        }
    }

    [Fact]
    public void BodyTemplateCommandsResolveInTheRowLayout()
    {
        var rig = new Rig();
        StaHost.Run(() =>
        {
            var registry = NodeRegistry.CreateDefault();
            var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
            rig.Vm = new GraphEditorViewModel(registry, new StubDialogs(), settings);
            rig.Vm.Graph.AddNode(new FilePathNode { X = 100, Y = 100 });
            rig.Window = new Window { Width = 1400, Height = 900, Content = new DyncameloEditorControl { ViewModel = rig.Vm }, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
            rig.Window.Show();
        });
        StaHost.Flush();
        using (rig)
        {
            StaHost.Run(() =>
            {
                    var node = rig.Vm.Items.OfType<NodeViewModel>().Single();
                var browse = FindDescendants<System.Windows.Controls.Button>(rig.Window).Single(b => Equals(b.Content, "…"));
                // The body templates used to find the node view model through a nodify:Node ancestor, which the row layout does not have.
                Assert.Same(node.BrowseFileCommand, browse.Command);
            });
        }
    }

    [Fact]
    public void AStaleMouseCaptureInsideTheEditorIsReleased()
    {
        var rig = Build();
        using (rig)
        {
            StaHost.Run(() =>
            {
                var control = (DyncameloEditorControl)rig.Window.Content;
                var editor = FindDescendants<Nodify.NodifyEditor>(rig.Window).Single();
                if (!System.Windows.Input.Mouse.Capture(editor))
                {
                    return; // this desktop cannot grant capture; nothing to release
                }

                Assert.Same(editor, System.Windows.Input.Mouse.Captured);
                control.ReleaseStaleMouseCapture();
                Assert.NotSame(editor, System.Windows.Input.Mouse.Captured);
            });
        }
    }

    [Fact]
    public void KeysTheHostWouldTakeAreClaimedAndRunByTheEditor()
    {
        var rig = Build();
        using (rig)
        {
            StaHost.Run(() =>
            {
                var control = (DyncameloEditorControl)rig.Window.Content;
                control.ModifierProvider = () => System.Windows.Input.ModifierKeys.Control;

                // Navisworks binds Ctrl+Z / Ctrl+Y itself; while the pane has focus the editor must get them.
                Assert.True(control.WantsHostKey(System.Windows.Input.Key.Z));
                Assert.True(control.WantsHostKey(System.Windows.Input.Key.Y));
                Assert.False(control.WantsHostKey(System.Windows.Input.Key.Q));

                rig.A.Model.X += 25;
                var before = rig.Vm.History.UndoCount;
                var movedTo = rig.A.Model.X;

                Assert.True(control.ProcessHostKey(System.Windows.Input.Key.Z));

                Assert.Equal(before - 1, rig.Vm.History.UndoCount);
                Assert.Equal(movedTo - 25, rig.A.Model.X);

                Assert.True(control.ProcessHostKey(System.Windows.Input.Key.Y));
                Assert.Equal(movedTo, rig.A.Model.X);
            });
        }
    }
}
