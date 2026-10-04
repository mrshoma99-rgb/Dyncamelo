using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CamelGraph.Integration.Tests;

/// <summary>
/// A retired node (<c>[NodeDeprecated("Use X")]</c>) must point at something that exists and must be left out of the catalogue.
/// Like the alias test this reads the C# source: CamelGraph.Navisworks cannot be loaded on this build agent.
/// </summary>
public class RetiredNodesTests
{
    private static readonly Regex Retired = new Regex(
        @"\[NodeName\(""(?<name>[^""]+)""\)\]\s*\[NodeDeprecated\(""(?<replacement>[^""]*)""\)\]",
        RegexOptions.Singleline);

    private static string Root() => Path.GetDirectoryName(SampleGraphFileTests.SamplesDirectory())!;

    private static List<(string Name, string Replacement, string File)> HarvestRetired()
    {
        var found = new List<(string, string, string)>();
        foreach (var project in new[] { "CamelGraph.Nodes", "CamelGraph.Navisworks" })
        {
            var directory = Path.Combine(Root(), "src", project);
            Assert.True(Directory.Exists(directory), "Source directory not found: " + directory);
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                {
                    continue;
                }

                foreach (Match match in Retired.Matches(File.ReadAllText(file).Replace("\r\n", "\n")))
                {
                    found.Add((match.Groups["name"].Value, match.Groups["replacement"].Value, Path.GetFileName(file)));
                }
            }
        }

        return found;
    }

    private static HashSet<string> CatalogueNames()
    {
        var path = Path.Combine(Root(), "docs", "camelgraph-nodes.json");
        Assert.True(File.Exists(path), "Catalogue not found: " + path);
        var catalogue = JObject.Parse(File.ReadAllText(path));
        return new HashSet<string>(catalogue["nodes"]!.Select(n => n.Value<string>("name")!), StringComparer.Ordinal);
    }

    [Fact]
    public void EveryRetiredNodeNamesReplacementsThatExist()
    {
        var retired = HarvestRetired();
        Assert.NotEmpty(retired);
        var names = CatalogueNames();

        foreach (var (name, replacement, file) in retired)
        {
            Assert.False(string.IsNullOrWhiteSpace(replacement), name + " (" + file + ") is retired without saying what to use.");
            var mentioned = Regex.Matches(replacement, @"\b[A-Z][A-Za-z]*\.[A-Z][A-Za-z]*\b").Cast<Match>().Select(m => m.Value).ToList();
            Assert.NotEmpty(mentioned);
            foreach (var other in mentioned)
            {
                Assert.True(names.Contains(other), name + " (" + file + ") says to use '" + other + "', which is not in the node catalogue.");
            }
        }
    }

    [Fact]
    public void RetiredNodesAreNotInTheCatalogue()
    {
        var names = CatalogueNames();
        foreach (var (name, _, file) in HarvestRetired())
        {
            Assert.False(names.Contains(name), name + " (" + file + ") is retired but is still listed in docs/camelgraph-nodes.json — regenerate it.");
        }
    }
}
