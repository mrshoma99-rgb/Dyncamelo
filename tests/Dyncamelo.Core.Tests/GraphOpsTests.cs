using System.Linq;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Core.Serialization;
using Dyncamelo.Core.Tests.Fixtures;
using Xunit;

namespace Dyncamelo.Core.Tests;

public class GraphOpsTests
{
    private static ZeroTouchNodeModel Add(GraphModel graph, string method, double x = 0, double y = 0)
    {
        var node = ZT.Node(method);
        node.X = x;
        node.Y = y;
        graph.AddNode(node);
        return node;
    }

    [Fact]
    public void RankOrdersCompatibilities()
    {
        Assert.True(GraphOps.Rank(Compat.Exact) < GraphOps.Rank(Compat.Convertible));
        Assert.True(GraphOps.Rank(Compat.Convertible) < GraphOps.Rank(Compat.Loose));
        Assert.True(GraphOps.Rank(Compat.Loose) < GraphOps.Rank(Compat.No));
    }

    [Fact]
    public void BestInputPrefersATypeMatchAndSkipsWiredInputsWhenAsked()
    {
        var graph = new GraphModel();
        var number = Add(graph, "Sqrt");           // double -> double
        var shout = Add(graph, "Shout");           // string -> string
        var join = Add(graph, "Add");              // (double, double) -> double

        Assert.Same(join.InPorts[0], GraphOps.BestInputFor(graph, number.OutPorts[0], join, freeOnly: true));
        ZT.Wire(graph, number, 0, join, 0);
        var other = Add(graph, "Sqrt");
        Assert.Same(join.InPorts[1], GraphOps.BestInputFor(graph, other.OutPorts[0], join, freeOnly: true));
        Assert.Same(join.InPorts[0], GraphOps.BestInputFor(graph, other.OutPorts[0], join, freeOnly: false));

        // A number output can only reach a text input loosely; refusing loose matches finds nothing.
        Assert.Null(GraphOps.BestInputFor(graph, number.OutPorts[0], shout, freeOnly: true, worstAcceptable: 1));
    }

    [Fact]
    public void InsertOnWireRoutesTheWireThroughTheNode()
    {
        var graph = new GraphModel();
        var source = Add(graph, "Sqrt", 0);
        var sink = Add(graph, "Sqrt", 400);
        ZT.Wire(graph, source, 0, sink, 0);
        var wire = graph.Connections.Single();
        var middle = Add(graph, "Sqrt", 200);

        Assert.True(GraphOps.CanInsert(graph, middle, wire, out var input, out var output));
        Assert.Same(middle.InPorts[0], input);
        Assert.Same(middle.OutPorts[0], output);

        Assert.True(GraphOps.InsertOnWire(graph, middle, wire));

        Assert.Equal(2, graph.Connections.Count);
        Assert.DoesNotContain(wire, graph.Connections);
        Assert.Same(source, graph.FindConnectionInto(middle.InPorts[0])!.SourceNode);
        Assert.Same(middle, graph.FindConnectionInto(sink.InPorts[0])!.SourceNode);
    }

    [Fact]
    public void InsertOnWireRefusesNodesThatCannotBridgeTheWire()
    {
        var graph = new GraphModel();
        var source = Add(graph, "Sqrt");
        var sink = Add(graph, "Sqrt", 300);
        ZT.Wire(graph, source, 0, sink, 0);
        var wire = graph.Connections.Single();

        var noInputs = Add(graph, "Answer", 100);        // nothing to receive the wire
        Assert.False(GraphOps.InsertOnWire(graph, noInputs, wire));

        var alreadyWired = Add(graph, "Sqrt", 100);
        var upstream = Add(graph, "Sqrt", -100);
        ZT.Wire(graph, upstream, 0, alreadyWired, 0);
        Assert.False(GraphOps.CanInsert(graph, alreadyWired, wire, out _, out _));

        Assert.False(GraphOps.CanInsert(graph, source, wire, out _, out _));   // an endpoint of the wire itself
        Assert.Same(wire, graph.Connections.Single(c => c.SourceNode == source));
    }

    [Fact]
    public void InsertOnWireIsOneUndoStep()
    {
        var graph = new GraphModel();
        var undo = new UndoManager();
        using var recorder = new GraphRecorder(graph, undo);
        var source = Add(graph, "Sqrt", 0);
        var sink = Add(graph, "Sqrt", 400);
        ZT.Wire(graph, source, 0, sink, 0);
        var middle = Add(graph, "Sqrt", 200);
        undo.Clear();
        var before = Snapshot(graph);

        using (undo.Begin("Insert on wire"))
        {
            Assert.True(GraphOps.InsertOnWire(graph, middle, graph.Connections.Single()));
        }

        Assert.Equal(1, undo.UndoCount);
        undo.Undo();
        Assert.Equal(before, Snapshot(graph));
        Assert.Single(graph.Connections);
    }

    [Fact]
    public void DissolveBridgesTheDataAroundTheNode()
    {
        var graph = new GraphModel();
        var source = Add(graph, "Sqrt", 0);
        var middle = Add(graph, "Sqrt", 200);
        var sinkA = Add(graph, "Sqrt", 400, 0);
        var sinkB = Add(graph, "Sqrt", 400, 100);
        ZT.Wire(graph, source, 0, middle, 0);
        ZT.Wire(graph, middle, 0, sinkA, 0);
        ZT.Wire(graph, middle, 0, sinkB, 0);

        var bridged = GraphOps.DissolveNode(graph, middle);

        Assert.Equal(2, bridged);
        Assert.DoesNotContain(middle, graph.Nodes);
        Assert.Equal(2, graph.Connections.Count);
        Assert.Same(source, graph.FindConnectionInto(sinkA.InPorts[0])!.SourceNode);
        Assert.Same(source, graph.FindConnectionInto(sinkB.InPorts[0])!.SourceNode);
    }

    [Fact]
    public void DissolveWithNothingFeedingJustDropsTheNode()
    {
        var graph = new GraphModel();
        var middle = Add(graph, "Sqrt", 200);
        var sink = Add(graph, "Sqrt", 400);
        ZT.Wire(graph, middle, 0, sink, 0);

        Assert.Equal(0, GraphOps.DissolveNode(graph, middle));
        Assert.Empty(graph.Connections);
        Assert.Single(graph.Nodes);
    }

    [Fact]
    public void DissolveIsOneUndoStepThatRestoresEverything()
    {
        var graph = new GraphModel();
        var undo = new UndoManager();
        using var recorder = new GraphRecorder(graph, undo);
        var source = Add(graph, "Sqrt", 0);
        var middle = Add(graph, "Sqrt", 200);
        var sink = Add(graph, "Sqrt", 400);
        ZT.Wire(graph, source, 0, middle, 0);
        ZT.Wire(graph, middle, 0, sink, 0);
        undo.Clear();
        var before = Snapshot(graph);

        using (undo.Begin("Delete and reconnect"))
        {
            GraphOps.DissolveNode(graph, middle);
        }

        Assert.Equal(1, undo.UndoCount);
        undo.Undo();
        Assert.Equal(before, Snapshot(graph));
    }

    [Fact]
    public void AutoConnectChainsNodesLeftToRight()
    {
        var graph = new GraphModel();
        var a = Add(graph, "Sqrt", 0);
        var c = Add(graph, "Sqrt", 400);
        var b = Add(graph, "Sqrt", 200);

        var made = GraphOps.AutoConnect(graph, new NodeModel[] { c, a, b });

        Assert.Equal(2, made);
        Assert.Same(a, graph.FindConnectionInto(b.InPorts[0])!.SourceNode);
        Assert.Same(b, graph.FindConnectionInto(c.InPorts[0])!.SourceNode);
    }

    [Fact]
    public void AutoConnectLeavesWiredInputsAlone()
    {
        var graph = new GraphModel();
        var first = Add(graph, "Sqrt", 0);
        var second = Add(graph, "Sqrt", 200);
        var existing = Add(graph, "Sqrt", -200);
        ZT.Wire(graph, existing, 0, second, 0);

        Assert.Equal(0, GraphOps.AutoConnect(graph, new NodeModel[] { first, second }));
        Assert.Same(existing, graph.FindConnectionInto(second.InPorts[0])!.SourceNode);
    }

    [Fact]
    public void MakeRoomShiftsOnlyWhatIsInTheWay()
    {
        var graph = new GraphModel();
        var source = Add(graph, "Sqrt", 0);
        var sink = Add(graph, "Sqrt", 250);
        var farther = Add(graph, "Sqrt", 500);
        var unrelated = Add(graph, "Sqrt", 260, 300);
        ZT.Wire(graph, source, 0, sink, 0);
        ZT.Wire(graph, sink, 0, farther, 0);
        var middle = Add(graph, "Sqrt", 200);

        var moves = GraphOps.MakeRoom(graph, middle, _ => 100d)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // Only nodes downstream of the inserted one move; none is connected yet, so nothing is downstream.
        Assert.Empty(moves);

        Assert.True(GraphOps.InsertOnWire(graph, middle, graph.Connections.First(c => c.SourceNode == source)));
        moves = GraphOps.MakeRoom(graph, middle, _ => 100d).ToDictionary(kv => kv.Key, kv => kv.Value);

        var shift = 200 + 100 + GraphOps.InsertGap - 250;
        Assert.Equal(250 + shift, moves[sink], 6);
        Assert.Equal(500 + shift, moves[farther], 6);
        Assert.DoesNotContain(unrelated, moves.Keys);
        Assert.DoesNotContain(middle, moves.Keys);
    }

    [Fact]
    public void MakeRoomDoesNothingWhenThereIsAlreadyASpace()
    {
        var graph = new GraphModel();
        var middle = Add(graph, "Sqrt", 0);
        var sink = Add(graph, "Sqrt", 1000);
        ZT.Wire(graph, middle, 0, sink, 0);
        Assert.Empty(GraphOps.MakeRoom(graph, middle, _ => 100d));
    }

    [Fact]
    public void FollowingLinksFindsUpstreamDownstreamAndSimilarNodes()
    {
        var graph = new GraphModel();
        var a = Add(graph, "Sqrt", 0);
        var b = Add(graph, "Sqrt", 200);
        var c = Add(graph, "Sqrt", 400);
        var stray = Add(graph, "Shout", 0, 200);
        ZT.Wire(graph, a, 0, b, 0);
        ZT.Wire(graph, b, 0, c, 0);

        Assert.Equal(new[] { b, c }.OrderBy(n => n.X), GraphOps.Downstream(graph, new[] { b }).OrderBy(n => n.X));
        Assert.Equal(new[] { a, b }.OrderBy(n => n.X), GraphOps.Upstream(graph, new[] { b }).OrderBy(n => n.X));
        Assert.Equal(3, GraphOps.Similar(graph, new[] { a }).Count);
        Assert.DoesNotContain(stray, GraphOps.Similar(graph, new[] { a }));
    }

    [Fact]
    public void WireMuteIsUndoable()
    {
        var graph = new GraphModel();
        var undo = new UndoManager();
        using var recorder = new GraphRecorder(graph, undo);
        var a = Add(graph, "Sqrt", 0);
        var b = Add(graph, "Sqrt", 200);
        ZT.Wire(graph, a, 0, b, 0);
        var wire = graph.Connections.Single();
        undo.Clear();

        graph.SetConnectionMuted(wire, true);
        Assert.True(wire.IsMuted);
        Assert.Equal(1, undo.UndoCount);

        undo.Undo();
        Assert.False(wire.IsMuted);
        undo.Redo();
        Assert.True(wire.IsMuted);
    }

    private static string Snapshot(GraphModel graph)
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterDefinition(ZT.Definition("Sqrt"));
        return new GraphSerializer(registry).Serialize(graph);
    }
}
