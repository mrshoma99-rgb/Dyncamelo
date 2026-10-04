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
    [InlineData(3601)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Wait_RefusesAnAbsurdDuration(double seconds)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => FlowWaitNodes.Wait("x", seconds));
        Assert.Contains("0 and 3600", ex.Message);
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
    }
}
