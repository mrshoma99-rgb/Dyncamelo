using System.IO;
using System.Linq;
using Xunit;

namespace Dyncamelo.Integration.Tests;

/// <summary>
/// Dyncamelo is licensed under the PolyForm Noncommercial License 1.0.0 (free for personal and other noncommercial use, a commercial
/// licence from BIMCamel for the rest). The licence is stated in several places (the file, the README, the build properties that end up in
/// the DLLs, the contributing guide, the store listing and help page); these tests keep them saying the same thing and keep the previous
/// licence out of the places that describe the current one.
/// </summary>
public class LicenceStatementsTests
{
    private static string Root() => Path.GetDirectoryName(SampleGraphFileTests.SamplesDirectory())!;

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    [Fact]
    public void TheLicenceFileIsThePolyFormNoncommercialTextWithARequiredNotice()
    {
        var licence = Read("LICENSE");

        Assert.StartsWith("Required Notice: Copyright (c) 2026 BIMCamel", licence);
        Assert.Contains("# PolyForm Noncommercial License 1.0.0", licence);
        Assert.Contains("https://polyformproject.org/licenses/noncommercial/1.0.0", licence);
        foreach (var heading in new[] { "Acceptance", "Copyright License", "Distribution License", "Notices", "Changes and New Works License", "Patent License", "Noncommercial Purposes", "Personal Uses", "Noncommercial Organizations", "No Other Rights", "Patent Defense", "Violations", "No Liability", "Definitions" })
        {
            Assert.Contains("## " + heading, licence);
        }

        Assert.DoesNotContain("Commons Clause", licence);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("CONTRIBUTING.md")]
    [InlineData("Directory.Build.props")]
    [InlineData("appstore/listing.md")]
    [InlineData("appstore/help/index.html")]
    public void TheDocumentsNameTheCurrentLicence(string file)
    {
        Assert.Contains("PolyForm Noncommercial", Read(file.Split('/')));
    }

    [Theory]
    [InlineData("CONTRIBUTING.md")]
    [InlineData("Directory.Build.props")]
    [InlineData("appstore/listing.md")]
    [InlineData("appstore/README.md")]
    [InlineData("appstore/help/index.html")]
    public void TheCurrentDocumentsDoNotStillDescribeThePreviousLicence(string file)
    {
        Assert.DoesNotContain("Commons Clause", Read(file.Split('/')));
    }

    [Fact]
    public void TheReadmeMentionsThePreviousLicenceOnlyAsHistory()
    {
        var readme = Read("README.md");

        // The history sentence and the notice for people who already hold an older release are the only places.
        Assert.Contains("Releases up to v0.45.1 were published under Apache 2.0 with the Commons Clause", readme);
        Assert.Contains("v0.26.2–v0.45.1 Apache 2.0 with the Commons Clause", readme);
        Assert.Equal(2, readme.Split(new[] { "Commons Clause" }, System.StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void TheTextOfThePreviousLicenceIsKeptForTheCopiesThatCameWithIt()
    {
        var old = Read("docs", "licenses", "Apache-2.0-with-Commons-Clause_v0.26.2-to-v0.45.1.txt");
        Assert.Contains("Commons Clause", old);
        Assert.Contains("Apache License", old);
        Assert.Contains("v0.26.2 to v0.45.1", Read("docs", "licenses", "README.md"));
    }

    [Fact]
    public void TheListingAndHelpPageSayWhoNeedsACommercialLicence()
    {
        Assert.Contains("commercial licence", Read("appstore", "listing.md"));
        Assert.Contains("commercial licence", Read("appstore", "help", "index.html"));
    }
}
