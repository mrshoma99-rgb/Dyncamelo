using System;
using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Nodes;

namespace Dyncamelo.Cli;

/// <summary>
/// Builds the shipped sample graphs in code (they are serialized to
/// <c>samples/*.dyc</c> by <c>dyncamelo write-samples</c>). Keeping the
/// authoring here means the samples can always be regenerated after a
/// format change instead of hand-editing JSON.
/// </summary>
internal static class SampleGraphs
{
    /// <summary>File name / graph pairs for every shipped sample.</summary>
    public static IReadOnlyList<KeyValuePair<string, GraphModel>> BuildAll(NodeRegistry registry)
    {
        return new List<KeyValuePair<string, GraphModel>>
        {
            new KeyValuePair<string, GraphModel>("hello-math.dyc", BuildHelloMath(registry)),
            new KeyValuePair<string, GraphModel>("list-lacing.dyc", BuildListLacing(registry)),
            new KeyValuePair<string, GraphModel>("string-report.dyc", BuildStringReport(registry)),
            new KeyValuePair<string, GraphModel>("csv-roundtrip.dyc", BuildCsvRoundtrip(registry)),
        };
    }

    /// <summary>Slider -> Add -> Multiply -> Round (defaulted digits) -> Watch. Expected watch value: 85.</summary>
    public static GraphModel BuildHelloMath(NodeRegistry registry)
    {
        var graph = new GraphModel
        {
            Name = "Hello Math",
            Description = "A slider and two number literals flowing through Add, Multiply and Round into a Watch node.",
        };

        var slider = new NumberSliderNode { Name = "Length" };
        slider.Min = 0;
        slider.Max = 100;
        slider.Step = 0.5;
        slider.Value = 12.5;
        Place(slider, 0, 0);

        var offset = new NumberInputNode { Name = "Offset", Value = 30 };
        Place(offset, 0, 180);

        var factor = new NumberInputNode { Name = "Factor", Value = 2 };
        Place(factor, 320, 180);

        var add = ZeroTouch(registry, "Add", 320, 40);
        var multiply = ZeroTouch(registry, "Multiply", 620, 90);
        var round = ZeroTouch(registry, "Math.Round", 900, 90); // "digits" stays on its default (0)
        var watch = new WatchNode { Name = "Result" };
        Place(watch, 1160, 90);

        graph.AddNode(slider);
        graph.AddNode(offset);
        graph.AddNode(factor);
        graph.AddNode(add);
        graph.AddNode(multiply);
        graph.AddNode(round);
        graph.AddNode(watch);

        Connect(graph, slider, "value", add, "a");
        Connect(graph, offset, "value", add, "b");
        Connect(graph, add, "result", multiply, "a");
        Connect(graph, factor, "value", multiply, "b");
        Connect(graph, multiply, "result", round, "number");
        Connect(graph, round, "result", watch, "value");

        graph.Notes.Add(new NoteModel
        {
            Text = "(Length + Offset) * Factor, rounded. The Round node's 'digits' input is left on its default (0).",
            X = 0,
            Y = -120,
        });

        return graph;
    }

    /// <summary>Two ranges of different lengths added with Shortest, Longest and Cross-Product lacing.</summary>
    public static GraphModel BuildListLacing(NodeRegistry registry)
    {
        var graph = new GraphModel
        {
            Name = "List Lacing",
            Description = "Adds [1,2,3] and [10,20] under all three lacing modes to show replication.",
        };

        var start1 = new NumberInputNode { Name = "Start A", Value = 1 };
        Place(start1, 0, 0);
        var end1 = new NumberInputNode { Name = "End A", Value = 3 };
        Place(end1, 0, 120);
        var rangeA = ZeroTouch(registry, "List.Range", 280, 40);
        rangeA.Name = "Range A";

        var start2 = new NumberInputNode { Name = "Start B", Value = 10 };
        Place(start2, 0, 300);
        var end2 = new NumberInputNode { Name = "End B", Value = 20 };
        Place(end2, 0, 420);
        var step2 = new NumberInputNode { Name = "Step B", Value = 10 };
        Place(step2, 0, 540);
        var rangeB = ZeroTouch(registry, "List.Range", 280, 380);
        rangeB.Name = "Range B";

        var addShortest = ZeroTouch(registry, "Add", 620, 0);
        addShortest.Name = "Add (Shortest)";
        addShortest.Lacing = LacingMode.Shortest;

        var addLongest = ZeroTouch(registry, "Add", 620, 220);
        addLongest.Name = "Add (Longest)";
        addLongest.Lacing = LacingMode.Longest;

        var addCross = ZeroTouch(registry, "Add", 620, 440);
        addCross.Name = "Add (Cross Product)";
        addCross.Lacing = LacingMode.CrossProduct;

        var watchShortest = new WatchListNode { Name = "Shortest" };
        Place(watchShortest, 920, 0);
        var watchLongest = new WatchListNode { Name = "Longest" };
        Place(watchLongest, 920, 220);
        var watchCross = new WatchListNode { Name = "Cross Product" };
        Place(watchCross, 920, 440);

        graph.AddNode(start1);
        graph.AddNode(end1);
        graph.AddNode(rangeA);
        graph.AddNode(start2);
        graph.AddNode(end2);
        graph.AddNode(step2);
        graph.AddNode(rangeB);
        graph.AddNode(addShortest);
        graph.AddNode(addLongest);
        graph.AddNode(addCross);
        graph.AddNode(watchShortest);
        graph.AddNode(watchLongest);
        graph.AddNode(watchCross);

        Connect(graph, start1, "value", rangeA, "start");
        Connect(graph, end1, "value", rangeA, "end");
        Connect(graph, start2, "value", rangeB, "start");
        Connect(graph, end2, "value", rangeB, "end");
        Connect(graph, step2, "value", rangeB, "step");

        foreach (var add in new[] { addShortest, addLongest, addCross })
        {
            Connect(graph, rangeA, "list", add, "a");
            Connect(graph, rangeB, "list", add, "b");
        }

        Connect(graph, addShortest, "result", watchShortest, "list");
        Connect(graph, addLongest, "result", watchLongest, "list");
        Connect(graph, addCross, "result", watchCross, "list");

        graph.Notes.Add(new NoteModel
        {
            Text = "Range A = [1, 2, 3], Range B = [10, 20]. Shortest -> [11, 22]; Longest -> [11, 22, 23] (last element repeats); Cross Product -> [[11, 21], [12, 22], [13, 23]].",
            X = 0,
            Y = -140,
        });

        return graph;
    }

    /// <summary>String split/count/join pipeline producing a small text report in two Watch nodes.</summary>
    public static GraphModel BuildStringReport(NodeRegistry registry)
    {
        var graph = new GraphModel
        {
            Name = "String Report",
            Description = "Splits a sentence into words, counts them and joins them back with a different separator.",
        };

        var text = new StringInputNode { Name = "Text", Value = "dyncamelo makes navisworks programmable" };
        Place(text, 0, 0);
        var separator = new StringInputNode { Name = "Separator", Value = " " };
        Place(separator, 0, 160);

        var split = ZeroTouch(registry, "String.Split", 320, 40);
        var count = ZeroTouch(registry, "List.Count", 620, 200);
        var countText = ZeroTouch(registry, "String.FromObject", 880, 200);

        var prefix = new StringInputNode { Name = "Prefix", Value = "Word count: " };
        Place(prefix, 880, 60);
        var concat = ZeroTouch(registry, "String.Concat", 1140, 120);

        var joinSeparator = new StringInputNode { Name = "Join separator", Value = ", " };
        Place(joinSeparator, 320, 340);
        var join = ZeroTouch(registry, "String.Join", 620, 380);

        var watchCount = new WatchNode { Name = "Count report" };
        Place(watchCount, 1420, 120);
        var watchWords = new WatchNode { Name = "Joined words" };
        Place(watchWords, 920, 380);

        graph.AddNode(text);
        graph.AddNode(separator);
        graph.AddNode(split);
        graph.AddNode(count);
        graph.AddNode(countText);
        graph.AddNode(prefix);
        graph.AddNode(concat);
        graph.AddNode(joinSeparator);
        graph.AddNode(join);
        graph.AddNode(watchCount);
        graph.AddNode(watchWords);

        Connect(graph, text, "value", split, "str");
        Connect(graph, separator, "value", split, "separator");
        Connect(graph, split, "list", count, "list");
        Connect(graph, count, "count", countText, "obj");
        Connect(graph, prefix, "value", concat, "a");
        Connect(graph, countText, "result", concat, "b");
        Connect(graph, concat, "result", watchCount, "value");
        Connect(graph, joinSeparator, "value", join, "separator");
        Connect(graph, split, "list", join, "list");
        Connect(graph, join, "result", watchWords, "value");

        graph.Notes.Add(new NoteModel
        {
            Text = "Expected: Count report = 'Word count: 4', Joined words = 'dyncamelo, makes, navisworks, programmable'.",
            X = 0,
            Y = -120,
        });

        return graph;
    }

    /// <summary>
    /// Writes two numeric rows to a CSV file (relative path, i.e. the current
    /// working directory) and reads them straight back. The write node's 'path'
    /// output feeds the read node's 'path' input, which both supplies the path
    /// and sequences the read after the write.
    /// </summary>
    public static GraphModel BuildCsvRoundtrip(NodeRegistry registry)
    {
        var graph = new GraphModel
        {
            Name = "CSV Round-trip",
            Description = "Builds a 2x3 table, writes it to a CSV file next to the current directory and reads it back.",
            RunType = RunType.Manual, // touches the file system; do not auto-run on every edit
        };

        var start1 = new NumberInputNode { Name = "Row 1 start", Value = 1 };
        Place(start1, 0, 0);
        var end1 = new NumberInputNode { Name = "Row 1 end", Value = 3 };
        Place(end1, 0, 120);
        var row1 = ZeroTouch(registry, "List.Range", 280, 40);
        row1.Name = "Row 1";

        var start2 = new NumberInputNode { Name = "Row 2 start", Value = 4 };
        Place(start2, 0, 280);
        var end2 = new NumberInputNode { Name = "Row 2 end", Value = 6 };
        Place(end2, 0, 400);
        var row2 = ZeroTouch(registry, "List.Range", 280, 320);
        row2.Name = "Row 2";

        var rows = new ListCreateNode { Name = "Rows" };
        rows.AddItemPort(); // item0 + item1
        Place(rows, 560, 160);

        var path = new FilePathNode { Name = "Output file" };
        path.Path = "dyncamelo-sample-output.csv"; // relative: resolved against the current working directory
        Place(path, 560, 380);

        var write = ZeroTouch(registry, "CSV.WriteToFile", 860, 200);
        var read = ZeroTouch(registry, "CSV.ReadFromFile", 1140, 200);
        var watch = new WatchListNode { Name = "Round-tripped rows" };
        Place(watch, 1420, 200);

        graph.AddNode(start1);
        graph.AddNode(end1);
        graph.AddNode(row1);
        graph.AddNode(start2);
        graph.AddNode(end2);
        graph.AddNode(row2);
        graph.AddNode(rows);
        graph.AddNode(path);
        graph.AddNode(write);
        graph.AddNode(read);
        graph.AddNode(watch);

        Connect(graph, start1, "value", row1, "start");
        Connect(graph, end1, "value", row1, "end");
        Connect(graph, start2, "value", row2, "start");
        Connect(graph, end2, "value", row2, "end");
        Connect(graph, row1, "list", rows, "item0");
        Connect(graph, row2, "list", rows, "item1");
        Connect(graph, path, "path", write, "path");
        Connect(graph, rows, "list", write, "data");
        Connect(graph, write, "path", read, "path");
        Connect(graph, read, "data", watch, "list");

        graph.Notes.Add(new NoteModel
        {
            Text = "Writes [[1,2,3],[4,5,6]] to 'dyncamelo-sample-output.csv' in the current working directory, then reads it back. Wiring the write node's path output into the read node sequences the read after the write.",
            X = 0,
            Y = -140,
        });

        return graph;
    }

    /// <summary>Instantiates a zero-touch node by its library display name.</summary>
    private static ZeroTouchNodeModel ZeroTouch(NodeRegistry registry, string name, double x, double y)
    {
        var definition = registry.Definitions.FirstOrDefault(d => d.Name == name);
        if (definition == null)
        {
            throw new InvalidOperationException("Node definition '" + name + "' is not registered.");
        }

        var node = new ZeroTouchNodeModel(definition);
        Place(node, x, y);
        return node;
    }

    private static void Place(NodeModel node, double x, double y)
    {
        node.X = x;
        node.Y = y;
    }

    private static void Connect(GraphModel graph, NodeModel source, string outputPort, NodeModel target, string inputPort)
    {
        var from = source.OutPorts.FirstOrDefault(p => p.Name == outputPort)
            ?? throw new InvalidOperationException("Node '" + source.Name + "' has no output port '" + outputPort + "'.");
        var to = target.InPorts.FirstOrDefault(p => p.Name == inputPort)
            ?? throw new InvalidOperationException("Node '" + target.Name + "' has no input port '" + inputPort + "'.");

        var result = graph.Connect(from, to);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                "Cannot connect " + source.Name + "." + outputPort + " -> " + target.Name + "." + inputPort + ": " + result.Message);
        }
    }
}
