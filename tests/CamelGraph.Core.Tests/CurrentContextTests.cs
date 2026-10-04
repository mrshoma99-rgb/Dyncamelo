using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Tests.Fixtures;
using Xunit;

namespace CamelGraph.Core.Tests;

/// <summary>Zero-touch nodes that look at the run they belong to through <see cref="EvaluationContext.Current"/>.</summary>
public static class CurrentContextFixtures
{
    public static EvaluationContext? Seen;
    public static int Passed;

    /// <summary>Remembers the context of the run and walks through its steps with a checkpoint in between.</summary>
    public static double Steps(double count)
    {
        Seen = EvaluationContext.Current;
        for (var i = 0; i < (int)count; i++)
        {
            EvaluationContext.Current?.Checkpoint();
            Passed++;
        }

        return count;
    }
}

public class CurrentContextTests
{
    private static readonly List<NodeDefinition> Definitions = AssemblyNodeLoader.LoadType(typeof(CurrentContextFixtures));

    private static ZeroTouchNodeModel Steps() => new ZeroTouchNodeModel(Definitions.Single(d => d.Method.Name == "Steps"));

    [Fact]
    public void NoRunMeansNoCurrentContext()
    {
        Assert.Null(EvaluationContext.Current);
    }

    [Fact]
    public void ANodeSeesTheContextOfTheRunThatCallsIt()
    {
        CurrentContextFixtures.Seen = null;
        var graph = new GraphModel();
        var source = ZT.Value(graph, 2.0);
        var node = Steps();
        graph.AddNode(node);
        ZT.Wire(graph, source, 0, node, 0);
        var context = new EvaluationContext();

        new GraphEngine().Run(graph, context);

        Assert.Same(context, CurrentContextFixtures.Seen);
        Assert.Null(EvaluationContext.Current);
    }

    [Fact]
    public void TheContextIsGivenBackWhenTheNodeThrows()
    {
        var node = Steps();
        node.InPorts[0].SetUserValue(1.0);
        var context = new EvaluationContext(new CancellationToken(true));

        Assert.Throws<OperationCanceledException>(() => node.Evaluate(new object?[] { 3.0 }, context));

        Assert.Null(EvaluationContext.Current);
    }

    [Fact]
    public void ANodeThatCheckpointsBetweenStepsCanBeStoppedInTheMiddle()
    {
        CurrentContextFixtures.Passed = 0;
        using var source = new CancellationTokenSource();
        var context = new EvaluationContext(source.Token);
        var beats = 0;
        context.Heartbeat = () =>
        {
            beats++;
            if (beats == 4)
            {
                source.Cancel();
            }
        };
        var node = Steps();

        Assert.Throws<OperationCanceledException>(() => node.Evaluate(new object?[] { 100.0 }, context));

        // The call was made with a beat of its own, then three steps ran before the fourth beat cancelled.
        Assert.InRange(CurrentContextFixtures.Passed, 1, 5);
        Assert.True(CurrentContextFixtures.Passed < 100);
    }

    [Fact]
    public void ARunStoppedInsideANodeStaysDirtyAndIsNotAFailure()
    {
        using var source = new CancellationTokenSource();
        var context = new EvaluationContext(source.Token);
        var beats = 0;
        context.Heartbeat = () =>
        {
            if (++beats == 3)
            {
                source.Cancel();
            }
        };
        var graph = new GraphModel();
        var value = ZT.Value(graph, 50.0);
        var node = Steps();
        graph.AddNode(node);
        ZT.Wire(graph, value, 0, node, 0);

        var result = new GraphEngine().Run(graph, context);

        Assert.True(result.Cancelled);
        Assert.NotEqual(NodeState.Error, node.State);
    }
}
