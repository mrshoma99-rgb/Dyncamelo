using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Dyncamelo.TestSupport.StandIns;

namespace Dyncamelo.TestSupport.Wiki;

/// <summary>What <c>tools/wiki/image-manifest.md</c> lists: the image ids and the names of the how-to graphs.</summary>
internal static class Manifest
{
    public static string Path() => System.IO.Path.Combine(RepoLocator.Root(), "tools", "wiki", "image-manifest.md");

    /// <summary>
    /// The image ids of the manifest's table of scenes (<c>wiki-start-screen</c>, ...). The generic rows (<c>wiki-sample-&lt;kebab-name&gt;</c>,
    /// <c>wiki-graph-&lt;name&gt;</c>) are left out: they stand for a picture per file.
    /// </summary>
    public static IReadOnlyList<string> ImageIds()
    {
        var path = Path();
        var ids = new List<string>();
        if (!File.Exists(path))
        {
            return ids;
        }

        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            if (!line.StartsWith("| `wiki-", StringComparison.Ordinal))
            {
                continue;
            }

            var firstCell = line.Split('|')[1];
            foreach (Match match in Regex.Matches(firstCell, "`(wiki-[a-z0-9-]+)`"))
            {
                ids.Add(match.Groups[1].Value);
            }
        }

        return ids;
    }

    /// <summary>The names of the how-to graphs (<c>first-script</c>, <c>colour-by-value</c>...), the first column of the manifest's second table.</summary>
    public static IReadOnlyList<string> GraphNames()
    {
        var path = Path();
        var names = new List<string>();
        if (!File.Exists(path))
        {
            return names;
        }

        var inGraphs = false;
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inGraphs = line.IndexOf("How-to graphs", StringComparison.OrdinalIgnoreCase) >= 0;
                continue;
            }

            if (inGraphs && line.StartsWith("| `", StringComparison.Ordinal))
            {
                var match = Regex.Match(line, "^\\| `([a-z0-9-]+)` \\|");
                if (match.Success)
                {
                    names.Add(match.Groups[1].Value);
                }
            }
        }

        return names;
    }

    /// <summary>A name turned into a file-name part: lower case, words joined by single hyphens ("Isolated Viewpoints (Loop)" gives "isolated-viewpoints-loop").</summary>
    public static string Kebab(string name)
    {
        var text = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return text.Length == 0 ? "graph" : text;
    }

    /// <summary>The image id of the picture of a sample graph file.</summary>
    public static string SampleImageId(string fileNameWithoutExtension) => "wiki-sample-" + Kebab(fileNameWithoutExtension);
}
