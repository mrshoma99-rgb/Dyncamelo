using System;
using System.IO;
using System.Linq;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Groups;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Nodes;
using CamelGraph.Core.Player;
using CamelGraph.Core.Serialization;
using CamelGraph.Core.Tests.Fixtures;
using Xunit;

namespace CamelGraph.Core.Tests;

/// <summary>Make Node Group keeps the nodes the Script Player uses outside the group, where the Player can find them.</summary>
public sealed class GroupPlayerNodesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dyc-groupplayer-" + Guid.NewGuid().ToString("N"));

    public GroupPlayerNodesTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, true);
        }
        catch (IOException)
        {
        }
    }

    private static NodeRegistry Registry()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterAssembly(typeof(MathFixtures).Assembly);
        return registry;
    }

    private sealed class Rig
    {
        public GraphModel Doc = new GraphModel { Name = "doc" };
        public NumberInputNode Gap = null!;
        public NumberInputNode Other = null!;
        public ZeroTouchNodeModel Add = null!;
        public WatchNode Result = null!;
    }

    // Gap(3) -> add.a ; Other(4) -> add.b ; add -> Result (Watch)
    private static Rig Build()
    {
        var rig = new Rig();
        rig.Gap = new NumberInputNode { Name = "Gap", Value = 3, X = 0, Y = 0 };
        rig.Other = new NumberInputNode { Name = "Other", Value = 4, X = 0, Y = 200 };
        rig.Add = ZT.Node("Add");
        rig.Add.X = 300;
        rig.Result = new WatchNode { Name = "Result", X = 600 };
        foreach (var node in new NodeModel[] { rig.Gap, rig.Other, rig.Add, rig.Result })
        {
            rig.Doc.AddNode(node);
        }

        ZT.Wire(rig.Doc, rig.Gap, 0, rig.Add, 0);
        ZT.Wire(rig.Doc, rig.Other, 0, rig.Add, 1);
        ZT.Wire(rig.Doc, rig.Add, 0, rig.Result, 0);
        return rig;
    }

    [Fact]
    public void InputAndWatchNodesStayOutsideTheGroupAndFeedItThroughSockets()
    {
        var rig = Build();

        var made = NodeGroupOps.MakeGroup(rig.Doc, new NodeModel[] { rig.Gap, rig.Add, rig.Result }, "Sum", null);

        Assert.True(made.Success, made.Message);
        Assert.Contains("Kept outside the group because the Player uses them: Gap, Result.", made.Message);
        Assert.Same(rig.Doc, rig.Gap.Graph);
        Assert.Same(rig.Doc, rig.Result.Graph);
        Assert.Single(made.Group!.Graph.Nodes.OfType<ZeroTouchNodeModel>());
        Assert.DoesNotContain(made.Group.Graph.Nodes, n => n is NumberInputNode || n is WatchNode);
        Assert.Equal(2, made.Group.Inputs.Count);                 // Gap and Other both feed the group through sockets
        Assert.Single(made.Group.Outputs);

        new GraphEngine().Run(rig.Doc);
        Assert.Equal("7", rig.Result.FormattedValue);             // the script computes what it did
    }

    [Fact]
    public void ThePlayerStillOffersTheFieldAndShowsTheResultAfterGrouping()
    {
        var rig = Build();
        NodeGroupOps.MakeGroup(rig.Doc, new NodeModel[] { rig.Gap, rig.Add, rig.Result }, "Sum", null);
        var path = Path.Combine(_folder, "script.dyc");
        new GraphSerializer(Registry()).SaveToFile(rig.Doc, path);

        var session = ScriptSession.Load(path, Registry());

        Assert.Equal(new[] { "Gap", "Other" }, session.Fields.Select(f => f.Label).OrderBy(l => l).ToArray());
        Assert.Equal("Result", Assert.Single(session.OutputNodes).Name);
        PortEditors.SetNumber(session.Fields.Single(f => f.Label == "Gap").Port, 10);
        Assert.Equal("14", session.Run(new EvaluationContext()).Outputs[0].Text);
    }

    [Fact]
    public void ANodeMarkedForThePlayerStaysOutsideAndAHiddenInputGoesIn()
    {
        var rig = Build();
        rig.Add.PlayerExposed = true;          // a result the author wants in the Player
        rig.Gap.PlayerExposed = false;         // an input the author hid from the Player

        var made = NodeGroupOps.MakeGroup(rig.Doc, new NodeModel[] { rig.Gap, rig.Add, rig.Result }, null, null);

        // Gap is hidden, so nothing needs it outside; Add and Result are shown in the Player, so only Gap goes in.
        Assert.True(made.Success, made.Message);
        Assert.Contains(made.Group!.Graph.Nodes, n => n == rig.Gap);
        Assert.Same(rig.Doc, rig.Add.Graph);
        Assert.Same(rig.Doc, rig.Result.Graph);
        Assert.Contains("Add", made.Message);
    }

    [Fact]
    public void ANodeWithAnInputOfferedAsAFieldStaysOutside()
    {
        var rig = Build();
        var step = ZT.Node("AddStep");
        rig.Doc.AddNode(step);
        step.InPorts[1].PlayerExposed = true;

        var made = NodeGroupOps.MakeGroup(rig.Doc, new NodeModel[] { rig.Add, step }, null, null);

        Assert.True(made.Success, made.Message);
        Assert.Same(rig.Doc, step.Graph);
        Assert.Contains(made.Group!.Graph.Nodes, n => n == rig.Add);
    }

    [Fact]
    public void ASelectionOfNothingButPlayerNodesCannotBecomeAGroup()
    {
        var rig = Build();
        var selection = new NodeModel[] { rig.Gap, rig.Result };

        Assert.False(NodeGroupOps.CanMakeGroup(rig.Doc, selection, out var reason));
        Assert.Contains("Nothing to group", reason);
        Assert.Contains("Gap", reason);

        var made = NodeGroupOps.MakeGroup(rig.Doc, selection, null, null);
        Assert.False(made.Success);
        Assert.Empty(rig.Doc.NodeGroups.Groups);
    }

    [Fact]
    public void APlayerNodeBetweenTheOthersCannotStayOutsideSoTheGroupIsRefused()
    {
        var doc = new GraphModel();
        var first = ZT.Node("Sqrt");
        var watch = new WatchNode { Name = "Middle result" };
        var second = ZT.Node("Sqrt");
        doc.AddNode(first);
        doc.AddNode(watch);
        doc.AddNode(second);
        ZT.Wire(doc, first, 0, watch, 0);
        ZT.Wire(doc, watch, 0, second, 0);

        var ok = NodeGroupOps.CanMakeGroup(doc, new NodeModel[] { first, watch, second }, out var reason);

        Assert.False(ok);
        Assert.Contains("Middle result", reason);
        Assert.Contains("Script Player", reason);
    }

    [Fact]
    public void ASelectionWithoutPlayerNodesBehavesAsBefore()
    {
        var rig = Build();

        var made = NodeGroupOps.MakeGroup(rig.Doc, new NodeModel[] { rig.Add }, null, null);

        Assert.True(made.Success, made.Message);
        Assert.DoesNotContain("Kept outside", made.Message);
    }

    [Fact]
    public void InsideAnOpenGroupEverythingSelectedGoesIn()
    {
        var rig = Build();
        var outer = NodeGroupOps.MakeGroup(rig.Doc, new NodeModel[] { rig.Add }, "Outer", null);
        var inner = outer.Group!.Graph;
        var watch = new WatchNode { Name = "Inside" };
        var sqrt = ZT.Node("Sqrt");
        inner.AddNode(watch);
        inner.AddNode(sqrt);

        var nested = NodeGroupOps.MakeGroup(inner, new NodeModel[] { watch, sqrt }, "Nested", null);

        Assert.True(nested.Success, nested.Message);
        Assert.DoesNotContain("Kept outside", nested.Message);
        Assert.Contains(nested.Group!.Graph.Nodes, n => n == watch);
    }
}
