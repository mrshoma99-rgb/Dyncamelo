using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// The Navisworks node project cannot be loaded by the Linux test projects, so these read the source of the nodes whose slow
/// parts were moved into plain .NET code (tested on its own) and pin that the nodes keep using it: no collection searched once per
/// item, no search per GUID, no pair-by-pair walk over the Navisworks API.
/// </summary>
public class NavisworksPerformanceSourceTests
{
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src", "Dyncamelo.Navisworks")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static string Source(string file) => File.ReadAllText(Path.Combine(Root(), "src", "Dyncamelo.Navisworks", file)).Replace("\r\n", "\n");

    // The text of a method (public or private, static) from its signature to the closing brace at the class' indentation.
    private static string Body(string source, string method)
    {
        var match = Regex.Match(source, @"\n    (public|private|internal) static [^\n]* " + Regex.Escape(method) + @"(<[^>\n]*>)?\(.*?\n    \}\n", RegexOptions.Singleline);
        Assert.True(match.Success, "method not found: " + method);
        return match.Value;
    }

    private static string WithoutComments(string text) => Regex.Replace(text, @"//[^\n]*", string.Empty);

    [Fact]
    public void SelectionRemoveFiltersInPlainDotNetAndSetsTheSelectionOnce()
    {
        var body = WithoutComments(Body(Source("SelectionExtraNodes.cs"), "Remove"));

        // The requests are hashed and the selection walked once ...
        Assert.Contains("ListRemoval.RemoveFirstOfEach(", body);
        Assert.Contains("ModelItemIdentityComparer.Instance", body);
        // ... never an item at a time out of a Navisworks collection, which searches it on every call ...
        Assert.DoesNotMatch(@"\.Remove\(", body);
        Assert.DoesNotMatch(@"foreach\s*\(", body);
        // ... and the selection is set once, and only when something was taken out.
        Assert.Equal(1, Regex.Matches(body, @"\.CopyFrom\(").Count);
        Assert.Matches(@"if \(removed > 0\)", body);
    }

    [Fact]
    public void TheItemIdentityComparerUsesTheSameRuleAsTheModelItemSet()
    {
        var comparer = Source("Internal/ModelItemIdentityComparer.cs");
        Assert.Contains("x.IsSameInstance(y)", comparer);
        Assert.Contains("item.InstanceHashCode", comparer);
        Assert.Contains("IsSameInstance", Source("Internal/ModelItemSet.cs"));
        Assert.Contains("InstanceHashCode", Source("Internal/ModelItemSet.cs"));
    }
}
