using System;
using System.Diagnostics;
using System.Linq;
using CamelGraph.Core.Loader;
using Xunit;

namespace CamelGraph.Nodes.Tests;

public class FlowWaitNodesTests
{
    [Fact]
    public void Wait_PassesTheValueThroughUnchanged()
    {
        var payload = new object();
        Assert.Same(payload, FlowWaitNodes.Wait(payload, 0));
        Assert.Null(FlowWaitNodes.Wait(null, 0));
        Assert.Equal("x", FlowWaitNodes.Wait("x", 0.01));
    }

    [Fact]
    public void Wait_ReallyWaits()
    {
        var clock = Stopwatch.StartNew();
        FlowWaitNodes.Wait("x", 0.15);
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(120), "waited only " + clock.Elapsed);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(601)]
    [InlineData(3600)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Wait_RefusesAnAbsurdDuration(double seconds)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => FlowWaitNodes.Wait("x", seconds));
        // Wave D (SYS-21): the hard maximum came down from 3600 to 600 seconds; the slider only reaches 60.
        Assert.Contains("0 and 600", ex.Message);
    }

    [Fact]
    public void Wait_IsRegisteredInTheWorkflowCategory_AsACreateNode()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var definition = registry.Definitions.Single(d => d.Name == "Flow.Wait");
        Assert.Equal("Workflow", definition.Category);
        Assert.Equal(CamelGraph.Core.Graph.NodeFunction.Create, definition.Function);
        Assert.True(definition.Inputs.Single(i => i.Name == "seconds").HasDefault);
        var range = definition.Inputs.Single(i => i.Name == "seconds").Range!;
        Assert.Equal(600d, range.Max);
        Assert.Equal(60d, range.SoftMax);
        Assert.Equal(0.5d, range.Step);
        Assert.Contains("loop", definition.Description);
    }
}
