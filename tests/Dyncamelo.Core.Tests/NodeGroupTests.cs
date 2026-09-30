using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Groups;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Core.Serialization;
using Dyncamelo.Core.Tests.Fixtures;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Dyncamelo.Core.Tests;

/// <summary>Node groups: a shared subgraph with its own interface, run by any number of instances.</summary>
public class NodeGroupTests
{
    private static NodeRegistry Registry()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterDefinitions(ZT.All);
        return registry;
    }

    /// <summary>A document with a group "Increment": x in, y out, y = x + 1.</summary>
    private static (GraphModel Doc, NodeGroup Group, ZeroTouchNodeModel Step) DocWithIncrement()
    {
        var doc = new GraphModel { Name = "Doc" };
        var group = doc.NodeGroups.Create("Increment");
        group.AddSocket(SocketSide.Input, "x", "number");
        group.AddSocket(SocketSide.Output, "y", "number");
        var step = ZT.Node("AddStep");
        group.Graph.AddNode(step);
        ZT.Wire(group.Graph, group.InputNode, 0, step, 0);
        ZT.Wire(group.Graph, step, 0, group.OutputNode, 0);
        return (doc, group, step);
    }

    private static GroupInstanceNode AddInstance(GraphModel graph, NodeGroup group, double? fedWith = null)
    {
        var instance = new GroupInstanceNode(group);
        graph.AddNode(instance);
        if (fedWith.HasValue)
        {
            var source = new NumberInputNode { Value = fedWith.Value };
            graph.AddNode(source);
            ZT.Wire(graph, source, 0, instance, 0);
        }

        return instance;
    }

    [Fact]
    public void EachInstanceRunsTheSharedBodyOnItsOwnInputs()
    {
        var (doc, group, _) = DocWithIncrement();
        var first = AddInstance(doc, group, 3);
        var second = AddInstance(doc, group, 10);

        var result = new GraphEngine().Run(doc);

        Assert.True(result.Success);
        Assert.Equal(4.0, (double)first.OutPorts[0].Value!);
        Assert.Equal(11.0, (double)second.OutPorts[0].Value!);
        Assert.Equal(NodeState.Executed, first.State);
    }

    [Fact]
    public void AnInstanceTakesItsSocketsAndNameFromTheGroup()
    {
        var (doc, group, _) = DocWithIncrement();
        var instance = AddInstance(doc, group);

        Assert.Equal("Increment", instance.Name);
        Assert.Equal(new[] { "x" }, instance.InPorts.Select(p => p.Name).ToArray());
        Assert.Equal(new[] { "y" }, instance.OutPorts.Select(p => p.Name).ToArray());
        Assert.Equal("number", instance.InPorts[0].KindHint);
        Assert.Equal(group.Inputs[0].Id, instance.InPorts[0].Id);
        Assert.False(instance.ShowInLibrary);
        Assert.False(group.InputNode.ShowInLibrary);

        group.Name = "Plus one";
        Assert.Equal("Plus one", instance.Name);   // still called after its group, so it follows the rename

        instance.Name = "My own name";
        group.Name = "Plus 1";
        Assert.Equal("My own name", instance.Name);
    }

    [Fact]
    public void EditingTheBodyMakesEveryInstanceStaleAndTheNextRunUsesTheEdit()
    {
        var (doc, group, step) = DocWithIncrement();
        var first = AddInstance(doc, group, 3);
        var second = AddInstance(doc, group, 10);
        var engine = new GraphEngine();
        engine.Run(doc);
        Assert.False(first.IsDirty);
        Assert.False(second.IsDirty);

        step.InPorts[1].SetUserValue(5.0);   // the step size inside the group

        Assert.True(first.IsDirty);
        Assert.True(second.IsDirty);
        engine.Run(doc);
        Assert.Equal(8.0, (double)first.OutPorts[0].Value!);
        Assert.Equal(15.0, (double)second.OutPorts[0].Value!);
    }

    [Fact]
    public void AnOutputSocketNothingIsWiredToLeavesAsNull()
    {
        var (doc, group, _) = DocWithIncrement();
        group.AddSocket(SocketSide.Output, "unused");
        var instance = AddInstance(doc, group, 1);

        new GraphEngine().Run(doc);

        Assert.Equal(2.0, (double)instance.OutPorts[0].Value!);
        Assert.Null(instance.OutPorts[1].Value);
    }

    [Fact]
    public void AnInstanceWithAnUnwiredInputWaitsLikeAnyNode()
    {
        var (doc, group, _) = DocWithIncrement();
        var instance = AddInstance(doc, group);

        new GraphEngine().Run(doc);

        Assert.Equal(NodeState.Idle, instance.State);
        Assert.Null(instance.OutPorts[0].Value);
    }

    [Fact]
    public void SocketEditsKeepWiresFollowRenamesAndReorderingAndUndoGetsThePortsBack()
    {
        var (doc, group, _) = DocWithIncrement();
        var instance = AddInstance(doc, group, 3);
        var port = instance.InPorts[0];
        var socket = group.Inputs[0];

        Assert.True(group.RenameSocket(SocketSide.Input, socket.Id, "amount"));
        Assert.Equal("amount", instance.InPorts[0].Name);
        Assert.Same(port, instance.InPorts[0]);
        Assert.NotNull(doc.FindConnectionInto(port));

        var extra = group.AddSocket(SocketSide.Input, "extra", "text");
        Assert.Equal(new[] { "amount", "extra" }, instance.InPorts.Select(p => p.Name).ToArray());
        Assert.True(group.MoveSocket(SocketSide.Input, extra.Id, 0));
        Assert.Equal(new[] { "extra", "amount" }, instance.InPorts.Select(p => p.Name).ToArray());
        Assert.Same(port, instance.InPorts[1]);

        // Removing a socket disconnects what was wired to it, inside the group and on the instance.
        var outer = doc.FindConnectionInto(port)!;
        var inner = group.Graph.Connections.Single(c => c.Source.Owner == group.InputNode);
        Assert.True(group.RemoveSocket(SocketSide.Input, socket.Id));
        Assert.DoesNotContain(port, instance.InPorts);
        Assert.DoesNotContain(outer, doc.Connections);
        Assert.DoesNotContain(inner, group.Graph.Connections);

        // Bringing the socket back under its old id returns the very same ports, so the wires can be put back.
        group.AddSocket(SocketSide.Input, "x", "number", index: 1, id: socket.Id);
        Assert.Contains(port, instance.InPorts);
        Assert.True(doc.ReinsertConnection(outer));
        Assert.True(group.Graph.ReinsertConnection(inner));
    }

    [Fact]
    public void SocketNamesStayUniqueOnTheirSide()
    {
        var (_, group, _) = DocWithIncrement();
        var second = group.AddSocket(SocketSide.Input, "x");
        Assert.Equal("x 2", second.Name);
        Assert.Equal("Input", group.AddSocket(SocketSide.Input, "  ").Name);
        Assert.Equal("x", group.AddSocket(SocketSide.Output, "x").Name);   // the output side has its own names
    }

    [Fact]
    public void GroupsCanBeNestedButNeverContainThemselves()
    {
        var (doc, increment, _) = DocWithIncrement();
        var plusTwo = doc.NodeGroups.Create("Plus two");
        plusTwo.AddSocket(SocketSide.Input, "x", "number");
        plusTwo.AddSocket(SocketSide.Output, "y", "number");
        var one = new GroupInstanceNode(increment);
        var two = new GroupInstanceNode(increment);
        plusTwo.Graph.AddNode(one);
        plusTwo.Graph.AddNode(two);
        ZT.Wire(plusTwo.Graph, plusTwo.InputNode, 0, one, 0);
        ZT.Wire(plusTwo.Graph, one, 0, two, 0);
        ZT.Wire(plusTwo.Graph, two, 0, plusTwo.OutputNode, 0);

        Assert.True(plusTwo.Uses(increment));
        Assert.False(increment.Uses(plusTwo));
        Assert.True(doc.NodeGroups.WouldCreateCycle(increment, plusTwo));   // Increment cannot take a Plus two inside
        Assert.True(doc.NodeGroups.WouldCreateCycle(increment, increment));
        Assert.False(doc.NodeGroups.WouldCreateCycle(plusTwo, increment));
        Assert.False(doc.NodeGroups.WouldCreateCycle(null, plusTwo));

        var outer = AddInstance(doc, plusTwo, 5);
        new GraphEngine().Run(doc);
        Assert.Equal(7.0, (double)outer.OutPorts[0].Value!);
    }

    [Fact]
    public void AGroupIsOnlyRemovedWhileNothingUsesIt()
    {
        var (doc, group, _) = DocWithIncrement();
        var instance = AddInstance(doc, group);

        Assert.False(doc.NodeGroups.Remove(group));
        doc.RemoveNode(instance);
        Assert.True(doc.NodeGroups.Remove(group));
        Assert.Empty(doc.NodeGroups.Groups);
    }

    [Fact]
    public void GroupNamesAreMadeUnique()
    {
        var doc = new GraphModel();
        Assert.Equal("Node Group", doc.NodeGroups.Create("").Name);
        Assert.Equal("Node Group 2", doc.NodeGroups.Create("Node Group").Name);
        Assert.Equal("Mine", doc.NodeGroups.Create("Mine").Name);
        Assert.Equal("mine 2", doc.NodeGroups.Create("mine").Name);   // the wanted spelling is kept, only made unique
    }

    // ----- problems and cancelling inside a group -----------------------------------------

    [Fact]
    public void AFailureInsideShowsOnTheInstanceAndTheRestOfTheGraphCarriesOn()
    {
        var doc = new GraphModel();
        var group = doc.NodeGroups.Create("Boom");
        group.AddSocket(SocketSide.Input, "x");
        group.AddSocket(SocketSide.Output, "y");
        var fail = ZT.Node("Fail");
        group.Graph.AddNode(fail);
        ZT.Wire(group.Graph, group.InputNode, 0, fail, 0);
        ZT.Wire(group.Graph, fail, 0, group.OutputNode, 0);
        var instance = AddInstance(doc, group, 1);
        var bystander = ZT.Value(doc, "still runs");

        new GraphEngine().Run(doc);

        Assert.Equal(NodeState.Error, instance.State);
        Assert.Contains("Inside 'Boom'", instance.StateMessage);
        Assert.Contains("boom", instance.StateMessage);
        Assert.Equal(NodeState.Executed, bystander.State);
    }

    [Fact]
    public void ProgressInsideAGroupCarriesTheGroupPath()
    {
        var (doc, group, _) = DocWithIncrement();
        AddInstance(doc, group, 1);
        var seen = new List<RunProgress>();

        new GraphEngine().Run(doc, new EvaluationContext { ProgressCallback = seen.Add });

        var inside = seen.Where(p => p.Scope.Count == 1).ToList();
        Assert.NotEmpty(inside);
        Assert.All(inside, p => Assert.Equal("Increment", p.Scope[0]));
        Assert.Contains(inside, p => p.Describe().Contains("Increment ▸"));
        Assert.Contains(seen, p => p.Scope.Count == 0);   // and the outer graph's own nodes have no path
    }

    [Fact]
    public void CancellingInsideAGroupStopsTheOuterRunAndTheInstanceResumes()
    {
        var (doc, group, _) = DocWithIncrement();
        var instance = AddInstance(doc, group, 3);
        using var source = new CancellationTokenSource();
        var context = new EvaluationContext(source.Token);
        context.Heartbeat = () =>
        {
            if (context.Scope.Count > 0)
            {
                source.Cancel();   // Esc pressed while the group's body is running
            }
        };

        var engine = new GraphEngine();
        var result = engine.Run(doc, context);

        Assert.True(result.Cancelled);
        Assert.True(instance.IsDirty);
        Assert.Null(instance.OutPorts[0].Value);
        Assert.NotEqual(NodeState.Error, instance.State);

        var resumed = engine.Run(doc);
        Assert.True(resumed.Success);
        Assert.Equal(4.0, (double)instance.OutPorts[0].Value!);
    }

    [Fact]
    public void ARunningGroupRefusesToRunItselfAgain()
    {
        var (doc, group, _) = DocWithIncrement();
        // Hand-built recursion (the editor never allows it): the body holds an instance of its own group.
        var inner = new GroupInstanceNode(group);
        group.Graph.AddNode(inner);
        ZT.Wire(group.Graph, group.InputNode, 0, inner, 0);
        ZT.Wire(group.Graph, inner, 0, group.OutputNode, 0);
        var instance = AddInstance(doc, group, 1);

        new GraphEngine().Run(doc);

        Assert.Equal(NodeState.Error, instance.State);
        Assert.Contains("contains itself", instance.StateMessage);
    }

    // ----- files ----------------------------------------------------------------------------

    [Fact]
    public void AFileWithoutGroupsIsStillVersionOneAndOlderReadersKeepOpeningIt()
    {
        var doc = new GraphModel { Name = "Plain" };
        doc.AddNode(new NumberInputNode { Value = 1 });

        var root = JObject.Parse(new GraphSerializer(Registry()).Serialize(doc));

        Assert.Equal(1, (int)root["Dyncamelo"]!["MinReaderVersion"]!);
        Assert.Equal(1, (int)root["Dyncamelo"]!["FormatVersion"]!);
        Assert.Null(root["NodeGroups"]);
    }

    [Fact]
    public void GroupsRoundTripThroughTheFileAndStillRun()
    {
        var (doc, group, step) = DocWithIncrement();
        step.InPorts[1].SetUserValue(2.0);
        group.Description = "adds two";
        var instance = AddInstance(doc, group, 3);
        instance.Name = "Renamed instance";
        var serializer = new GraphSerializer(Registry());

        var json = serializer.Serialize(doc);
        var root = JObject.Parse(json);
        Assert.Equal(2, (int)root["Dyncamelo"]!["MinReaderVersion"]!);
        Assert.Single((JArray)root["NodeGroups"]!);

        var back = serializer.Deserialize(json);
        var loadedGroup = Assert.Single(back.NodeGroups.Groups);
        Assert.Equal("Increment", loadedGroup.Name);
        Assert.Equal("adds two", loadedGroup.Description);
        Assert.Equal(group.Id, loadedGroup.Id);
        Assert.Equal(group.Inputs[0].Id, loadedGroup.Inputs[0].Id);
        Assert.Equal("number", loadedGroup.Inputs[0].Kind);
        Assert.NotNull(loadedGroup.InputNode);
        Assert.NotNull(loadedGroup.OutputNode);

        var loadedInstance = back.Nodes.OfType<GroupInstanceNode>().Single();
        Assert.Same(loadedGroup, loadedInstance.Definition);
        Assert.Equal("Renamed instance", loadedInstance.Name);
        Assert.NotNull(back.FindConnectionInto(loadedInstance.InPorts[0]));

        new GraphEngine().Run(back);
        Assert.Equal(5.0, (double)loadedInstance.OutPorts[0].Value!);

        Assert.Equal(json, serializer.Serialize(back));   // and saving what was loaded changes nothing
    }

    [Fact]
    public void NestedGroupsLoadInAnyOrder()
    {
        var (doc, increment, _) = DocWithIncrement();
        var plusTwo = doc.NodeGroups.Create("Plus two");
        plusTwo.AddSocket(SocketSide.Input, "x");
        plusTwo.AddSocket(SocketSide.Output, "y");
        var inside = new GroupInstanceNode(increment);
        plusTwo.Graph.AddNode(inside);
        ZT.Wire(plusTwo.Graph, plusTwo.InputNode, 0, inside, 0);
        ZT.Wire(plusTwo.Graph, inside, 0, plusTwo.OutputNode, 0);
        AddInstance(doc, plusTwo, 1);
        var serializer = new GraphSerializer(Registry());

        // Put the user of a group before the group it uses in the file.
        var root = JObject.Parse(serializer.Serialize(doc));
        var groups = (JArray)root["NodeGroups"]!;
        groups.ReplaceAll(groups.Reverse().ToList());
        var back = serializer.Deserialize(root.ToString());

        Assert.Equal(2, back.NodeGroups.Groups.Count);
        var loadedOuter = back.NodeGroups.Find("Plus two")!;
        Assert.True(loadedOuter.Uses(back.NodeGroups.Find("Increment")!));
        new GraphEngine().Run(back);
        Assert.Equal(2.0, (double)back.Nodes.OfType<GroupInstanceNode>().Single().OutPorts[0].Value!);   // 1 + the one Increment inside
    }

    [Fact]
    public void AnInstanceWhoseGroupIsMissingBecomesAPlaceholderThatKeepsItsData()
    {
        var (doc, group, _) = DocWithIncrement();
        AddInstance(doc, group, 1);
        var serializer = new GraphSerializer(Registry());
        var root = JObject.Parse(serializer.Serialize(doc));
        root.Remove("NodeGroups");

        var back = serializer.Deserialize(root.ToString());

        var missing = back.Nodes.OfType<MissingNodeModel>().Single();
        Assert.Contains("Unknown node group", missing.StateMessage + missing.Name + string.Join(" ", missing.Messages.Select(m => m.Text)));
        Assert.Equal(GroupInstanceNode.TypeName, (string)missing.RawJson["NodeType"]!);
    }

    [Fact]
    public void AFileWhoseGroupContainsItselfIsRefused()
    {
        var (doc, group, _) = DocWithIncrement();
        var serializer = new GraphSerializer(Registry());
        var root = JObject.Parse(serializer.Serialize(doc));
        var body = (JObject)((JArray)root["NodeGroups"]!)[0]["Graph"]!;
        ((JArray)body["Nodes"]!).Add(new JObject
        {
            ["Id"] = Guid.NewGuid().ToString("N"),
            ["NodeType"] = GroupInstanceNode.TypeName,
            ["Name"] = "Loop",
            ["Data"] = new JObject { ["GroupId"] = group.Id.ToString("N") },
        });

        Assert.Throws<GraphFormatException>(() => serializer.Deserialize(root.ToString()));
    }

    [Fact]
    public void CopyingAnInstanceCarriesItsDefinitionToAnotherDocument()
    {
        var (doc, group, _) = DocWithIncrement();
        var instance = AddInstance(doc, group, 3);
        var serializer = new GraphSerializer(Registry());
        var fragment = serializer.SerializeFragment(new NodeModel[] { instance });

        var other = new GraphModel();
        var pasted = serializer.PasteFragment(other, fragment, 40, 40);

        var copy = Assert.IsType<GroupInstanceNode>(Assert.Single(pasted));
        Assert.Same(Assert.Single(other.NodeGroups.Groups), copy.Definition);
        Assert.NotSame(group, copy.Definition);   // a separate document gets its own definition

        // Pasting again reuses it instead of making a second copy; pasting into the same document shares the original.
        serializer.PasteFragment(other, fragment, 80, 80);
        Assert.Single(other.NodeGroups.Groups);
        var same = serializer.PasteFragment(doc, fragment, 10, 10);
        Assert.Single(doc.NodeGroups.Groups);
        Assert.Same(group, ((GroupInstanceNode)same[0]).Definition);

        var source = new NumberInputNode { Value = 7 };
        other.AddNode(source);
        ZT.Wire(other, source, 0, copy, 0);
        new GraphEngine().Run(other);
        Assert.Equal(8.0, (double)copy.OutPorts[0].Value!);
    }

    [Fact]
    public void PastedGroupsKeepTheirNamesUniqueInTheTargetDocument()
    {
        var (doc, group, _) = DocWithIncrement();
        var instance = AddInstance(doc, group);
        var serializer = new GraphSerializer(Registry());
        var fragment = serializer.SerializeFragment(new NodeModel[] { instance });

        var other = new GraphModel();
        other.NodeGroups.Create("Increment");   // a different group that happens to share the name
        serializer.PasteFragment(other, fragment, 0, 0);

        Assert.Equal(new[] { "Increment", "Increment 2" }, other.NodeGroups.Groups.Select(g => g.Name).ToArray());
    }
}
