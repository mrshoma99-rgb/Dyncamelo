using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>Every top-level category of the node library has a glyph in the tree (a category added without one shows a bare name).</summary>
public class LibraryIconsTests
{
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Dyncamelo.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    [Fact]
    public void EveryTopLevelCategoryHasAnIconInTheLibraryTree()
    {
        var root = Root();
        var catalogue = JToken.Parse(File.ReadAllText(Path.Combine(root, "docs", "dyncamelo-nodes.json")));
        var nodes = catalogue is JArray array ? array : (JArray)catalogue["nodes"]!;
        var categories = nodes.Select(n => ((string?)n["category"] ?? string.Empty).Split('.')[0]).Where(c => c.Length > 0).Distinct().OrderBy(c => c).ToList();
        Assert.True(categories.Count >= 15, "the catalogue lists only " + categories.Count + " top-level categories");

        var tree = File.ReadAllText(Path.Combine(root, "src", "Dyncamelo.UI", "Views", "DyncameloEditorControl.xaml"));
        var theme = File.ReadAllText(Path.Combine(root, "src", "Dyncamelo.UI", "Themes", "DyncameloDark.xaml"));
        var triggered = new HashSet<string>(Regex.Matches(tree, @"Binding=""\{Binding IconKey\}""\s+Value=""(?<k>[^""]+)""").Cast<Match>().Select(m => m.Groups["k"].Value));
        var drawn = new HashSet<string>(Regex.Matches(theme, @"x:Key=""Dyc\.Icon\.Cat\.(?<k>[A-Za-z]+)""").Cast<Match>().Select(m => m.Groups["k"].Value));

        var withoutTrigger = categories.Where(c => !triggered.Contains(c)).ToList();
        var withoutGlyph = categories.Where(c => !drawn.Contains(c)).ToList();
        Assert.True(withoutTrigger.Count == 0, "No icon trigger in the library tree for: " + string.Join(", ", withoutTrigger));
        Assert.True(withoutGlyph.Count == 0, "No Dyc.Icon.Cat.* glyph in the theme for: " + string.Join(", ", withoutGlyph));
    }
}
