using System.Linq;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Nodes;
using Xunit;

namespace Dyncamelo.Integration.Tests;

/// <summary>End-to-end math and string chains through build -> save -> load -> run.</summary>
public class MathAndStringChainTests
{
    [Fact]
    public void MathChain_ComputesThroughSaveLoadRun()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "math-chain" };

        var a = new NumberInputNode { Name = "A", Value = 3 };
        var b = new NumberInputNode { Name = "B", Value = 4 };
        var factor = new NumberInputNode { Name = "Factor", Value = 2 };
        var add = Pipeline.ZeroTouch(registry, "Add", "Sum");
        var multiply = Pipeline.ZeroTouch(registry, "Multiply", "Product");
        var watch = new WatchNode { Name = "Watch" };

        graph.AddNode(a);
        graph.AddNode(b);
        graph.AddNode(factor);
        graph.AddNode(add);
        graph.AddNode(multiply);
        graph.AddNode(watch);

        Pipeline.Connect(graph, a, "value", add, "a");
        Pipeline.Connect(graph, b, "value", add, "b");
        Pipeline.Connect(graph, add, "result", multiply, "a");
        Pipeline.Connect(graph, factor, "value", multiply, "b");
        Pipeline.Connect(graph, multiply, "result", watch, "value");

        var run = Pipeline.SaveLoadAndRun(graph, registry, out var result);

        Assert.True(result.Success);
        Assert.Equal(6, result.ExecutedNodes.Count);
        Assert.All(run.Nodes, n => Assert.Equal(NodeState.Executed, n.State));
        Assert.Equal(7d, Pipeline.Output(run, "Sum"));
        Assert.Equal(14d, Pipeline.Output(run, "Product"));
        Assert.Equal(14d, Pipeline.Output(run, "Watch"));
        Assert.Equal("14", ((WatchNode)Pipeline.Node(run, "Watch")).FormattedValue);
    }

    [Fact]
    public void StringPipeline_SplitCountJoinThroughSaveLoadRun()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "string-pipeline" };

        var text = new StringInputNode { Name = "Text", Value = "hello brave new world" };
        var space = new StringInputNode { Name = "Space", Value = " " };
        var dash = new StringInputNode { Name = "Dash", Value = "-" };
        var split = Pipeline.ZeroTouch(registry, "String.Split", "Split");
        var count = Pipeline.ZeroTouch(registry, "List.Count", "Count");
        var join = Pipeline.ZeroTouch(registry, "String.Join", "Join");
        var length = Pipeline.ZeroTouch(registry, "String.Length", "Length");

        graph.AddNode(text);
        graph.AddNode(space);
        graph.AddNode(dash);
        graph.AddNode(split);
        graph.AddNode(count);
        graph.AddNode(join);
        graph.AddNode(length);

        Pipeline.Connect(graph, text, "value", split, "str");
        Pipeline.Connect(graph, space, "value", split, "separator");
        Pipeline.Connect(graph, split, "list", count, "list");
        Pipeline.Connect(graph, dash, "value", join, "separator");
        Pipeline.Connect(graph, split, "list", join, "list");
        Pipeline.Connect(graph, join, "result", length, "str");

        var run = Pipeline.SaveLoadAndRun(graph, registry, out var result);

        Assert.True(result.Success);
        Assert.All(run.Nodes, n => Assert.Equal(NodeState.Executed, n.State));
        Assert.Equal(
            new[] { "hello", "brave", "new", "world" },
            Pipeline.AsList(Pipeline.Output(run, "Split")).Cast<string>().ToArray());
        Assert.Equal(4, Pipeline.Output(run, "Count"));
        Assert.Equal("hello-brave-new-world", Pipeline.Output(run, "Join"));
        Assert.Equal(21, Pipeline.Output(run, "Length"));
    }
}
