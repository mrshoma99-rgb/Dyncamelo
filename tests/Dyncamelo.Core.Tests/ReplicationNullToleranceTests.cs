using System.Collections.Generic;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Tests.Fixtures;
using Xunit;

namespace Dyncamelo.Core.Tests;

/// <summary>
/// Null tolerance under replication (Dynamo's null-propagation semantics): a
/// null element of a laced list maps to a null RESULT for that position — the
/// node method never sees it, the other elements still compute, and the node
/// reports one summary warning instead of failing outright. The same applies
/// to per-element exceptions. Single (non-laced) calls keep failing loudly.
/// </summary>
public class ReplicationNullToleranceTests
{
    private readonly GraphEngine _engine = new GraphEngine();

    private static List<object?> L(params object?[] items) => new List<object?>(items);

    private (NodeModel node, RunResult result) RunOver(string method, object? input)
    {
        var graph = new GraphModel();
        var source = ZT.Value(graph, input);
        var node = ZT.Node(method);
        graph.AddNode(node);
        ZT.Wire(graph, source, 0, node, 0);
        var result = _engine.Run(graph);
        return (node, result);
    }

    // -------------------------------------------------------- null elements

    [Fact]
    public void NullElement_ReferenceTypePort_YieldsNullResult_OthersCompute()
    {
        var (node, result) = RunOver("Shout", L("a", null, "b"));

        Assert.True(result.Success);
        Assert.Equal(new List<object?> { "A!", null, "B!" }, node.OutPorts[0].Value);
        Assert.Equal(NodeState.Warning, node.State);
        Assert.Contains("1 of 3", node.StateMessage);
        Assert.Contains("null element", node.StateMessage);
    }

    [Fact]
    public void NullElement_ValueTypePort_YieldsNullResult_OthersCompute()
    {
        var (node, _) = RunOver("Sqrt", L(1.0, null, 9.0));

        Assert.Equal(new List<object?> { 1.0, null, 3.0 }, node.OutPorts[0].Value);
        Assert.Equal(NodeState.Warning, node.State);
    }

    [Fact]
    public void NullElements_PreserveNestedStructure()
    {
        var (node, _) = RunOver("Sqrt", L(L(1.0, null), L(4.0)));

        var outer = Assert.IsType<List<object?>>(node.OutPorts[0].Value);
        Assert.Equal(new List<object?> { 1.0, null }, outer[0]);
        Assert.Equal(new List<object?> { 2.0 }, outer[1]);
    }

    [Fact]
    public void NullList_InsideListOfLists_YieldsNull_NotAnError()
    {
        // The null sits one level up: [[1,4], null] mapped by a scalar port.
        var (node, result) = RunOver("Sqrt", L(L(1.0, 4.0), null));

        Assert.True(result.Success);
        var outer = Assert.IsType<List<object?>>(node.OutPorts[0].Value);
        Assert.Equal(new List<object?> { 1.0, 2.0 }, outer[0]);
        Assert.Null(outer[1]);
        Assert.Equal(NodeState.Warning, node.State);
    }

    [Fact]
    public void NullOnUnlacedOptionalInput_IsNotAWarning()
    {
        // AddStep(x, step = 1): laced over x with step unwired (null default
        // machinery) — nulls on non-laced inputs keep flowing as before.
        var graph = new GraphModel();
        var source = ZT.Value(graph, L(1.0, 2.0));
        var node = ZT.Node("AddStep");
        graph.AddNode(node);
        ZT.Wire(graph, source, 0, node, 0);

        Assert.True(_engine.Run(graph).Success);
        Assert.Equal(new List<object?> { 2.0, 3.0 }, node.OutPorts[0].Value);
        Assert.Equal(NodeState.Executed, node.State);
    }

    // ------------------------------------------------- per-element failures

    [Fact]
    public void ThrowingElement_YieldsNull_OthersCompute_NodeWarns()
    {
        var (node, result) = RunOver("ReciprocalStrict", L(1.0, 0.0, 4.0));

        Assert.True(result.Success);
        Assert.Equal(new List<object?> { 1.0, null, 0.25 }, node.OutPorts[0].Value);
        Assert.Equal(NodeState.Warning, node.State);
        Assert.Contains("division by zero", node.StateMessage);
    }

    [Fact]
    public void AllElementsThrowing_YieldsAllNulls_StillWarningNotError()
    {
        var (node, result) = RunOver("Fail", L(1.0, 2.0));

        Assert.True(result.Success);
        Assert.Equal(new List<object?> { null, null }, node.OutPorts[0].Value);
        Assert.Equal(NodeState.Warning, node.State);
        Assert.Contains("2 of 2", node.StateMessage);
        Assert.Contains("boom", node.StateMessage);
    }

    [Fact]
    public void SingleCall_StillFailsLoudly()
    {
        var (node, result) = RunOver("Fail", 1.0);

        Assert.True(result.Success); // node failures never abort the run
        Assert.Equal(NodeState.Error, node.State);
        Assert.Contains("boom", node.StateMessage);
        Assert.Null(node.OutPorts[0].Value);
    }

    // ------------------------------------------------------- lacing modes

    [Fact]
    public void LongestLacing_NullElement_YieldsNullAtItsPosition()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, L(1.0, null, 3.0));
        var b = ZT.Value(graph, L(10.0));
        var add = ZT.Node("Add");
        add.Lacing = LacingMode.Longest;
        graph.AddNode(add);
        ZT.Wire(graph, a, 0, add, 0);
        ZT.Wire(graph, b, 0, add, 1);

        Assert.True(_engine.Run(graph).Success);
        Assert.Equal(new List<object?> { 11.0, null, 13.0 }, add.OutPorts[0].Value);
        Assert.Equal(NodeState.Warning, add.State);
    }

    [Fact]
    public void CrossProductLacing_NullOuterElement_YieldsNullRow_ShapedLikeTheOthers()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, L(1.0, null));
        var b = ZT.Value(graph, L(10.0, 20.0));
        var add = ZT.Node("Add");
        add.Lacing = LacingMode.CrossProduct;
        graph.AddNode(add);
        ZT.Wire(graph, a, 0, add, 0);
        ZT.Wire(graph, b, 0, add, 1);

        Assert.True(_engine.Run(graph).Success);
        var outer = Assert.IsType<List<object?>>(add.OutPorts[0].Value);
        Assert.Equal(new List<object?> { 11.0, 21.0 }, outer[0]);
        Assert.Equal(new List<object?> { null, null }, outer[1]);
    }
}
