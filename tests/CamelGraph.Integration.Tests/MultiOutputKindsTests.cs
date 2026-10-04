using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CamelGraph.Core.Editing;
using Xunit;

namespace CamelGraph.Integration.Tests;

/// <summary>
/// A multi-output node (<c>[MultiReturn]</c>) returns a dictionary, so its sockets carry no type of their own: without
/// <c>[PortKinds]</c> every one is a grey "any" socket until the graph has run. Reads the C# source, as the alias test does.
/// </summary>
public class MultiOutputKindsTests
{
    private static readonly Regex MultiReturn = new Regex(
        @"^[ \t]*\[MultiReturn\((?<keys>(?:[^\)""]|""[^""]*"")*)\)\][ \t]*\r?\n(?<rest>(?:[ \t]*\[[^\r\n]*\][ \t]*\r?\n|[ \t]*//[^\r\n]*\r?\n)*)",
        RegexOptions.Multiline);

    private static readonly Regex Kinds = new Regex(@"\[PortKinds\((?<kinds>(?:[^\)""]|""[^""]*"")*)\)\]");
    private static readonly Regex Name = new Regex(@"\[NodeName\(""(?<name>[^""]+)""\)\]");
    private static readonly Regex Retired = new Regex(@"\[NodeDeprecated\(");

    private static IEnumerable<(string File, string Node, string[] Keys, string[]? Kinds, bool IsRetired)> Harvest()
    {
        var root = Path.GetDirectoryName(SampleGraphFileTests.SamplesDirectory())!;
        foreach (var project in new[] { "CamelGraph.Nodes", "CamelGraph.Navisworks" })
        {
            var directory = Path.Combine(root, "src", project);
            Assert.True(Directory.Exists(directory), "Source directory not found: " + directory);
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                {
                    continue;
                }

                // A Windows checkout has CRLF line endings; the block logic below looks for blank lines.
                var text = File.ReadAllText(file).Replace("\r\n", "\n");
                foreach (Match match in MultiReturn.Matches(text))
                {
                    // The attributes of the same method: the lines around [MultiReturn] up to the method itself.
                    var start = text.LastIndexOf("\n\n", match.Index, StringComparison.Ordinal);
                    var end = text.IndexOf("public static", match.Index, StringComparison.Ordinal);
                    var block = text.Substring(Math.Max(start, 0), end - Math.Max(start, 0));
                    var keys = Regex.Matches(match.Groups["keys"].Value, "\"([^\"]*)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
                    var kinds = Kinds.Match(block);
                    var name = Name.Match(block);
                    yield return (
                        Path.GetFileName(file),
                        name.Success ? name.Groups["name"].Value : "(unnamed)",
                        keys,
                        kinds.Success ? Regex.Matches(kinds.Groups["kinds"].Value, "\"([^\"]*)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToArray() : null,
                        Retired.IsMatch(block));
                }
            }
        }
    }

    [Fact]
    public void EveryMultiOutputNodeDeclaresTheKindOfEachOutput()
    {
        var all = Harvest().ToList();
        Assert.True(all.Count > 50, "expected the multi-output nodes, found " + all.Count);

        foreach (var node in all.Where(n => !n.IsRetired))
        {
            Assert.True(node.Kinds != null, node.Node + " (" + node.File + ") has no [PortKinds] for its outputs — its sockets would be grey until it runs.");
            Assert.True(
                node.Kinds!.Length == node.Keys.Length,
                node.Node + " (" + node.File + ") declares " + node.Kinds.Length + " kinds for " + node.Keys.Length + " outputs.");
        }
    }

    [Fact]
    public void EveryDeclaredKindIsOneTheEditorUnderstands()
    {
        foreach (var node in Harvest().Where(n => n.Kinds != null))
        {
            foreach (var kind in node.Kinds!.Where(k => k.Length > 0))
            {
                Assert.True(PortKinds.TryParse(kind, out _), node.Node + " (" + node.File + ") declares the unknown kind '" + kind + "'.");
            }
        }
    }
}
