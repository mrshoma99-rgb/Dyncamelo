using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Core.Types;
using Dyncamelo.Nodes;

namespace Dyncamelo.Cli.Commands;

/// <summary>
/// <c>dyncamelo run &lt;graph.dyc&gt; [--pack ...]</c>: loads a graph, executes it
/// headlessly and prints a per-node report, Watch values and diagnostics.
/// Exit code 0 when no node ended in the Error state, 1 otherwise.
/// </summary>
internal static class RunCommand
{
    public static int Execute(CommandArguments args, TextWriter output)
    {
        var graphPath = args.RequireSinglePositional("path to a .dyc graph file");
        var registry = RegistryBuilder.Build(args.Packs, output);
        var graph = GraphIo.Load(graphPath, registry);

        output.WriteLine("Graph: " + (graph.Name.Length > 0 ? graph.Name : Path.GetFileNameWithoutExtension(graphPath))
            + "  (" + graph.Nodes.Count + " nodes, " + graph.Connections.Count + " connectors)");
        output.WriteLine();

        // Per-node wall time, measured between the engine's NodeExecuted events.
        var timings = new Dictionary<NodeModel, TimeSpan>();
        var engine = new GraphEngine();
        var nodeStopwatch = new Stopwatch();
        engine.NodeExecuted += (sender, e) =>
        {
            timings[e.Node] = nodeStopwatch.Elapsed;
            nodeStopwatch.Restart();
        };

        nodeStopwatch.Start();
        var result = engine.Run(graph);

        PrintNodeTable(graph, timings, result, output);
        PrintWatchValues(graph, output);
        PrintDiagnostics(graph, output);

        int errors = graph.Nodes.Count(n => n.State == NodeState.Error);
        int warnings = graph.Nodes.Count(n => n.State == NodeState.Warning);
        output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "Run {0} in {1:0.0} ms: {2} node(s) executed, {3} warning(s), {4} error(s).",
            result.Cancelled ? "cancelled" : "finished",
            result.Elapsed.TotalMilliseconds,
            result.ExecutedNodes.Count,
            warnings,
            errors));

        return errors > 0 ? ExitCodes.GraphHasErrors : ExitCodes.Success;
    }

    private static void PrintNodeTable(
        GraphModel graph,
        IReadOnlyDictionary<NodeModel, TimeSpan> timings,
        RunResult result,
        TextWriter output)
    {
        var executed = new HashSet<NodeModel>(result.ExecutedNodes);
        int nameWidth = Math.Max("Node".Length, graph.Nodes.Count == 0 ? 0 : graph.Nodes.Max(n => n.Name.Length));

        output.WriteLine(Pad("Node", nameWidth) + "  " + Pad("State", 9) + "  Time");
        output.WriteLine(new string('-', nameWidth) + "  " + new string('-', 9) + "  --------");

        foreach (var node in graph.Nodes)
        {
            string state = node.State.ToString();
            if (node.IsFrozen)
            {
                state = "Frozen";
            }

            string time;
            if (executed.Contains(node) && timings.TryGetValue(node, out var elapsed))
            {
                time = elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms";
            }
            else
            {
                time = executed.Contains(node) ? "< 0.1 ms" : "(cached)";
            }

            output.WriteLine(Pad(node.Name, nameWidth) + "  " + Pad(state, 9) + "  " + time);
        }

        output.WriteLine();
    }

    private static void PrintWatchValues(GraphModel graph, TextWriter output)
    {
        var watches = graph.Nodes.Where(n => n is WatchNode || n is WatchListNode).ToList();
        if (watches.Count == 0)
        {
            return;
        }

        output.WriteLine("Watch values");
        foreach (var node in watches)
        {
            string formatted = node is WatchNode watch
                ? watch.FormattedValue
                : ((WatchListNode)node).FormattedValue;
            if (formatted.Length == 0)
            {
                formatted = TypeCoercion.FormatValue(node.OutPorts.Count > 0 ? node.OutPorts[0].Value : null);
            }

            var lines = formatted.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 1)
            {
                output.WriteLine("  " + node.Name + " = " + lines[0]);
            }
            else
            {
                output.WriteLine("  " + node.Name + " =");
                foreach (var line in lines)
                {
                    output.WriteLine("    " + line);
                }
            }
        }

        output.WriteLine();
    }

    private static void PrintDiagnostics(GraphModel graph, TextWriter output)
    {
        var diagnostics = graph.Nodes
            .SelectMany(n => n.Messages.Select(m => new { Node = n, Message = m }))
            .Where(d => d.Message.Severity >= MessageSeverity.Warning)
            .OrderByDescending(d => d.Message.Severity)
            .ToList();
        if (diagnostics.Count == 0)
        {
            return;
        }

        output.WriteLine("Diagnostics");
        foreach (var diagnostic in diagnostics)
        {
            output.WriteLine("  [" + diagnostic.Message.Severity + "] " + diagnostic.Node.Name + ": " + diagnostic.Message.Text);
        }

        output.WriteLine();
    }

    private static string Pad(string text, int width)
    {
        return text.Length >= width ? text : text + new string(' ', width - text.Length);
    }
}
