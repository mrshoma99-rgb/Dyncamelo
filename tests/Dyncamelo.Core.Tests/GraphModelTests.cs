using System.Linq;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Core.Tests.Fixtures;
using Xunit;

namespace Dyncamelo.Core.Tests;

public class GraphModelTests
{
    [Fact]
    public void Connect_CreatesConnection_AndMarksTargetDirty()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, 1.0);
        var add = ZT.Node("Add");
        graph.AddNode(add);
        var b = ZT.Value(graph, 2.0);

        var result = graph.Connect(a.OutPorts[0], add.InPorts[0]);

        Assert.True(result.Success);
        Assert.NotNull(result.Connection);
        Assert.Single(graph.Connections);
        Assert.True(add.IsDirty);
        Assert.Same(a, result.Connection!.SourceNode);
        Assert.Same(add, result.Connection.TargetNode);
        Assert.NotNull(b); // silence unused
    }

    [Fact]
    public void Connect_RejectsDirectCycle()
    {
        var graph = new GraphModel();
        var add1 = ZT.Node("Add");
        var add2 = ZT.Node("Add");
        graph.AddNode(add1);
        graph.AddNode(add2);
        ZT.Wire(graph, add1, 0, add2, 0);

        var result = graph.Connect(add2.OutPorts[0], add1.InPorts[0]);

        Assert.False(result.Success);
        Assert.Contains("cycle", result.Message);
        Assert.Single(graph.Connections);
    }

    [Fact]
    public void Connect_RejectsTransitiveCycle()
    {
        var graph = new GraphModel();
        var n1 = ZT.Node("Sqrt");
        var n2 = ZT.Node("Sqrt");
        var n3 = ZT.Node("Sqrt");
        graph.AddNode(n1);
        graph.AddNode(n2);
        graph.AddNode(n3);
        ZT.Wire(graph, n1, 0, n2, 0);
        ZT.Wire(graph, n2, 0, n3, 0);

        var result = graph.Connect(n3.OutPorts[0], n1.InPorts[0]);

        Assert.False(result.Success);
        Assert.Contains("cycle", result.Message);
    }

    [Fact]
    public void Connect_RejectsSelfConnection()
    {
        var graph = new GraphModel();
        var add = ZT.Node("Add");
        graph.AddNode(add);

        var result = graph.Connect(add.OutPorts[0], add.InPorts[0]);

        Assert.False(result.Success);
    }

    [Fact]
    public void Connect_ReplacesExistingConnectionIntoInputPort()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, 1.0);
        var b = ZT.Value(graph, 2.0);
        var sqrt = ZT.Node("Sqrt");
        graph.AddNode(sqrt);
        ZT.Wire(graph, a, 0, sqrt, 0);

        var result = graph.Connect(b.OutPorts[0], sqrt.InPorts[0]);

        Assert.True(result.Success);
        Assert.Single(graph.Connections);
        Assert.Same(b, graph.Connections[0].SourceNode);
    }

    [Fact]
    public void Connect_RejectsWrongDirections()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, 1.0);
        var sqrt = ZT.Node("Sqrt");
        graph.AddNode(sqrt);

        Assert.False(graph.Connect(sqrt.InPorts[0], a.OutPorts[0]).Success);
        Assert.False(graph.Connect(a.OutPorts[0], a.OutPorts[0]).Success);
    }

    [Fact]
    public void Disconnect_RestoresDefault_AndDirtiesTarget()
    {
        var graph = new GraphModel();
        var x = ZT.Value(graph, 5.0);
        var step = ZT.Value(graph, 3.0);
        var addStep = ZT.Node("AddStep");
        graph.AddNode(addStep);
        ZT.Wire(graph, x, 0, addStep, 0);
        ZT.Wire(graph, step, 0, addStep, 1);
        Assert.False(addStep.InPorts[1].UsingDefaultValue);

        var connection = graph.FindConnectionInto(addStep.InPorts[1]);
        Assert.True(graph.Disconnect(connection!));

        Assert.True(addStep.InPorts[1].UsingDefaultValue);
        Assert.True(addStep.IsDirty);
        Assert.Single(graph.Connections);
    }

    [Fact]
    public void RemoveNode_RemovesConnections_AndDirtiesConsumers()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, 4.0);
        var sqrt = ZT.Node("Sqrt");
        graph.AddNode(sqrt);
        ZT.Wire(graph, a, 0, sqrt, 0);
        new Dyncamelo.Core.Execution.GraphEngine().Run(graph);
        Assert.False(sqrt.IsDirty);

        Assert.True(graph.RemoveNode(a));

        Assert.Empty(graph.Connections);
        Assert.True(sqrt.IsDirty);
        Assert.Null(a.Graph);
    }

    [Fact]
    public void MarkDirty_PropagatesToTransitiveDownstreamOnly()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, 1.0);
        var b = ZT.Node("Sqrt");
        var c = ZT.Node("Sqrt");
        var unrelated = ZT.Value(graph, 9.0);
        graph.AddNode(b);
        graph.AddNode(c);
        ZT.Wire(graph, a, 0, b, 0);
        ZT.Wire(graph, b, 0, c, 0);
        new Dyncamelo.Core.Execution.GraphEngine().Run(graph);
        Assert.All(graph.Nodes, n => Assert.False(n.IsDirty));

        graph.MarkDirty(a);

        Assert.True(a.IsDirty);
        Assert.True(b.IsDirty);
        Assert.True(c.IsDirty);
        Assert.False(unrelated.IsDirty);
    }

    [Fact]
    public void Events_AreRaisedForStructuralMutations()
    {
        var graph = new GraphModel();
        NodeModel? added = null;
        NodeModel? removed = null;
        ConnectionModel? connected = null;
        ConnectionModel? disconnected = null;
        int modifiedCount = 0;
        graph.NodeAdded += (_, e) => added = e.Node;
        graph.NodeRemoved += (_, e) => removed = e.Node;
        graph.ConnectionAdded += (_, e) => connected = e.Connection;
        graph.ConnectionRemoved += (_, e) => disconnected = e.Connection;
        graph.Modified += (_, _) => modifiedCount++;

        var a = ZT.Value(graph, 1.0);
        var sqrt = ZT.Node("Sqrt");
        graph.AddNode(sqrt);
        Assert.Same(sqrt, added);

        var result = graph.Connect(a.OutPorts[0], sqrt.InPorts[0]);
        Assert.Same(result.Connection, connected);

        graph.Disconnect(result.Connection!);
        Assert.Same(result.Connection, disconnected);

        graph.RemoveNode(a);
        Assert.Same(a, removed);
        Assert.True(modifiedCount > 0);
    }

    [Fact]
    public void SliderValueChange_MarksNodeAndDownstreamDirty()
    {
        var graph = new GraphModel();
        var slider = new NumberSliderNode();
        graph.AddNode(slider);
        var sqrt = ZT.Node("Sqrt");
        graph.AddNode(sqrt);
        ZT.Wire(graph, slider, 0, sqrt, 0);
        new Dyncamelo.Core.Execution.GraphEngine().Run(graph);
        Assert.False(sqrt.IsDirty);

        slider.Value = 25;

        Assert.True(slider.IsDirty);
        Assert.True(sqrt.IsDirty);
    }
}
