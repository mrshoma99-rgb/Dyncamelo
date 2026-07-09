using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Tests.Fixtures;
using Xunit;

namespace Dyncamelo.Core.Tests;

public class ReplicationTests
{
    private readonly GraphEngine _engine = new GraphEngine();

    private static List<object?> AsList(object? value) => Assert.IsType<List<object?>>(value);

    private ZeroTouchNodeResult RunBinary(object? left, object? right, LacingMode lacing, string method = "Add")
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, left);
        var b = ZT.Value(graph, right);
        var node = ZT.Node(method);
        node.Lacing = lacing;
        graph.AddNode(node);
        ZT.Wire(graph, a, 0, node, 0);
        ZT.Wire(graph, b, 0, node, 1);
        _engine.Run(graph);
        return new ZeroTouchNodeResult(node);
    }

    private class ZeroTouchNodeResult
    {
        public ZeroTouchNodeResult(Dyncamelo.Core.Loader.ZeroTouchNodeModel node)
        {
            Node = node;
        }

        public Dyncamelo.Core.Loader.ZeroTouchNodeModel Node { get; }

        public object? Output => Node.OutPorts[0].Value;
    }

    [Fact]
    public void ScalarPort_ReceivingList_MapsAutomatically()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, new List<object?> { 1.0, 4.0, 9.0 });
        var sqrt = ZT.Node("Sqrt");
        graph.AddNode(sqrt);
        ZT.Wire(graph, a, 0, sqrt, 0);

        _engine.Run(graph);

        var result = AsList(sqrt.OutPorts[0].Value);
        Assert.Equal(new List<object?> { 1.0, 2.0, 3.0 }, result);
        Assert.Equal(NodeState.Executed, sqrt.State);
    }

    [Fact]
    public void ListPort_ReceivingList_ConsumesItWhole()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, new List<object?> { 1.0, 2.0, 3.0 });
        var sum = ZT.Node("Sum");
        graph.AddNode(sum);
        ZT.Wire(graph, a, 0, sum, 0);

        _engine.Run(graph);

        Assert.Equal(6.0, (double)sum.OutPorts[0].Value!); // no replication over declared rank
    }

    [Fact]
    public void ListPort_ReceivingNestedList_MapsOverExcessRankOnly()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, new List<object?>
        {
            new List<object?> { 1.0, 2.0 },
            new List<object?> { 3.0, 4.0, 5.0 },
        });
        var sum = ZT.Node("Sum");
        graph.AddNode(sum);
        ZT.Wire(graph, a, 0, sum, 0);

        _engine.Run(graph);

        var result = AsList(sum.OutPorts[0].Value);
        Assert.Equal(new List<object?> { 3.0, 12.0 }, result);
    }

    [Fact]
    public void ShortestLacing_ZipsToShortestLength()
    {
        var result = RunBinary(
            new List<object?> { 1.0, 2.0, 3.0 },
            new List<object?> { 10.0, 20.0 },
            LacingMode.Shortest);

        Assert.Equal(new List<object?> { 11.0, 22.0 }, AsList(result.Output));
    }

    [Fact]
    public void AutoLacing_IsShortest()
    {
        var result = RunBinary(
            new List<object?> { 1.0, 2.0, 3.0 },
            new List<object?> { 10.0, 20.0 },
            LacingMode.Auto);

        Assert.Equal(new List<object?> { 11.0, 22.0 }, AsList(result.Output));
    }

    [Fact]
    public void LongestLacing_RepeatsLastElement()
    {
        var result = RunBinary(
            new List<object?> { 1.0, 2.0, 3.0 },
            new List<object?> { 10.0, 20.0 },
            LacingMode.Longest);

        Assert.Equal(new List<object?> { 11.0, 22.0, 23.0 }, AsList(result.Output));
    }

    [Fact]
    public void LongestLacing_EmptyList_YieldsEmptyResultAndWarning()
    {
        var result = RunBinary(
            new List<object?>(),
            new List<object?> { 10.0, 20.0 },
            LacingMode.Longest);

        Assert.Empty(AsList(result.Output));
        Assert.Equal(NodeState.Warning, result.Node.State);
        Assert.Contains("empty", result.Node.StateMessage);
    }

    [Fact]
    public void ShortestLacing_EmptyList_YieldsEmptyResult()
    {
        var result = RunBinary(
            new List<object?>(),
            new List<object?> { 10.0, 20.0 },
            LacingMode.Shortest);

        Assert.Empty(AsList(result.Output));
    }

    [Fact]
    public void CrossProductLacing_LeftmostInputIsOutermostLoop()
    {
        var result = RunBinary(
            new List<object?> { 1.0, 2.0 },
            new List<object?> { 10.0, 20.0, 30.0 },
            LacingMode.CrossProduct);

        var outer = AsList(result.Output);
        Assert.Equal(2, outer.Count);
        Assert.Equal(new List<object?> { 11.0, 21.0, 31.0 }, AsList(outer[0]));
        Assert.Equal(new List<object?> { 12.0, 22.0, 32.0 }, AsList(outer[1]));
    }

    [Fact]
    public void ScalarArgument_IsBroadcastToEveryInvocation()
    {
        var result = RunBinary(
            new List<object?> { 1.0, 2.0, 3.0 },
            10.0,
            LacingMode.Shortest);

        Assert.Equal(new List<object?> { 11.0, 12.0, 13.0 }, AsList(result.Output));
    }

    [Fact]
    public void NestedLists_RecurseLevelByLevel_WithBroadcastAtInnerLevels()
    {
        // [[1,2],[3,4]] + [10,20] (Shortest): level 1 pairs [1,2]<->10 and [3,4]<->20,
        // the scalar is broadcast inside each pair.
        var result = RunBinary(
            new List<object?>
            {
                new List<object?> { 1.0, 2.0 },
                new List<object?> { 3.0, 4.0 },
            },
            new List<object?> { 10.0, 20.0 },
            LacingMode.Shortest);

        var outer = AsList(result.Output);
        Assert.Equal(2, outer.Count);
        Assert.Equal(new List<object?> { 11.0, 12.0 }, AsList(outer[0]));
        Assert.Equal(new List<object?> { 23.0, 24.0 }, AsList(outer[1]));
    }

    [Fact]
    public void JaggedNesting_HandlesMixedScalarAndListElements()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, new List<object?> { new List<object?> { 4.0, 9.0 }, 16.0 });
        var sqrt = ZT.Node("Sqrt");
        graph.AddNode(sqrt);
        ZT.Wire(graph, a, 0, sqrt, 0);

        _engine.Run(graph);

        var outer = AsList(sqrt.OutPorts[0].Value);
        Assert.Equal(new List<object?> { 2.0, 3.0 }, AsList(outer[0]));
        Assert.Equal(4.0, (double)outer[1]!);
    }

    [Fact]
    public void ThreeReplicatedInputs_Shortest()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, new List<object?> { 1.0, 2.0 });
        var b = ZT.Value(graph, new List<object?> { 10.0, 20.0, 30.0 });
        var c = ZT.Value(graph, new List<object?> { 100.0, 200.0 });
        var add3 = ZT.Node("Add3");
        graph.AddNode(add3);
        ZT.Wire(graph, a, 0, add3, 0);
        ZT.Wire(graph, b, 0, add3, 1);
        ZT.Wire(graph, c, 0, add3, 2);

        _engine.Run(graph);

        Assert.Equal(new List<object?> { 111.0, 222.0 }, AsList(add3.OutPorts[0].Value));
    }

    [Fact]
    public void NullElement_YieldsNullResultElement_AndWarning_OtherElementsCompute()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, new List<object?> { 4.0, null, 9.0 });
        var sqrt = ZT.Node("Sqrt");
        graph.AddNode(sqrt);
        ZT.Wire(graph, a, 0, sqrt, 0);

        _engine.Run(graph);

        var result = AsList(sqrt.OutPorts[0].Value);
        Assert.Equal(new List<object?> { 2.0, null, 3.0 }, result);
        Assert.Equal(NodeState.Warning, sqrt.State);
        Assert.Contains("Null value", sqrt.StateMessage);
    }

    [Fact]
    public void UncoercibleElement_YieldsNullResultElement_AndWarning()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, new List<object?> { 4.0, "not a number", 9.0 });
        var sqrt = ZT.Node("Sqrt");
        graph.AddNode(sqrt);
        ZT.Wire(graph, a, 0, sqrt, 0);

        _engine.Run(graph);

        var result = AsList(sqrt.OutPorts[0].Value);
        Assert.Equal(3, result.Count);
        Assert.Equal(2.0, (double)result[0]!);
        Assert.Null(result[1]);
        Assert.Equal(3.0, (double)result[2]!);
        Assert.Equal(NodeState.Warning, sqrt.State);
    }

    [Fact]
    public void ObjectPort_NeverReplicates()
    {
        var graph = new GraphModel();
        var list = new List<object?> { 1.0, 2.0 };
        var a = ZT.Value(graph, list);
        var watch = new Dyncamelo.Core.Nodes.WatchNode();
        graph.AddNode(watch);
        ZT.Wire(graph, a, 0, watch, 0);

        _engine.Run(graph);

        Assert.Same(list, watch.OutPorts[0].Value); // whole list flowed through, unmapped
    }

    [Fact]
    public void MultiReturnNode_UnderReplication_CollectsEachOutputIntoLists()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, new List<object?> { 17, 9 });
        var b = ZT.Value(graph, 5);
        var divMod = ZT.Node("DivMod");
        graph.AddNode(divMod);
        ZT.Wire(graph, a, 0, divMod, 0);
        ZT.Wire(graph, b, 0, divMod, 1);

        _engine.Run(graph);

        Assert.Equal(new List<object?> { 3, 1 }, AsList(divMod.OutPorts[0].Value));
        Assert.Equal(new List<object?> { 2, 4 }, AsList(divMod.OutPorts[1].Value));
    }

    [Fact]
    public void LazyEnumerableOutputs_AreMaterializedToLists()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, 1.0);
        var b = ZT.Value(graph, 2.0);
        var c = ZT.Value(graph, 3.0);
        var makeList = ZT.Node("MakeList");
        graph.AddNode(makeList);
        ZT.Wire(graph, a, 0, makeList, 0);
        ZT.Wire(graph, b, 0, makeList, 1);
        ZT.Wire(graph, c, 0, makeList, 2);
        var sum = ZT.Node("Sum");
        graph.AddNode(sum);
        ZT.Wire(graph, makeList, 0, sum, 0);

        _engine.Run(graph);

        Assert.True(makeList.OutPorts[0].Value is System.Collections.IList);
        Assert.Equal(6.0, (double)sum.OutPorts[0].Value!);
    }
}
