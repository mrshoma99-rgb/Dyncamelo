using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Cli.Commands;

/// <summary>
/// <c>dyncamelo list-nodes [--pack ...]</c>: prints the full node registry
/// (built-ins, Dyncamelo.Nodes and any extra packs) grouped by category,
/// with each node's ports.
/// </summary>
internal static class ListNodesCommand
{
    public static int Execute(CommandArguments args, TextWriter output)
    {
        if (args.Positional.Count > 0)
        {
            throw new CliError("list-nodes takes no positional arguments (got '" + args.Positional[0] + "').");
        }

        var registry = RegistryBuilder.Build(args.Packs, output);
        var entries = new List<Entry>();

        // Zero-touch definitions carry their metadata directly.
        foreach (var definition in registry.Definitions)
        {
            entries.Add(new Entry(
                definition.Category,
                definition.Name,
                FormatSignature(
                    definition.Inputs.Select(p => FormatPort(p.Name, p.Type, p.HasDefault, p.DefaultValue)),
                    definition.Outputs.Select(p => FormatPort(p.Name, p.Type, false, null)))));
        }

        // Hand-written NodeModel types are described by a throwaway instance.
        foreach (var nodeType in registry.NodeTypes)
        {
            var node = registry.CreateNode(nodeType);
            if (node == null)
            {
                continue;
            }

            entries.Add(new Entry(
                node.Category.Length > 0 ? node.Category : "(uncategorized)",
                node.Name + "  [" + nodeType + "]",
                FormatSignature(
                    node.InPorts.Select(p => FormatPort(p.Name, p.DeclaredType, p.HasDefault, p.DefaultValue)),
                    node.OutPorts.Select(p => FormatPort(p.Name, p.DeclaredType, false, null)))));
        }

        foreach (var group in entries.GroupBy(e => e.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            output.WriteLine(group.Key);
            foreach (var entry in group.OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                output.WriteLine("  " + entry.Name + "  " + entry.Signature);
            }

            output.WriteLine();
        }

        output.WriteLine(entries.Count.ToString(CultureInfo.InvariantCulture) + " node(s) registered.");
        return ExitCodes.Success;
    }

    private static string FormatSignature(IEnumerable<string> inputs, IEnumerable<string> outputs)
    {
        return "(" + string.Join(", ", inputs) + ") -> " + string.Join(", ", outputs);
    }

    private static string FormatPort(string name, Type type, bool hasDefault, object? defaultValue)
    {
        var text = name + ": " + TypeDisplay.Format(type);
        if (hasDefault)
        {
            text += " = " + (defaultValue == null
                ? "null"
                : Convert.ToString(defaultValue, CultureInfo.InvariantCulture));
        }

        return text;
    }

    private sealed class Entry
    {
        public Entry(string category, string name, string signature)
        {
            Category = category;
            Name = name;
            Signature = signature;
        }

        public string Category { get; }
        public string Name { get; }
        public string Signature { get; }
    }
}
