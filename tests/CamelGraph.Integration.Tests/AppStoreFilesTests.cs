using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using CamelGraph.Core.Editing;
using Xunit;

namespace CamelGraph.Integration.Tests;

/// <summary>
/// The files prepared for the Autodesk App Store (appstore/, tools/build_store_package.py). The form has limits (long description 4000
/// characters, a logo of 80 x 80, screenshots of at most 2000 x 2000 pixels and 20 MB, up to 10 of them), the help page ships inside the
/// package and must not need a network, and the numbers in the listing must stay true as the library grows.
/// </summary>
public class AppStoreFilesTests
{
    private static string Root() => Path.GetDirectoryName(SampleGraphFileTests.SamplesDirectory())!;

    private static string Store(params string[] parts) => Path.Combine(new[] { Root(), "appstore" }.Concat(parts).ToArray());

    private static string Listing() => File.ReadAllText(Store("listing.md"));

    private static string LongDescription()
    {
        var match = Regex.Match(Listing(), "## Long description.*?```text\\r?\\n(.*?)\\r?\\n```", RegexOptions.Singleline);
        Assert.True(match.Success, "listing.md has no ```text block under '## Long description'");
        return match.Groups[1].Value;
    }

    private static (int Width, int Height) PngSize(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 24 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G', path + " is not a PNG");
        int Big(int at) => (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];
        return (Big(16), Big(20));
    }

    [Fact]
    public void TheLongDescriptionFitsTheForm()
    {
        var text = LongDescription();
        Assert.InRange(text.Length, 500, 4000);
    }

    [Fact]
    public void TheDescriptionsNumbersMatchTheLibrary()
    {
        var path = Path.Combine(Root(), "docs", "camelgraph-nodes.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var nodes = document.RootElement.GetProperty("nodes").EnumerateArray().ToList();
        var categories = nodes.Select(n => n.GetProperty("category").GetString()).Distinct().Count();

        var text = LongDescription();
        var claimed = Regex.Match(text, "More than (\\d+) nodes in (\\d+) categories");
        Assert.True(claimed.Success, "the description should say 'More than N nodes in M categories'");
        Assert.True(nodes.Count > int.Parse(claimed.Groups[1].Value), "the description claims more nodes than the library has");
        Assert.Equal(categories, int.Parse(claimed.Groups[2].Value));

        // "Fourteen ready-made sample graphs": the title-case files in samples/ are the ones the editor lists.
        var samples = Directory.GetFiles(SampleGraphFileTests.SamplesDirectory(), "*.dyc").Count(f => char.IsUpper(Path.GetFileName(f)[0]));
        Assert.Contains("Fourteen ready-made sample graphs", text);
        Assert.Equal(14, samples);
    }

    [Fact]
    public void TheListingLinksTheSamePrivacyPolicyAndSupportPageAsThePublisherFile()
    {
        var publisher = JsonDocument.Parse(File.ReadAllText(Store("publisher.json"))).RootElement;
        var listing = Listing();
        Assert.Contains(publisher.GetProperty("privacyPolicyUrl").GetString()!, listing);
        Assert.Contains(publisher.GetProperty("supportUrl").GetString()!, listing);
        Assert.Contains("Help > Privacy Policy", LongDescription());
    }

    [Fact]
    public void ThePublisherFileHasAWellFormedUpgradeCodeAndNoInventedEmail()
    {
        var publisher = JsonDocument.Parse(File.ReadAllText(Store("publisher.json"))).RootElement;
        Assert.Matches("^\\{[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\\}$", publisher.GetProperty("upgradeCode").GetString()!);

        // The support address belongs to the publisher: empty until they set it, and never a placeholder that looks real.
        var email = publisher.GetProperty("supportEmail").GetString()!;
        Assert.True(email.Length == 0 || Regex.IsMatch(email, "^[^@\\s]+@[^@\\s]+\\.[^@\\s.]+$"), "supportEmail is neither empty nor an address");
        Assert.DoesNotContain("example.", email, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("icon-80.png", 80)]
    [InlineData("icon-120.png", 120)]
    [InlineData("icon-256.png", 256)]
    public void TheLogosHaveTheirSizes(string file, int size)
    {
        Assert.Equal((size, size), PngSize(Store("assets", file)));
    }

    [Fact]
    public void TheIconTheManifestPointsAtIsAnIcoFile()
    {
        var bytes = File.ReadAllBytes(Store("assets", "CamelGraph.ico"));
        Assert.True(bytes.Length > 6 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 1 && bytes[3] == 0, "not an .ico file");
    }

    [Fact]
    public void TheScreenshotsFitTheStoresLimits()
    {
        var folder = Store("assets", "screenshots");
        if (!Directory.Exists(folder))
        {
            return;
        }

        var files = Directory.GetFiles(folder, "*.png");
        Assert.True(files.Length <= 10, "the store takes at most 10 screenshots");
        foreach (var file in files)
        {
            var (width, height) = PngSize(file);
            Assert.InRange(width, 400, 2000);
            Assert.InRange(height, 300, 2000);
            Assert.True(new FileInfo(file).Length <= 20 * 1024 * 1024, file + " is over 20 MB");
        }
    }

    [Fact]
    public void TheHelpPageIsSelfContainedAndNamesRealMenus()
    {
        var html = File.ReadAllText(Store("help", "index.html"));

        // It ships inside the package and opens from disk: nothing may be fetched from the network when it is shown.
        Assert.DoesNotMatch("(?i)<script|<link\\b|<iframe|<img\\b|\\bsrc\\s*=|url\\(", html);
        Assert.Contains("{{VERSION}}", html);
        Assert.Contains("{{SUPPORT_EMAIL_LINE}}", html);

        // Every menu or command the page tells people to use has to exist.
        var commands = CommandCatalog.All.Select(c => c.Title.TrimEnd('…', '.')).ToList();
        foreach (var name in new[] { "Run Self-Test", "Copy Diagnostics", "Privacy Policy" })
        {
            Assert.Contains(name, html);
            Assert.Contains(name, commands);
        }

        Assert.Contains("Sample Graphs", html);
        var editor = File.ReadAllText(Path.Combine(Root(), "src", "CamelGraph.UI", "Views", "CamelGraphEditorControl.xaml.cs"));
        Assert.Contains("Header = \"Sample Graphs\"", editor);
    }

    [Fact]
    public void TheStoreMarkerIsTheSameInThePackageBuilderAndInTheCode()
    {
        var script = File.ReadAllText(Path.Combine(Root(), "tools", "build_store_package.py"));
        Assert.Contains("MARKER_FILE = \"" + DistributionChannel.MarkerFileName + "\"", script);
        Assert.Contains("MARKER_TEXT = \"" + DistributionChannel.AppStore + "\"", script);
    }

    [Fact]
    public void TheNavisworksReleasesInThePackageAreTheOnesTheListingNames()
    {
        var script = File.ReadAllText(Path.Combine(Root(), "tools", "build_store_package.py"));
        foreach (var (year, folder) in new[] { ("2024", "v21"), ("2025", "v22"), ("2026", "v23") })
        {
            Assert.Contains("\"" + year + "\": (\"" + folder + "\"", script);
            Assert.Contains("Navisworks Manage / Simulate " + year, Listing());
            Assert.Contains("`Contents/" + folder + "`", Listing());
        }
    }
}
