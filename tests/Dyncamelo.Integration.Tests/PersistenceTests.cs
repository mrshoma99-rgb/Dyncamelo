using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Core.Serialization;
using Dyncamelo.Core.Types;
using Dyncamelo.Nodes;
using Xunit;

namespace Dyncamelo.Integration.Tests;

/// <summary>File-level persistence: save -> load -> re-run equivalence, and CSV write/read through the file nodes.</summary>
public class PersistenceTests : IDisposable
{
    private readonly string _tempDir;

    public PersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dyncamelo-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [Fact]
    public void SaveToFile_LoadFromFile_ReRunProducesIdenticalResults()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = BuildMixedGraph(registry);

        // Run the original, snapshot every output value per node id.
        new GraphEngine().Run(graph);
        var expected = Snapshot(graph);

        var path = Path.Combine(_tempDir, "mixed.dyc");
        var serializer = new GraphSerializer(registry);
        serializer.SaveToFile(graph, path);

        var reloaded = serializer.LoadFromFile(path);

        // Structure survived: same nodes (by id), same connector count, all dirty again.
        Assert.Equal(graph.Nodes.Count, reloaded.Nodes.Count);
        Assert.Equal(graph.Connections.Count, reloaded.Connections.Count);
        Assert.All(reloaded.Nodes, n => Assert.True(n.IsDirty));

        var rerun = new GraphEngine().Run(reloaded);
        Assert.True(rerun.Success);

        var actual = Snapshot(reloaded);
        Assert.Equal(expected.Keys.OrderBy(k => k), actual.Keys.OrderBy(k => k));
        foreach (var pair in expected)
        {
            Assert.Equal(pair.Value, actual[pair.Key]);
        }
    }

    [Fact]
    public void CsvRoundTrip_WriteThenReadThroughFileNodes()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "csv-roundtrip" };
        var csvPath = Path.Combine(_tempDir, "table.csv");

        // Row 1: two strings that need RFC 4180 quoting. Row 2: two numbers.
        var s1 = new StringInputNode { Name = "S1", Value = "hello, world" };
        var s2 = new StringInputNode { Name = "S2", Value = "he said \"hi\"" };
        var n1 = new NumberInputNode { Name = "N1", Value = 1.5 };
        var n2 = new NumberInputNode { Name = "N2", Value = -2 };

        var row1 = new ListCreateNode { Name = "Row1" };
        row1.AddItemPort();
        var row2 = new ListCreateNode { Name = "Row2" };
        row2.AddItemPort();
        var rows = new ListCreateNode { Name = "Rows" };
        rows.AddItemPort();

        var path = new FilePathNode { Name = "Path" };
        path.Path = csvPath;

        var write = Pipeline.ZeroTouch(registry, "CSV.WriteToFile", "Write");
        var read = Pipeline.ZeroTouch(registry, "CSV.ReadFromFile", "Read");

        graph.AddNode(s1);
        graph.AddNode(s2);
        graph.AddNode(n1);
        graph.AddNode(n2);
        graph.AddNode(row1);
        graph.AddNode(row2);
        graph.AddNode(rows);
        graph.AddNode(path);
        graph.AddNode(write);
        graph.AddNode(read);

        Pipeline.Connect(graph, s1, "value", row1, "item0");
        Pipeline.Connect(graph, s2, "value", row1, "item1");
        Pipeline.Connect(graph, n1, "value", row2, "item0");
        Pipeline.Connect(graph, n2, "value", row2, "item1");
        Pipeline.Connect(graph, row1, "list", rows, "item0");
        Pipeline.Connect(graph, row2, "list", rows, "item1");
        Pipeline.Connect(graph, path, "path", write, "path");
        Pipeline.Connect(graph, rows, "list", write, "data");
        // The write's path output feeds the read: supplies the path AND sequences read-after-write.
        Pipeline.Connect(graph, write, "path", read, "path");

        var run = Pipeline.SaveLoadAndRun(graph, registry, out var result);

        Assert.True(result.Success);
        Assert.All(run.Nodes, n => Assert.Equal(NodeState.Executed, n.State));
        Assert.True(File.Exists(csvPath));

        var data = Pipeline.AsList(Pipeline.Output(run, "Read"));
        Assert.Equal(2, data.Count);

        var readRow1 = Pipeline.AsList(data[0]);
        Assert.Equal("hello, world", readRow1[0]);
        Assert.Equal("he said \"hi\"", readRow1[1]);

        var readRow2 = Pipeline.AsDoubles(data[1]); // numeric cells come back as numbers
        Assert.Equal(new[] { 1.5d, -2d }, readRow2);
    }

    /// <summary>A graph touching math, strings, lists and lacing — good drift detector.</summary>
    private static GraphModel BuildMixedGraph(NodeRegistry registry)
    {
        var graph = new GraphModel { Name = "mixed" };

        var start = new NumberInputNode { Name = "Start", Value = 1 };
        var end = new NumberInputNode { Name = "End", Value = 3 };
        var range = Pipeline.ZeroTouch(registry, "List.Range", "Range");
        var offset = new NumberInputNode { Name = "Offset", Value = 10 };
        var add = Pipeline.ZeroTouch(registry, "Add", "Add"); // [1,2,3] + 10 -> replicated
        add.Lacing = LacingMode.Longest;
        var text = new StringInputNode { Name = "Text", Value = "a b" };
        var sep = new StringInputNode { Name = "Sep", Value = " " };
        var split = Pipeline.ZeroTouch(registry, "String.Split", "Split");
        var watch = new WatchNode { Name = "Watch" };

        graph.AddNode(start);
        graph.AddNode(end);
        graph.AddNode(range);
        graph.AddNode(offset);
        graph.AddNode(add);
        graph.AddNode(text);
        graph.AddNode(sep);
        graph.AddNode(split);
        graph.AddNode(watch);

        Pipeline.Connect(graph, start, "value", range, "start");
        Pipeline.Connect(graph, end, "value", range, "end");
        Pipeline.Connect(graph, range, "list", add, "a");
        Pipeline.Connect(graph, offset, "value", add, "b");
        Pipeline.Connect(graph, add, "result", watch, "value");
        Pipeline.Connect(graph, text, "value", split, "str");
        Pipeline.Connect(graph, sep, "value", split, "separator");

        graph.Notes.Add(new NoteModel { Text = "round-trip me", X = 1, Y = 2 });
        return graph;
    }

    /// <summary>Formats every output port of every node, keyed by "nodeId/portName".</summary>
    private static Dictionary<string, string> Snapshot(GraphModel graph)
    {
        var snapshot = new Dictionary<string, string>();
        foreach (var node in graph.Nodes)
        {
            foreach (var port in node.OutPorts)
            {
                snapshot[node.Id.ToString("N") + "/" + port.Name] = TypeCoercion.FormatValue(port.Value);
            }
        }

        return snapshot;
    }
}
