using System;
using System.IO;
using Dyncamelo.UI.Services;
using Xunit;

namespace Dyncamelo.UI.Tests;

/// <summary>The privacy policy inside the app is the repository's PRIVACY.md, and the update check can be switched off and stays off.</summary>
public class PrivacyUiTests
{
    [Fact]
    public void TheEmbeddedPolicyIsTheFileInTheRepository()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dyncamelo.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var file = File.ReadAllText(Path.Combine(dir!.FullName, "PRIVACY.md")).Replace("\r\n", "\n");

        Assert.Equal(file, PrivacyPolicy.Text());
        Assert.Contains("## What Dyncamelo sends over the network", PrivacyPolicy.Text());
    }

    [Fact]
    public void TheUpdateCheckSettingIsOnByDefaultAndSurvivesARestart()
    {
        var path = Path.Combine(Path.GetTempPath(), "dyc-privacy-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = new UiSettingsService(path);
            Assert.True(settings.CheckForUpdates);

            settings.SetCheckForUpdates(false);

            Assert.False(new UiSettingsService(path).CheckForUpdates);

            settings.ResetPreferences();
            Assert.True(new UiSettingsService(path).CheckForUpdates);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
