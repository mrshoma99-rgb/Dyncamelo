using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// Reading the C# source of src/CamelGraph.Navisworks from the tests: that project cannot be loaded on the Linux test hosts,
/// so the tests that pin how its nodes are written look at the text.
/// </summary>
internal static class NavisworksSourceText
{
    internal static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src", "CamelGraph.Navisworks")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    /// <summary>The text of a file of src/CamelGraph.Navisworks (line ends as \n).</summary>
    internal static string Source(params string[] path) =>
        File.ReadAllText(Path.Combine(Root(), "src", "CamelGraph.Navisworks", Path.Combine(path))).Replace("\r\n", "\n");

    /// <summary>The text of a method from the line with its signature to the closing brace at the same indentation.</summary>
    internal static string Body(string source, string signature)
    {
        var match = Regex.Match(source, @"\n    [^\n]*" + Regex.Escape(signature) + @"[^\n]*\n(?:    [^\n]*\n|\n)*?    \}\n");
        Assert.True(match.Success, "method not found: " + signature);
        return match.Value;
    }
}
