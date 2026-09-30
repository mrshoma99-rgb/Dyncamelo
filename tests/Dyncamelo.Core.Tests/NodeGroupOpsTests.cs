using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Groups;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Core.Serialization;
using Dyncamelo.Core.Tests.Fixtures;
using Xunit;

namespace Dyncamelo.Core.Tests;

/// <summary>Making a group from a selection, ungrouping, editing the interface and making an instance single-user.</summary>
public class NodeGroupOpsTests
{
    private sealed class Rig
    {
        public GraphModel Doc = new GraphModel { Name = "Doc" };
        public NumberInputNode V = null!;
        public NumberInputNode W = null!;
        public ZeroTouchNodeModel Sqrt = null!;
        public ZeroTouchNodeModel Step = null!;
        public ZeroTouchNodeModel Add = null!;
        public ZeroTouchNodeModel Tail = null!;
        public UndoManager Undo = new UndoManager();
        public GraphRecorder Recorder = null!;

        public double Result => (double)Tail.OutPorts[0].Value!;
    }

    // v(16) -> sqrt -> step(+1) -> add.a ; w(10) -> add.b ; add -> tail(sqrt)   => sqrt(5 + 10)
    private static Rig Build()
    {
        var rig = new Rig();
        var doc = rig.Doc;
        rig.V = new NumberInputNode { Value = 16, X = 0, Y = 0 };
        rig.W = new NumberInputNode { Value = 10, X = 0, Y = 200 };
        rig.Sqrt = ZT.Node("Sqrt");
        rig.Sqrt.X = 250;
        rig.Step = ZT.Node("AddStep");
        rig.Step.X = 500;
        rig.Add = ZT.Node("Add");
        rig.Add.X = 750;
        rig.Tail = ZT.Node("Sqrt");
        rig.Tail.X = 1000;
        foreach (var node in new NodeModel[] { rig.V, rig.W, rig.Sqrt, rig.Step, rig.Add, rig.Tail })
        {
            doc.AddNode(node);
        }

        ZT.Wire(doc, rig.V, 0, rig.Sqrt, 0);
        ZT.Wire(doc, rig.Sqrt, 0, rig.Step, 0);
        ZT.Wire(doc, rig.Step, 0, rig.Add, 0);
        ZT.Wire(doc, rig.W, 0, rig.Add, 1);
        ZT.Wire(doc, rig.Add, 0, rig.Tail, 0);
        rig.Recorder = new GraphRecorder(doc, rig.Undo);
        new GraphEngine().Run(doc);
        return rig;
    }

    private static NodeModel[] Middle(Rig rig) => new NodeModel[] { rig.Sqrt, rig.Step, rig.Add };

    private static GraphSerializer Serializer()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterDefinitions(ZT.All);
        return new GraphSerializer(registry);
    }

    [Fact]
    public void AGroupMadeFromASelectionComputesExactlyWhatTheSelectionDid()
    {
        var rig = Build();
        var before = rig.Result;

        var result = NodeGroupOps.MakeGroup(rig.Doc, Middle(rig), "Middle", rig.Undo);

        Assert.True(result.Success, result.Message);
        var group = result.Group!;
        var instance = result.Instance!;
        Assert.Equal("Middle", group.Name);
        Assert.Equal(4, rig.Doc.Nodes.Count);   // v, w, tail and the instance
        Assert.Contains(instance, rig.Doc.Nodes);
        Assert.Equal(new[] { "x", "b" }, group.Inputs.Select(s => s.Name).ToArray());   // the ports the outside wires fed
        Assert.Equal(new[] { "number", "number" }, group.Inputs.Select(s => s.Kind).ToArray());
        Assert.Equal(new[] { "result" }, group.Outputs.Select(s => s.Name).ToArray());
        Assert.Equal(5, group.Graph.Nodes.Count);   // the three nodes and Group Input/Output
        Assert.Equal(3, rig.Doc.Connections.Count);
        Assert.Equal(5, group.Graph.Connections.Count);   // sqrt->step, step->add, input->sqrt, input->add, add->output

        new GraphEngine().Run(rig.Doc);
        Assert.Equal(before, rig.Result);
        Assert.Contains("Made node group 'Middle'", result.Message);
    }

    [Fact]
    public void MakingAGroupIsOneUndoStepAndRedoBringsItBack()
    {
        var rig = Build();
        var before = rig.Result;
        rig.Undo.Clear();

        var result = NodeGroupOps.MakeGroup(rig.Doc, Middle(rig), "Middle", rig.Undo);
        var group = result.Group!;
        Assert.Equal(1, rig.Undo.UndoCount);
        Assert.Equal("Make node group", rig.Undo.UndoLabel);

        rig.Undo.Undo();
        Assert.Equal(6, rig.Doc.Nodes.Count);
        Assert.Equal(5, rig.Doc.Connections.Count);
        Assert.Empty(rig.Doc.NodeGroups.Groups);
        Assert.DoesNotContain(group.Graph.Nodes, n => Middle(rig).Contains(n));
        Assert.All(Middle(rig), n => Assert.Same(rig.Doc, n.Graph));
        new GraphEngine().Run(rig.Doc);
        Assert.Equal(before, rig.Result);

        rig.Undo.Redo();
        Assert.Equal(4, rig.Doc.Nodes.Count);
        Assert.Single(rig.Doc.NodeGroups.Groups);
        Assert.Equal(5, group.Graph.Nodes.Count);
        Assert.Equal(5, group.Graph.Connections.Count);
        Assert.Equal(3, rig.Doc.Connections.Count);
        foreach (var node in rig.Doc.Nodes)
        {
            node.MarkDirty();
        }

        new GraphEngine().Run(rig.Doc);
        Assert.Equal(before, rig.Result);

        rig.Undo.Undo();
        rig.Undo.Redo();
        rig.Undo.Undo();
        Assert.Equal(6, rig.Doc.Nodes.Count);
    }

    [Fact]
    public void ASelectionWithSomethingInTheMiddleIsRefusedAndNothingChanges()
    {
        var rig = Build();

        var result = NodeGroupOps.MakeGroup(rig.Doc, new NodeModel[] { rig.Sqrt, rig.Add }, null, rig.Undo);

        Assert.False(result.Success);
        Assert.Contains("sits between", result.Message);
        Assert.Equal(6, rig.Doc.Nodes.Count);
        Assert.Empty(rig.Doc.NodeGroups.Groups);
    }

    [Fact]
    public void NothingSelectedOrAGroupsOwnInterfaceNodesCannotBeGrouped()
    {
        var rig = Build();
        var made = NodeGroupOps.MakeGroup(rig.Doc, Middle(rig), null, null).Group!;

        Assert.False(NodeGroupOps.CanMakeGroup(rig.Doc, new NodeModel[0], out var none));
        Assert.Contains("Select", none);
        Assert.False(NodeGroupOps.CanMakeGroup(made.Graph, new NodeModel[] { made.InputNode }, out var io));
        Assert.Contains("Group Input", io);
    }

    [Fact]
    public void AGroupCanBeMadeInsideAnotherGroupAndItsBodyStaysWired()
    {
        var rig = Build();
        var outer = NodeGroupOps.MakeGroup(rig.Doc, Middle(rig), "Outer", null).Group!;
        var inner = NodeGroupOps.MakeGroup(outer.Graph, new NodeModel[] { rig.Sqrt, rig.Step }, "Inner", null);

        Assert.True(inner.Success, inner.Message);
        Assert.True(outer.Uses(inner.Group!));
        Assert.Equal(2, rig.Doc.NodeGroups.Groups.Count);
        new GraphEngine().Run(rig.Doc);
        Assert.Equal(System.Math.Sqrt(15), rig.Result, 9);
    }

    [Fact]
    public void UngroupingPutsTheNodesBackAndTheGraphComputesTheSame()
    {
        var rig = Build();
        var before = rig.Result;
        var made = NodeGroupOps.MakeGroup(rig.Doc, Middle(rig), "Middle", null);
        new GraphEngine().Run(rig.Doc);

        var result = NodeGroupOps.Ungroup(rig.Doc, made.Instance!, Serializer(), rig.Undo);

        Assert.True(result.Success, result.Message);
        Assert.Equal(3, result.Nodes.Count);
        Assert.Equal(6, rig.Doc.Nodes.Count);
        Assert.DoesNotContain(made.Instance!, rig.Doc.Nodes);
        Assert.Equal(5, rig.Doc.Connections.Count);
        Assert.Single(rig.Doc.NodeGroups.Groups);   // the definition stays
        foreach (var node in rig.Doc.Nodes)
        {
            node.MarkDirty();
        }

        new GraphEngine().Run(rig.Doc);
        Assert.Equal(before, rig.Result);
    }

    [Fact]
    public void UngroupingIsOneUndoStep()
    {
        var rig = Build();
        var made = NodeGroupOps.MakeGroup(rig.Doc, Middle(rig), "Middle", null);
        rig.Undo.Clear();

        NodeGroupOps.Ungroup(rig.Doc, made.Instance!, Serializer(), rig.Undo);
        Assert.Equal(1, rig.Undo.UndoCount);
        Assert.Equal("Ungroup", rig.Undo.UndoLabel);

        rig.Undo.Undo();
        Assert.Equal(4, rig.Doc.Nodes.Count);
        Assert.Contains(made.Instance!, rig.Doc.Nodes);
        Assert.Equal(3, rig.Doc.Connections.Count);
    }

    [Fact]
    public void ValuesPinnedOnAnUnwiredGroupInputSurviveUngrouping()
    {
        var doc = new GraphModel();
        var group = doc.NodeGroups.Create("Plus");
        group.AddSocket(SocketSide.Input, "x", "number");
        group.AddSocket(SocketSide.Output, "y", "number");
        var step = ZT.Node("AddStep");
        group.Graph.AddNode(step);
        ZT.Wire(group.Graph, group.InputNode, 0, step, 0);
        ZT.Wire(group.Graph, step, 0, group.OutputNode, 0);
        var instance = new GroupInstanceNode(group);
        doc.AddNode(instance);
        instance.InPorts[0].SetUserValue(41.0);

        var result = NodeGroupOps.Ungroup(doc, instance, Serializer(), null);

        Assert.True(result.Success);
        var copy = Assert.IsType<ZeroTouchNodeModel>(Assert.Single(result.Nodes));
        Assert.True(copy.InPorts[0].HasUserValue);
        Assert.Equal(41.0, copy.InPorts[0].UserValue);
    }

    [Fact]
    public void AWireStraightThroughAGroupIsBridgedWhenUngrouping()
    {
        var doc = new GraphModel();
        var group = doc.NodeGroups.Create("Pass");
        group.AddSocket(SocketSide.Input, "in");
        group.AddSocket(SocketSide.Output, "out");
        ZT.Wire(group.Graph, group.InputNode, 0, group.OutputNode, 0);
        var source = new NumberInputNode { Value = 7 };
        var instance = new GroupInstanceNode(group);
        var sink = ZT.Node("Sqrt");
        doc.AddNode(source);
        doc.AddNode(instance);
        doc.AddNode(sink);
        ZT.Wire(doc, source, 0, instance, 0);
        ZT.Wire(doc, instance, 0, sink, 0);

        NodeGroupOps.Ungroup(doc, instance, Serializer(), null);

        Assert.Equal(source.OutPorts[0], doc.FindConnectionInto(sink.InPorts[0])!.Source);
    }

    // ----- interface edits ---------------------------------------------------------------------

    [Fact]
    public void InterfaceEditsUndoWithTheirWires()
    {
        var rig = Build();
        var made = NodeGroupOps.MakeGroup(rig.Doc, Middle(rig), "Middle", null);
        var group = made.Group!;
        var instance = made.Instance!;
        var undo = new UndoManager();
        var socket = group.Inputs[0];
        var outerWire = rig.Doc.FindConnectionInto(instance.InPorts[0])!;
        var innerWire = group.Graph.Connections.Single(c => c.Source.Owner == group.InputNode && c.Source.Id == socket.Id);

        Assert.True(NodeGroupOps.RemoveSocket(group, SocketSide.Input, socket.Id, undo));
        Assert.Single(group.Inputs);
        Assert.DoesNotContain(outerWire, rig.Doc.Connections);
        Assert.DoesNotContain(innerWire, group.Graph.Connections);

        undo.Undo();
        Assert.Equal(2, group.Inputs.Count);
        Assert.Equal(socket.Id, group.Inputs[0].Id);   // back in its old place with its old id
        Assert.Contains(outerWire, rig.Doc.Connections);
        Assert.Contains(innerWire, group.Graph.Connections);
        new GraphEngine().Run(rig.Doc);
        Assert.Equal(System.Math.Sqrt(15), rig.Result, 9);

        undo.Redo();
        Assert.Single(group.Inputs);
    }

    [Fact]
    public void RenamingRetypingAndReorderingASocketAreUndoable()
    {
        var rig = Build();
        var group = NodeGroupOps.MakeGroup(rig.Doc, Middle(rig), "Middle", null).Group!;
        var undo = new UndoManager();
        var first = group.Inputs[0];

        Assert.True(NodeGroupOps.RenameSocket(group, SocketSide.Input, first.Id, "base", undo));
        Assert.True(NodeGroupOps.SetSocketKind(group, SocketSide.Input, first.Id, "text", undo));
        Assert.True(NodeGroupOps.MoveSocket(group, SocketSide.Input, first.Id, 1, undo));
        Assert.Equal(new[] { "b", "base" }, group.Inputs.Select(s => s.Name).ToArray());
        Assert.Equal("text", group.Inputs[1].Kind);
        Assert.Equal(3, undo.UndoCount);

        undo.Undo();
        undo.Undo();
        undo.Undo();
        Assert.Equal(new[] { "x", "b" }, group.Inputs.Select(s => s.Name).ToArray());
        Assert.Equal("number", group.Inputs[0].Kind);

        undo.Redo();
        undo.Redo();
        undo.Redo();
        Assert.Equal(new[] { "b", "base" }, group.Inputs.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void AddingASocketIsUndoable()
    {
        var rig = Build();
        var group = NodeGroupOps.MakeGroup(rig.Doc, Middle(rig), "Middle", null).Group!;
        var undo = new UndoManager();

        var added = NodeGroupOps.AddSocket(group, SocketSide.Output, "extra", "text", undo);

        Assert.Equal(2, group.Outputs.Count);
        undo.Undo();
        Assert.Single(group.Outputs);
        undo.Redo();
        Assert.Equal(added.Id, group.Outputs[1].Id);
    }

    // ----- groups themselves ------------------------------------------------------------------

    [Fact]
    public void MakeSingleUserGivesOneInstanceItsOwnCopy()
    {
        var rig = Build();
        var made = NodeGroupOps.MakeGroup(rig.Doc, Middle(rig), "Middle", null);
        var first = made.Instance!;
        new GraphEngine().Run(rig.Doc);
        var source = new NumberInputNode { Value = 1 };
        var other = new NumberInputNode { Value = 2 };
        var second = new GroupInstanceNode(made.Group!);
        rig.Doc.AddNode(source);
        rig.Doc.AddNode(other);
        rig.Doc.AddNode(second);
        ZT.Wire(rig.Doc, source, 0, second, 0);
        ZT.Wire(rig.Doc, other, 0, second, 1);
        var undo = new UndoManager();

        var result = NodeGroupOps.MakeSingleUser(rig.Doc, second, Serializer(), undo);

        Assert.True(result.Success, result.Message);
        var copy = result.Group!;
        Assert.NotSame(made.Group, copy);
        Assert.Equal("Middle copy", copy.Name);
        Assert.Same(copy, second.Definition);
        Assert.Same(made.Group, first.Definition);
        Assert.Equal(2, rig.Doc.Connections.Count(c => c.TargetNode == second));   // its wires stayed
        Assert.Equal(5, copy.Graph.Nodes.Count);

        // Editing the copy no longer touches the first instance.
        first.IsDirty = false;
        var copyStep = copy.Graph.Nodes.OfType<ZeroTouchNodeModel>().First(n => n.Name == "AddStep" || n.Definition.Method.Name == "AddStep");
        copyStep.InPorts[1].SetUserValue(100.0);
        Assert.True(second.IsDirty);
        Assert.False(first.IsDirty);

        new GraphEngine().Run(rig.Doc);
        Assert.Equal(System.Math.Sqrt(15), rig.Result, 9);          // the original is unchanged
        Assert.Equal(System.Math.Sqrt(1) + 100 + 2, (double)second.OutPorts[0].Value!, 9);

        undo.Undo();
        Assert.Same(made.Group, second.Definition);
        Assert.DoesNotContain(copy, rig.Doc.NodeGroups.Groups);
        undo.Redo();
        Assert.Same(copy, second.Definition);
    }

    [Fact]
    public void MakeSingleUserNeedsAnotherInstanceToBeUseful()
    {
        var rig = Build();
        var made = NodeGroupOps.MakeGroup(rig.Doc, Middle(rig), "Middle", null);

        var result = NodeGroupOps.MakeSingleUser(rig.Doc, made.Instance!, Serializer(), null);

        Assert.False(result.Success);
        Assert.Contains("only used here", result.Message);
    }

    [Fact]
    public void RenamingAndRemovingAGroupAreUndoable()
    {
        var rig = Build();
        var made = NodeGroupOps.MakeGroup(rig.Doc, Middle(rig), "Middle", null);
        var group = made.Group!;
        var undo = new UndoManager();

        Assert.True(NodeGroupOps.RenameGroup(group, "Renamed", undo));
        Assert.Equal("Renamed", made.Instance!.Name);
        undo.Undo();
        Assert.Equal("Middle", group.Name);
        Assert.Equal("Middle", made.Instance.Name);

        Assert.False(NodeGroupOps.RemoveUnusedGroup(group, undo));   // still used
        rig.Doc.RemoveNode(made.Instance);
        Assert.True(NodeGroupOps.RemoveUnusedGroup(group, undo));
        Assert.Empty(rig.Doc.NodeGroups.Groups);
        undo.Undo();
        Assert.Single(rig.Doc.NodeGroups.Groups);
    }

    [Fact]
    public void AGroupMadeAndSavedLoadsBackWithItsBodyAndRuns()
    {
        var rig = Build();
        var before = rig.Result;
        NodeGroupOps.MakeGroup(rig.Doc, Middle(rig), "Middle", null);
        var serializer = Serializer();

        var back = serializer.Deserialize(serializer.Serialize(rig.Doc));
        new GraphEngine().Run(back);

        var tail = back.Nodes.OfType<ZeroTouchNodeModel>().Single();
        Assert.Equal(before, (double)tail.OutPorts[0].Value!);
    }
}
