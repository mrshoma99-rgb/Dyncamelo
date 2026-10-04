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

/// <summary>A hand-written node with a multi-input port, for tests that do not need the loader.</summary>
public sealed class CollectorNode : NodeModel
{
    public CollectorNode()
    {
        Name = "Collector";
        AddMultiInput("items", typeof(IList<object>));
        AddOutput("result", typeof(IList<object>));
    }

    public override string NodeType => "TestCollector";

    public override object?[] Evaluate(object?[] inputs, EvaluationContext context) => new object?[] { inputs[0] };
}

public class MultiInputTests
{
    private static ValueNode Source(GraphModel graph, object? value)
    {
        var node = new ValueNode { Value = value };
        graph.AddNode(node);
        return node;
    }

    private static NodeModel Gatherer(GraphModel graph)
    {
        var node = ZT.Node("Gather");
        graph.AddNode(node);
        return node;
    }

    private static object? Run(GraphModel graph, NodeModel node)
    {
        new GraphEngine().Run(graph);
        return node.OutPorts[0].Value;
    }

    // ----- loading -----------------------------------------------------------------------

    [Fact]
    public void TheAttributeMakesAListParameterMultiInput()
    {
        Assert.True(ZT.Node("SumAll").InPorts[0].IsMultiInput);
        Assert.True(ZT.Node("Gather").InPorts[0].IsMultiInput);
        Assert.False(ZT.Node("Sum").InPorts[0].IsMultiInput);
    }

    [Fact]
    public void TheAttributeIsIgnoredOnAParameterThatIsNotAList()
    {
        Assert.False(ZT.Node("NotAList").InPorts[0].IsMultiInput);
    }

    [Fact]
    public void AMultiInputPortMustBeListTyped()
    {
        Assert.Throws<ArgumentException>(() => new BadCollector());
    }

    private sealed class BadCollector : NodeModel
    {
        public BadCollector()
        {
            AddMultiInput("x", typeof(double));
        }

        public override string NodeType => "TestBad";

        public override object?[] Evaluate(object?[] inputs, EvaluationContext context) => new object?[0];
    }

    // ----- wiring ------------------------------------------------------------------------

    [Fact]
    public void AMultiInputPortTakesManyWiresInTheOrderTheyWereMade()
    {
        var graph = new GraphModel();
        var target = Gatherer(graph);
        var a = Source(graph, 1);
        var b = Source(graph, 2);
        var c = Source(graph, 3);

        Assert.True(graph.Connect(b.OutPorts[0], target.InPorts[0]).Success);
        Assert.True(graph.Connect(c.OutPorts[0], target.InPorts[0]).Success);
        Assert.True(graph.Connect(a.OutPorts[0], target.InPorts[0]).Success);

        var wires = graph.FindConnectionsInto(target.InPorts[0]);
        Assert.Equal(new[] { b.OutPorts[0], c.OutPorts[0], a.OutPorts[0] }, wires.Select(w => w.Source));
    }

    [Fact]
    public void ASingleWireInputStillReplacesItsWire()
    {
        var graph = new GraphModel();
        var target = ZT.Node("Sum");
        graph.AddNode(target);
        var a = Source(graph, new List<double> { 1 });
        var b = Source(graph, new List<double> { 2 });

        Assert.True(graph.Connect(a.OutPorts[0], target.InPorts[0]).Success);
        Assert.True(graph.Connect(b.OutPorts[0], target.InPorts[0]).Success);

        var wire = Assert.Single(graph.FindConnectionsInto(target.InPorts[0]));
        Assert.Equal(b.OutPorts[0], wire.Source);
    }

    [Fact]
    public void TheSameOutputCannotBeWiredIntoAMultiInputTwice()
    {
        var graph = new GraphModel();
        var target = Gatherer(graph);
        var a = Source(graph, 1);

        Assert.True(graph.Connect(a.OutPorts[0], target.InPorts[0]).Success);
        var second = graph.Connect(a.OutPorts[0], target.InPorts[0]);

        Assert.False(second.Success);
        Assert.Contains("already", second.Message);
        Assert.Single(graph.Connections);
    }

    [Fact]
    public void ACycleIsStillRefusedThroughAMultiInput()
    {
        var graph = new GraphModel();
        var head = new CollectorNode();
        var tail = new CollectorNode();
        graph.AddNode(head);
        graph.AddNode(tail);
        Assert.True(graph.Connect(head.OutPorts[0], tail.InPorts[0]).Success);

        var back = graph.Connect(tail.OutPorts[0], head.InPorts[0]);

        Assert.False(back.Success);
        Assert.Contains("cycle", back.Message);
    }

    [Fact]
    public void RemovingWiresKeepsTheDefaultOffUntilTheLastOneGoes()
    {
        var graph = new GraphModel();
        var target = ZT.Node("SumAll");
        graph.AddNode(target);
        var port = target.InPorts[0];
        port.HasDefault = false;
        var a = Source(graph, 1d);
        var b = Source(graph, 2d);
        var first = graph.Connect(a.OutPorts[0], port).Connection!;
        var second = graph.Connect(b.OutPorts[0], port).Connection!;

        graph.Disconnect(first);
        Assert.Single(graph.FindConnectionsInto(port));
        Assert.Same(second, graph.FindConnectionInto(port));
    }

    [Fact]
    public void ARemovedWireGoesBackToItsPlaceAmongTheOthers()
    {
        var graph = new GraphModel();
        var target = Gatherer(graph);
        var wires = Enumerable.Range(1, 3).Select(i => graph.Connect(Source(graph, i).OutPorts[0], target.InPorts[0]).Connection!).ToList();

        graph.Disconnect(wires[1]);
        Assert.True(graph.ReinsertConnection(wires[1]));

        Assert.Equal(wires, graph.FindConnectionsInto(target.InPorts[0]));
        Assert.False(graph.ReinsertConnection(wires[1]));    // already there
    }

    [Fact]
    public void ANewWireCanTakeTheOrderOfOneItReplaces()
    {
        var graph = new GraphModel();
        var target = Gatherer(graph);
        var wires = Enumerable.Range(1, 3).Select(i => graph.Connect(Source(graph, i).OutPorts[0], target.InPorts[0]).Connection!).ToList();
        var replacement = Source(graph, 99);

        var sequence = wires[1].SequenceForTests();
        graph.Disconnect(wires[1]);
        var made = graph.Connect(replacement.OutPorts[0], target.InPorts[0], sequence).Connection!;

        var order = graph.FindConnectionsInto(target.InPorts[0]);
        Assert.Equal(new[] { wires[0], made, wires[2] }, order);
        Assert.Equal(99, Run(graph, target) is List<object> list ? list[1] : null);
    }

    // ----- evaluation --------------------------------------------------------------------

    [Fact]
    public void ManyWiresArriveAsOneCombinedListInWireOrder()
    {
        var graph = new GraphModel();
        var target = Gatherer(graph);
        graph.Connect(Source(graph, "a").OutPorts[0], target.InPorts[0]);
        graph.Connect(Source(graph, new List<object> { "b", "c" }).OutPorts[0], target.InPorts[0]);
        graph.Connect(Source(graph, "d").OutPorts[0], target.InPorts[0]);

        Assert.Equal(new List<object> { "a", "b", "c", "d" }, Run(graph, target));
    }

    [Fact]
    public void OneWireIsPassedThroughUntouched()
    {
        var graph = new GraphModel();
        var target = Gatherer(graph);
        var nested = new List<object> { new List<object> { 1, 2 }, new List<object> { 3 } };
        graph.Connect(Source(graph, nested).OutPorts[0], target.InPorts[0]);

        // Not flattened: a single wire behaves exactly as it did before the port became multi-input.
        var result = Assert.IsType<List<object>>(Run(graph, target));
        Assert.Equal(2, result.Count);
        Assert.IsType<List<object>>(result[0]);
    }

    [Fact]
    public void WiresContributeNothingWhenTheyCarryNull()
    {
        var graph = new GraphModel();
        var target = Gatherer(graph);
        graph.Connect(Source(graph, 1).OutPorts[0], target.InPorts[0]);
        graph.Connect(Source(graph, null).OutPorts[0], target.InPorts[0]);
        graph.Connect(Source(graph, 2).OutPorts[0], target.InPorts[0]);

        Assert.Equal(new List<object> { 1, 2 }, Run(graph, target));
    }

    [Fact]
    public void ANumericMultiInputSumsEveryWire()
    {
        var graph = new GraphModel();
        var target = ZT.Node("SumAll");
        graph.AddNode(target);
        graph.Connect(Source(graph, 1d).OutPorts[0], target.InPorts[0]);
        graph.Connect(Source(graph, new List<double> { 2, 3 }).OutPorts[0], target.InPorts[0]);
        graph.Connect(Source(graph, 4d).OutPorts[0], target.InPorts[0]);

        Assert.Equal(10d, Run(graph, target));
    }

    [Fact]
    public void AMutedWireDropsOutOfTheCombination()
    {
        var graph = new GraphModel();
        var target = Gatherer(graph);
        graph.Connect(Source(graph, "a").OutPorts[0], target.InPorts[0]);
        var muted = graph.Connect(Source(graph, "b").OutPorts[0], target.InPorts[0]).Connection!;
        graph.Connect(Source(graph, "c").OutPorts[0], target.InPorts[0]);

        graph.SetConnectionMuted(muted, true);

        Assert.Equal(new List<object> { "a", "c" }, Run(graph, target));
    }

    [Fact]
    public void WithEveryWireMutedThePortIsUnconnected()
    {
        var graph = new GraphModel();
        var target = Gatherer(graph);
        var only = graph.Connect(Source(graph, "a").OutPorts[0], target.InPorts[0]).Connection!;
        graph.SetConnectionMuted(only, true);

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Idle, target.State);
        Assert.Contains(target.Messages, m => m.Text.Contains("not connected"));
    }

    [Fact]
    public void ANodeWithNoWireOnAMultiInputDoesNotRun()
    {
        var graph = new GraphModel();
        var target = Gatherer(graph);

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Idle, target.State);
    }

    [Fact]
    public void AFailedUpstreamNodeOnAnyWireStopsTheConsumer()
    {
        var graph = new GraphModel();
        var target = Gatherer(graph);
        var ok = Source(graph, 1);
        var bad = new SelfReportedErrorNode();
        graph.AddNode(bad);
        graph.Connect(ok.OutPorts[0], target.InPorts[0]);
        graph.Connect(bad.OutPorts[0], target.InPorts[0]);

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Warning, target.State);
    }

    [Fact]
    public void ChangingOneSourceRerunsTheConsumer()
    {
        var graph = new GraphModel();
        var target = Gatherer(graph);
        var a = Source(graph, 1);
        var b = Source(graph, 2);
        graph.Connect(a.OutPorts[0], target.InPorts[0]);
        graph.Connect(b.OutPorts[0], target.InPorts[0]);
        Assert.Equal(new List<object> { 1, 2 }, Run(graph, target));

        b.Value = 20;

        Assert.Equal(new List<object> { 1, 20 }, Run(graph, target));
    }

    // ----- combining ---------------------------------------------------------------------

    [Fact]
    public void CombineFlattensOneLevelAndKeepsStringsAndDictionariesWhole()
    {
        var dictionary = new Dictionary<string, object> { ["k"] = 1 };
        var combined = MultiInput.Combine(new object?[]
        {
            "text", new object[] { 1, new List<object> { 2, 3 } }, dictionary, null, 5,
        });

        Assert.Equal(5, combined.Count);
        Assert.Equal("text", combined[0]);
        Assert.Equal(1, combined[1]);
        Assert.IsType<List<object>>(combined[2]);        // one level only
        Assert.Same(dictionary, combined[3]);
        Assert.Equal(5, combined[4]);
    }

    // ----- persistence -------------------------------------------------------------------

    private static NodeRegistry Registry()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterDefinitions(ZT.All);
        registry.RegisterNodeType("TestValue", () => new ValueNode());
        return registry;
    }

    [Fact]
    public void ManyWiresAndTheirOrderSurviveASaveAndLoad()
    {
        var graph = new GraphModel();
        var target = Gatherer(graph);
        var sources = Enumerable.Range(0, 4).Select(i => Source(graph, i)).ToList();
        foreach (var index in new[] { 2, 0, 3, 1 })
        {
            graph.Connect(sources[index].OutPorts[0], target.InPorts[0]);
        }

        var serializer = new GraphSerializer(Registry());
        var loaded = serializer.Deserialize(serializer.Serialize(graph));

        var loadedTarget = loaded.Nodes.Single(n => n is ZeroTouchNodeModel);
        var loadedWires = loaded.FindConnectionsInto(loadedTarget.InPorts[0]);
        Assert.Equal(4, loadedWires.Count);
        Assert.Equal(new[] { sources[2].Id, sources[0].Id, sources[3].Id, sources[1].Id }, loadedWires.Select(w => w.SourceNode.Id));
    }

    [Fact]
    public void ANodeThatCannotBeResolvedKeepsBothWiresIntoOneInput()
    {
        var graph = new GraphModel();
        var a = Source(graph, 1);
        var b = Source(graph, 2);
        var unknown = new Newtonsoft.Json.Linq.JObject
        {
            ["Id"] = Guid.NewGuid().ToString("N"),
            ["NodeType"] = "SomethingFromANewerVersion",
            ["Name"] = "Newer",
            ["InputPorts"] = new Newtonsoft.Json.Linq.JArray(new Newtonsoft.Json.Linq.JObject { ["Name"] = "items" }),
        };
        var serializer = new GraphSerializer(Registry());
        var root = Newtonsoft.Json.Linq.JObject.Parse(serializer.Serialize(graph));
        ((Newtonsoft.Json.Linq.JArray)root["Nodes"]!).Add(unknown);
        var connectors = (Newtonsoft.Json.Linq.JArray)(root["Connectors"] ?? (root["Connectors"] = new Newtonsoft.Json.Linq.JArray()));
        foreach (var source in new[] { a, b })
        {
            connectors.Add(new Newtonsoft.Json.Linq.JObject
            {
                ["Id"] = Guid.NewGuid().ToString("N"),
                ["FromNode"] = source.Id.ToString("N"),
                ["FromPort"] = "value",
                ["ToNode"] = unknown.Value<string>("Id"),
                ["ToPort"] = "items",
            });
        }

        var loaded = serializer.Deserialize(root.ToString());

        var missing = loaded.Nodes.OfType<MissingNodeModel>().Single();
        Assert.Equal(2, loaded.FindConnectionsInto(missing.InPorts[0]).Count);
        Assert.Equal(2, serializer.Serialize(loaded).Split(new[] { "\"ToPort\": \"items\"" }, StringSplitOptions.None).Length - 1);
    }

    // ----- copy / paste ------------------------------------------------------------------

    [Fact]
    public void DuplicatingNodesKeepsEveryWireBetweenThemInOrder()
    {
        var graph = new GraphModel();
        var target = Gatherer(graph);
        var sources = Enumerable.Range(0, 3).Select(i => Source(graph, i)).ToList();
        foreach (var source in sources)
        {
            graph.Connect(source.OutPorts[0], target.InPorts[0]);
        }

        var serializer = new GraphSerializer(Registry());
        var fragment = serializer.SerializeFragment(graph.Nodes.ToList());
        var pasted = serializer.PasteFragment(graph, fragment, 40, 40);

        var copy = pasted.Single(n => n is ZeroTouchNodeModel);
        Assert.Equal(3, graph.FindConnectionsInto(copy.InPorts[0]).Count);
    }
}

internal static class ConnectionTestExtensions
{
    // The wire's creation order, which is internal to Core.
    public static int SequenceForTests(this ConnectionModel connection) => connection.Sequence;
}

public class MultiInputEditingTests
{
    private static ValueNode Source(GraphModel graph, object? value)
    {
        var node = new ValueNode { Value = value };
        graph.AddNode(node);
        return node;
    }

    private static CollectorNode Sink(GraphModel graph)
    {
        var node = new CollectorNode();
        graph.AddNode(node);
        return node;
    }

    private static List<NodeModel> Order(GraphModel graph, PortModel port) =>
        graph.FindConnectionsInto(port).Select(w => w.SourceNode).ToList();

    [Fact]
    public void AutoConnectKeepsAddingToAMultiInputAndPrefersAFreeSingleInput()
    {
        var graph = new GraphModel();
        var a = Source(graph, 1);
        var b = Source(graph, 2);
        var sink = Sink(graph);

        Assert.Equal(sink.InPorts[0], GraphOps.BestInputFor(graph, a.OutPorts[0], sink, freeOnly: true));
        graph.Connect(a.OutPorts[0], sink.InPorts[0]);

        // Still available to a different output, but not to the one that is already wired in.
        Assert.Equal(sink.InPorts[0], GraphOps.BestInputFor(graph, b.OutPorts[0], sink, freeOnly: true));
        Assert.Null(GraphOps.BestInputFor(graph, a.OutPorts[0], sink, freeOnly: true));
    }

    [Fact]
    public void InsertingANodeOnAWireIntoAMultiInputKeepsTheWiresPlace()
    {
        var graph = new GraphModel();
        var a = Source(graph, 1);
        var b = Source(graph, 2);
        var c = Source(graph, 3);
        var sink = Sink(graph);
        graph.Connect(a.OutPorts[0], sink.InPorts[0]);
        var middle = graph.Connect(b.OutPorts[0], sink.InPorts[0]).Connection!;
        graph.Connect(c.OutPorts[0], sink.InPorts[0]);
        var inserted = new CollectorNode();
        graph.AddNode(inserted);

        Assert.True(GraphOps.InsertOnWire(graph, inserted, middle));

        Assert.Equal(new NodeModel[] { a, inserted, c }, Order(graph, sink.InPorts[0]));
        Assert.Equal(new NodeModel[] { b }, Order(graph, inserted.InPorts[0]));
    }

    [Fact]
    public void DissolvingANodeFeedingAMultiInputPutsItsFeedersInItsPlace()
    {
        var graph = new GraphModel();
        var a = Source(graph, 1);
        var x = Source(graph, 2);
        var y = Source(graph, 3);
        var c = Source(graph, 4);
        var middle = Sink(graph);
        var sink = Sink(graph);
        graph.Connect(x.OutPorts[0], middle.InPorts[0]);
        graph.Connect(y.OutPorts[0], middle.InPorts[0]);
        graph.Connect(a.OutPorts[0], sink.InPorts[0]);
        graph.Connect(middle.OutPorts[0], sink.InPorts[0]);
        graph.Connect(c.OutPorts[0], sink.InPorts[0]);

        var bridged = GraphOps.DissolveNode(graph, middle);

        Assert.Equal(2, bridged);
        Assert.Equal(new NodeModel[] { a, x, y, c }, Order(graph, sink.InPorts[0]));
    }

    [Fact]
    public void SwappingTwoWiresOfOneMultiInputSwapsTheirOrder()
    {
        var graph = new GraphModel();
        var sources = Enumerable.Range(0, 3).Select(i => Source(graph, i)).ToList();
        var sink = Sink(graph);
        var wires = sources.Select(s => graph.Connect(s.OutPorts[0], sink.InPorts[0]).Connection!).ToList();

        Assert.True(GraphOps.SwapLinks(graph, wires[0], wires[2]));

        Assert.Equal(new NodeModel[] { sources[2], sources[1], sources[0] }, Order(graph, sink.InPorts[0]));
    }

    [Fact]
    public void ASwapThatWouldDuplicateAWireIsRefusedAndNothingChanges()
    {
        var graph = new GraphModel();
        var a = Source(graph, 1);
        var b = Source(graph, 2);
        var c = Source(graph, 3);
        var multi = Sink(graph);
        var otherSink = Sink(graph);
        graph.Connect(a.OutPorts[0], multi.InPorts[0]);
        var second = graph.Connect(b.OutPorts[0], multi.InPorts[0]).Connection!;
        graph.Connect(c.OutPorts[0], multi.InPorts[0]);
        var elsewhere = graph.Connect(a.OutPorts[0], otherSink.InPorts[0]).Connection!;

        // b→multi and a→otherSink swap destinations: a→multi already exists, so the swap is refused and nothing changes.
        Assert.False(GraphOps.SwapLinks(graph, second, elsewhere));
        Assert.Equal(new NodeModel[] { a, b, c }, Order(graph, multi.InPorts[0]));
        Assert.Equal(new NodeModel[] { a }, Order(graph, otherSink.InPorts[0]));
    }

    [Theory]
    [InlineData(0, 2, new[] { 1, 2, 0, 3 })]
    [InlineData(3, 0, new[] { 3, 0, 1, 2 })]
    [InlineData(1, 2, new[] { 0, 2, 1, 3 })]
    public void MovingAWireReordersTheOthersAroundIt(int from, int to, int[] expected)
    {
        var graph = new GraphModel();
        var sources = Enumerable.Range(0, 4).Select(i => Source(graph, i)).ToList();
        var sink = Sink(graph);
        var wires = sources.Select(s => graph.Connect(s.OutPorts[0], sink.InPorts[0]).Connection!).ToList();

        var moved = GraphOps.MoveWire(graph, wires[from], to);

        Assert.NotNull(moved);
        Assert.Equal(expected.Select(i => (NodeModel)sources[i]), Order(graph, sink.InPorts[0]));
    }

    [Fact]
    public void MovingAWireToWhereItIsDoesNothingAndSingleInputsRefuse()
    {
        var graph = new GraphModel();
        var a = Source(graph, 1);
        var b = Source(graph, 2);
        var sink = Sink(graph);
        var first = graph.Connect(a.OutPorts[0], sink.InPorts[0]).Connection!;
        graph.Connect(b.OutPorts[0], sink.InPorts[0]);

        Assert.Null(GraphOps.MoveWire(graph, first, 0));
        Assert.Same(first, graph.FindConnectionInto(sink.InPorts[0]));

        var single = ZT.Node("Sum");
        graph.AddNode(single);
        var wire = graph.Connect(a.OutPorts[0], single.InPorts[0]).Connection!;
        Assert.Null(GraphOps.MoveWire(graph, wire, 3));
    }

    [Fact]
    public void MovingAWireKeepsMutedWiresMuted()
    {
        var graph = new GraphModel();
        var sources = Enumerable.Range(0, 3).Select(i => Source(graph, i)).ToList();
        var sink = Sink(graph);
        var wires = sources.Select(s => graph.Connect(s.OutPorts[0], sink.InPorts[0]).Connection!).ToList();
        graph.SetConnectionMuted(wires[1], true);

        GraphOps.MoveWire(graph, wires[0], 2);

        var muted = graph.FindConnectionsInto(sink.InPorts[0]).Single(w => w.IsMuted);
        Assert.Equal(sources[1], muted.SourceNode);
    }
}
