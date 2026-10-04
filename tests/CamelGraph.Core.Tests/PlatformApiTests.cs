using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Groups;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Tests.Fixtures;
using Xunit;

namespace CamelGraph.Core.Tests;

/// <summary>Zero-touch nodes that use the platform attributes and the warning channel.</summary>
public static class PlatformFixtures
{
    public static int LiveReads;
    public static int LiveValue = 1;
    public static int DownstreamCalls;

    public static double WarnIfNegative(double x)
    {
        if (x < 0)
        {
            NodeWarnings.Add("negative value " + x.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return Math.Abs(x);
    }

    public static double WarnRepeatedly(double x)
    {
        NodeWarnings.Add("same");
        NodeWarnings.Add("same");
        NodeWarnings.Add("other");
        return x;
    }

    public static double WarnMany(double x)
    {
        for (var i = 0; i < 100; i++)
        {
            NodeWarnings.Add("problem " + i);
        }

        return x;
    }

    public static bool IsNullish([AcceptsNull] string text) => text == null;

    public static string BlankToDash([AcceptsNull] string text, string other = "") => text ?? "-";

    public static double NullableDouble([AcceptsNull] double x) => x;

    public static string WhatIs([ScalarInput] object value) => value == null ? "null" : value.GetType().Name;

    public static string WhatIsAny(object value) => value == null ? "null" : value.GetType().Name;

    public static string NotAnObject([ScalarInput] string text) => text;

    [LiveState]
    public static int ReadLive()
    {
        LiveReads++;
        return LiveValue;
    }

    [LiveState]
    public static List<object> ReadLiveList()
    {
        LiveReads++;
        return new List<object> { LiveValue, "x" };
    }

    public static int Downstream(int x)
    {
        DownstreamCalls++;
        return x * 10;
    }

    public static int Downstream(object x)
    {
        DownstreamCalls++;
        return 0;
    }
}

public class PlatformApiTests
{
    private static readonly List<NodeDefinition> Definitions = AssemblyNodeLoader.LoadType(typeof(PlatformFixtures));

    private static ZeroTouchNodeModel Node(string method, int parameters = -1) =>
        new ZeroTouchNodeModel(Definitions.Single(d => d.Method.Name == method && (parameters < 0 || d.Method.GetParameters().Length == parameters)));

    private static List<object?> L(params object?[] items) => new List<object?>(items);

    private static (GraphModel Graph, ZeroTouchNodeModel Node) Run(string method, object? input)
    {
        var graph = new GraphModel();
        var source = ZT.Value(graph, input);
        var node = Node(method);
        graph.AddNode(node);
        ZT.Wire(graph, source, 0, node, 0);
        new GraphEngine().Run(graph);
        return (graph, node);
    }

    // ------------------------------------------------------------- NodeWarnings

    [Fact]
    public void AWarningFromASingleCallMakesTheNodeAmberAndKeepsItsOutput()
    {
        var (_, node) = Run("WarnIfNegative", -3.0);

        Assert.Equal(NodeState.Warning, node.State);
        Assert.Equal(3.0, node.OutPorts[0].Value);
        Assert.Contains("negative value -3", node.StateMessage);
        Assert.Single(node.Messages);
    }

    [Fact]
    public void ACleanCallHasNoWarning()
    {
        var (_, node) = Run("WarnIfNegative", 3.0);

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Empty(node.Messages);
    }

    [Fact]
    public void WarningsOfALacedNodeAreSummarisedInOneLine()
    {
        var (_, node) = Run("WarnIfNegative", L(1.0, -2.0, -3.0, 4.0));

        Assert.Equal(NodeState.Warning, node.State);
        Assert.Equal(L(1.0, 2.0, 3.0, 4.0), node.OutPorts[0].Value);
        var message = Assert.Single(node.Messages);
        Assert.Equal("2 of 4 calls: negative value -2", message.Text);
    }

    [Fact]
    public void AWarningDoesNotLeakToTheNextCallOrToAnotherNode()
    {
        var graph = new GraphModel();
        var source = ZT.Value(graph, L(-1.0, 2.0, 3.0));
        var warns = Node("WarnIfNegative");
        var clean = ZT.Node("Sqrt");
        graph.AddNode(warns);
        graph.AddNode(clean);
        ZT.Wire(graph, source, 0, warns, 0);
        ZT.Wire(graph, warns, 0, clean, 0);

        new GraphEngine().Run(graph);

        Assert.Equal("1 of 3 calls: negative value -1", Assert.Single(warns.Messages).Text);
        Assert.Equal(NodeState.Executed, clean.State);
        Assert.Empty(clean.Messages);
    }

    [Fact]
    public void RepeatedMessagesOfOneCallAreCountedNotListedTwice()
    {
        var (_, node) = Run("WarnRepeatedly", 1.0);

        Assert.Equal(new[] { "same (2 times)", "other" }, node.Messages.Select(m => m.Text).ToArray());
    }

    [Fact]
    public void ManyDifferentWarningsAreCappedWithACount()
    {
        var (_, node) = Run("WarnMany", 1.0);

        Assert.Equal(6, node.Messages.Count);
        Assert.Equal("… and 95 more warning(s).", node.Messages.Last().Text);
    }

    [Fact]
    public void AddOutsideARunIsHarmless()
    {
        NodeWarnings.Add("nobody is listening");
        NodeWarnings.Add(null!);
        NodeWarnings.Add("   ");
        PlatformFixtures.WarnIfNegative(-1.0);
    }

    [Fact]
    public void EvaluatingTheNodeDirectlyStillShowsItsWarnings()
    {
        var node = Node("WarnIfNegative");

        var outputs = node.Evaluate(new object?[] { -5.0 }, new EvaluationContext());

        Assert.Equal(5.0, outputs[0]);
        Assert.Contains("negative value -5", node.StateMessage);
    }

    [Fact]
    public void ANodeInsideAGroupWarnsOnItsOwnNodeNotOnTheCollectorOfTheInstance()
    {
        var doc = new GraphModel();
        var group = doc.NodeGroups.Create("Inner");
        group.AddSocket(SocketSide.Input, "x");
        group.AddSocket(SocketSide.Output, "y");
        var warns = Node("WarnIfNegative");
        group.Graph.AddNode(warns);
        ZT.Wire(group.Graph, group.InputNode, 0, warns, 0);
        ZT.Wire(group.Graph, warns, 0, group.OutputNode, 0);
        var instance = new GroupInstanceNode(group);
        doc.AddNode(instance);
        var source = ZT.Value(doc, -2.0);
        ZT.Wire(doc, source, 0, instance, 0);

        new GraphEngine().Run(doc);

        Assert.Contains("negative value -2", warns.StateMessage);
        Assert.Equal(2.0, instance.OutPorts[0].Value);
        Assert.DoesNotContain("negative value", instance.StateMessage);
    }

    // -------------------------------------------------------------- AcceptsNull

    [Fact]
    public void AcceptsNullPassesTheNullElementToTheNode()
    {
        var (_, node) = Run("IsNullish", L("a", null, "c"));

        Assert.Equal(L(false, true, false), node.OutPorts[0].Value);
        Assert.Equal(NodeState.Executed, node.State);
        Assert.Empty(node.Messages);
    }

    [Fact]
    public void WithoutAcceptsNullANullElementStillGivesANullResultAndAWarning()
    {
        var graph = new GraphModel();
        var source = ZT.Value(graph, L("a", null));
        var node = ZT.Node("Shout");
        graph.AddNode(node);
        ZT.Wire(graph, source, 0, node, 0);

        new GraphEngine().Run(graph);

        Assert.Equal(L("A!", null), node.OutPorts[0].Value);
        Assert.Equal(NodeState.Warning, node.State);
    }

    [Fact]
    public void AcceptsNullOnlyAffectsTheMarkedParameter()
    {
        var graph = new GraphModel();
        var texts = ZT.Value(graph, L("a", null));
        var others = ZT.Value(graph, L("x", null));
        var node = Node("BlankToDash");
        graph.AddNode(node);
        ZT.Wire(graph, texts, 0, node, 0);
        ZT.Wire(graph, others, 0, node, 1);

        new GraphEngine().Run(graph);

        // Second element: 'other' is null and not marked, so the call is skipped as before; first element computes.
        Assert.Equal(L("a", null), node.OutPorts[0].Value);
        Assert.Equal(NodeState.Warning, node.State);
    }

    [Fact]
    public void AcceptsNullOnAValueTypeStillWarnsAboutTheNull()
    {
        var (_, node) = Run("NullableDouble", L(1.0, null));

        Assert.Equal(L(1.0, null), node.OutPorts[0].Value);
        Assert.Equal(NodeState.Warning, node.State);
        Assert.Contains("Null value passed to input 'x'", node.StateMessage);
    }

    [Fact]
    public void TheLoaderRecordsAcceptsNullOnThePort()
    {
        Assert.True(Node("IsNullish").InPorts[0].AcceptsNull);
        Assert.False(ZT.Node("Shout").InPorts[0].AcceptsNull);
    }

    // ------------------------------------------------------------- ScalarInput

    [Fact]
    public void AScalarObjectPortMapsTheNodeOverAList()
    {
        var (_, node) = Run("WhatIs", L("a", 2.5, true));

        Assert.Equal(L("String", "Double", "Boolean"), node.OutPorts[0].Value);
    }

    [Fact]
    public void AScalarObjectPortStillTakesASingleValue()
    {
        var (_, node) = Run("WhatIs", 7);

        Assert.Equal("Int32", node.OutPorts[0].Value);
    }

    [Fact]
    public void AScalarObjectPortMapsNestedListsLevelByLevel()
    {
        var (_, node) = Run("WhatIs", L(L("a", 1), L(2.5)));

        Assert.Equal(L(L("String", "Int32"), L("Double")), node.OutPorts[0].Value);
    }

    [Fact]
    public void APlainObjectPortStillReceivesTheListWhole()
    {
        var (_, node) = Run("WhatIsAny", L("a", 2.5));

        Assert.Equal("List`1", node.OutPorts[0].Value);
    }

    [Fact]
    public void ListLevelsCanStillHandTheWholeListToAScalarObjectPort()
    {
        var graph = new GraphModel();
        var source = ZT.Value(graph, L("a", 2.5));
        var node = Node("WhatIs");
        graph.AddNode(node);
        ZT.Wire(graph, source, 0, node, 0);
        node.InPorts[0].SetLevels(true, 2, true);

        new GraphEngine().Run(graph);

        Assert.Equal("List`1", node.OutPorts[0].Value);
    }

    [Fact]
    public void ScalarInputIsIgnoredOnAParameterThatIsNotAnObject()
    {
        Assert.False(Node("NotAnObject").InPorts[0].IsScalarInput);
        Assert.True(Node("WhatIs").InPorts[0].IsScalarInput);
    }

    [Fact]
    public void AScalarObjectPortIsShownAsASingleItem()
    {
        var kind = PortKinds.FromPort(Node("WhatIs").InPorts[0]);
        var plain = PortKinds.FromPort(Node("WhatIsAny").InPorts[0]);

        Assert.Equal(PortDepth.Item, kind.Depth);
        Assert.Equal(PortFamily.Any, kind.Family);
        Assert.Equal(PortDepth.Unknown, plain.Depth);
        Assert.Equal(PortEditorKind.None, PortEditors.Resolve(Node("WhatIs").InPorts[0]));
    }

    // --------------------------------------------------------------- LiveState

    private static void ResetLive()
    {
        PlatformFixtures.LiveReads = 0;
        PlatformFixtures.LiveValue = 1;
        PlatformFixtures.DownstreamCalls = 0;
    }

    private static (GraphModel Graph, ZeroTouchNodeModel Live, ZeroTouchNodeModel After) LiveGraph()
    {
        var graph = new GraphModel();
        var live = Node("ReadLive");
        // Downstream(int) and Downstream(object) exist: take the int overload.
        var after = new ZeroTouchNodeModel(Definitions.Single(d => d.Method.Name == "Downstream" && d.Method.GetParameters()[0].ParameterType == typeof(int)));
        graph.AddNode(live);
        graph.AddNode(after);
        ZT.Wire(graph, live, 0, after, 0);
        return (graph, live, after);
    }

    [Fact]
    public void ALiveStateNodeRunsOnEveryRunAndSeesTheNewState()
    {
        ResetLive();
        var (graph, live, after) = LiveGraph();
        var engine = new GraphEngine();

        engine.Run(graph);
        Assert.Equal(1, live.OutPorts[0].Value);
        Assert.Equal(10, after.OutPorts[0].Value);

        PlatformFixtures.LiveValue = 4;   // the user changed the selection; nothing in the graph changed
        engine.Run(graph);

        Assert.Equal(2, PlatformFixtures.LiveReads);
        Assert.Equal(4, live.OutPorts[0].Value);
        Assert.Equal(40, after.OutPorts[0].Value);
    }

    [Fact]
    public void NodesAfterALiveStateNodeRunAgainOnlyWhenItsOutputChanged()
    {
        ResetLive();
        var (graph, _, after) = LiveGraph();
        var engine = new GraphEngine();

        engine.Run(graph);
        var second = engine.Run(graph);   // same state: the live node reads again, the node after it does not

        Assert.Equal(2, PlatformFixtures.LiveReads);
        Assert.Equal(1, PlatformFixtures.DownstreamCalls);
        Assert.Single(second.ExecutedNodes);
        Assert.Equal(10, after.OutPorts[0].Value);

        PlatformFixtures.LiveValue = 2;
        var third = engine.Run(graph);

        Assert.Equal(2, PlatformFixtures.DownstreamCalls);
        Assert.Equal(2, third.ExecutedNodes.Count);
        Assert.Equal(2, third.PlannedCount);
        Assert.Equal(20, after.OutPorts[0].Value);
    }

    [Fact]
    public void ALiveListOutputIsComparedByContent()
    {
        ResetLive();
        var graph = new GraphModel();
        var live = Node("ReadLiveList");
        var after = new ZeroTouchNodeModel(Definitions.Single(d => d.Method.Name == "Downstream" && d.Method.GetParameters()[0].ParameterType == typeof(object)));
        graph.AddNode(live);
        graph.AddNode(after);
        ZT.Wire(graph, live, 0, after, 0);
        var engine = new GraphEngine();

        engine.Run(graph);
        engine.Run(graph);

        Assert.Equal(2, PlatformFixtures.LiveReads);
        Assert.Equal(1, PlatformFixtures.DownstreamCalls);
    }

    [Fact]
    public void RunningALiveStateNodeDoesNotCountAsAnEditOfTheGraph()
    {
        ResetLive();
        var (graph, _, _) = LiveGraph();
        var engine = new GraphEngine();
        engine.Run(graph);
        var modified = 0;
        graph.Modified += (_, _) => modified++;

        PlatformFixtures.LiveValue = 3;
        engine.Run(graph);

        Assert.Equal(0, modified);
    }

    [Fact]
    public void AFrozenOrMutedLiveStateNodeIsLeftAlone()
    {
        ResetLive();
        var (graph, live, _) = LiveGraph();
        var engine = new GraphEngine();
        engine.Run(graph);

        live.IsFrozen = true;
        engine.Run(graph);
        Assert.Equal(1, PlatformFixtures.LiveReads);
        live.IsFrozen = false;
        live.IsMuted = true;
        engine.Run(graph);
        Assert.Equal(1, PlatformFixtures.LiveReads);
    }

    [Fact]
    public void AGroupWithALiveStateNodeInsideRunsOnEveryRun()
    {
        ResetLive();
        var doc = new GraphModel();
        var group = doc.NodeGroups.Create("Reader");
        group.AddSocket(SocketSide.Output, "value");
        var live = Node("ReadLive");
        group.Graph.AddNode(live);
        ZT.Wire(group.Graph, live, 0, group.OutputNode, 0);
        var instance = new GroupInstanceNode(group);
        doc.AddNode(instance);
        var engine = new GraphEngine();

        Assert.True(group.ContainsLiveState);
        Assert.True(instance.IsLiveState);
        engine.Run(doc);
        PlatformFixtures.LiveValue = 9;
        engine.Run(doc);

        Assert.Equal(2, PlatformFixtures.LiveReads);
        Assert.Equal(9, instance.OutPorts[0].Value);
    }

    [Fact]
    public void AGroupWithoutALiveStateNodeIsNotLive()
    {
        var doc = new GraphModel();
        var group = doc.NodeGroups.Create("Plain");
        group.AddSocket(SocketSide.Input, "x");
        var step = ZT.Node("AddStep");
        group.Graph.AddNode(step);

        Assert.False(group.ContainsLiveState);
        Assert.False(new GroupInstanceNode(group).IsLiveState);
    }

    [Fact]
    public void TheLoaderRecordsLiveState()
    {
        Assert.True(Node("ReadLive").IsLiveState);
        Assert.False(ZT.Node("Sqrt").IsLiveState);
    }
}
