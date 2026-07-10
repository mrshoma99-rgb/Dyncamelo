using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Dyncamelo.Integration.Tests;

/// <summary>
/// Pins the legacy-id migration path for shipped Navisworks nodes. In v0.4 an
/// optional <c>resolveTo</c> parameter was added to the seven Search pickers
/// and <c>Selection.Current</c>, which changed their mangled zero-touch
/// definition ids; every pre-0.4 .dyc that used them would load as a
/// MissingNodeModel without an alias. Dyncamelo.Navisworks cannot be compiled
/// or loaded on this build agent, so — like
/// <see cref="SampleGraphStaticValidationTests"/> — this test harvests the
/// <c>[NodeAliases]</c> attributes straight from the C# source and asserts
/// that every known pre-0.4 definition id is (still) registered as an alias
/// on a method whose path matches the alias. Removing or misspelling one of
/// these aliases breaks users' saved graphs on upgrade and breaks this test.
/// </summary>
public class LegacyDefinitionIdAliasTests
{
    /// <summary>Every definition id shipped before v0.4 whose signature changed in v0.4.</summary>
    private static readonly string[] LegacyIds =
    {
        "Dyncamelo.Navisworks.SearchNodes.ByPropertyValue@string,string,object,Autodesk.Navisworks.Api.Document",
        "Dyncamelo.Navisworks.SearchNodes.ByPropertyContains@string,string,string,Autodesk.Navisworks.Api.Document",
        "Dyncamelo.Navisworks.SearchNodes.ByPropertyWildcard@string,string,string,Autodesk.Navisworks.Api.Document",
        "Dyncamelo.Navisworks.SearchNodes.ByPropertyCompare@string,string,string,double,Autodesk.Navisworks.Api.Document",
        "Dyncamelo.Navisworks.SearchNodes.HasProperty@string,string,Autodesk.Navisworks.Api.Document",
        "Dyncamelo.Navisworks.SearchNodes.HasCategory@string,Autodesk.Navisworks.Api.Document",
        "Dyncamelo.Navisworks.SearchNodes.InItems@System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>,string,string,object,Autodesk.Navisworks.Api.Document",
        "Dyncamelo.Navisworks.SelectionNodes.Current@Autodesk.Navisworks.Api.Document",
    };

    [Fact]
    public void EveryPre04DefinitionId_IsRegisteredAsANodeAlias()
    {
        var aliases = HarvestNavisworksAliases();
        foreach (var legacyId in LegacyIds)
        {
            Assert.True(
                aliases.TryGetValue(legacyId, out var methodPath),
                "Pre-0.4 definition id '" + legacyId + "' has no [NodeAliases] entry in the " +
                "Dyncamelo.Navisworks source — saved graphs using it will open as missing nodes.");

            // The alias must sit on the method it used to identify: same
            // Namespace.Class.Method, only the parameter list may differ.
            var expectedPath = legacyId.Substring(0, legacyId.IndexOf('@'));
            Assert.True(
                expectedPath == methodPath,
                "Alias '" + legacyId + "' is attached to method '" + methodPath +
                "' but its path says '" + expectedPath + "'.");
        }
    }

    [Fact]
    public void EveryNodeAlias_PointsAtItsOwnMethodPath()
    {
        foreach (var pair in HarvestNavisworksAliases())
        {
            var at = pair.Key.IndexOf('@');
            var path = at < 0 ? pair.Key : pair.Key.Substring(0, at);
            Assert.True(
                path == pair.Value,
                "[NodeAliases(\"" + pair.Key + "\")] is attached to method '" + pair.Value +
                "' — an alias may only record an old signature of the SAME method.");
        }
    }

    // ------------------------------------------------------------------
    // Source harvesting (mirrors SampleGraphStaticValidationTests).
    // ------------------------------------------------------------------

    private static readonly Regex NamespaceRegex =
        new Regex(@"^\s*namespace\s+([\w\.]+)\s*;", RegexOptions.Multiline);

    private static readonly Regex ClassRegex =
        new Regex(
            @"^\s*public\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)",
            RegexOptions.Multiline);

    /// <summary>A NodeAliases attribute and the public static method that follows it.</summary>
    private static readonly Regex AliasAttributeRegex = new Regex(
        @"^[ \t]*\[NodeAliases\((?<args>(?:[^\)\r\n""]|""[^""\r\n]*"")*)\)\][ \t]*\r?\n" +
        @"(?:^[ \t]*(?:\[(?:[^\]\r\n""]|""[^""\r\n]*"")*\]|//[^\r\n]*)[ \t]*\r?\n)*" +
        @"^[ \t]*public\s+static\s+[\w\.\<\>\[\]\?,\s]+?\s+(?<name>\w+)\s*\(",
        RegexOptions.Multiline);

    /// <summary>Maps every harvested alias id to the Namespace.Class.Method it is attached to.</summary>
    private static Dictionary<string, string> HarvestNavisworksAliases()
    {
        var sourceDirectory = Path.Combine(
            Path.GetDirectoryName(SampleGraphFileTests.SamplesDirectory())!,
            "src", "Dyncamelo.Navisworks");
        Assert.True(Directory.Exists(sourceDirectory), "Source directory not found: " + sourceDirectory);

        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            var namespaceMatch = NamespaceRegex.Match(text);
            if (!namespaceMatch.Success)
            {
                continue;
            }

            var ns = namespaceMatch.Groups[1].Value;
            var classMatches = ClassRegex.Matches(text).Cast<Match>().ToList();

            foreach (Match match in AliasAttributeRegex.Matches(text))
            {
                var owner = classMatches.LastOrDefault(c => c.Index < match.Index);
                Assert.True(owner != null, file + ": [NodeAliases] found outside any public class.");

                var methodPath = ns + "." + owner!.Groups[1].Value + "." + match.Groups["name"].Value;
                foreach (Match arg in Regex.Matches(match.Groups["args"].Value, "\"([^\"]*)\""))
                {
                    var alias = arg.Groups[1].Value;
                    Assert.False(
                        aliases.ContainsKey(alias) && aliases[alias] != methodPath,
                        "Alias '" + alias + "' is declared on two different methods.");
                    aliases[alias] = methodPath;
                }
            }
        }

        Assert.True(aliases.Count > 0, "No [NodeAliases] attributes harvested from the Dyncamelo.Navisworks source.");
        return aliases;
    }
}
