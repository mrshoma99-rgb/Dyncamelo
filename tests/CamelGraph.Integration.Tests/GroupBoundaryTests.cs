using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Groups;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Nodes;
using Xunit;

namespace CamelGraph.Integration.Tests;

/// <summary>
/// What crosses the border of a node group: a switched-off branch (Flow.When), a failure that is recovered (Flow.Try) and a failure
/// or switch-off that arrives from outside must behave inside a group as they do on the canvas — per socket, and independent
/// chains of the body keep running.
/// </summary>
public class GroupBoundaryTests
{
    private static NodeRegistry Registry() => Pipeline.CreateRegistry();

    private static void Wire(GraphModel graph, PortModel from, PortModel to)
    {
        var result = graph.Connect(from, to);
        Assert.True(result.Success, result.Message);
    }

    private static PortModel GIn(NodeGroup group, string socket) => group.InputNode.OutPorts.First(p => p.Name == socket);

    private static PortModel GOut(NodeGroup group, string socket) => group.OutputNode.InPorts.First(p => p.Name == socket);

    private static PortModel Out(NodeModel node, string name) => node.OutPorts.First(p => p.Name == name);

    private static PortModel In(NodeModel node, string name) => node.InPorts.First(p => p.Name == name);

    private static T Add<T>(GraphModel graph, T node, string? name = null)
        where T : NodeModel
    {
        if (name != null)
        {
            node.Name = name;
        }

        graph.AddNode(node);
        return node;
    }

    // ----------------------------------------------------------- Flow.When across the border

    [Fact]
    public void AWhenSwitchedOffInsideAGroupStaysSwitchedOffAfterTheGroup()
    {
        // String "head" -> Flow.When(false) -> String.Concat(a, "tail"), with only Flow.When grouped.
        var registry = Registry();
        var doc = new GraphModel { Name = "doc" };
        var head = Add(doc, new StringInputNode { Value = "head" }, "Head");
        var off = Add(doc, new BooleanToggleNode { Value = false }, "Off");
        var tail = Add(doc, new StringInputNode { Value = "tail" }, "Tail");
        var when = Add(doc, Pipeline.ZeroTouch(registry, "Flow.When"), "Gate");
        var concat = Add(doc, Pipeline.ZeroTouch(registry, "String.Concat"), "After");
        Wire(doc, Out(head, "value"), In(when, "value"));
        Wire(doc, Out(off, "value"), In(when, "condition"));
        Wire(doc, Out(when, "value"), In(concat, "a"));
        Wire(doc, Out(tail, "value"), In(concat, "b"));

        var made = NodeGroupOps.MakeGroup(doc, new NodeModel[] { when }, "G", null);
        Assert.True(made.Success, made.Message);
        new GraphEngine().Run(doc);

        var instance = made.Instance!;
        Assert.Same(InactiveValue.Instance, instance.OutPorts[0].Value);
        Assert.Equal(NodeState.Idle, concat.State);
        Assert.Same(InactiveValue.Instance, concat.OutPorts[0].Value);
        Assert.Contains(concat.Messages, m => m.Text.Contains("Skipped"));
    }

    [Fact]
    public void ASwitchedOffSocketDoesNotTakeTheOtherSocketsOfTheGroupDown()
    {
        var registry = Registry();
        var doc = new GraphModel { Name = "doc" };
        var group = doc.NodeGroups.Create("Two ways");
        group.AddSocket(SocketSide.Input, "x");
        group.AddSocket(SocketSide.Output, "a");
        group.AddSocket(SocketSide.Output, "b");
        var off = Add(group.Graph, new BooleanToggleNode { Value = false }, "Off");
        var when = Add(group.Graph, Pipeline.ZeroTouch(registry, "Flow.When"), "Gate");
        Wire(group.Graph, GIn(group, "x"), In(when, "value"));
        Wire(group.Graph, Out(off, "value"), In(when, "condition"));
        Wire(group.Graph, Out(when, "value"), GOut(group, "a"));
        Wire(group.Graph, GIn(group, "x"), GOut(group, "b"));

        var source = Add(doc, new NumberInputNode { Value = 4 }, "Source");
        var instance = Add(doc, new GroupInstanceNode(group));
        var afterA = Add(doc, Pipeline.ZeroTouch(registry, "Math.Sqrt"), "AfterA");
        var afterB = Add(doc, Pipeline.ZeroTouch(registry, "Math.Sqrt"), "AfterB");
        Wire(doc, Out(source, "value"), instance.InPorts[0]);
        Wire(doc, instance.OutPorts[0], In(afterA, "number"));
        Wire(doc, instance.OutPorts[1], In(afterB, "number"));

        new GraphEngine().Run(doc);

        Assert.Same(InactiveValue.Instance, instance.OutPorts[0].Value);
        Assert.Equal(4d, instance.OutPorts[1].Value);
        Assert.Equal(NodeState.Idle, afterA.State);
        Assert.Equal(NodeState.Executed, afterB.State);
        Assert.Equal(2d, afterB.OutPorts[0].Value);
        Assert.Equal(NodeState.Executed, instance.State);
    }

    [Fact]
    public void WhenTheGateOpensAgainTheGroupDeliversTheValueOnTheNextRun()
    {
        var registry = Registry();
        var doc = new GraphModel { Name = "doc" };
        var head = Add(doc, new NumberInputNode { Value = 9 }, "Head");
        var gate = Add(doc, new BooleanToggleNode { Value = false }, "Gate switch");
        var when = Add(doc, Pipeline.ZeroTouch(registry, "Flow.When"), "Gate");
        var after = Add(doc, Pipeline.ZeroTouch(registry, "Math.Sqrt"), "After");
        Wire(doc, Out(head, "value"), In(when, "value"));
        Wire(doc, Out(gate, "value"), In(when, "condition"));
        Wire(doc, Out(when, "value"), In(after, "number"));
        var made = NodeGroupOps.MakeGroup(doc, new NodeModel[] { when }, "G", null);
        var engine = new GraphEngine();
        engine.Run(doc);
        Assert.Equal(NodeState.Idle, after.State);

        gate.Value = true;
        engine.Run(doc);

        Assert.Equal(NodeState.Executed, after.State);
        Assert.Equal(3d, after.OutPorts[0].Value);
        Assert.Equal(NodeState.Executed, made.Instance!.State);
    }

    // ----------------------------------------------------------- Flow.Try and groups

    private static (GraphModel Doc, NodeModel Broken, NodeModel Attempt, NodeModel After) TryGraph(NodeRegistry registry)
    {
        var doc = new GraphModel { Name = "doc" };
        var text = Add(doc, new StringInputNode { Value = "not a number" }, "Text");
        var broken = Add(doc, Pipeline.ZeroTouch(registry, "String.ToNumber"), "Broken");
        var fallback = Add(doc, new NumberInputNode { Value = -1 }, "Fallback");
        var attempt = Add(doc, Pipeline.ZeroTouch(registry, "Flow.Try"), "Attempt");
        var after = Add(doc, Pipeline.ZeroTouch(registry, "Math.Abs"), "After");
        Wire(doc, Out(text, "value"), In(broken, "text"));
        Wire(doc, Out(broken, "result"), In(attempt, "value"));
        Wire(doc, Out(fallback, "value"), In(attempt, "fallback"));
        Wire(doc, Out(attempt, "result"), In(after, "number"));
        return (doc, broken, attempt, after);
    }

    [Fact]
    public void AFailureRecoveredByATryInsideTheSameGroupDoesNotMakeTheInstanceFail()
    {
        var registry = Registry();
        var (doc, broken, attempt, after) = TryGraph(registry);

        var made = NodeGroupOps.MakeGroup(doc, new[] { broken, attempt }, "Recovering", null);
        Assert.True(made.Success, made.Message);
        new GraphEngine().Run(doc);

        var instance = made.Instance!;
        Assert.NotEqual(NodeState.Error, instance.State);
        Assert.Equal(NodeState.Error, broken.State);     // the node still shows what happened, inside the group
        Assert.Equal(-1d, instance.OutPorts.First(p => p.Name == "result").Value);
        Assert.Equal(NodeState.Executed, after.State);
        Assert.Equal(1d, after.OutPorts[0].Value);
    }

    [Fact]
    public void AFailureThatIsNotRecoveredInsideTheGroupStillFailsTheInstance()
    {
        var registry = Registry();
        var doc = new GraphModel { Name = "doc" };
        var text = Add(doc, new StringInputNode { Value = "x" }, "Text");
        var broken = Add(doc, Pipeline.ZeroTouch(registry, "String.ToNumber"), "Broken");
        var fine = Add(doc, new NumberInputNode { Value = 4 }, "Fine");
        var sqrt = Add(doc, Pipeline.ZeroTouch(registry, "Math.Sqrt"), "Sqrt");
        Wire(doc, Out(text, "value"), In(broken, "text"));
        Wire(doc, Out(fine, "value"), In(sqrt, "number"));

        var made = NodeGroupOps.MakeGroup(doc, new NodeModel[] { broken, sqrt }, "Mixed", null);
        new GraphEngine().Run(doc);

        // 'Broken' has no consumer: nothing recovers it, so the group reports it. The healthy part still delivers.
        Assert.Equal(NodeState.Error, made.Instance!.State);
        Assert.Contains(made.Instance.Messages, m => m.Text.Contains("Broken"));
    }

    [Fact]
    public void ATryInsideAGroupCatchesAFailureThatArrivesFromOutside()
    {
        var registry = Registry();
        var (doc, broken, attempt, after) = TryGraph(registry);

        var made = NodeGroupOps.MakeGroup(doc, new[] { attempt }, "Catcher", null);
        Assert.True(made.Success, made.Message);
        new GraphEngine().Run(doc);

        Assert.Equal(NodeState.Error, broken.State);
        Assert.Equal(NodeState.Executed, attempt.State);                 // the Try inside ran
        var instance = made.Instance!;
        Assert.Equal(NodeState.Executed, instance.State);
        Assert.Equal(-1d, instance.OutPorts.First(p => p.Name == "result").Value);
        Assert.Equal(true, Out(attempt, "failed").Value);
        Assert.Contains("Broken", (string)Out(attempt, "error").Value!);
        Assert.Equal(NodeState.Executed, after.State);
        Assert.Equal(1d, after.OutPorts[0].Value);
    }

    // ----------------------------------------------------------- one bad input stops only its own chain

    private static (GraphModel Doc, GroupInstanceNode Instance, NodeModel AfterOne, NodeModel AfterTwo) TwoChains(
        NodeRegistry registry, NodeModel firstSource, string firstOut, NodeModel secondSource, string secondOut)
    {
        var doc = new GraphModel { Name = "doc" };
        doc.AddNode(firstSource);
        doc.AddNode(secondSource);
        var one = Add(doc, Pipeline.ZeroTouch(registry, "Math.Negate"), "One");
        var two = Add(doc, Pipeline.ZeroTouch(registry, "Math.Negate"), "Two");
        var afterOne = Add(doc, Pipeline.ZeroTouch(registry, "Math.Abs"), "After one");
        var afterTwo = Add(doc, Pipeline.ZeroTouch(registry, "Math.Abs"), "After two");
        Wire(doc, Out(firstSource, firstOut), In(one, "number"));
        Wire(doc, Out(secondSource, secondOut), In(two, "number"));
        Wire(doc, Out(one, "value"), In(afterOne, "number"));
        Wire(doc, Out(two, "value"), In(afterTwo, "number"));
        var made = NodeGroupOps.MakeGroup(doc, new NodeModel[] { one, two }, "Chains", null);
        Assert.True(made.Success, made.Message);
        return (doc, made.Instance!, afterOne, afterTwo);
    }

    [Fact]
    public void AFailedInputStopsOnlyTheChainThatDependsOnIt()
    {
        var registry = Registry();
        var text = new StringInputNode { Value = "x", Name = "Text" };
        var broken = Pipeline.ZeroTouch(registry, "String.ToNumber", "Broken");
        var y = new NumberInputNode { Value = 3, Name = "Y" };
        var rig = TwoChains(registry, broken, "result", y, "value");
        rig.Doc.AddNode(text);
        Wire(rig.Doc, Out(text, "value"), In(broken, "text"));

        new GraphEngine().Run(rig.Doc);

        Assert.Equal(-3d, rig.Instance.OutPorts[1].Value);               // the independent chain still computed
        Assert.Equal(NodeState.Executed, rig.AfterTwo.State);
        Assert.Equal(3d, rig.AfterTwo.OutPorts[0].Value);
        Assert.Equal(NodeState.Warning, rig.AfterOne.State);             // only what depends on the failure stops
        Assert.True(rig.AfterOne.FailedUpstream);
        Assert.Null(rig.AfterOne.OutPorts[0].Value);
        Assert.NotEqual(NodeState.Error, rig.Instance.State);
        Assert.Equal(NodeState.Warning, rig.Instance.State);
        Assert.Contains(rig.Instance.Messages, m => m.Text.Contains("Upstream failure"));
    }

    [Fact]
    public void ASwitchedOffInputStopsOnlyTheChainThatDependsOnIt()
    {
        var registry = Registry();
        var x = new NumberInputNode { Value = 5, Name = "X" };
        var off = new BooleanToggleNode { Value = false, Name = "Off" };
        var gate = Pipeline.ZeroTouch(registry, "Flow.When", "Gate");
        var y = new NumberInputNode { Value = 3, Name = "Y" };
        var rig = TwoChains(registry, gate, "value", y, "value");
        rig.Doc.AddNode(x);
        rig.Doc.AddNode(off);
        Wire(rig.Doc, Out(x, "value"), In(gate, "value"));
        Wire(rig.Doc, Out(off, "value"), In(gate, "condition"));

        new GraphEngine().Run(rig.Doc);

        Assert.Equal(-3d, rig.Instance.OutPorts[1].Value);
        Assert.Same(InactiveValue.Instance, rig.Instance.OutPorts[0].Value);
        Assert.Equal(NodeState.Idle, rig.AfterOne.State);
        Assert.Equal(NodeState.Executed, rig.AfterTwo.State);
        Assert.Equal(NodeState.Executed, rig.Instance.State);
    }

    [Fact]
    public void AGroupWhoseOutputsAreAllSwitchedOffIsSkippedNotRed()
    {
        var registry = Registry();
        var doc = new GraphModel { Name = "doc" };
        var x = Add(doc, new NumberInputNode { Value = 5 }, "X");
        var off = Add(doc, new BooleanToggleNode { Value = false }, "Off");
        var gate = Add(doc, Pipeline.ZeroTouch(registry, "Flow.When"), "Gate");
        var negate = Add(doc, Pipeline.ZeroTouch(registry, "Math.Negate"), "Negate");
        var after = Add(doc, Pipeline.ZeroTouch(registry, "Math.Abs"), "After");
        Wire(doc, Out(x, "value"), In(gate, "value"));
        Wire(doc, Out(off, "value"), In(gate, "condition"));
        Wire(doc, Out(gate, "value"), In(negate, "number"));
        Wire(doc, Out(negate, "value"), In(after, "number"));
        var made = NodeGroupOps.MakeGroup(doc, new NodeModel[] { negate }, "Only", null);

        new GraphEngine().Run(doc);

        Assert.Same(InactiveValue.Instance, made.Instance!.OutPorts[0].Value);
        Assert.Equal(NodeState.Idle, after.State);
        Assert.Contains(made.Instance.Messages, m => m.Text.Contains("Skipped"));
    }

    // ----------------------------------------------------------- a frozen node inside a group

    [Fact]
    public void AFrozenNodeInsideAGroupKeepsTheOutputAndSaysSo()
    {
        var registry = Registry();
        var doc = new GraphModel { Name = "doc" };
        var number = Add(doc, new NumberInputNode { Value = 2 }, "Number");
        var negate = Add(doc, Pipeline.ZeroTouch(registry, "Math.Negate"), "Negate");
        var abs = Add(doc, Pipeline.ZeroTouch(registry, "Math.Abs"), "Abs");
        var after = Add(doc, Pipeline.ZeroTouch(registry, "Math.Sqrt"), "After");
        Wire(doc, Out(number, "value"), In(negate, "number"));
        Wire(doc, Out(negate, "value"), In(abs, "number"));
        Wire(doc, Out(abs, "result"), In(after, "number"));
        var made = NodeGroupOps.MakeGroup(doc, new NodeModel[] { negate, abs }, "Frozen inside", null);
        var engine = new GraphEngine();
        engine.Run(doc);
        Assert.Equal(2d, made.Instance!.OutPorts[0].Value);

        negate.IsFrozen = true;
        number.Value = 5;
        engine.Run(doc);

        Assert.Equal(2d, made.Instance.OutPorts[0].Value);               // frozen: the old result stays, as on the canvas
        Assert.Equal(NodeState.Warning, made.Instance.State);
        Assert.Contains(made.Instance.Messages, m => m.Text.Contains("frozen"));
    }

    // ----------------------------------------------------------- the instance says so

    [Fact]
    public void AGroupWithoutOutputsSaysWhenAnInputFailed()
    {
        var registry = Registry();
        var doc = new GraphModel { Name = "doc" };
        var text = Add(doc, new StringInputNode { Value = "x" }, "Text");
        var broken = Add(doc, Pipeline.ZeroTouch(registry, "String.ToNumber"), "Broken");
        var negate = Add(doc, Pipeline.ZeroTouch(registry, "Math.Negate"), "Negate");
        Wire(doc, Out(text, "value"), In(broken, "text"));
        Wire(doc, Out(broken, "result"), In(negate, "number"));
        var made = NodeGroupOps.MakeGroup(doc, new NodeModel[] { negate }, "No outputs", null);
        Assert.Empty(made.Group!.Outputs);

        new GraphEngine().Run(doc);

        Assert.Equal(NodeState.Warning, made.Instance!.State);
        Assert.True(made.Instance.FailedUpstream);
        Assert.Contains(made.Instance.Messages, m => m.Text.Contains("Upstream failure"));
        Assert.Equal(NodeState.Warning, negate.State);              // the node inside waited for the failed input
    }

    [Fact]
    public void AGroupWithoutOutputsSaysWhenPartOfItWasSwitchedOff()
    {
        var registry = Registry();
        var doc = new GraphModel { Name = "doc" };
        var x = Add(doc, new NumberInputNode { Value = 5 }, "X");
        var off = Add(doc, new BooleanToggleNode { Value = false }, "Off");
        var gate = Add(doc, Pipeline.ZeroTouch(registry, "Flow.When"), "Gate");
        var negate = Add(doc, Pipeline.ZeroTouch(registry, "Math.Negate"), "Negate");
        Wire(doc, Out(x, "value"), In(gate, "value"));
        Wire(doc, Out(off, "value"), In(gate, "condition"));
        Wire(doc, Out(gate, "value"), In(negate, "number"));
        var made = NodeGroupOps.MakeGroup(doc, new NodeModel[] { negate }, "No outputs", null);

        new GraphEngine().Run(doc);

        Assert.Equal(NodeState.Executed, made.Instance!.State);
        Assert.Contains(made.Instance.Messages, m => m.Text.Contains("skipped"));
        Assert.Equal(NodeState.Idle, negate.State);
    }

    [Fact]
    public void AFailureCrossesTwoLevelsOfGroupsAndATryOutsideStillSeesWhatFailed()
    {
        var registry = Registry();
        var doc = new GraphModel { Name = "doc" };
        var text = Add(doc, new StringInputNode { Value = "x" }, "Text");
        var broken = Add(doc, Pipeline.ZeroTouch(registry, "String.ToNumber"), "Broken");
        var negate = Add(doc, Pipeline.ZeroTouch(registry, "Math.Negate"), "Negate");
        var attempt = Add(doc, Pipeline.ZeroTouch(registry, "Flow.Try"), "Attempt");
        Wire(doc, Out(text, "value"), In(broken, "text"));
        Wire(doc, Out(broken, "result"), In(negate, "number"));
        Wire(doc, Out(negate, "value"), In(attempt, "value"));
        var inner = NodeGroupOps.MakeGroup(doc, new NodeModel[] { negate }, "Inner", null);
        var outer = NodeGroupOps.MakeGroup(doc, new NodeModel[] { inner.Instance! }, "Outer", null);
        Assert.True(outer.Success, outer.Message);

        new GraphEngine().Run(doc);

        Assert.Equal(NodeState.Executed, attempt.State);
        Assert.Equal(true, Out(attempt, "failed").Value);
        Assert.Contains("Broken", (string)Out(attempt, "error").Value!);
        Assert.NotEqual(NodeState.Error, outer.Instance!.State);
    }
}
