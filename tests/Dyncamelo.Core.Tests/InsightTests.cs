using System;
using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Serialization;
using Dyncamelo.Core.Tests.Fixtures;
using Dyncamelo.Core.Types;
using Xunit;

namespace Dyncamelo.Core.Tests;

/// <summary>The problems list, "why didn't this run?", running up to a node and the small helpers behind the navigation aids.</summary>
public class ProblemsTests
{
    private readonly GraphEngine _engine = new GraphEngine();

    // value -> fail (throws) -> add, plus an independent sqrt: one error, one consequence, one healthy node.
    private sealed class Rig
    {
        public GraphModel Graph = new GraphModel();
        public ValueNode Seed = null!;
        public NodeModel Fail = null!;
        public NodeModel After = null!;
        public NodeModel Healthy = null!;
    }

    private Rig Build()
    {
        var rig = new Rig();
        rig.Seed = ZT.Value(rig.Graph, 4.0);
        rig.Fail = ZT.Node("Fail");
        rig.After = ZT.Node("AddStep");
        rig.Healthy = ZT.Node("Sqrt");
        rig.Fail.X = 200;
        rig.After.X = 400;
        rig.Healthy.X = 100;
        rig.Healthy.Y = 300;
        rig.Graph.AddNode(rig.Fail);
        rig.Graph.AddNode(rig.After);
        rig.Graph.AddNode(rig.Healthy);
        ZT.Wire(rig.Graph, rig.Seed, 0, rig.Fail, 0);
        ZT.Wire(rig.Graph, rig.Fail, 0, rig.After, 0);
        ZT.Wire(rig.Graph, rig.Seed, 0, rig.Healthy, 0);
        _engine.Run(rig.Graph);
        return rig;
    }

    [Fact]
    public void ErrorsComeBeforeWarningsAndHealthyNodesAreLeftOut()
    {
        var rig = Build();

        var problems = Problems.Collect(rig.Graph);

        Assert.Equal(new[] { rig.Fail, rig.After }, problems.Select(p => p.Node).ToArray());
        Assert.Equal(ProblemSeverity.Error, problems[0].Severity);
        Assert.Equal("boom", problems[0].Text);
        Assert.Equal(ProblemSeverity.Warning, problems[1].Severity);
        Assert.Contains("Upstream failure", problems[1].Text);
    }

    [Fact]
    public void AGraphThatHasNotRunHasNoProblems()
    {
        var graph = new GraphModel();
        ZT.Value(graph, 1.0);

        Assert.Empty(Problems.Collect(graph));
    }

    [Fact]
    public void StepWalksForwardAndBackwardAndWraps()
    {
        var rig = Build();
        var problems = Problems.Collect(rig.Graph);

        Assert.Same(problems[0], Problems.Step(problems, null, forward: true));
        Assert.Same(problems[1], Problems.Step(problems, problems[0].Node, forward: true));
        Assert.Same(problems[0], Problems.Step(problems, problems[1].Node, forward: true));
        Assert.Same(problems[1], Problems.Step(problems, problems[0].Node, forward: false));
        Assert.Same(problems[1], Problems.Step(problems, null, forward: false));
        Assert.Same(problems[0], Problems.Step(problems, rig.Healthy, forward: true));   // a node with no problem: start at the first
        Assert.Null(Problems.Step(new List<NodeProblem>(), null, forward: true));
    }

    [Fact]
    public void AFailedNodeIsExplainedByItsError()
    {
        var rig = Build();

        var text = RunExplanation.Explain(rig.Graph, rig.Fail);

        Assert.Contains("failed", text);
        Assert.Contains("boom", text);
    }

    [Fact]
    public void ANodeAfterAFailureNamesTheNodeThatFailed()
    {
        var rig = Build();

        var text = RunExplanation.Explain(rig.Graph, rig.After);

        Assert.Contains("'" + rig.Fail.Name + "'", text);
        Assert.Contains("failed", text);
    }

    [Fact]
    public void AFrozenNodeAndItsFollowersSayWhy()
    {
        var rig = Build();
        rig.Fail.IsFrozen = true;

        Assert.Contains("frozen", RunExplanation.Explain(rig.Graph, rig.Fail));
        var after = RunExplanation.Explain(rig.Graph, rig.After);
        Assert.Contains("frozen", after);
        Assert.Contains("'" + rig.Fail.Name + "'", after);
    }

    [Fact]
    public void AMutedNodeSaysItPassesItsInputThrough()
    {
        var rig = Build();
        rig.Healthy.IsMuted = true;

        Assert.Contains("muted", RunExplanation.Explain(rig.Graph, rig.Healthy));
    }

    [Fact]
    public void ANodeThatHasNeverRunSaysToPressRun()
    {
        var graph = new GraphModel();
        var node = ZT.Node("Sqrt");
        graph.AddNode(node);

        Assert.Contains("Press Run", RunExplanation.Explain(graph, node));
    }

    [Fact]
    public void ANodeWaitingForAnInputSaysWhich()
    {
        var graph = new GraphModel();
        var node = ZT.Node("Sqrt");
        graph.AddNode(node);
        _engine.Run(graph);

        var text = RunExplanation.Explain(graph, node);

        Assert.Contains("waiting for input", text);
    }

    [Fact]
    public void AHealthyNodeSaysItIsUpToDateAndAChangedOneSaysItRunsNext()
    {
        var rig = Build();
        Assert.Contains("up to date", RunExplanation.Explain(rig.Graph, rig.Healthy));

        rig.Healthy.MarkDirty();

        Assert.Contains("runs again", RunExplanation.Explain(rig.Graph, rig.Healthy));
    }

    // ----- run up to ------------------------------------------------------------------------------

    [Fact]
    public void RunUpToOnlyRunsTheTargetAndWhatItDependsOn()
    {
        var graph = new GraphModel();
        var seed = ZT.Value(graph, 16.0);
        var first = ZT.Node("Sqrt");
        var second = ZT.Node("AddStep");
        var sideBranch = ZT.Node("Sqrt");
        graph.AddNode(first);
        graph.AddNode(second);
        graph.AddNode(sideBranch);
        ZT.Wire(graph, seed, 0, first, 0);
        ZT.Wire(graph, first, 0, second, 0);
        ZT.Wire(graph, seed, 0, sideBranch, 0);

        var result = _engine.RunUpTo(graph, new NodeModel[] { first });

        Assert.Equal(new NodeModel[] { seed, first }, result.ExecutedNodes.ToArray());
        Assert.Equal(2, result.PlannedCount);
        Assert.Equal(4.0, first.OutPorts[0].Value);
        Assert.True(second.IsDirty);
        Assert.True(sideBranch.IsDirty);
        Assert.Null(second.OutPorts[0].Value);
    }

    [Fact]
    public void AnOrdinaryRunAfterwardsFinishesTheRest()
    {
        var graph = new GraphModel();
        var seed = ZT.Value(graph, 16.0);
        var first = ZT.Node("Sqrt");
        var second = ZT.Node("AddStep");
        graph.AddNode(first);
        graph.AddNode(second);
        ZT.Wire(graph, seed, 0, first, 0);
        ZT.Wire(graph, first, 0, second, 0);
        _engine.RunUpTo(graph, new NodeModel[] { first });

        var result = _engine.Run(graph);

        Assert.Equal(new NodeModel[] { second }, result.ExecutedNodes.ToArray());
        Assert.Equal(5.0, second.OutPorts[0].Value);
    }

    [Fact]
    public void RunUpToSkipsAFrozenTarget()
    {
        var graph = new GraphModel();
        var seed = ZT.Value(graph, 16.0);
        var node = ZT.Node("Sqrt");
        graph.AddNode(node);
        ZT.Wire(graph, seed, 0, node, 0);
        node.IsFrozen = true;

        var result = _engine.RunUpTo(graph, new NodeModel[] { node });

        Assert.DoesNotContain(node, result.ExecutedNodes);
    }
}

public class HistoryAndBookmarkTests
{
    private sealed class Counter : IUndoStep
    {
        private readonly int[] _box;
        public Counter(int[] box, string label) { _box = box; Label = label; }
        public string Label { get; }
        public void Undo() => _box[0]--;
        public void Redo() => _box[0]++;
    }

    private static UndoManager Build(int[] box, params string[] labels)
    {
        var undo = new UndoManager { CoalesceWindow = TimeSpan.Zero };
        foreach (var label in labels)
        {
            box[0]++;
            undo.Record(new Counter(box, label));
        }

        return undo;
    }

    [Fact]
    public void HistoryListsTheStepsOldestFirstAndTheUndoneOnesNextRedoFirst()
    {
        var box = new int[1];
        var undo = Build(box, "one", "two", "three");
        undo.Undo();
        undo.Undo();

        Assert.Equal(new[] { "one" }, undo.UndoLabels.ToArray());
        Assert.Equal(new[] { "two", "three" }, undo.RedoLabels.ToArray());
    }

    [Fact]
    public void JumpToGoesBackAndForwardToAnyStep()
    {
        var box = new int[1];
        var undo = Build(box, "a", "b", "c", "d");

        Assert.Equal(-3, undo.JumpTo(1));
        Assert.Equal(1, box[0]);
        Assert.Equal(1, undo.UndoCount);
        Assert.Equal(3, undo.RedoCount);

        Assert.Equal(2, undo.JumpTo(3));
        Assert.Equal(3, box[0]);

        Assert.Equal(-3, undo.JumpTo(0));
        Assert.Equal(0, box[0]);
        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void JumpToClampsToTheEnds()
    {
        var box = new int[1];
        var undo = Build(box, "a", "b");

        undo.JumpTo(-5);
        Assert.Equal(0, box[0]);

        undo.JumpTo(99);
        Assert.Equal(2, box[0]);
        Assert.Equal(0, undo.RedoCount);
    }

    [Fact]
    public void BookmarksSurviveSaveAndLoadAndAreOmittedWhenThereAreNone()
    {
        var registry = NodeRegistry.CreateDefault();
        var serializer = new GraphSerializer(registry);
        var graph = new GraphModel { Name = "Tour" };
        Assert.DoesNotContain("Bookmarks", serializer.Serialize(graph));

        graph.Bookmarks.Add(new BookmarkModel { Name = "Inputs", X = 120.5, Y = -40, Zoom = 0.75 });
        graph.Bookmarks.Add(new BookmarkModel { Name = "Export", X = 2000, Y = 300, Zoom = 1 });

        var loaded = serializer.Deserialize(serializer.Serialize(graph));

        Assert.Equal(new[] { "Inputs", "Export" }, loaded.Bookmarks.Select(b => b.Name).ToArray());
        Assert.Equal(120.5, loaded.Bookmarks[0].X);
        Assert.Equal(-40, loaded.Bookmarks[0].Y);
        Assert.Equal(0.75, loaded.Bookmarks[0].Zoom);
        Assert.Equal(graph.Bookmarks[0].Id, loaded.Bookmarks[0].Id);
    }

    [Fact]
    public void AddingRenamingAndRemovingABookmarkCanBeUndone()
    {
        var graph = new GraphModel();
        var undo = new UndoManager { CoalesceWindow = TimeSpan.Zero };
        using var recorder = new GraphRecorder(graph, undo);

        var bookmark = new BookmarkModel { Name = "Start" };
        graph.Bookmarks.Add(bookmark);
        bookmark.Name = "Renamed";
        graph.Bookmarks.Remove(bookmark);
        Assert.Empty(graph.Bookmarks);

        undo.Undo();
        Assert.Single(graph.Bookmarks);
        Assert.Equal("Renamed", graph.Bookmarks[0].Name);

        undo.Undo();
        Assert.Equal("Start", graph.Bookmarks[0].Name);

        undo.Undo();
        Assert.Empty(graph.Bookmarks);
    }
}

public class NavigationTests
{
    private static NodeModel At(GraphModel graph, double x, double y)
    {
        var node = ZT.Node("Sqrt");
        node.X = x;
        node.Y = y;
        graph.AddNode(node);
        return node;
    }

    [Fact]
    public void ArrowsSelectTheNearestNodeInThatDirection()
    {
        var graph = new GraphModel();
        var centre = At(graph, 500, 500);
        var right = At(graph, 800, 510);
        var farRight = At(graph, 1400, 500);
        var left = At(graph, 200, 500);
        var above = At(graph, 500, 200);
        var below = At(graph, 520, 800);

        Assert.Same(right, GraphOps.Nearest(graph.Nodes, centre, NavDirection.Right));
        Assert.Same(left, GraphOps.Nearest(graph.Nodes, centre, NavDirection.Left));
        Assert.Same(above, GraphOps.Nearest(graph.Nodes, centre, NavDirection.Up));
        Assert.Same(below, GraphOps.Nearest(graph.Nodes, centre, NavDirection.Down));
        Assert.Same(farRight, GraphOps.Nearest(graph.Nodes, right, NavDirection.Right));
    }

    [Fact]
    public void ANodeStraightAheadBeatsACloserOneFarOffToTheSide()
    {
        var graph = new GraphModel();
        var from = At(graph, 0, 0);
        var straight = At(graph, 400, 10);
        At(graph, 250, 600);

        Assert.Same(straight, GraphOps.Nearest(graph.Nodes, from, NavDirection.Right));
    }

    [Fact]
    public void NothingInTheDirectionMeansNoMove()
    {
        var graph = new GraphModel();
        var only = At(graph, 0, 0);
        At(graph, 500, 0);

        Assert.Null(GraphOps.Nearest(graph.Nodes, only, NavDirection.Left));
        Assert.Null(GraphOps.Nearest(graph.Nodes, only, NavDirection.Up));
    }

    [Fact]
    public void WithNothingSelectedAnArrowStartsAtTheTopLeft()
    {
        var graph = new GraphModel();
        At(graph, 300, 0);
        var topLeft = At(graph, 10, 400);
        At(graph, 600, 600);

        Assert.Same(topLeft, GraphOps.Nearest(graph.Nodes, null, NavDirection.Right));
        Assert.Null(GraphOps.Nearest(new List<NodeModel>(), null, NavDirection.Down));
    }
}

public class ValueSummaryAndHintTests
{
    [Fact]
    public void ScalarsStringsAndNullAreDescribed()
    {
        Assert.Equal("no value yet", ValueSummary.Describe(null));
        Assert.Equal("42", ValueSummary.Describe(42));
        Assert.Equal("\"hello\"", ValueSummary.Describe("hello"));
        Assert.Equal("1.5", ValueSummary.Describe(1.5));
    }

    [Fact]
    public void ListsShowTheCountAndTheFirstFewItems()
    {
        Assert.Equal("0 items", ValueSummary.Describe(new List<int>()));
        Assert.Equal("1 item: [7]", ValueSummary.Describe(new[] { 7 }));
        Assert.Equal("3 items: [1, 2, 3]", ValueSummary.Describe(new[] { 1, 2, 3 }));
        Assert.Equal("120 items: [0, 1, 2, 3, … 116 more]", ValueSummary.Describe(Enumerable.Range(0, 120).ToList()));
    }

    [Fact]
    public void LongTextIsCutAndNewlinesFlattened()
    {
        var text = ValueSummary.Describe(new string('x', 200) + "\nmore", maxLength: 20);

        Assert.DoesNotContain("\n", text);
        Assert.True(text.Length <= 22);
        Assert.EndsWith("…\"", text);
    }

    [Fact]
    public void DictionariesCountEntries()
    {
        var text = ValueSummary.Describe(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });

        Assert.StartsWith("2 entries:", text);
    }

    [Fact]
    public void TheStatusHintFollowsWhatIsHappeningAndUsesTheKeysInForce()
    {
        var keymap = new Keymap();

        Assert.Contains("Esc", HintLine.ForStatusBar(new HintContext { IsRunning = true }, keymap));
        Assert.Contains("Release on a socket", HintLine.ForStatusBar(new HintContext { DraggingWire = true }, keymap));

        var selected = HintLine.ForStatusBar(new HintContext { SelectedNodes = 2, HasNodes = true }, keymap);
        Assert.Contains("M to mute", selected);
        Assert.Contains("Shift+M to freeze", selected);
        Assert.DoesNotContain("open the node group", selected);

        var group = HintLine.ForStatusBar(new HintContext { SelectedNodes = 1, SelectedGroupInstance = true, HasNodes = true }, keymap);
        Assert.StartsWith("Tab to open the node group", group);

        Assert.Contains("Shift+Tab to go back", HintLine.ForStatusBar(new HintContext { InsideGroup = true, HasNodes = true }, keymap));
        Assert.Contains("to add your first node", HintLine.ForStatusBar(new HintContext(), keymap));
        Assert.Contains("F8 for the next problem", HintLine.ForStatusBar(new HintContext { HasNodes = true, HasErrors = true }, keymap));
    }

    [Fact]
    public void ARebindingOrAnUnboundCommandChangesTheHint()
    {
        var keymap = new Keymap(new Dictionary<string, string> { ["node.mute"] = "Ctrl+Alt+M", ["node.freeze"] = string.Empty });

        var hint = HintLine.ForStatusBar(new HintContext { SelectedNodes = 1, HasNodes = true }, keymap);

        Assert.Contains("Ctrl+Alt+M to mute", hint);
        Assert.DoesNotContain("freeze", hint);
    }

    [Fact]
    public void TheEmptyCanvasHintListsTheWaysToStartAndFollowsTheDoubleClickSetting()
    {
        var keymap = new Keymap();

        var lines = HintLine.ForEmptyCanvas(keymap, "number");

        Assert.Contains(lines, l => l.StartsWith("Space to search"));
        Assert.Contains("Double-click: add a Number node", lines);
        Assert.Contains(lines, l => l.Contains(".dyc"));
        Assert.DoesNotContain(HintLine.ForEmptyCanvas(keymap, "none"), l => l.StartsWith("Double-click"));
    }
}
