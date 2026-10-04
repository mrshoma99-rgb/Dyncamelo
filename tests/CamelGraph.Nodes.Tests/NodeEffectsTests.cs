using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// A graph from a file is only run after the user has been told about nodes that start programs, use the network or change existing
/// files. These tests pin which library nodes declare that, and fail when a node that does any of it is added without declaring it.
/// </summary>
public class NodeEffectsTests
{
    private static readonly Dictionary<string, NodeEffects> Expected = new Dictionary<string, NodeEffects>(StringComparer.Ordinal)
    {
        ["System.Run"] = NodeEffects.RunsPrograms,
        ["System.OpenPath"] = NodeEffects.RunsPrograms,
        ["Web.Get"] = NodeEffects.UsesNetwork,
        ["Web.Post"] = NodeEffects.UsesNetwork,
        ["File.Delete"] = NodeEffects.ChangesFiles,
        ["File.Move"] = NodeEffects.ChangesFiles,
        ["File.Copy"] = NodeEffects.ChangesFiles,
        ["Directory.Delete"] = NodeEffects.ChangesFiles,
        ["Zip.Extract"] = NodeEffects.ChangesFiles,
        ["Table.ToCsvFile"] = NodeEffects.WritesFiles,
        ["Table.ToExcelFile"] = NodeEffects.WritesFiles,
    };

    [Fact]
    public void ExactlyTheseLibraryNodesDeclareAnEffect()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);

        var declared = registry.Definitions
            .Where(d => d.Effects != NodeEffects.None)
            .ToDictionary(d => d.Name, d => d.Effects, StringComparer.Ordinal);

        Assert.Equal(Expected.OrderBy(p => p.Key).ToList(), declared.OrderBy(p => p.Key).ToList());
    }

    [Fact]
    public void NoOtherSourceFileStartsAProgramUsesTheNetworkOrDeletesFiles()
    {
        // A node that does one of these must say so with [NodeEffects], or opening somebody's graph runs it without a word.
        // Files allowed to use these APIs are the two that declare the effects; any other file needs the same attribute, then
        // an entry here.
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SystemNodes.cs", "FileExtraNodes.cs" };
        var risky = new Regex(
            @"\bProcess\.Start\b|\bProcessStartInfo\b|\bHttpClient\b|\bWebRequest\.Create\b|\bWebClient\b|\bFile\.(Delete|Move|Replace)\(|\bDirectory\.(Delete|Move)\(|ExtractToDirectory|\.ExtractToFile\(",
            RegexOptions.CultureInvariant);

        var root = RepositoryRoot();
        var offenders = new List<string>();
        foreach (var project in new[] { "CamelGraph.Nodes", "CamelGraph.Navisworks" })
        {
            foreach (var file in Directory.GetFiles(Path.Combine(root, "src", project), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                    file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                var name = Path.GetFileName(file);
                if (risky.IsMatch(text) && !allowed.Contains(name))
                {
                    offenders.Add(project + "/" + name + ": " + risky.Match(text).Value);
                }
            }
        }

        Assert.True(offenders.Count == 0, "Tag the node(s) with [NodeEffects] and allow the file in this test:\n" + string.Join("\n", offenders));

        foreach (var name in allowed)
        {
            var path = Directory.GetFiles(Path.Combine(root, "src", "CamelGraph.Nodes"), name, SearchOption.AllDirectories).Single();
            Assert.Contains("NodeEffects", File.ReadAllText(path));
        }
    }

    [Fact]
    public void TheShippedSamplesAskNothingWhenOpened()
    {
        // Samples open as trusted, so none may hold a node that runs programs, uses the network or deletes, moves or overwrites files.
        // (Writing a new file, WritesFiles, and changing the model, ChangesModel, are what a sample such as "Export Properties to Excel"
        // is for: they are listed to the user when a graph comes from a file, but a built-in sample may do them.)
        const NodeEffects dangerous = NodeEffects.RunsPrograms | NodeEffects.UsesNetwork | NodeEffects.ChangesFiles;
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var flagged = new HashSet<string>(registry.Definitions.Where(d => (d.Effects & dangerous) != NodeEffects.None).Select(d => d.Id), StringComparer.Ordinal);

        var bad = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(RepositoryRoot(), "samples"), "*.dyc"))
        {
            var text = File.ReadAllText(file);
            foreach (var id in flagged)
            {
                if (text.Contains("\"" + id + "\""))
                {
                    bad.Add(Path.GetFileName(file) + " uses " + id);
                }
            }
        }

        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src", "CamelGraph.Navisworks")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory != null, "Could not find the repository root above " + AppContext.BaseDirectory);
        return directory!.FullName;
    }
}
