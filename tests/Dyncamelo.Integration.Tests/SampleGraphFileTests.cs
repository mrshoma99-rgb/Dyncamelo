using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Core.Serialization;
using Dyncamelo.Nodes;
using Xunit;

namespace Dyncamelo.Integration.Tests;

/// <summary>
/// Loads every shipped runnable .dyc file in <c>samples/</c> from disk and
/// runs it. This pins the on-disk .dyc format: if serialization, node names
/// or port names drift, these tests fail before any user's saved graph
/// breaks. Samples that need Navisworks nodes cannot run here; they are
/// statically pinned by <see cref="SampleGraphStaticValidationTests"/>.
/// </summary>
public class SampleGraphFileTests
{
    private static readonly string[] ExpectedSamples =
    {
        "Bulk Selection Sets from Values.dyc",
        "Clash Triage and BCF Export.dyc",
        "Color Elements by Property.dyc",
        "Export Properties to Excel.dyc",
        "Getting Started - Math and Watch.dyc",
        "Isolated Viewpoints per Item.dyc",
        "QTO Rollup by Category.dyc",
        "Spotlight Viewpoints per Item.dyc",
        "csv-roundtrip.dyc",
        "hello-math.dyc",
        "list-lacing.dyc",
        "string-report.dyc",
    };

    /// <summary>Samples built purely from general nodes, runnable without Navisworks.</summary>
    private static readonly string[] RunnableSamples =
    {
        "Getting Started - Math and Watch.dyc",
        "csv-roundtrip.dyc",
        "hello-math.dyc",
        "list-lacing.dyc",
        "string-report.dyc",
    };

    public static IEnumerable<object[]> SampleFiles()
    {
        return RunnableSamples
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new object[] { p });
    }

    [Fact]
    public void SamplesDirectory_ContainsTheShippedGraphs()
    {
        var found = Directory.EnumerateFiles(SamplesDirectory(), "*.dyc")
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(ExpectedSamples.OrderBy(n => n, StringComparer.Ordinal).ToArray(), found);
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void SampleGraph_LoadsResolvesAndRunsGreen(string fileName)
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var serializer = new GraphSerializer(registry);

        var graph = serializer.LoadFromFile(Path.Combine(SamplesDirectory(), fileName));

        // Every node type and zero-touch definition must resolve.
        Assert.DoesNotContain(graph.Nodes, n => n is MissingNodeModel);

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.All(graph.Nodes, n => Assert.Equal(NodeState.Executed, n.State));

        AssertPinnedValues(fileName, graph);
    }

    /// <summary>Pins the semantic results of each sample, not just "it runs".</summary>
    private static void AssertPinnedValues(string fileName, GraphModel graph)
    {
        switch (fileName)
        {
            case "Getting Started - Math and Watch.dyc":
                Assert.Equal("32", Watch(graph, "Area"));
                Assert.Equal("Area = 32", Watch(graph, "Report"));
                break;

            case "hello-math.dyc":
                Assert.Equal("85", Watch(graph, "Result"));
                break;

            case "list-lacing.dyc":
                Assert.Equal(new[] { 11d, 22d }, Pipeline.AsDoubles(Pipeline.Output(graph, "Shortest")));
                Assert.Equal(new[] { 11d, 22d, 23d }, Pipeline.AsDoubles(Pipeline.Output(graph, "Longest")));
                var cross = Pipeline.AsList(Pipeline.Output(graph, "Cross Product"));
                Assert.Equal(3, cross.Count);
                Assert.Equal(new[] { 11d, 21d }, Pipeline.AsDoubles(cross[0]));
                Assert.Equal(new[] { 12d, 22d }, Pipeline.AsDoubles(cross[1]));
                Assert.Equal(new[] { 13d, 23d }, Pipeline.AsDoubles(cross[2]));
                break;

            case "string-report.dyc":
                Assert.Equal("Word count: 4", Watch(graph, "Count report"));
                Assert.Equal("dyncamelo, makes, navisworks, programmable", Watch(graph, "Joined words"));
                break;

            case "csv-roundtrip.dyc":
                var rows = Pipeline.AsList(Pipeline.Output(graph, "Round-tripped rows"));
                Assert.Equal(2, rows.Count);
                Assert.Equal(new[] { 1d, 2d, 3d }, Pipeline.AsDoubles(rows[0]));
                Assert.Equal(new[] { 4d, 5d, 6d }, Pipeline.AsDoubles(rows[1]));
                break;

            default:
                Assert.Fail("Sample '" + fileName + "' has no pinned expectations — add them here.");
                break;
        }
    }

    private static string Watch(GraphModel graph, string nodeName)
    {
        var node = Pipeline.Node(graph, nodeName);
        return node is WatchNode watch ? watch.FormattedValue : ((WatchListNode)node).FormattedValue;
    }

    /// <summary>Walks up from the test binaries to the repository root (marked by Dyncamelo.sln).</summary>
    internal static string SamplesDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dyncamelo.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir != null, "Could not locate the repository root (Dyncamelo.sln) above " + AppContext.BaseDirectory);
        var samples = Path.Combine(dir!.FullName, "samples");
        Assert.True(Directory.Exists(samples), "Samples directory not found: " + samples);
        return samples;
    }
}
