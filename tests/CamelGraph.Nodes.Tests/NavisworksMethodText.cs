using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// Reading one method of a node class of src/CamelGraph.Navisworks from the tests, as text: that project cannot be loaded on the Linux
/// test hosts, so the tests that pin how a node is declared (its attributes, its parameters, what its body calls) look at the source.
/// </summary>
internal static class NavisworksMethodText
{
    /// <summary>
    /// The declaration of a public static method: everything from the end of the member before it (its summary, its attributes) to
    /// the opening brace of its body, parameters included.
    /// </summary>
    /// <param name="file">The file of src/CamelGraph.Navisworks (or a path below it).</param>
    /// <param name="method">The method name.</param>
    internal static string Declaration(string file, string method) => DeclarationIn(NavisworksSourceText.Source(file.Split('/')), method);

    /// <summary>The declaration of a method in the given text, see <see cref="Declaration"/>.</summary>
    /// <param name="source">The text of the file.</param>
    /// <param name="method">The method name.</param>
    internal static string DeclarationIn(string source, string method)
    {
        var match = Regex.Match(
            source,
            @"^    (?:public|internal|private) static [^\n]* " + Regex.Escape(method) + @"(?:\(|\n)",
            RegexOptions.Multiline);
        var at = match.Success ? match.Index : -1;

        Assert.True(at >= 0, "method not found: " + method);

        var start = source.LastIndexOf("\n    }\n", at, StringComparison.Ordinal);
        var classStart = source.LastIndexOf("\n{\n", at, StringComparison.Ordinal);
        start = Math.Max(start, classStart);
        var end = source.IndexOf("\n    {\n", at, StringComparison.Ordinal);
        Assert.True(end > at, "no body found for: " + method);
        return source.Substring(Math.Max(start, 0), end - Math.Max(start, 0));
    }

    /// <summary>
    /// The text of the body of a public static method, from its opening brace to the closing brace at the same indentation.
    /// </summary>
    /// <param name="file">The file of src/CamelGraph.Navisworks.</param>
    /// <param name="method">The method name.</param>
    internal static string Body(string file, string method)
    {
        var source = NavisworksSourceText.Source(file.Split('/'));
        var declaration = DeclarationIn(source, method);
        var begin = source.IndexOf(declaration, StringComparison.Ordinal) + declaration.Length;
        var close = source.IndexOf("\n    }\n", begin, StringComparison.Ordinal);
        Assert.True(close > begin, "no end of body found for: " + method);
        return source.Substring(begin, close - begin);
    }

    /// <summary>The names of all public static methods of the file, in order.</summary>
    /// <param name="file">The file of src/CamelGraph.Navisworks.</param>
    internal static string[] PublicStaticMethods(string file)
    {
        var source = NavisworksSourceText.Source(file.Split('/'));
        return source.Split('\n')
            .Where(l => l.StartsWith("    public static ", StringComparison.Ordinal))
            .Select(l => l.Substring(0, l.IndexOf('(') < 0 ? l.Length : l.IndexOf('(')))
            .Select(l => l.Substring(l.LastIndexOf(' ') + 1))
            .ToArray();
    }
}
