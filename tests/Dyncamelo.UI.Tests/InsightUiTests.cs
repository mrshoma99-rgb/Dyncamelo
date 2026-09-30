using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.UI.Services;
using Dyncamelo.UI.ViewModels;
using Xunit;

namespace Dyncamelo.UI.Tests;

/// <summary>A node whose evaluation always fails.</summary>
internal sealed class ThrowNode : NodeModel
{
    public ThrowNode()
    {
        Name = "Thrower";
        Category = "Test";
        AddInput("in", typeof(double), 0.0);
        AddOutput("out", typeof(double));
    }

    public override string NodeType => "TestThrow";

    public override object?[] Evaluate(object?[] inputs, EvaluationContext context) => throw new InvalidOperationException("it broke");
}

/// <summary>Finding your way in the editor: problems, history, bookmarks, run up to, navigation, the palette's node search and the hints.</summary>
public class InsightUiTests
{
    private sealed class Rig
    {
        public GraphEditorViewModel Vm = null!;
        public ScriptedDialogs Dialogs = null!;
        public SumNode A = null!;
        public ThrowNode T = null!;
        public SumNode C = null!;
        public List<RevealEventArgs> Reveals = new List<RevealEventArgs>();
    }

    private static Rig Build(bool run = true)
    {
        var rig = new Rig { Dialogs = new ScriptedDialogs() };
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterNodeType("TestSum", () => new SumNode());
        var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
        rig.Vm = new GraphEditorViewModel(registry, rig.Dialogs, settings);
        rig.A = new SumNode { X = 0, Y = 0 };
        rig.T = new ThrowNode { X = 300, Y = 0 };
        rig.C = new SumNode { X = 600, Y = 0 };
        rig.Vm.Graph.AddNode(rig.A);
        rig.Vm.Graph.AddNode(rig.T);
        rig.Vm.Graph.AddNode(rig.C);
        Assert.True(rig.Vm.Graph.Connect(rig.A.OutPorts[0], rig.T.InPorts[0]).Success);
        Assert.True(rig.Vm.Graph.Connect(rig.T.OutPorts[0], rig.C.InPorts[0]).Success);
        rig.Vm.RevealRequested += (_, e) => rig.Reveals.Add(e);
        if (run)
        {
            rig.Vm.RunGraph();
        }

        return rig;
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

    // ----- problems --------------------------------------------------------------------------

    [Fact]
    public void ThePanelListsTheFailureFirstAndClickingARowSelectsTheNodeAndBringsItIntoView()
    {
        StaHost.Run(() =>
        {
            var rig = Build();
            Assert.Equal(1, rig.Vm.ErrorCount);
            Assert.Equal(1, rig.Vm.WarningCount);

            rig.Vm.ToggleProblemsCommand.Execute(null);

            Assert.True(rig.Vm.IsInfoPanelOpen);
            Assert.Equal("Problems", rig.Vm.InfoPanelTitle);
            Assert.Equal(2, rig.Vm.InfoPanelItems.Count);
            Assert.Equal(PanelItemKind.Error, rig.Vm.InfoPanelItems[0].Kind);
            Assert.Equal("Thrower", rig.Vm.InfoPanelItems[0].Title);
            Assert.Equal("it broke", rig.Vm.InfoPanelItems[0].Detail);
            Assert.Equal(PanelItemKind.Warning, rig.Vm.InfoPanelItems[1].Kind);

            rig.Vm.InfoPanelItems[0].ActivateCommand.Execute(null);

            Assert.Same(Vm(rig.Vm, rig.T), Assert.Single(rig.Vm.SelectedItems));
            var reveal = Assert.Single(rig.Reveals);
            Assert.True(reveal.Bounds.Contains(new Point(300, 0)));

            rig.Vm.ToggleProblemsCommand.Execute(null);
            Assert.False(rig.Vm.IsInfoPanelOpen);
        });
    }

    [Fact]
    public void ThePanelFollowsTheNextRun()
    {
        StaHost.Run(() =>
        {
            var rig = Build();
            rig.Vm.ToggleProblemsCommand.Execute(null);
            Assert.Equal(2, rig.Vm.InfoPanelItems.Count);

            rig.Vm.Graph.Disconnect(rig.Vm.Graph.Connections.First(c => c.TargetNode == rig.T));
            rig.T.IsMuted = true;
            rig.Vm.RunGraph();

            Assert.Empty(rig.Vm.InfoPanelItems);
            Assert.True(rig.Vm.IsInfoPanelEmpty);
            Assert.Contains("No problems", rig.Vm.InfoPanelEmptyText);
        });
    }

    [Fact]
    public void F8WalksThroughTheProblemsAndSaysWhichOneItIsOn()
    {
        StaHost.Run(() =>
        {
            var rig = Build();

            rig.Vm.NextProblemCommand.Execute(null);
            Assert.Same(Vm(rig.Vm, rig.T), Assert.Single(rig.Vm.SelectedItems));
            Assert.Contains("Error 1 of 2", rig.Vm.StatusMessage);
            Assert.Contains("it broke", rig.Vm.StatusMessage);

            rig.Vm.NextProblemCommand.Execute(null);
            Assert.Same(Vm(rig.Vm, rig.C), Assert.Single(rig.Vm.SelectedItems));
            Assert.Contains("Warning 2 of 2", rig.Vm.StatusMessage);

            rig.Vm.PreviousProblemCommand.Execute(null);
            Assert.Same(Vm(rig.Vm, rig.T), Assert.Single(rig.Vm.SelectedItems));
        });
    }

    [Fact]
    public void WithoutProblemsF8SaysSo()
    {
        StaHost.Run(() =>
        {
            var rig = Build(run: false);

            rig.Vm.NextProblemCommand.Execute(null);

            Assert.Contains("No problems", rig.Vm.StatusMessage);
            Assert.Empty(rig.Vm.SelectedItems);
        });
    }

    [Fact]
    public void WhyDidntThisRunExplainsTheSelectedNode()
    {
        StaHost.Run(() =>
        {
            var rig = Build();

            Select(rig.Vm, rig.C);
            rig.Vm.ExplainSelectedCommand.Execute(null);
            Assert.Contains("'Thrower' before it failed", rig.Vm.StatusMessage);

            rig.Vm.SelectedItems.Clear();
            rig.Vm.ExplainSelectedCommand.Execute(null);
            Assert.Contains("Select a node", rig.Vm.StatusMessage);
        });
    }

    // ----- undo history ----------------------------------------------------------------------

    [Fact]
    public void TheHistoryListsEveryStepAndClickingOneJumpsThere()
    {
        StaHost.Run(() =>
        {
            var rig = Build(run: false);
            rig.Vm.History.Clear();
            var first = new SumNode { X = 900 };
            rig.Vm.Graph.AddNode(first);
            rig.Vm.History.CoalesceWindow = TimeSpan.Zero;
            var second = new SumNode { X = 1200 };
            rig.Vm.Graph.AddNode(second);
            Assert.Equal(5, rig.Vm.Graph.Nodes.Count);

            rig.Vm.ToggleHistoryCommand.Execute(null);

            Assert.Equal("Undo history", rig.Vm.InfoPanelTitle);
            var items = rig.Vm.InfoPanelItems;
            Assert.Equal(PanelItemKind.Start, items[0].Kind);
            Assert.True(items[items.Count - 1].IsCurrent);
            Assert.Equal(rig.Vm.History.UndoCount + 1, items.Count);

            items[0].ActivateCommand.Execute(null);

            Assert.Equal(3, rig.Vm.Graph.Nodes.Count);
            Assert.True(rig.Vm.InfoPanelItems[0].IsCurrent);
            Assert.Contains(rig.Vm.InfoPanelItems, i => i.Kind == PanelItemKind.Future);

            var last = rig.Vm.InfoPanelItems[rig.Vm.InfoPanelItems.Count - 1];
            last.ActivateCommand.Execute(null);

            Assert.Equal(5, rig.Vm.Graph.Nodes.Count);
            Assert.DoesNotContain(rig.Vm.InfoPanelItems, i => i.Kind == PanelItemKind.Future);
        });
    }

    // ----- run up to a node ------------------------------------------------------------------

    [Fact]
    public void RunUpToTheSelectedNodeLeavesTheRestWaiting()
    {
        StaHost.Run(() =>
        {
            var rig = Build(run: false);
            Select(rig.Vm, rig.A);

            rig.Vm.RunToHereCommand.Execute(null);

            Assert.False(rig.A.IsDirty);
            Assert.True(rig.T.IsDirty);
            Assert.True(rig.C.IsDirty);
            Assert.Equal(NodeState.Idle, rig.T.State);
            Assert.Contains("Ran up to 'Sum'", rig.Vm.StatusMessage);
            Assert.Contains("still waiting", rig.Vm.StatusMessage);

            rig.Vm.RunGraph();
            Assert.Equal(NodeState.Error, rig.T.State);
        });
    }

    [Fact]
    public void RunUpToWithNothingSelectedSaysWhatToDo()
    {
        StaHost.Run(() =>
        {
            var rig = Build(run: false);

            rig.Vm.RunToHereCommand.Execute(null);

            Assert.Contains("Select the node", rig.Vm.StatusMessage);
            Assert.True(rig.A.IsDirty);
        });
    }

    // ----- bookmarks -------------------------------------------------------------------------

    [Fact]
    public void ABookmarkRemembersTheViewAndGoingToItRestoresIt()
    {
        StaHost.Run(() =>
        {
            var rig = Build(run: false);
            rig.Vm.ViewProvider = () => new ViewSnapshot(new Point(1500, -200), 0.6);
            rig.Dialogs.PromptAnswer = "  Export  ";

            rig.Vm.AddBookmarkCommand.Execute(null);

            var bookmark = Assert.Single(rig.Vm.Graph.Bookmarks);
            Assert.Equal("Export", bookmark.Name);
            Assert.Equal(1500, bookmark.X);
            Assert.Equal(0.6, bookmark.Zoom);

            rig.Vm.ToggleBookmarksCommand.Execute(null);
            var row = Assert.Single(rig.Vm.InfoPanelItems);
            Assert.Equal(PanelItemKind.Bookmark, row.Kind);
            Assert.True(row.CanRemove);

            row.ActivateCommand.Execute(null);
            var reveal = Assert.Single(rig.Reveals);
            Assert.Equal(new Point(1500, -200), reveal.Bounds.TopLeft);
            Assert.Equal(0.6, reveal.Zoom);
            Assert.True(reveal.AlwaysCenter);

            row.RemoveCommand!.Execute(null);
            Assert.Empty(rig.Vm.Graph.Bookmarks);
            Assert.True(rig.Vm.IsInfoPanelEmpty);
        });
    }

    [Fact]
    public void CancellingTheNamePromptAddsNoBookmark()
    {
        StaHost.Run(() =>
        {
            var rig = Build(run: false);
            rig.Vm.ViewProvider = () => new ViewSnapshot(new Point(0, 0), 1);
            rig.Dialogs.PromptAnswer = null;

            rig.Vm.AddBookmarkCommand.Execute(null);

            Assert.Empty(rig.Vm.Graph.Bookmarks);
        });
    }

    // ----- framing and arrows ----------------------------------------------------------------

    [Fact]
    public void FrameSelectedAsksTheViewToFitTheSelection()
    {
        StaHost.Run(() =>
        {
            var rig = Build(run: false);
            Select(rig.Vm, rig.A, rig.C);

            rig.Vm.FrameSelectedCommand.Execute(null);

            var reveal = Assert.Single(rig.Reveals);
            Assert.True(reveal.Zoom < 0);
            Assert.True(reveal.Bounds.Contains(new Point(0, 0)));
            Assert.True(reveal.Bounds.Contains(new Point(700, 50)));
        });
    }

    [Fact]
    public void FrameSelectedWithNothingSelectedSaysSo()
    {
        StaHost.Run(() =>
        {
            var rig = Build(run: false);

            rig.Vm.FrameSelectedCommand.Execute(null);

            Assert.Empty(rig.Reveals);
            Assert.Contains("Select something", rig.Vm.StatusMessage);
        });
    }

    [Fact]
    public void ArrowCommandsMoveTheSelectionToTheNeighbour()
    {
        StaHost.Run(() =>
        {
            var rig = Build(run: false);
            Select(rig.Vm, rig.A);

            rig.Vm.NavigateRightCommand.Execute(null);
            Assert.Same(Vm(rig.Vm, rig.T), Assert.Single(rig.Vm.SelectedItems));

            rig.Vm.NavigateRightCommand.Execute(null);
            Assert.Same(Vm(rig.Vm, rig.C), Assert.Single(rig.Vm.SelectedItems));

            rig.Vm.NavigateRightCommand.Execute(null);   // nothing further: stays
            Assert.Same(Vm(rig.Vm, rig.C), Assert.Single(rig.Vm.SelectedItems));

            rig.Vm.NavigateLeftCommand.Execute(null);
            Assert.Same(Vm(rig.Vm, rig.T), Assert.Single(rig.Vm.SelectedItems));
        });
    }

    // ----- finding a node --------------------------------------------------------------------

    [Fact]
    public void TheAtSignSearchesTheNodesOnTheCanvasAndEnterGoesToOne()
    {
        StaHost.Run(() =>
        {
            var rig = Build(run: false);

            rig.Vm.FindNodeCommand.Execute(null);
            Assert.True(rig.Vm.IsPaletteOpen);
            Assert.Equal("@", rig.Vm.PaletteQuery);
            Assert.Equal(3, rig.Vm.PaletteResults.Count);
            Assert.All(rig.Vm.PaletteResults, r => Assert.True(r.IsNode));

            rig.Vm.PaletteQuery = "@thrower";
            var entry = Assert.Single(rig.Vm.PaletteResults);
            Assert.Equal("Thrower", entry.Title);

            Assert.True(rig.Vm.RunPaletteEntry(entry));
            Assert.False(rig.Vm.IsPaletteOpen);
            Assert.Same(Vm(rig.Vm, rig.T), Assert.Single(rig.Vm.SelectedItems));
            Assert.Single(rig.Reveals);
        });
    }

    [Fact]
    public void PlainPaletteTextAlsoFindsNodesAlongsideCommands()
    {
        StaHost.Run(() =>
        {
            var rig = Build(run: false);
            rig.Vm.OpenPalette();
            Assert.DoesNotContain(rig.Vm.PaletteResults, r => r.IsNode);   // the empty list is commands only

            rig.Vm.PaletteQuery = "thrower";

            Assert.Contains(rig.Vm.PaletteResults, r => r.IsNode && r.Title == "Thrower");
        });
    }

    // ----- hints and scale -------------------------------------------------------------------

    [Fact]
    public void TheEmptyCanvasHintShowsUntilThereIsANodeAndCanBeSwitchedOff()
    {
        StaHost.Run(() =>
        {
            var dialogs = new ScriptedDialogs();
            var registry = NodeRegistry.CreateDefault();
            var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
            var vm = new GraphEditorViewModel(registry, dialogs, settings);

            Assert.True(vm.IsEmptyCanvasHintVisible);
            Assert.Contains(vm.EmptyCanvasHintLines, l => l.Contains(".dyc"));
            Assert.Contains("to add your first node", vm.HintText);

            vm.Graph.AddNode(new SumNode());
            Assert.False(vm.IsEmptyCanvasHintVisible);
            Assert.Contains("to add a node", vm.HintText);

            vm.NewCommand.Execute(null);
            Assert.True(vm.IsEmptyCanvasHintVisible);
            vm.ShowEmptyCanvasHints = false;
            Assert.False(vm.IsEmptyCanvasHintVisible);
            Assert.Equal(false, vm.ReadSetting("emptyHints"));
        });
    }

    [Fact]
    public void TheHintFollowsTheSelection()
    {
        StaHost.Run(() =>
        {
            var rig = Build(run: false);

            Select(rig.Vm, rig.A);
            Assert.Contains("M to mute", rig.Vm.HintText);

            rig.Vm.SelectedItems.Clear();
            Assert.DoesNotContain("M to mute", rig.Vm.HintText);
        });
    }

    [Fact]
    public void TheWindowScaleIsAFactorAndBadValuesFallBackToNormal()
    {
        StaHost.Run(() =>
        {
            var rig = Build(run: false);
            Assert.Equal(1d, rig.Vm.UiScaleFactor);

            rig.Vm.WriteSetting("uiScale", "125");
            Assert.Equal(1.25d, rig.Vm.UiScaleFactor);
            Assert.Equal("125", rig.Vm.ReadSetting("uiScale"));

            rig.Vm.UiScale = "banana";
            Assert.Equal(1d, rig.Vm.UiScaleFactor);
        });
    }

    // ----- socket tooltips -------------------------------------------------------------------

    [Fact]
    public void SocketTooltipsShowTheValueTheyHoldAndWhatArrivesOnAWire()
    {
        StaHost.Run(() =>
        {
            var rig = Build();
            var sum = Vm(rig.Vm, rig.A);
            var throwerInput = Vm(rig.Vm, rig.T).Inputs[0];

            // a = 1, b = 2, c = 3 by default.
            Assert.Contains("value: 6", sum.Outputs[0].ToolTip);
            Assert.Contains("receives: 6", throwerInput.ToolTip);

            // A node that failed says so instead of claiming a value.
            Assert.Contains("the node failed", Vm(rig.Vm, rig.T).Outputs[0].ToolTip);
        });
    }

    [Fact]
    public void ALongListIsSummarisedInTheTooltip()
    {
        StaHost.Run(() =>
        {
            var rig = Build(run: false);
            var output = Vm(rig.Vm, rig.A).Outputs[0];
            rig.A.OutPorts[0].Value = Enumerable.Range(0, 500).Select(i => (double)i).ToList();

            Assert.Contains("500 items: [0, 1, 2, 3, … 496 more]", output.ToolTip);
        });
    }
}
