using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// The Navisworks-API nodes of the model-data wave cannot run on a build agent (nothing here can execute the Navisworks
/// API, and no Linux test project references the Navisworks library), so this reads their C# source and pins what the
/// library relies on: the exact node names, the library category, the explicit role, a description and search tags, and
/// that every multi-output node declares one port kind per output.
/// </summary>
public class NavisModelDataNodeContractTests
{
    private sealed class Declared
    {
        public string File = string.Empty;
        public string Name = string.Empty;
        public string Category = string.Empty;
        public string? Role;
        public bool HasDescription;
        public bool HasSearchTags;
        public string[] MultiReturn = Array.Empty<string>();
        public string[] Kinds = Array.Empty<string>();
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dyncamelo.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static readonly string[] Files =
    {
        "ModelDataNodes.cs",
        "SelectionExtraNodes.cs",
        "ModelItemIfcNodes.cs",
    };

    private static List<Declared> Harvest()
    {
        var result = new List<Declared>();
        foreach (var file in Files)
        {
            var path = Path.Combine(RepositoryRoot(), "src", "Dyncamelo.Navisworks", file);
            Assert.True(File.Exists(path), "Missing source file " + path);
            var text = File.ReadAllText(path).Replace("\r\n", "\n");
            var classCategory = Regex.Match(text, @"\[NodeCategory\(""(?<c>[^""]+)""\)\]\s*public static class").Groups["c"].Value;

            foreach (Match match in Regex.Matches(text, @"\[NodeName\(""(?<name>[^""]+)""\)\]"))
            {
                var start = Math.Max(text.LastIndexOf("\n\n", match.Index, StringComparison.Ordinal), 0);
                var end = text.IndexOf("public static", match.Index, StringComparison.Ordinal);
                var block = text.Substring(start, end - start);

                var categories = Regex.Matches(block, @"\[NodeCategory\(""(?<c>[^""]+)""\)\]");
                var role = Regex.Match(block, @"\[NodeFunction\(Dyncamelo\.Core\.Graph\.NodeFunction\.(?<r>\w+)\)\]");
                var multi = Regex.Match(block, @"\[MultiReturn\((?<k>[^\)]*)\)\]");
                var kinds = Regex.Match(block, @"\[PortKinds\((?<k>[^\)]*)\)\]");

                result.Add(new Declared
                {
                    File = file,
                    Name = match.Groups["name"].Value,
                    // The method's own [NodeCategory] (when it has one) comes after the class's, so the last one wins.
                    Category = categories.Count == 0 ? classCategory : categories[categories.Count - 1].Groups["c"].Value,
                    Role = role.Success ? role.Groups["r"].Value : null,
                    HasDescription = block.Contains("[NodeDescription("),
                    HasSearchTags = block.Contains("[NodeSearchTags("),
                    MultiReturn = multi.Success ? Quoted(multi.Groups["k"].Value) : Array.Empty<string>(),
                    Kinds = kinds.Success ? Quoted(kinds.Groups["k"].Value) : Array.Empty<string>(),
                });
            }
        }

        return result;
    }

    private static string[] Quoted(string list) =>
        Regex.Matches(list, "\"([^\"]*)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();

    public static IEnumerable<object[]> Expected()
    {
        yield return new object[] { "Properties.Discover", "Navisworks.Properties", "Info" };
        yield return new object[] { "Properties.ToTable", "Navisworks.Properties", "Info" };
        yield return new object[] { "Model.Snapshot", "Navisworks.Model", "Info" };
        yield return new object[] { "Model.Statistics", "Navisworks.Model", "Info" };
        yield return new object[] { "Selection.Invert", "Navisworks.Selection", "Info" };
        yield return new object[] { "Selection.Remove", "Navisworks.Selection", "Modify" };
        yield return new object[] { "Search.ByGuid", "Navisworks.Search", "Info" };
        yield return new object[] { "SelectionSet.Info", "Navisworks.SelectionSets", "Info" };
        yield return new object[] { "SelectionSet.Duplicate", "Navisworks.SelectionSets", "Modify" };
        yield return new object[] { "ModelItem.IfcGuid", "Navisworks.ModelItem", "Info" };
    }

    [Theory]
    [MemberData(nameof(Expected))]
    public void EachNodeIsDeclaredWithItsCategoryAndAnExplicitRole(string name, string category, string role)
    {
        var node = Assert.Single(Harvest(), d => d.Name == name);

        Assert.Equal(category, node.Category);
        Assert.Equal(role, node.Role);
        Assert.True(node.HasDescription, name + " has no [NodeDescription].");
        Assert.True(node.HasSearchTags, name + " has no [NodeSearchTags].");
    }

    [Fact]
    public void ThereAreExactlyTheTenNodesOfTheWave()
    {
        var names = Harvest().Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(Expected().Select(e => (string)e[0]).OrderBy(n => n, StringComparer.Ordinal).ToArray(), names);
    }

    [Fact]
    public void MultiOutputNodesDeclareOnePortKindPerOutput()
    {
        var multi = Harvest().Where(d => d.MultiReturn.Length > 0).ToList();

        Assert.Equal(new[] { "Search.ByGuid", "SelectionSet.Info" }, multi.Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal));
        foreach (var node in multi)
        {
            Assert.Equal(node.MultiReturn.Length, node.Kinds.Length);
        }

        var byGuid = multi.Single(d => d.Name == "Search.ByGuid");
        Assert.Equal(new[] { "items", "missing" }, byGuid.MultiReturn);
        Assert.Equal(new[] { "item*", "text*" }, byGuid.Kinds);

        var info = multi.Single(d => d.Name == "SelectionSet.Info");
        Assert.Equal(new[] { "name", "kind", "itemCount", "folder" }, info.MultiReturn);
        Assert.Equal(new[] { "text", "text", "integer", "text" }, info.Kinds);
    }

    [Fact]
    public void NodeNamesDoNotClashWithTheShippedCatalogue()
    {
        var catalogue = Path.Combine(RepositoryRoot(), "docs", "dyncamelo-nodes.json");
        var shipped = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(catalogue))["nodes"]!
            .Select(n => n.Value<string>("name"))
            .ToList();

        // Once the lead regenerates the catalogue the new names are in it too, so only a name that appears more
        // than once among the shipped ones, or twice in the wave itself, is a clash.
        var inWave = Harvest().Select(d => d.Name).ToList();
        Assert.Equal(inWave.Count, inWave.Distinct(StringComparer.Ordinal).Count());
        foreach (var name in inWave)
        {
            Assert.True(shipped.Count(s => s == name) <= 1, name + " appears more than once in the catalogue.");
        }
    }
}
