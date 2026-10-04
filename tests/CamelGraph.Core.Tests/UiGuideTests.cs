using System;
using System.IO;
using CamelGraph.Core.Editing;
using Xunit;

namespace CamelGraph.Core.Tests;

public class UiGuideTests
{
    private static string GuidePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CamelGraph.sln")) && !Directory.Exists(Path.Combine(dir.FullName, "docs")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "docs", "UI_GUIDE.md");
    }

    [Fact]
    public void TheCommittedGuideMatchesTheCatalogues()
    {
        var path = GuidePath();
        var expected = UiGuide.Build();
        if (Environment.GetEnvironmentVariable("CAMELGRAPH_REGEN_DOCS") == "1")
        {
            File.WriteAllText(path, expected, new System.Text.UTF8Encoding(false));
        }

        Assert.True(File.Exists(path), "docs/UI_GUIDE.md is missing. Regenerate: CAMELGRAPH_REGEN_DOCS=1 dotnet test tests/CamelGraph.Core.Tests --filter UiGuideTests");
        var actual = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.True(
            expected == actual,
            "docs/UI_GUIDE.md is out of date with the command/settings catalogues. Regenerate: CAMELGRAPH_REGEN_DOCS=1 dotnet test tests/CamelGraph.Core.Tests --filter UiGuideTests");
    }

    [Fact]
    public void TheGuideListsEveryCommandAndSetting()
    {
        var guide = UiGuide.Build();
        foreach (var command in CommandCatalog.All)
        {
            Assert.Contains("| " + command.Title, guide);
        }

        foreach (var setting in SettingsCatalog.All)
        {
            Assert.Contains("| " + setting.Title + " |", guide);
        }

        foreach (var family in PortKindPalette.Families)
        {
            Assert.Contains("| " + family + " |", guide);
        }
    }
}
