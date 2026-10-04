using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Nodes;
using CamelGraph.Core.Serialization;
using CamelGraph.Core.Tests.Fixtures;
using Xunit;

namespace CamelGraph.Core.Tests;

public class UndoManagerTests
{
    private sealed class Counter : IUndoStep
    {
        public int Value;
        public List<string> Log = new List<string>();
        public string Label { get; set; } = "step";
        public void Undo() => Log.Add("undo");
        public void Redo() => Log.Add("redo");
    }

    private sealed class Merge : ICoalescingStep
    {
        public int Last;
        public string Label => "merge";
        public Merge(int v) => Last = v;
        public void Undo() { }
        public void Redo() { }
        public bool TryMerge(IUndoStep newer)
        {
            if (newer is Merge m)
            {
                Last = m.Last;
                return true;
            }

            return false;
        }
    }

    [Fact]
    public void UndoRedoMoveStepsBetweenStacks()
    {
        var mgr = new UndoManager();
        var a = new Counter { Label = "A" };
        mgr.Record(a);
        Assert.True(mgr.CanUndo);
        Assert.Equal("A", mgr.UndoLabel);
        Assert.Equal("A", mgr.Undo());
        Assert.False(mgr.CanUndo);
        Assert.Equal("A", mgr.RedoLabel);
        Assert.Equal("A", mgr.Redo());
        Assert.Equal(new[] { "undo", "redo" }, a.Log);
    }

    [Fact]
    public void NewEditClearsRedo()
    {
        var mgr = new UndoManager();
        mgr.Record(new Counter());
        mgr.Undo();
        Assert.True(mgr.CanRedo);
        mgr.Record(new Counter());
        Assert.False(mgr.CanRedo);
    }

    [Fact]
    public void TransactionIsOneItemAndUndoesInReverse()
    {
        var mgr = new UndoManager();
        var order = new List<string>();
        using (mgr.Begin("Group"))
        {
            mgr.Record(new Logging("1", order));
            mgr.Record(new Logging("2", order));
            mgr.Record(new Logging("3", order));
        }

        Assert.Equal(1, mgr.UndoCount);
        Assert.Equal("Group", mgr.UndoLabel);
        mgr.Undo();
        Assert.Equal(new[] { "u3", "u2", "u1" }, order);
        order.Clear();
        mgr.Redo();
        Assert.Equal(new[] { "r1", "r2", "r3" }, order);
    }

    private sealed class Logging : IUndoStep
    {
        private readonly string _n;
        private readonly List<string> _log;
        public Logging(string n, List<string> log) { _n = n; _log = log; }
        public string Label => _n;
        public void Undo() => _log.Add("u" + _n);
        public void Redo() => _log.Add("r" + _n);
    }

    [Fact]
    public void NestedTransactionsCountAsTheOutermost()
    {
        var mgr = new UndoManager();
        using (mgr.Begin("Outer"))
        {
            mgr.Record(new Counter());
            using (mgr.Begin("Inner"))
            {
                mgr.Record(new Counter());
            }

            mgr.Record(new Counter());
            Assert.Equal(0, mgr.UndoCount);
        }

        Assert.Equal(1, mgr.UndoCount);
        Assert.Equal("Outer", mgr.UndoLabel);
    }

    [Fact]
    public void EmptyTransactionRecordsNothing()
    {
        var mgr = new UndoManager();
        using (mgr.Begin("nothing"))
        {
        }

        Assert.False(mgr.CanUndo);
    }

    [Fact]
    public void CancelRevertsAndDiscards()
    {
        var mgr = new UndoManager();
        var log = new List<string>();
        var txn = mgr.Begin("Failing");
        mgr.Record(new Logging("1", log));
        mgr.Record(new Logging("2", log));
        txn.Cancel();
        txn.Dispose();
        Assert.Equal(new[] { "u2", "u1" }, log);
        Assert.False(mgr.CanUndo);
        Assert.False(mgr.CanRedo);
        Assert.False(mgr.InTransaction);
    }

    [Fact]
    public void CoalescesWithinTheWindowOnly()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var mgr = new UndoManager { Clock = () => now };
        var m1 = new Merge(1);
        mgr.Record(m1);
        now = now.AddMilliseconds(200);
        mgr.Record(new Merge(2));
        Assert.Equal(1, mgr.UndoCount);
        Assert.Equal(2, m1.Last);
        now = now.AddMilliseconds(1000);
        mgr.Record(new Merge(3));
        Assert.Equal(2, mgr.UndoCount);
    }

    [Fact]
    public void CoalescesInsideTransactionsRegardlessOfTime()
    {
        var now = DateTime.UtcNow;
        var mgr = new UndoManager { Clock = () => now };
        using (mgr.Begin("Drag"))
        {
            for (var i = 0; i < 50; i++)
            {
                now = now.AddSeconds(2);
                mgr.Record(new Merge(i));
            }
        }

        Assert.Equal(1, mgr.UndoCount);
    }

    [Fact]
    public void CapacityDropsTheOldest()
    {
        var mgr = new UndoManager { Capacity = 3 };
        for (var i = 0; i < 10; i++)
        {
            mgr.Record(new Counter { Label = i.ToString() });
        }

        Assert.Equal(3, mgr.UndoCount);
        Assert.Equal("9", mgr.UndoLabel);
    }

    [Fact]
    public void RecordingIsIgnoredWhileReplayingAndSuspended()
    {
        var mgr = new UndoManager();
        mgr.Record(new Reentrant(mgr));
        mgr.Undo();
        Assert.Equal(0, mgr.UndoCount);
        Assert.Equal(1, mgr.RedoCount);

        using (mgr.Suspend())
        {
            mgr.Record(new Counter());
        }

        Assert.Equal(0, mgr.UndoCount);
        mgr.Record(new Counter());
        Assert.Equal(1, mgr.UndoCount);
    }

    private sealed class Reentrant : IUndoStep
    {
        private readonly UndoManager _m;
        public Reentrant(UndoManager m) => _m = m;
        public string Label => "re";
        public void Undo() => _m.Record(new Counter()); // must be ignored
        public void Redo() { }
    }

    [Fact]
    public void ClearForgetsEverythingAndRaisesChanged()
    {
        var mgr = new UndoManager();
        var raised = 0;
        mgr.Changed += (s, e) => raised++;
        mgr.Record(new Counter());
        mgr.Clear();
        Assert.False(mgr.CanUndo);
        Assert.True(raised >= 2);
    }

    [Fact]
    public void FailingReplayClearsHistoryAndRethrows()
    {
        var mgr = new UndoManager();
        mgr.Record(new Counter());
        mgr.Record(new Throwing());
        Assert.Throws<InvalidOperationException>(() => mgr.Undo());
        Assert.False(mgr.CanUndo);
        Assert.False(mgr.CanRedo);
        Assert.False(mgr.IsReplaying);
    }

    private sealed class Throwing : IUndoStep
    {
        public string Label => "boom";
        public void Undo() => throw new InvalidOperationException();
        public void Redo() { }
    }
}

public class GraphRecorderTests
{
    private static (GraphModel Graph, UndoManager Undo, GraphRecorder Recorder) Setup()
    {
        var graph = new GraphModel();
        var undo = new UndoManager();
        return (graph, undo, new GraphRecorder(graph, undo));
    }

    private static string Snapshot(GraphModel graph)
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterDefinition(ZT.Definition("Add"));
        return new GraphSerializer(registry).Serialize(graph);
    }

    [Fact]
    public void AddNodeUndoRedo()
    {
        var (graph, undo, _) = Setup();
        var node = new RerouteNode();
        graph.AddNode(node);
        Assert.Equal(1, undo.UndoCount);

        undo.Undo();
        Assert.Empty(graph.Nodes);
        undo.Redo();
        Assert.Same(node, graph.Nodes.Single());
    }

    [Fact]
    public void DeleteNodeRestoresWiresValuesAndIdentity()
    {
        var (graph, undo, _) = Setup();
        var src = ZT.Value(graph, 3d);
        var step = ZT.Node("AddStep");
        graph.AddNode(step);
        var sink = ZT.Node("Sqrt");
        graph.AddNode(sink);
        ZT.Wire(graph, src, 0, step, 0);
        ZT.Wire(graph, step, 0, sink, 0);
        step.InPorts[1].SetUserValue(4d);
        step.IsMuted = true;
        undo.Clear();
        var before = Snapshot(graph);
        var stepId = step.Id;

        using (undo.Begin("Delete node"))
        {
            graph.RemoveNode(step);
        }

        Assert.DoesNotContain(graph.Nodes, n => n.Id == stepId);
        Assert.Empty(graph.Connections);
        Assert.Equal(1, undo.UndoCount);

        undo.Undo();

        Assert.Same(step, graph.Nodes.Single(n => n.Id == stepId));
        Assert.Equal(2, graph.Connections.Count);
        Assert.True(step.IsMuted);
        Assert.Equal(4d, step.InPorts[1].UserValue);
        Assert.Equal(before, Snapshot(graph));

        undo.Redo();
        Assert.DoesNotContain(graph.Nodes, n => n.Id == stepId);
        undo.Undo();
        Assert.Equal(before, Snapshot(graph));
    }

    [Fact]
    public void ConnectReplacingAWireRestoresTheOldOneOnUndo()
    {
        var (graph, undo, _) = Setup();
        var a = ZT.Value(graph, 1d);
        var b = ZT.Value(graph, 2d);
        var sqrt = ZT.Node("Sqrt");
        graph.AddNode(sqrt);
        ZT.Wire(graph, a, 0, sqrt, 0);
        undo.Clear();

        using (undo.Begin("Connect"))
        {
            ZT.Wire(graph, b, 0, sqrt, 0);
        }

        Assert.Same(b, graph.FindConnectionInto(sqrt.InPorts[0])!.SourceNode);
        undo.Undo();
        Assert.Same(a, graph.FindConnectionInto(sqrt.InPorts[0])!.SourceNode);
        undo.Redo();
        Assert.Same(b, graph.FindConnectionInto(sqrt.InPorts[0])!.SourceNode);
    }

    [Fact]
    public void DisconnectUndoKeepsTheSameConnectionInstanceAndMute()
    {
        var (graph, undo, _) = Setup();
        var a = ZT.Value(graph, 1d);
        var sqrt = ZT.Node("Sqrt");
        graph.AddNode(sqrt);
        ZT.Wire(graph, a, 0, sqrt, 0);
        var wire = graph.Connections.Single();
        graph.SetConnectionMuted(wire, true);
        undo.Clear();

        graph.Disconnect(wire);
        Assert.False(sqrt.InPorts[0].UsingDefaultValue && !sqrt.InPorts[0].HasDefault);
        undo.Undo();

        Assert.Same(wire, graph.Connections.Single());
        Assert.True(wire.IsMuted);
    }

    [Fact]
    public void MoveIsRecordedOncePerDragAndUndoesToTheStart()
    {
        var (graph, undo, _) = Setup();
        var node = new RerouteNode { X = 10, Y = 20 };
        graph.AddNode(node);
        undo.Clear();

        using (undo.Begin("Move"))
        {
            for (var i = 1; i <= 30; i++)
            {
                node.X = 10 + i;
                node.Y = 20 + i * 2;
            }
        }

        Assert.Equal(1, undo.UndoCount);
        undo.Undo();
        Assert.Equal(10, node.X);
        Assert.Equal(20, node.Y);
        undo.Redo();
        Assert.Equal(40, node.X);
        Assert.Equal(80, node.Y);
    }

    [Fact]
    public void RenameFreezeMuteAndLacingAreUndoable()
    {
        var (graph, undo, _) = Setup();
        var node = ZT.Node("Add");
        graph.AddNode(node);
        undo.Clear();
        var original = node.Name;

        node.Name = "Renamed";
        undo.Undo();
        Assert.Equal(original, node.Name);

        node.IsFrozen = true;
        Assert.Equal("Freeze", undo.UndoLabel);
        undo.Undo();
        Assert.False(node.IsFrozen);

        node.IsMuted = true;
        Assert.Equal("Mute", undo.UndoLabel);
        undo.Undo();
        Assert.False(node.IsMuted);

        node.Lacing = LacingMode.CrossProduct;
        undo.Undo();
        Assert.Equal(LacingMode.Auto, node.Lacing);
        undo.Redo();
        Assert.Equal(LacingMode.CrossProduct, node.Lacing);
    }

    [Fact]
    public void PinnedInputValuesAreUndoableAndCoalesce()
    {
        var (graph, undo, _) = Setup();
        var node = ZT.Node("AddStep");
        graph.AddNode(node);
        undo.Clear();
        var port = node.InPorts[1];

        for (var i = 1; i <= 20; i++)
        {
            port.SetUserValue((double)i);
        }

        Assert.Equal(1, undo.UndoCount);
        undo.Undo();
        Assert.False(port.HasUserValue);
        undo.Redo();
        Assert.Equal(20d, port.UserValue);
        port.ClearUserValue();
        undo.Undo();
        Assert.Equal(20d, port.UserValue);
    }

    [Fact]
    public void ListLevelsAndHiddenFlagsAreUndoable()
    {
        var (graph, undo, _) = Setup();
        var node = ZT.Node("Add");
        graph.AddNode(node);
        undo.Clear();

        node.InPorts[0].SetLevels(true, 2, true);
        undo.Undo();
        Assert.False(node.InPorts[0].UseLevels);
        Assert.Equal(-1, node.InPorts[0].Level);

        node.InPorts[1].IsHidden = true;
        undo.Undo();
        Assert.False(node.InPorts[1].IsHidden);
        undo.Redo();
        Assert.True(node.InPorts[1].IsHidden);
    }

    [Fact]
    public void InputNodeDataPropertiesAreTracked()
    {
        var (graph, undo, _) = Setup();
        var number = new NumberInputNode();
        graph.AddNode(number);
        undo.Clear();

        number.Value = 42d;
        undo.Undo();
        Assert.Equal(0d, number.Value);
        undo.Redo();
        Assert.Equal(42d, number.Value);
    }

    [Fact]
    public void NotesAndGroupsAreUndoable()
    {
        var (graph, undo, _) = Setup();
        var note = new NoteModel { Text = "hello" };
        graph.Notes.Add(note);
        undo.Undo();
        Assert.Empty(graph.Notes);
        undo.Redo();
        Assert.Same(note, graph.Notes.Single());

        undo.Clear();
        note.Text = "changed";
        undo.Undo();
        Assert.Equal("hello", note.Text);

        var group = new GroupModel { Title = "G" };
        graph.Groups.Add(group);
        undo.Clear();
        graph.Groups.Remove(group);
        undo.Undo();
        Assert.Same(group, graph.Groups.Single());
        group.Width = 500;
        undo.Undo();
        Assert.NotEqual(500, group.Width);
    }

    [Fact]
    public void RunsAreNotRecorded()
    {
        var (graph, undo, _) = Setup();
        var watch = new WatchNode();
        graph.AddNode(watch);
        var src = ZT.Value(graph, 5d);
        ZT.Wire(graph, src, 0, watch, 0);
        undo.Clear();

        using (undo.Suspend())
        {
            new GraphEngine().Run(graph);
        }

        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void UndoRedoRaiseModifiedSoAutoRunFollows()
    {
        var (graph, undo, _) = Setup();
        var node = ZT.Node("Sqrt");
        graph.AddNode(node);
        undo.Clear();
        node.IsFrozen = true;
        var modified = 0;
        graph.Modified += (s, e) => modified++;
        undo.Undo();
        undo.Redo();
        Assert.True(modified >= 2);
    }

    [Fact]
    public void FullEditingSessionRoundTripsToTheOriginalGraphBytes()
    {
        var (graph, undo, _) = Setup();
        var a = ZT.Value(graph, 2d);
        var b = ZT.Node("AddStep");
        graph.AddNode(b);
        var c = ZT.Node("Sqrt");
        graph.AddNode(c);
        ZT.Wire(graph, a, 0, b, 0);
        undo.Clear();
        var start = Snapshot(graph);

        b.X = 100;
        b.InPorts[1].SetUserValue(9d);
        ZT.Wire(graph, b, 0, c, 0);
        graph.Notes.Add(new NoteModel { Text = "n" });
        graph.RemoveNode(a);
        c.IsMuted = true;

        while (undo.CanUndo)
        {
            undo.Undo();
        }

        Assert.Equal(start, Snapshot(graph));
        var end = new List<string>();
        while (undo.CanRedo)
        {
            undo.Redo();
        }

        Assert.Equal(1, graph.Notes.Count);
        Assert.DoesNotContain(graph.Nodes, n => ReferenceEquals(n, a));
        Assert.True(c.IsMuted);
    }

    [Fact]
    public void DisposedRecorderStopsRecording()
    {
        var (graph, undo, recorder) = Setup();
        recorder.Dispose();
        graph.AddNode(new RerouteNode());
        Assert.False(undo.CanUndo);
    }
}
