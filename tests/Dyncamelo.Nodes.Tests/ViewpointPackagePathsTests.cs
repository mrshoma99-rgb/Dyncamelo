using System;
using System.IO;
using Dyncamelo.Nodes.Portable;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// Pins the path hardening for the viewpoint package nodes: pasted quotes are
/// stripped, relative paths anchor to the given base folder (never the process
/// working directory), and a folder input gets the default package file name
/// appended — the raw inputs that used to surface as bare "access denied".
/// </summary>
public class ViewpointPackagePathsTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "dyncamelo-docs");

    [Fact]
    public void RootedFilePath_PassesThroughUnchanged()
    {
        var path = Path.Combine(Path.GetTempPath(), "views.json");
        Assert.Equal(path, ViewpointPackagePaths.ResolveForWrite(path, Base));
        Assert.Equal(path, ViewpointPackagePaths.ResolveForRead(path, Base));
    }

    [Fact]
    public void SurroundingQuotes_AndWhitespace_AreStripped()
    {
        var path = Path.Combine(Path.GetTempPath(), "views.json");
        Assert.Equal(path, ViewpointPackagePaths.ResolveForWrite("  \"" + path + "\"  ", Base));
        Assert.Equal(path, ViewpointPackagePaths.ResolveForWrite("'" + path + "'", Base));
    }

    [Fact]
    public void RelativePath_AnchorsToTheBaseFolder_NotTheWorkingDirectory()
    {
        Assert.Equal(
            Path.Combine(Base, "views.json"),
            ViewpointPackagePaths.ResolveForWrite("views.json", Base));
        Assert.Equal(
            Path.Combine(Base, "views.json"),
            ViewpointPackagePaths.ResolveForRead("views.json", Base));
    }

    [Fact]
    public void ExistingDirectory_GetsTheDefaultFileNameAppended()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dyncamelo-paths-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var expected = Path.Combine(directory, ViewpointPackagePaths.DefaultFileName);
            Assert.Equal(expected, ViewpointPackagePaths.ResolveForWrite(directory, Base));
            Assert.Equal(expected, ViewpointPackagePaths.ResolveForRead(directory, Base));
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void TrailingSeparator_MeansFolder_EvenWhenItDoesNotExistYet()
    {
        var directory = Path.Combine(Path.GetTempPath(), "not-created-yet") + Path.DirectorySeparatorChar;
        var resolved = ViewpointPackagePaths.ResolveForWrite(directory, Base);
        Assert.Equal(ViewpointPackagePaths.DefaultFileName, Path.GetFileName(resolved));
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "not-created-yet"), resolved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    public void EmptyInput_Throws(string input)
    {
        Assert.Throws<ArgumentException>(() => ViewpointPackagePaths.ResolveForWrite(input, Base));
    }
}
