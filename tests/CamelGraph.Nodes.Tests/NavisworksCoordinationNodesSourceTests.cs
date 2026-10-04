using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CamelGraph.Nodes.Coordination;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The Navisworks node project cannot be referenced by the Linux test projects (nothing here can load the
/// Navisworks API), so this reads the C# source of the thirteen nodes added in the maintenance / viewpoint /
/// camera / appearance / TimeLiner / transform wave and pins the attribute contract the library relies on:
/// an explicit role, a one-sentence description, search tags, port kinds on multi-output nodes, and the
/// "leave unchanged" defaults ClashTest.Edit documents.
/// </summary>
public class NavisworksCoordinationNodesSourceTests
{
    private static readonly Regex NodeNameAttribute = new Regex(@"^\s*\[NodeName\(""(?<name>[^""]+)""\)\]", RegexOptions.Multiline);
    private static readonly Regex Literal = new Regex(@"""(?<text>(?:[^""\\]|\\.)*)""");

    private static readonly string[] Files =
    {
        "ClashTestMaintenanceNodes.cs",
        "ViewpointExtraNodes.cs",
        "AppearanceExtraNodes.cs",
        Path.Combine("TimeLiner", "TimelinerProgressNodes.cs"),
        "TransformExtraNodes.cs",
    };

    /// <summary>Node name -> expected role.</summary>
    private static readonly Dictionary<string, string> Expected = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ClashTest.Edit"] = "Modify",
        ["ClashTest.Delete"] = "Modify",
        ["ClashTest.Duplicate"] = "Modify",
        ["ClashTest.ClearResults"] = "Modify",
        ["SavedViewpoint.Info"] = "Info",
        ["SavedViewpoint.Update"] = "Modify",
        ["Camera.SetStandardView"] = "Modify",
        ["Appearance.Focus"] = "Modify",
        ["TimelinerTask.SetProgress"] = "Modify",
        ["TimelinerTask.SetActual"] = "Modify",
        ["TimelinerTask.Delete"] = "Modify",
        ["ModelItem.Scale"] = "Modify",
        ["ModelItem.MoveTo"] = "Modify",
    };

    private sealed class NodeBlock
    {
        public string File = string.Empty;
        public string Name = string.Empty;
        public string Text = string.Empty;
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

    private static List<NodeBlock> Harvest()
    {
        var root = RepositoryRoot();
        var blocks = new List<NodeBlock>();
        foreach (var file in Files)
        {
            var path = Path.Combine(root, "src", "CamelGraph.Navisworks", file);
            Assert.True(File.Exists(path), "Missing source file " + path);
            var text = File.ReadAllText(path).Replace("\r\n", "\n");
            foreach (Match match in NodeNameAttribute.Matches(text))
            {
                var signature = text.IndexOf("public static", match.Index, StringComparison.Ordinal);
                Assert.True(signature > match.Index, "No method follows [NodeName] " + match.Groups["name"].Value);

                // The block runs to the method's opening brace, so it includes the parameter list.
                var end = text.IndexOf("\n    {", signature, StringComparison.Ordinal);
                Assert.True(end > signature, "No method body follows [NodeName] " + match.Groups["name"].Value);
                blocks.Add(new NodeBlock
                {
                    File = file,
                    Name = match.Groups["name"].Value,
                    Text = text.Substring(match.Index, end - match.Index),
                });
            }
        }

        return blocks;
    }

    private static string Description(NodeBlock block)
    {
        var start = block.Text.IndexOf("[NodeDescription(", StringComparison.Ordinal);
        Assert.True(start >= 0, block.Name + " has no [NodeDescription].");
        var end = block.Text.IndexOf(")]", start, StringComparison.Ordinal);
        var attribute = block.Text.Substring(start, end - start);
        return string.Concat(Literal.Matches(attribute).Cast<Match>().Select(m => m.Groups["text"].Value));
    }

    [Fact]
    public void ExactlyTheThirteenNewNodesAreDeclared_EachOnce()
    {
        var names = Harvest().Select(b => b.Name).ToList();
        Assert.Equal(Expected.Keys.OrderBy(n => n, StringComparer.Ordinal), names.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryNodeStatesItsRoleExplicitly()
    {
        foreach (var block in Harvest())
        {
            var role = Regex.Match(block.Text, @"\[NodeFunction\(CamelGraph\.Core\.Graph\.NodeFunction\.(?<role>\w+)\)\]");
            Assert.True(role.Success, block.Name + " must carry an explicit [NodeFunction(...)] — Navisworks categories are not guessed.");
            Assert.Equal(Expected[block.Name], role.Groups["role"].Value);
        }
    }

    [Fact]
    public void EveryNodeHasAOneSentenceDescriptionAndSearchTags()
    {
        foreach (var block in Harvest())
        {
            var description = Description(block);
            Assert.False(string.IsNullOrWhiteSpace(description), block.Name + " has an empty description.");
            Assert.EndsWith(".", description.TrimEnd());
            Assert.Contains("[NodeSearchTags(", block.Text);
        }
    }

    [Fact]
    public void EveryNodeNamesItsOutput_OrDeclaresPortKindsForEachOutput()
    {
        foreach (var block in Harvest())
        {
            var multi = Regex.Match(block.Text, @"\[MultiReturn\((?<keys>[^\)]*)\)\]");
            if (multi.Success)
            {
                var keys = Literal.Matches(multi.Groups["keys"].Value).Count;
                var kinds = Regex.Match(block.Text, @"\[PortKinds\((?<kinds>[^\)]*)\)\]");
                Assert.True(kinds.Success, block.Name + " is multi-output and needs [PortKinds].");
                Assert.Equal(keys, Literal.Matches(kinds.Groups["kinds"].Value).Count);
            }
            else
            {
                Assert.Contains("[return: NodeName(\"", block.Text);
            }
        }
    }

    [Fact]
    public void SavedViewpointInfo_ReturnsTheSevenDocumentedOutputsWithTheirKinds()
    {
        var block = Harvest().Single(b => b.Name == "SavedViewpoint.Info");
        Assert.Contains(
            "[MultiReturn(\"name\", \"folder\", \"hasSection\", \"hasOverrides\", \"commentCount\", \"position\", \"lookAt\")]",
            block.Text);
        Assert.Contains(
            "[PortKinds(\"text\", \"text\", \"boolean\", \"boolean\", \"integer\", \"geometry\", \"geometry\")]",
            block.Text);
    }

    [Fact]
    public void ClashTestEdit_DefaultsMatchTheLeaveUnchangedRules()
    {
        var block = Harvest().Single(b => b.Name == "ClashTest.Edit");
        var unchanged = ClashEditRules.Unchanged;

        Assert.Contains("string newName = \"\"", block.Text);
        Assert.Contains("string testType = \"" + unchanged + "\"", block.Text);
        Assert.Contains("double tolerance = -1", block.Text);
        Assert.Contains("string mergeComposites = \"" + unchanged + "\"", block.Text);
        Assert.Contains("IEnumerable<ModelItem>? itemsA = null", block.Text);
        Assert.Contains("IEnumerable<ModelItem>? itemsB = null", block.Text);

        // The dropdown offers exactly the canonical test types, after the sentinel.
        var choices = Regex.Match(block.Text, @"\[NodeChoices\((?<choices>""unchanged""[^\)]*)\)\]\s*string testType");
        Assert.True(choices.Success, "the testType dropdown must start with the 'unchanged' sentinel");
        var offered = Literal.Matches(choices.Groups["choices"].Value).Cast<Match>().Select(m => m.Groups["text"].Value).ToList();
        Assert.Equal(new[] { unchanged }.Concat(ClashEditRules.TestTypes), offered);
    }

    [Fact]
    public void ClashTestEdit_AndStandardView_UseTheDocumentedViewAndTypeSpellings()
    {
        var view = Harvest().Single(b => b.Name == "Camera.SetStandardView");
        var choices = Regex.Match(view.Text, @"\[NodeChoices\((?<choices>[^\)]*)\)\]\s*string view");
        Assert.True(choices.Success);
        var offered = Literal.Matches(choices.Groups["choices"].Value).Cast<Match>().Select(m => m.Groups["text"].Value).ToList();
        Assert.Equal(CamelGraph.Nodes.Spatial.CameraMath.StandardViewNames, offered);
        Assert.Contains("string view = \"iso\"", view.Text);
    }

    [Fact]
    public void AppearanceFocus_DefaultsMatchTheBrief()
    {
        var block = Harvest().Single(b => b.Name == "Appearance.Focus");
        Assert.Contains("double otherTransparency = 85", block.Text);
        Assert.Contains("bool resetFirst = true", block.Text);
    }
}
