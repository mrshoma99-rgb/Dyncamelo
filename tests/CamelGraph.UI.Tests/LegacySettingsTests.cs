using System;
using System.IO;
using CamelGraph.UI.Services;
using Xunit;

namespace CamelGraph.UI.Tests;

/// <summary>
/// Settings written by a version from before the rename (the product was called Dyncamelo up to 0.48) must keep working: the
/// node ids in them start with "Dyncamelo." and the dark theme was called "DyncameloDark".
/// </summary>
public class LegacySettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "camelgraph-legacy-settings-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void FavouriteAndRecentNodesSavedUnderTheOldNameAreUpgraded()
    {
        File.WriteAllText(_path,
            "{\"favoriteNodeIds\":[\"Dyncamelo.Nodes.MathNodes.Atan2@double,double\",\"StringInput\"]," +
            "\"recentNodeIds\":[\"Dyncamelo.Nodes.TableToolkitNodes.Sort@Dyncamelo.Nodes.DyncameloTable,string,bool\"]}");

        var settings = new UiSettingsService(_path);

        Assert.Equal(new[] { "CamelGraph.Nodes.MathNodes.Atan2@double,double", "StringInput" }, settings.FavoriteNodeIds);
        Assert.Equal(new[] { "CamelGraph.Nodes.TableToolkitNodes.Sort@CamelGraph.Nodes.CamelGraphTable,string,bool" }, settings.RecentNodeIds);
    }

    [Fact]
    public void TheDarkThemeSavedUnderTheOldNameBecomesTheCurrentOne()
    {
        File.WriteAllText(_path, "{\"paletteId\":\"DyncameloDark\"}");

        var settings = new UiSettingsService(_path);

        Assert.Equal("CamelGraphDark", settings.PaletteId);
        Assert.NotNull(PaletteCatalog.ById(settings.PaletteId));
    }

    [Fact]
    public void AnUpgradedIdIsSavedUnderTheNewName()
    {
        File.WriteAllText(_path, "{\"favoriteNodeIds\":[\"Dyncamelo.Nodes.MathNodes.Atan2@double,double\"]}");

        var settings = new UiSettingsService(_path);
        settings.SetFavorite("CamelGraph.Nodes.MathNodes.Atan2@double,double", true);
        settings.Save();

        var written = File.ReadAllText(_path);
        Assert.DoesNotContain("Dyncamelo.", written);
        Assert.Single(new UiSettingsService(_path).FavoriteNodeIds);
    }
}
