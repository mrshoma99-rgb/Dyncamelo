using System.Linq;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Loader;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>"colour" finds the Color.* nodes, which are spelt the American way in their names (VAL-24).</summary>
public class LibrarySpellingSearchTests
{
    // The library's haystack is name + category + tags + description, folded; every search word must occur in it.
    private static string Haystack(NodeDefinition d) =>
        SearchSpelling.Fold((d.Name + "\n" + d.Category + "\n" + string.Join("\n", d.SearchTags) + "\n" + d.Description).ToLowerInvariant());

    private static NodeRegistry Registry()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        return registry;
    }

    [Fact]
    public void ColourFindsEveryColorNode()
    {
        var colorNodes = Registry().Definitions.Where(d => d.Name.StartsWith("Color.", System.StringComparison.Ordinal)).ToList();
        Assert.True(colorNodes.Count >= 15, "expected the Color.* family");

        var query = SearchSpelling.Fold("colour");
        var missed = colorNodes.Where(d => !Haystack(d).Contains(query)).Select(d => d.Name).ToList();

        Assert.True(missed.Count == 0, "'colour' does not find: " + string.Join(", ", missed));
    }

    [Fact]
    public void AnAmericanSpellingStillFindsANodeDescribedTheBritishWay()
    {
        var registry = Registry();
        var british = registry.Definitions.Where(d => d.Description.ToLowerInvariant().Contains("colour")).ToList();
        Assert.NotEmpty(british);

        var query = SearchSpelling.Fold("color");

        Assert.All(british, d => Assert.Contains(query, Haystack(d)));
    }
}
