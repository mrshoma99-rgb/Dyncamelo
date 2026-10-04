using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Tests.Fixtures;
using CamelGraph.Core.Types;
using Xunit;

namespace CamelGraph.Core.Tests;

/// <summary>A stand-in for a model element that a host's converter resolves.</summary>
public sealed class ResolvedElement
{
    public ResolvedElement(string id) => Id = id;

    public string Id { get; }
}

/// <summary>Nodes with element ports, to see what a converter can say.</summary>
public static class ConverterWarningFixtures
{
    public static int Count([MultiInput] IEnumerable<ResolvedElement> items) => items.Count();

    public static string One(ResolvedElement item) => item.Id;

    public static int CountPlain(IEnumerable<ResolvedElement> items) => items.Count();
}

/// <summary>
/// A picked selection is turned back into elements by a converter the host registers, BEFORE the node's method runs. A picked element that
/// cannot be found any more must be reported on the node (an amber warning), not dropped without a word: the converter reports with
/// NodeWarnings.Add, which only works when the engine collects warnings while it converts the arguments.
/// </summary>
public class ConverterWarningTests
{
    private static readonly List<NodeDefinition> Definitions = AssemblyNodeLoader.LoadType(typeof(ConverterWarningFixtures));

    static ConverterWarningTests()
    {
        TypeCoercion.RegisterConverter(typeof(string), typeof(ResolvedElement), value => Resolve((string)value).FirstOrDefault());
        TypeCoercion.RegisterConverter(typeof(string), typeof(List<ResolvedElement>), value => Resolve((string)value));
    }

    // "found:a;lost:b;found:c" resolves a and c and reports b.
    private static List<ResolvedElement> Resolve(string text)
    {
        if (!text.StartsWith("el:", StringComparison.Ordinal))
        {
            throw new InvalidCastException("not a pick");
        }

        var parts = text.Substring(3).Split(';').Where(p => p.Length > 0).ToList();
        var found = parts.Where(p => !p.StartsWith("lost", StringComparison.Ordinal)).Select(p => new ResolvedElement(p)).ToList();
        if (found.Count < parts.Count)
        {
            NodeWarnings.Add((parts.Count - found.Count) + " of " + parts.Count + " picked elements were not found.");
        }

        return found;
    }

    private static ZeroTouchNodeModel Run(string method, string pinned)
    {
        var graph = new GraphModel();
        var node = new ZeroTouchNodeModel(Definitions.Single(d => d.Method.Name == method));
        graph.AddNode(node);
        node.InPorts[0].SetUserValue(pinned);
        new GraphEngine().Run(graph);
        return node;
    }

    [Theory]
    [InlineData("Count")]
    [InlineData("CountPlain")]
    public void AWarningFromTheConverterShowsOnTheNode(string method)
    {
        var node = Run(method, "el:a;lost1;c");

        Assert.Equal(NodeState.Warning, node.State);
        Assert.Contains("1 of 3 picked elements were not found", node.StateMessage);
        Assert.Equal(2, node.OutPorts[0].Value);
    }

    [Fact]
    public void ACleanPickGivesNoWarning()
    {
        var node = Run("Count", "el:a;b");

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(2, node.OutPorts[0].Value);
    }

    [Fact]
    public void TheWarningOfOneNodeDoesNotLeakToTheNextRun()
    {
        var first = Run("Count", "el:a;lost1");
        var second = Run("Count", "el:a;b");

        Assert.Equal(NodeState.Warning, first.State);
        Assert.Equal(NodeState.Executed, second.State);
    }

    [Fact]
    public void ASingleElementInputReportsToo()
    {
        var node = Run("One", "el:lost1;lost2");

        Assert.NotEqual(NodeState.Executed, node.State);
    }
}
