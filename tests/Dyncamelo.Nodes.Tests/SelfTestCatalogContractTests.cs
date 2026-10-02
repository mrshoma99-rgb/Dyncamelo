using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Dyncamelo.Core.SelfTest;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// The built-in self-test names Navisworks nodes, which this test project cannot load. Reading the generated node catalogue pins every
/// node, input and output the cases use, so a rename in the Navisworks nodes fails the build here instead of in a user's session.
/// </summary>
public class SelfTestCatalogContractTests
{
    private sealed class Entry
    {
        public HashSet<string> Inputs { get; } = new HashSet<string>(StringComparer.Ordinal);

        public HashSet<string> Outputs { get; } = new HashSet<string>(StringComparer.Ordinal);
    }

    private static Dictionary<string, Entry> Catalogue()
    {
        var root = RepositoryRoot();
        using var stream = File.OpenRead(Path.Combine(root, "docs", "dyncamelo-nodes.json"));
        using var document = JsonDocument.Parse(stream);
        var result = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var node in document.RootElement.GetProperty("nodes").EnumerateArray())
        {
            var entry = new Entry();
            foreach (var input in node.GetProperty("inputs").EnumerateArray())
            {
                entry.Inputs.Add(input.GetProperty("name").GetString()!);
            }

            foreach (var output in node.GetProperty("outputs").EnumerateArray())
            {
                entry.Outputs.Add(output.GetProperty("name").GetString()!);
            }

            result[node.GetProperty("name").GetString()!] = entry;
        }

        return result;
    }

    [Fact]
    public void EveryNodeInputAndOutputTheCasesNameExistsInTheNodeCatalogue()
    {
        var catalogue = Catalogue();
        var problems = new List<string>();
        foreach (var testCase in SelfTestCatalog.All)
        {
            for (var i = 0; i < testCase.Steps.Count; i++)
            {
                var step = testCase.Steps[i];
                if (!catalogue.TryGetValue(step.Node, out var entry))
                {
                    problems.Add(testCase.Title + ": no node called '" + step.Node + "'");
                    continue;
                }

                foreach (var input in step.Inputs.Keys)
                {
                    if (!entry.Inputs.Contains(input))
                    {
                        problems.Add(testCase.Title + ": '" + step.Node + "' has no input '" + input + "'");
                    }
                }

                foreach (var wire in step.Wires)
                {
                    if (!entry.Inputs.Contains(wire.Key))
                    {
                        problems.Add(testCase.Title + ": '" + step.Node + "' has no input '" + wire.Key + "'");
                    }

                    if (wire.Value.Key >= i)
                    {
                        problems.Add(testCase.Title + ": step " + i + " is wired from a step that has not run yet");
                    }
                    else if (!catalogue[testCase.Steps[wire.Value.Key].Node].Outputs.Contains(wire.Value.Value))
                    {
                        problems.Add(testCase.Title + ": '" + testCase.Steps[wire.Value.Key].Node + "' has no output '" + wire.Value.Value + "'");
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void TheCasesOnlyUseNodesFromTheReadOnlyListAndTheListHoldsNoNodeThatChangesThings()
    {
        var catalogue = Catalogue();
        var outside = SelfTestCatalog.All.SelectMany(c => c.Steps).Select(s => s.Node).Where(n => !SelfTestCatalog.ReadOnlyNodes.Contains(n)).Distinct().ToList();
        Assert.True(outside.Count == 0, "Not on the read-only list: " + string.Join(", ", outside));

        var unknown = SelfTestCatalog.ReadOnlyNodes.Where(n => !catalogue.ContainsKey(n)).ToList();
        Assert.True(unknown.Count == 0, "On the read-only list but not in the node catalogue: " + string.Join(", ", unknown));

        // The verbs of the nodes that change things; none of the read-only nodes may be named with one.
        var changing = new[]
        {
            "Set", "Create", "Delete", "Remove", "Rename", "Add", "Append", "Apply", "Override", "Hide", "Show", "Isolate", "Reset", "Run",
            "Save", "Export", "Import", "Write", "Move", "Rotate", "Scale", "Translate", "Update", "Merge", "Open", "Refresh", "Clear",
            "Assign", "Attach", "Duplicate", "Copy", "Focus", "Zoom", "Invert", "Select", "Look", "Group", "Sort", "Auto", "Bulk",
        };
        var offenders = SelfTestCatalog.ReadOnlyNodes
            .Where(n => changing.Any(v => n.Substring(n.LastIndexOf('.') + 1).StartsWith(v, StringComparison.Ordinal)))
            .ToList();
        Assert.True(offenders.Count == 0, "These read-only nodes are named like nodes that change things: " + string.Join(", ", offenders));
    }

    [Fact]
    public void ThereAreEnoughCasesAndTheyCoverTheAreasThatMatter()
    {
        var areas = SelfTestCatalog.All.Select(c => c.Area).Distinct().ToList();

        Assert.True(SelfTestCatalog.All.Count >= 25, "only " + SelfTestCatalog.All.Count + " cases");
        foreach (var area in new[] { "Application and document", "Saved items", "Model", "Model items", "Properties" })
        {
            Assert.Contains(area, areas);
        }

        Assert.Equal(SelfTestCatalog.All.Count, SelfTestCatalog.All.Select(c => c.Area + "|" + c.Title).Distinct().Count());
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src", "Dyncamelo.Navisworks")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory != null, "Could not find the repository root above " + AppContext.BaseDirectory);
        return directory!.FullName;
    }
}
