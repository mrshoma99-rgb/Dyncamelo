using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Serialization;
using Newtonsoft.Json.Linq;

namespace Dyncamelo.Cli.Commands;

/// <summary>
/// <c>dyncamelo validate &lt;graph.dyc&gt; [--pack ...]</c>: loads the graph
/// without running it and reports unresolved node definitions and connectors
/// that could not be re-attached (missing nodes or mismatched port names).
/// Exit code 0 when clean, 1 when issues were found, 2 when unreadable.
/// </summary>
internal static class ValidateCommand
{
    public static int Execute(CommandArguments args, TextWriter output)
    {
        var graphPath = args.RequireSinglePositional("path to a .dyc graph file");
        var registry = RegistryBuilder.Build(args.Packs, output);

        var json = GraphIo.ReadGraphText(graphPath);
        var graph = GraphIo.Deserialize(json, registry, graphPath);

        var issues = new List<string>();

        // 1. Nodes whose type tag / zero-touch definition is not in the registry.
        foreach (var missing in graph.Nodes.OfType<MissingNodeModel>())
        {
            issues.Add("Node '" + missing.Name + "': " + missing.Reason);
        }

        // 2. Connectors from the file that the tolerant loader silently dropped.
        issues.AddRange(FindDroppedConnectors(json, graph));

        output.WriteLine("Graph:      " + (graph.Name.Length > 0 ? graph.Name : Path.GetFileNameWithoutExtension(graphPath)));
        output.WriteLine("Nodes:      " + graph.Nodes.Count);
        output.WriteLine("Connectors: " + graph.Connections.Count);
        output.WriteLine();

        if (issues.Count == 0)
        {
            output.WriteLine("OK: all node definitions resolved and all connectors re-attached.");
            return ExitCodes.Success;
        }

        output.WriteLine(issues.Count + " issue(s):");
        foreach (var issue in issues)
        {
            output.WriteLine("  - " + issue);
        }

        return ExitCodes.GraphHasErrors;
    }

    /// <summary>
    /// Re-reads the raw Connectors array and diagnoses every entry that has no
    /// matching live connection in the loaded graph.
    /// </summary>
    private static IEnumerable<string> FindDroppedConnectors(string json, GraphModel graph)
    {
        JArray? connectors;
        try
        {
            connectors = JObject.Parse(json)["Connectors"] as JArray;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            yield break; // unreadable JSON is already reported by the loader
        }

        if (connectors == null)
        {
            yield break;
        }

        var nodesById = graph.Nodes.ToDictionary(n => n.Id);
        foreach (var token in connectors.OfType<JObject>())
        {
            var fromPort = token.Value<string>("FromPort") ?? string.Empty;
            var toPort = token.Value<string>("ToPort") ?? string.Empty;
            var label = "Connector " + Describe(token);

            if (!TryParseGuid(token.Value<string>("FromNode"), out var fromId) ||
                !TryParseGuid(token.Value<string>("ToNode"), out var toId))
            {
                yield return label + ": malformed node id.";
                continue;
            }

            if (!nodesById.TryGetValue(fromId, out var fromNode))
            {
                yield return label + ": source node " + fromId.ToString("N") + " does not exist.";
                continue;
            }

            if (!nodesById.TryGetValue(toId, out var toNode))
            {
                yield return label + ": target node " + toId.ToString("N") + " does not exist.";
                continue;
            }

            bool restored = graph.Connections.Any(c =>
                c.SourceNode == fromNode && c.Source.Name == fromPort &&
                c.TargetNode == toNode && c.Target.Name == toPort);
            if (restored)
            {
                continue;
            }

            if (fromNode.OutPorts.All(p => p.Name != fromPort))
            {
                yield return label + ": node '" + fromNode.Name + "' has no output port '" + fromPort + "'.";
            }
            else if (toNode.InPorts.All(p => p.Name != toPort))
            {
                yield return label + ": node '" + toNode.Name + "' has no input port '" + toPort + "'.";
            }
            else
            {
                yield return label + ": could not be re-attached (incompatible or duplicate connection).";
            }
        }
    }

    private static string Describe(JObject connector)
    {
        return "'" + (connector.Value<string>("FromPort") ?? "?") + " -> " + (connector.Value<string>("ToPort") ?? "?") + "'";
    }

    private static bool TryParseGuid(string? text, out Guid guid)
    {
        if (!string.IsNullOrEmpty(text) && Guid.TryParse(text, out guid))
        {
            return true;
        }

        guid = Guid.Empty;
        return false;
    }
}
