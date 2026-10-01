using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Nodes;
using Xunit;

namespace Dyncamelo.Integration.Tests;

/// <summary>Flow.When switches a branch off (idle, not red); Flow.Try catches the failure of the node before it.</summary>
public class ConditionalFlowTests
{
    private static GraphModel WhenGraph(bool condition, out Dyncamelo.Core.Loader.NodeRegistry registry)
    {
        registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "when" };

        var number = new NumberInputNode { Name = "Number", Value = 4 };
        var flag = new BooleanToggleNode { Name = "Flag", Value = condition };
        var when = Pipeline.ZeroTouch(registry, "Flow.When", "Gate");
        var square = Pipeline.ZeroTouch(registry, "Math.Sqrt", "After");
        var again = Pipeline.ZeroTouch(registry, "Math.Sqrt", "AfterAfter");
        var sideBranch = Pipeline.ZeroTouch(registry, "Math.Sqrt", "Side");

        foreach (var node in new NodeModel[] { number, flag, when, square, again, sideBranch })
        {
            graph.AddNode(node);
        }

        Pipeline.Connect(graph, number, "value", when, "value");
        Pipeline.Connect(graph, flag, "value", when, "condition");
        Pipeline.Connect(graph, when, "value", square, "number");
        Pipeline.Connect(graph, square, "result", again, "number");
        Pipeline.Connect(graph, number, "value", sideBranch, "number");
        return graph;
    }

    [Fact]
    public void WhenTrue_TheBranchRunsAsIfTheGateWerenTThere()
    {
        var graph = WhenGraph(true, out var registry);
        var run = Pipeline.SaveLoadAndRun(graph, registry, out var result);

        Assert.True(result.Success);
        Assert.Equal(4d, Pipeline.Output(run, "Gate"));
        Assert.Equal(2d, Pipeline.Output(run, "After"));
        Assert.Equal(NodeState.Executed, Pipeline.Node(run, "AfterAfter").State);
    }

    [Fact]
    public void WhenFalse_EveryNodeAfterItIsIdleAndNothingTurnsRed()
    {
        var graph = WhenGraph(false, out var registry);
        var run = Pipeline.SaveLoadAndRun(graph, registry, out var result);

        Assert.True(result.Success);
        Assert.Same(InactiveValue.Instance, Pipeline.Output(run, "Gate"));

        foreach (var name in new[] { "After", "AfterAfter" })
        {
            var node = Pipeline.Node(run, name);
            Assert.Equal(NodeState.Idle, node.State);
            Assert.Same(InactiveValue.Instance, node.OutPorts[0].Value);
            Assert.Contains(node.Messages, m => m.Text.Contains("Skipped") && m.Severity == MessageSeverity.Info);
        }

        // A branch that does not go through the gate is unaffected.
        Assert.Equal(NodeState.Executed, Pipeline.Node(run, "Side").State);
        Assert.Equal(2d, Pipeline.Output(run, "Side"));
    }

    [Fact]
    public void WhenFalse_ThenTrueAgain_TheBranchRunsOnTheNextRun()
    {
        var graph = WhenGraph(false, out var registry);
        var run = Pipeline.SaveLoad(graph, registry);
        var engine = new GraphEngine();
        engine.Run(run);
        Assert.Equal(NodeState.Idle, Pipeline.Node(run, "After").State);

        ((BooleanToggleNode)Pipeline.Node(run, "Flag")).Value = true;
        engine.Run(run);

        Assert.Equal(NodeState.Executed, Pipeline.Node(run, "After").State);
        Assert.Equal(2d, Pipeline.Output(run, "After"));
    }

    [Fact]
    public void ASkippedBranchContributesNothingToAMultiInputSocket()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "multi" };
        var a = new NumberInputNode { Name = "A", Value = 1 };
        var b = new NumberInputNode { Name = "B", Value = 2 };
        var off = new BooleanToggleNode { Name = "Off", Value = false };
        var gate = Pipeline.ZeroTouch(registry, "Flow.When", "Gate");
        var count = Pipeline.ZeroTouch(registry, "List.CountTrue", "Count");
        var t1 = new BooleanToggleNode { Name = "T1", Value = true };
        var t2 = new BooleanToggleNode { Name = "T2", Value = true };
        foreach (var node in new NodeModel[] { a, b, off, gate, count, t1, t2 })
        {
            graph.AddNode(node);
        }

        Pipeline.Connect(graph, t1, "value", count, "list");
        Pipeline.Connect(graph, t2, "value", count, "list");
        Pipeline.Connect(graph, a, "value", gate, "value");
        Pipeline.Connect(graph, off, "value", gate, "condition");
        Pipeline.Connect(graph, gate, "value", count, "list");

        var run = Pipeline.SaveLoadAndRun(graph, registry, out _);

        Assert.Equal(NodeState.Executed, Pipeline.Node(run, "Count").State);
        Assert.Equal(2, System.Convert.ToInt32(Pipeline.Output(run, "Count", "trueCount")));
    }

    [Fact]
    public void Try_PassesTheResultThroughWhenNothingFailed()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "try-ok" };
        var number = new NumberInputNode { Name = "Number", Value = 9 };
        var sqrt = Pipeline.ZeroTouch(registry, "Math.Sqrt", "Sqrt");
        var attempt = Pipeline.ZeroTouch(registry, "Flow.Try", "Attempt");
        foreach (var node in new NodeModel[] { number, sqrt, attempt })
        {
            graph.AddNode(node);
        }

        Pipeline.Connect(graph, number, "value", sqrt, "number");
        Pipeline.Connect(graph, sqrt, "result", attempt, "value");

        var run = Pipeline.SaveLoadAndRun(graph, registry, out _);

        Assert.Equal(3d, Pipeline.Output(run, "Attempt", "result"));
        Assert.Equal(false, Pipeline.Output(run, "Attempt", "failed"));
        Assert.Equal(string.Empty, Pipeline.Output(run, "Attempt", "error"));
    }

    [Fact]
    public void Try_AfterAFailure_GivesTheFallbackAndTheReason_AndTheRestOfTheGraphRuns()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "try-fail" };
        var text = new StringInputNode { Name = "Text", Value = "not a number" };
        var toNumber = Pipeline.ZeroTouch(registry, "String.ToNumber", "Broken");
        var fallback = new NumberInputNode { Name = "Fallback", Value = -1 };
        var attempt = Pipeline.ZeroTouch(registry, "Flow.Try", "Attempt");
        var after = Pipeline.ZeroTouch(registry, "Math.Abs", "After");
        foreach (var node in new NodeModel[] { text, toNumber, fallback, attempt, after })
        {
            graph.AddNode(node);
        }

        Pipeline.Connect(graph, text, "value", toNumber, "text");
        Pipeline.Connect(graph, toNumber, "result", attempt, "value");
        Pipeline.Connect(graph, fallback, "value", attempt, "fallback");
        Pipeline.Connect(graph, attempt, "result", after, "number");

        var run = Pipeline.SaveLoadAndRun(graph, registry, out var result);

        Assert.True(result.Success);
        Assert.Equal(NodeState.Error, Pipeline.Node(run, "Broken").State);
        Assert.Equal(NodeState.Executed, Pipeline.Node(run, "Attempt").State);
        Assert.Equal(-1d, Pipeline.Output(run, "Attempt", "result"));
        Assert.Equal(true, Pipeline.Output(run, "Attempt", "failed"));
        var error = Assert.IsType<string>(Pipeline.Output(run, "Attempt", "error"));
        Assert.Contains("Broken", error);
        Assert.Equal(NodeState.Executed, Pipeline.Node(run, "After").State);
        Assert.Equal(1d, Pipeline.Output(run, "After"));
    }

    [Fact]
    public void Try_SeesThroughAChainOfStoppedNodesToTheNodeThatFailed()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "try-chain" };
        var text = new StringInputNode { Name = "Text", Value = "x" };
        var broken = Pipeline.ZeroTouch(registry, "String.ToNumber", "Broken");
        var middle = Pipeline.ZeroTouch(registry, "Math.Abs", "Middle");
        var attempt = Pipeline.ZeroTouch(registry, "Flow.Try", "Attempt");
        foreach (var node in new NodeModel[] { text, broken, middle, attempt })
        {
            graph.AddNode(node);
        }

        Pipeline.Connect(graph, text, "value", broken, "text");
        Pipeline.Connect(graph, broken, "result", middle, "number");
        Pipeline.Connect(graph, middle, "result", attempt, "value");

        var run = Pipeline.SaveLoadAndRun(graph, registry, out _);

        Assert.Equal(NodeState.Warning, Pipeline.Node(run, "Middle").State);
        Assert.Equal(true, Pipeline.Output(run, "Attempt", "failed"));
        Assert.Contains("Broken", (string)Pipeline.Output(run, "Attempt", "error")!);
    }

    [Fact]
    public void ANodeWithoutTheCatchAttributeIsStillStoppedByAFailure()
    {
        var registry = Pipeline.CreateRegistry();
        var definition = registry.Definitions.Single(d => d.Name == "Flow.Try");
        Assert.True(definition.CatchesUpstreamErrors);
        Assert.All(
            registry.Definitions.Where(d => d.Name != "Flow.Try"),
            d => Assert.False(d.CatchesUpstreamErrors, d.Name));
    }
}
