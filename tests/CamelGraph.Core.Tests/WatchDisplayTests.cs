using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Nodes;
using CamelGraph.Core.Player;
using CamelGraph.Core.Serialization;
using CamelGraph.Core.Tests.Fixtures;
using CamelGraph.Core.Types;
using Xunit;

namespace CamelGraph.Core.Tests;

/// <summary>The Watch node never shows an old value as the current result, and never builds a text of tens of megabytes.</summary>
public sealed class WatchDisplayTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dyc-watch-" + Guid.NewGuid().ToString("N"));

    public WatchDisplayTests() => Directory.CreateDirectory(_folder);

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

    // number -> 1 / x (fails for 0) -> watch
    private static (GraphModel Graph, NumberInputNode Number, ZeroTouchNodeModel Reciprocal, WatchNode Watch) Rig()
    {
        var graph = new GraphModel { Name = "watch rig" };
        var number = new NumberInputNode { Name = "Number", Value = 4 };
        var reciprocal = ZT.Node("ReciprocalStrict");
        var watch = new WatchNode { Name = "Result" };
        graph.AddNode(number);
        graph.AddNode(reciprocal);
        graph.AddNode(watch);
        ZT.Wire(graph, number, 0, reciprocal, 0);
        ZT.Wire(graph, reciprocal, 0, watch, 0);
        return (graph, number, reciprocal, watch);
    }

    [Fact]
    public void AWatchWhoseUpstreamFailedNoLongerShowsTheOldValue()
    {
        var (graph, number, _, watch) = Rig();
        var engine = new GraphEngine();
        engine.Run(graph);
        Assert.Equal("0.25", watch.FormattedValue);
        Assert.Equal("0.25", watch.PlayerText);

        number.Value = 0;
        engine.Run(graph);

        Assert.Equal(NodeState.Warning, watch.State);
        Assert.Equal(string.Empty, watch.FormattedValue);
        Assert.Equal(string.Empty, watch.PlayerText);
    }

    [Fact]
    public void AWatchThatIsUnwiredOrSwitchedOffOrMutedIsCleared()
    {
        var (graph, _, reciprocal, watch) = Rig();
        var engine = new GraphEngine();
        engine.Run(graph);
        Assert.Equal("0.25", watch.FormattedValue);

        // muted: the node does not evaluate
        watch.IsMuted = true;
        engine.Run(graph);
        Assert.Equal(string.Empty, watch.FormattedValue);
        watch.IsMuted = false;
        engine.Run(graph);
        Assert.Equal("0.25", watch.FormattedValue);

        // a branch that was switched off
        var off = ZT.Value(graph, InactiveValue.Instance);
        graph.Disconnect(graph.FindConnectionInto(watch.InPorts[0])!);
        ZT.Wire(graph, off, 0, watch, 0);
        engine.Run(graph);
        Assert.Equal(NodeState.Idle, watch.State);
        Assert.Equal(string.Empty, watch.FormattedValue);

        // unwired
        engine.Run(graph);
        graph.Disconnect(graph.FindConnectionInto(watch.InPorts[0])!);
        engine.Run(graph);
        Assert.Equal(NodeState.Idle, watch.State);
        Assert.Equal(string.Empty, watch.FormattedValue);
        Assert.NotNull(reciprocal);
    }

    [Fact]
    public void ThePlayerSaysThereIsNoValueInsteadOfShowingTheOldOne()
    {
        var (graph, number, _, _) = Rig();
        var registry = NodeRegistry();
        var path = Path.Combine(_folder, "script.dyc");
        new GraphSerializer(registry).SaveToFile(graph, path);
        var session = ScriptSession.Load(path, registry);

        var first = session.Run(new EvaluationContext());
        Assert.Equal("0.25", Assert.Single(first.Outputs).Text);
        Assert.True(first.Outputs[0].HasValue);

        PortEditors_SetNumber(session, 0);
        var second = session.Run(new EvaluationContext());

        var output = Assert.Single(second.Outputs);
        Assert.False(output.HasValue);
        Assert.StartsWith("(no value:", output.Text);
        Assert.DoesNotContain("0.25", output.Text);
        Assert.NotNull(number);
    }

    private static void PortEditors_SetNumber(ScriptSession session, double value) =>
        CamelGraph.Core.Editing.PortEditors.SetNumber(session.Fields[0].Port, value);

    private static CamelGraph.Core.Loader.NodeRegistry NodeRegistry()
    {
        var registry = CamelGraph.Core.Loader.NodeRegistry.CreateDefault();
        registry.RegisterAssembly(typeof(MathFixtures).Assembly);
        return registry;
    }

    // ----------------------------------------------------------------- caps

    [Fact]
    public void ALongListShowsItsFirstItemsAndCountsTheRest()
    {
        var watch = new WatchNode();
        var list = Enumerable.Range(0, 5000).Select(i => (object?)i).ToList();

        watch.Evaluate(new object?[] { list }, new EvaluationContext());

        Assert.StartsWith("[0, 1, 2,", watch.FormattedValue);
        Assert.EndsWith("999, … 4,000 more]", watch.FormattedValue);
        Assert.DoesNotContain("1000,", watch.FormattedValue);
    }

    [Fact]
    public void AShortValueIsFormattedExactlyAsBefore()
    {
        var watch = new WatchNode();
        var value = new List<object?> { 1.5, "a", null, new List<object?> { 2, 3 }, new Dictionary<string, object> { ["k"] = 1 } };

        watch.Evaluate(new object?[] { value }, new EvaluationContext());

        Assert.Equal(TypeCoercion.FormatValue(value), watch.FormattedValue);
        Assert.Equal("[1.5, a, null, [2, 3], {k : 1}]", watch.FormattedValue);
    }

    [Fact]
    public void ThePlayerTextOfAListIsACountAndOneItemPerLine()
    {
        var watch = new WatchNode();

        watch.Evaluate(new object?[] { new List<object?> { 10, 20, 30 } }, new EvaluationContext());
        Assert.Equal("3 items\n10\n20\n30", watch.PlayerText);

        watch.Evaluate(new object?[] { new List<object?> { "only" } }, new EvaluationContext());
        Assert.Equal("1 item\nonly", watch.PlayerText);

        watch.Evaluate(new object?[] { "plain" }, new EvaluationContext());
        Assert.Equal("plain", watch.PlayerText);
    }

    [Fact]
    public void ThePlayerTextOfALongListFitsWhatThePlayerKeeps()
    {
        var watch = new WatchNode();
        var list = Enumerable.Range(0, 500).Select(i => (object?)i).ToList();

        watch.Evaluate(new object?[] { list }, new EvaluationContext());

        var lines = watch.PlayerText.Split('\n');
        Assert.Equal(ScriptSession.MaxOutputLines, lines.Length);
        Assert.Equal("500 items", lines[0]);
        Assert.Equal("0", lines[1]);
        Assert.Equal("… 202 more items", lines[lines.Length - 1]);
    }

    [Fact]
    public void ValueTextCapsNestedValuesAndKeepsSmallOnesIdentical()
    {
        var nested = new List<object?> { new List<object?> { 1, 2, 3 }, new List<object?> { 4, 5, 6 }, new List<object?> { 7, 8, 9 } };

        Assert.Equal("[[1, 2, 3], [4, 5, 6], [7, 8, 9]]", ValueText.Format(nested, 100));
        Assert.Equal("[[1, 2, 3], [4, 5, … 1 more], … 1 more]", ValueText.Format(nested, 5));
        Assert.Equal("null", ValueText.Format(null, 5));
        Assert.Equal("[]", ValueText.Format(new List<object?>(), 5));
        var longText = new string('x', ValueText.MaxStringLength + 10);
        Assert.EndsWith("… 10 more characters", ValueText.Format(longText, 5));
    }
}
