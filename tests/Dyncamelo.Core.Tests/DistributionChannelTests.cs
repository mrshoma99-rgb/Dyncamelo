using System;
using System.IO;
using Dyncamelo.Core.Editing;
using Xunit;

namespace Dyncamelo.Core.Tests;

public class DistributionChannelTests
{
    [Theory]
    [InlineData(null, "direct")]
    [InlineData("", "direct")]
    [InlineData("   \r\n  ", "direct")]
    [InlineData("autodesk-app-store", "autodesk-app-store")]
    [InlineData("  Autodesk-App-Store \r\n", "autodesk-app-store")]
    [InlineData("\n\nautodesk-app-store\nsomething else", "autodesk-app-store")]
    [InlineData("github", "direct")]
    [InlineData("something else\nautodesk-app-store", "direct")]
    public void TheMarkerTextNamesTheChannel(string? text, string expected)
    {
        Assert.Equal(expected, DistributionChannel.Parse(text));
    }

    [Fact]
    public void AFolderWithTheMarkerIsAStoreInstallAndAnyOtherFolderIsNot()
    {
        var folder = Path.Combine(Path.GetTempPath(), "dyc-dist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Assert.Equal(DistributionChannel.Direct, DistributionChannel.Detect(folder));
            Assert.False(DistributionChannel.IsAppStore(folder));

            File.WriteAllText(Path.Combine(folder, DistributionChannel.MarkerFileName), DistributionChannel.AppStore + Environment.NewLine);
            Assert.Equal(DistributionChannel.AppStore, DistributionChannel.Detect(folder));
            Assert.True(DistributionChannel.IsAppStore(folder));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void ANullOrMissingFolderIsTheDirectChannelAndNeverThrows()
    {
        Assert.Equal(DistributionChannel.Direct, DistributionChannel.Detect(null));
        Assert.Equal(DistributionChannel.Direct, DistributionChannel.Detect(string.Empty));
        Assert.Equal(DistributionChannel.Direct, DistributionChannel.Detect(Path.Combine(Path.GetTempPath(), "dyc-no-such-folder-" + Guid.NewGuid().ToString("N"))));
    }
}
