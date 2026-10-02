using System;
using System.IO;
using System.Linq;
using Dyncamelo.Core.Editing;
using Xunit;

namespace Dyncamelo.Integration.Tests;

/// <summary>
/// The Autodesk App Store requires a privacy policy that says what data an app collects and how, who else gets it, how long it is kept
/// and how to delete it or withdraw consent, with its text inside the app. These tests keep PRIVACY.md complete and true to the code.
/// </summary>
public class PrivacyPolicyContentTests
{
    private static string Policy()
    {
        var root = Path.GetDirectoryName(SampleGraphFileTests.SamplesDirectory())!;
        return File.ReadAllText(Path.Combine(root, "PRIVACY.md"));
    }

    [Theory]
    [InlineData("What Dyncamelo stores on your computer")]
    [InlineData("What Dyncamelo sends over the network")]
    [InlineData("What a graph can send")]
    [InlineData("Third parties")]
    [InlineData("Keeping and deleting data")]
    [InlineData("Withdrawing consent and asking questions")]
    public void ThePolicyHasEverySectionTheStoreAsksFor(string heading)
    {
        Assert.Contains("## " + heading, Policy());
    }

    [Fact]
    public void ThePolicyNamesTheFilesTheNetworkRequestAndTheSwitch()
    {
        var policy = Policy();

        foreach (var file in new[] { "ui-settings.json", "errors.log", "update-check.txt", "recovery", "%APPDATA%\\Dyncamelo", "api.github.com", "Dyncamelo-UpdateCheck" })
        {
            Assert.Contains(file, policy);
        }

        // The setting it tells the reader to switch off is the real one, under its real name and section.
        var setting = SettingsCatalog.Find("checkForUpdates");
        Assert.NotNull(setting);
        Assert.Equal("Privacy", setting!.Section);
        Assert.Contains("Settings > Privacy > \"" + setting.Title + "\"", policy);
        Assert.True(setting.DefaultToggle);

        Assert.Contains("Help > Privacy Policy", policy);
        Assert.NotNull(CommandCatalog.Find("help.privacy"));
        Assert.Equal("Privacy Policy", CommandCatalog.Find("help.privacy")!.Title);
    }

    [Fact]
    public void ThePolicyNamesTheFolderTheCodeUsesAndPromisesNothingTheCodeDoesNot()
    {
        var root = Path.GetDirectoryName(SampleGraphFileTests.SamplesDirectory())!;
        var settingsSource = File.ReadAllText(Path.Combine(root, "src", "Dyncamelo.UI", "Services", "UiSettingsService.cs"));
        var updateSource = File.ReadAllText(Path.Combine(root, "src", "Dyncamelo.App", "UpdateCheck.cs"));

        Assert.Contains("\"Dyncamelo\"", settingsSource);
        Assert.Contains("ui-settings.json", settingsSource);
        Assert.Contains("\"recovery\"", settingsSource);
        Assert.Contains("update-check.txt", updateSource);
        Assert.Contains("\"https://api.github.com/repos/\" + Owner + \"/\" + Repo + \"/releases/latest\"", updateSource);
        Assert.Contains("Owner = \"mrshoma99-rgb\"", updateSource);
        Assert.Contains("Repo = \"dyncamelo\"", updateSource);
        Assert.Contains("Dyncamelo-UpdateCheck", updateSource);

        // "Dyncamelo makes no network request of its own" holds only while the first network use in the app is the update check.
        var network = new[] { "HttpClient", "WebClient", "WebRequest.Create", "HttpWebRequest", "TcpClient", "System.Net.Sockets", "new Socket(" };
        var offenders = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Where(f => network.Any(n => File.ReadAllText(f).Contains(n)))
            .Select(f => Path.GetFileName(f))
            .OrderBy(n => n)
            .ToList();

        // The two places that use the network are the update check and the Web.* nodes; the policy covers both.
        Assert.Equal(new[] { "SystemNodes.cs", "UpdateCheck.cs" }, offenders);
    }
}
