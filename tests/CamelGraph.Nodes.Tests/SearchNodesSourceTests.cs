using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The Navisworks node project cannot be loaded by the Linux test projects, so this reads the source of the property-search nodes and pins
/// two things the library relies on: they never go back to one whole-model search per data type, and the nodes that search the whole model
/// carry the search button that reads the selection (never the model) to choose a tab or property name from.
/// </summary>
public class SearchNodesSourceTests
{
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src", "CamelGraph.Navisworks")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static string Source(string file) => File.ReadAllText(Path.Combine(Root(), "src", "CamelGraph.Navisworks", file)).Replace("\r\n", "\n");

    // The text of a public static method from its name to the opening brace of its body.
    private static string Signature(string source, string method)
    {
        var match = Regex.Match(source, @"public static [^\n]* " + Regex.Escape(method) + @"\((?<parameters>.*?)\)\n    \{", RegexOptions.Singleline);
        Assert.True(match.Success, "method not found: " + method);
        return match.Groups["parameters"].Value;
    }

    [Fact]
    public void NoSearchNodeWalksTheModelOncePerDataType()
    {
        var source = Source("SearchNodes.cs");

        // The equals, compare and scoped searches try a value as several data types; that must be alternatives of ONE search.
        Assert.DoesNotMatch(@"foreach \(var \w+ in BuildEqualityVariants", source);
        Assert.DoesNotMatch(@"foreach \(var variant in variants\)", source);
        Assert.Contains("RunAlternativesSearch(", source);
        foreach (var method in new[] { "ByPropertyValue", "ByPropertyCompare", "InItems" })
        {
            var body = Regex.Match(source, @"public static [^\n]* " + method + @"\(.*?\n    \}\n", RegexOptions.Singleline).Value;
            Assert.True(body.Length > 0, "method not found: " + method);
            Assert.True(body.Contains("RunEqualitySearch(") || body.Contains("RunAlternativesSearch("), method + " does not use the single combined search");
            Assert.DoesNotContain("new Search()", body);
        }

        // The live search sets use the same alternatives.
        Assert.Contains("AddAlternatives(search, categoryName, propertyName, BuildEqualityVariants(value)", source);
    }

    [Fact]
    public void ASearchResultIsNotCopiedAgainWhenNoResolutionIsAsked()
    {
        var source = Source("SearchNodes.cs");

        Assert.Contains("level == SelectionLevel.Self ? found : SelectionLevels.Resolve(found, level)", source);
        Assert.DoesNotMatch(@"SelectionLevels\.Resolve\(RunSearch\(", source);
    }

    [Theory]
    [InlineData("SearchNodes.cs", "ByProperty", "categoryName", "propertyName")]
    [InlineData("SearchNodes.cs", "HasProperty", "categoryName", "propertyName")]
    [InlineData("SearchNodes.cs", "HasCategory", "categoryName", null)]
    [InlineData("SelectionSetNodes.cs", "CreateFromSearch", "categoryName", "propertyName")]
    [InlineData("SelectionSetNodes.cs", "BulkByPropertyValues", "categoryName", "propertyName")]
    public void TheNodesThatSearchTheWholeModelOfferTheSelectionsTabsAndProperties(string file, string method, string tab, string? property)
    {
        var parameters = Signature(Source(file), method);

        Assert.Contains("[NodeTabChoice(NodeDataSource.Selection)] string " + tab, parameters);
        if (property != null)
        {
            Assert.Contains("[NodePropertyChoice(NodeDataSource.Selection, \"" + tab + "\")] string " + property, parameters);
        }
    }

    [Fact]
    public void TheScopedSearchOffersTheTabsAndPropertiesOfItsOwnItems()
    {
        var parameters = Signature(Source("SearchNodes.cs"), "InItems");

        Assert.Contains("[NodeTabChoice(\"items\")] string categoryName", parameters);
        Assert.Contains("[NodePropertyChoice(\"items\", \"categoryName\")] string propertyName", parameters);
    }
}
