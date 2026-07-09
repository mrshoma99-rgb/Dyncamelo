using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Nodes;
using Xunit;

namespace Dyncamelo.Integration.Tests;

/// <summary>Engine semantics observed through the public pipeline: error isolation and dirty re-runs.</summary>
public class ExecutionSemanticsTests
{
    [Fact]
    public void ErrorIsolation_FailingNodeDoesNotAbortTheRun()
    {
        // Branch 1 (broken): "not a number" -> String.ToNumber (throws) -> Add -> Watch.
        // Branch 2 (healthy, independent): 5 + 2 -> executes normally.
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "error-isolation" };

        var badText = new StringInputNode { Name = "BadText", Value = "not a number" };
        var toNumber = Pipeline.ZeroTouch(registry, "String.ToNumber", "Broken");
        var one = new NumberInputNode { Name = "One", Value = 1 };
        var downstream = Pipeline.ZeroTouch(registry, "Add", "Downstream");
        var watch = new WatchNode { Name = "BrokenWatch" };

        var c = new NumberInputNode { Name = "C", Value = 5 };
        var d = new NumberInputNode { Name = "D", Value = 2 };
        var healthy = Pipeline.ZeroTouch(registry, "Add", "Healthy");

        graph.AddNode(badText);
        graph.AddNode(toNumber);
        graph.AddNode(one);
        graph.AddNode(downstream);
        graph.AddNode(watch);
        graph.AddNode(c);
        graph.AddNode(d);
        graph.AddNode(healthy);

        Pipeline.Connect(graph, badText, "value", toNumber, "str");
        Pipeline.Connect(graph, toNumber, "result", downstream, "a");
        Pipeline.Connect(graph, one, "value", downstream, "b");
        Pipeline.Connect(graph, downstream, "result", watch, "value");
        Pipeline.Connect(graph, c, "value", healthy, "a");
        Pipeline.Connect(graph, d, "value", healthy, "b");

        var run = Pipeline.SaveLoadAndRun(graph, registry, out var result);

        // The run completes despite the mid-graph failure.
        Assert.True(result.Success);

        var broken = Pipeline.Node(run, "Broken");
        Assert.Equal(NodeState.Error, broken.State);
        Assert.Contains(broken.Messages, m => m.Severity == MessageSeverity.Error);
        Assert.Null(broken.OutPorts[0].Value);

        // Direct downstream of the error: Warning + null outputs, no exception.
        var downstreamNode = Pipeline.Node(run, "Downstream");
        Assert.Equal(NodeState.Warning, downstreamNode.State);
        Assert.Contains(downstreamNode.Messages, m => m.Text.Contains("Upstream failure"));
        Assert.Null(downstreamNode.OutPorts[0].Value);

        // The independent branch is untouched.
        Assert.Equal(NodeState.Executed, Pipeline.Node(run, "Healthy").State);
        Assert.Equal(7d, Pipeline.Output(run, "Healthy"));
    }

    [Fact]
    public void DirtyRerun_OnlyDownstreamOfTheChangeReExecutes()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "dirty-rerun" };

        var a = new NumberInputNode { Name = "A", Value = 2 };
        var b = new NumberInputNode { Name = "B", Value = 3 };
        var sum = Pipeline.ZeroTouch(registry, "Add", "Sum");
        var watch = new WatchNode { Name = "SumWatch" };
        var c = new NumberInputNode { Name = "C", Value = 10 };
        var d = new NumberInputNode { Name = "D", Value = 4 };
        var other = Pipeline.ZeroTouch(registry, "Multiply", "Other");

        graph.AddNode(a);
        graph.AddNode(b);
        graph.AddNode(sum);
        graph.AddNode(watch);
        graph.AddNode(c);
        graph.AddNode(d);
        graph.AddNode(other);

        Pipeline.Connect(graph, a, "value", sum, "a");
        Pipeline.Connect(graph, b, "value", sum, "b");
        Pipeline.Connect(graph, sum, "result", watch, "value");
        Pipeline.Connect(graph, c, "value", other, "a");
        Pipeline.Connect(graph, d, "value", other, "b");

        var run = Pipeline.SaveLoad(graph, registry);
        var engine = new GraphEngine();

        var first = engine.Run(run);
        Assert.Equal(7, first.ExecutedNodes.Count); // everything, first time
        Assert.Equal(5d, Pipeline.Output(run, "Sum"));
        Assert.Equal(40d, Pipeline.Output(run, "Other"));

        // Change one input value on the reloaded graph.
        ((NumberInputNode)Pipeline.Node(run, "A")).Value = 5;

        var second = engine.Run(run);

        // Exactly the changed node and its transitive downstream re-executed.
        Assert.Equal(
            new[] { "A", "Sum", "SumWatch" },
            second.ExecutedNodes.Select(n => n.Name).OrderBy(n => n).ToArray());
        Assert.DoesNotContain(second.ExecutedNodes, n => n.Name == "Other" || n.Name == "B" || n.Name == "C" || n.Name == "D");

        // Values updated downstream, cached elsewhere.
        Assert.Equal(8d, Pipeline.Output(run, "Sum"));
        Assert.Equal(8d, Pipeline.Output(run, "SumWatch"));
        Assert.Equal(40d, Pipeline.Output(run, "Other"));

        // A third run with no changes executes nothing.
        var third = engine.Run(run);
        Assert.Empty(third.ExecutedNodes);
    }
}
