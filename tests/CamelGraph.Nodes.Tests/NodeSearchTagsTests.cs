using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Nodes;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The hand-written nodes (inputs, displays, loops, List.Create...) carry their search words on the node (NodeModel.SearchTags),
/// because they have no [NodeSearchTags] attribute (VAL-25, SYS-24). Without them "dropdown", "checkbox", "preview" and "for each"
/// find none of them.
/// </summary>
public class NodeSearchTagsTests
{
    private static NodeRegistry Registry()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        return registry;
    }

    private static NodeModel Create(string name)
    {
        var registry = Registry();
        var type = registry.NodeTypes.Single(t => registry.CreateNode(t)!.Name == name);
        return registry.CreateNode(type)!;
    }

    [Theory]
    [InlineData("Watch", "preview", "inspect", "debug", "print", "show", "output", "result")]
    [InlineData("Choice", "dropdown", "select", "option", "pick")]
    [InlineData("Boolean", "checkbox", "switch", "true", "false", "flag")]
    [InlineData("Number Slider", "range", "scrub")]
    [InlineData("Integer Slider", "range", "scrub")]
    [InlineData("File Path", "browse", "open")]
    [InlineData("Directory Path", "folder", "browse")]
    [InlineData("Date", "calendar", "time")]
    [InlineData("Watch Image", "picture", "png", "photo")]
    [InlineData("Loop.Item", "for each", "foreach", "iterate", "repeat", "per item", "batch")]
    [InlineData("Loop.Collect", "for each", "foreach", "iterate", "repeat", "per item", "batch", "collect")]
    [InlineData("Color Picker", "colour")]
    public void TheSuggestedWordsAreThere(string node, params string[] words)
    {
        var tags = Create(node).SearchTags;

        foreach (var word in words)
        {
            Assert.Contains(word, tags);
        }
    }

    [Fact]
    public void EveryHandWrittenNodeTypeHasSearchTags()
    {
        var registry = Registry();
        var missing = new List<string>();
        foreach (var type in registry.NodeTypes)
        {
            var node = registry.CreateNode(type)!;
            if (node is CamelGraph.Core.Groups.GroupBoundNode)
            {
                continue; // plumbing of node groups: found as "group", not by its own words
            }

            if (node.SearchTags.Count == 0)
            {
                missing.Add(node.Name + " (" + type + ")");
            }
        }

        Assert.True(missing.Count == 0, "Give these nodes a SearchTags override: " + string.Join(", ", missing));
    }

    [Fact]
    public void NoNodeModelInTheSourceTreeIsWithoutSearchTags()
    {
        // Includes CapturedSelectionNode, which lives in the Navisworks project and cannot be loaded here.
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "ZeroTouchNodeModel", "MissingNodeModel", "GroupBoundNode" };
        var declaration = new Regex(@"class\s+(\w+)\s*:\s*NodeModel\b");
        var offenders = new List<string>();
        var root = RepositoryRoot();
        foreach (var file in Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (Match match in declaration.Matches(text))
            {
                if (!allowed.Contains(match.Groups[1].Value) && !Regex.IsMatch(text, @"override\s+[\w.<>]+\s+SearchTags\b"))
                {
                    offenders.Add(Path.GetFileName(file) + ": " + match.Groups[1].Value);
                }
            }
        }

        Assert.True(offenders.Count == 0, "A NodeModel needs a SearchTags override so the library search can find it:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void AZeroTouchNodeTakesItsTagsFromTheAttribute()
    {
        var registry = Registry();
        var definition = registry.Definitions.First(d => d.SearchTags.Count > 0);

        var node = new ZeroTouchNodeModel(definition);

        Assert.Equal(definition.SearchTags, node.SearchTags);
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
