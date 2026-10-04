using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Tests.Fixtures;
using Xunit;

namespace CamelGraph.Core.Tests;

public class NodePacksTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "camelgraph-packs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder left behind is harmless.
        }
    }

    private string Dll(string folder, string name)
    {
        var path = Path.Combine(_root, folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not really a dll; the loader under test never opens it");
        return path;
    }

    // Pretends every DLL is the test assembly, which has fixture nodes in it.
    private static Assembly FixturePack(string path) => typeof(MathFixtures).Assembly;

    [Fact]
    public void ThePerUserFolderIsSearchedBeforeTheFolderBesideThePlugIn()
    {
        var folders = NodePacks.Folders(@"C:\Plug-in\2024", @"C:\Users\me\AppData\Roaming\CamelGraph\Packages");

        Assert.Equal(@"C:\Users\me\AppData\Roaming\CamelGraph\Packages", folders[0]);
        Assert.Equal(Path.Combine(@"C:\Plug-in\2024", "Packages"), folders[1]);
    }

    [Fact]
    public void TheUserFolderLivesWithTheSettingsNotInTheBundleSoAnUpdateKeepsIt()
    {
        var path = NodePacks.UserFolder.Replace('/', '\\');

        Assert.EndsWith(@"CamelGraph\Packages", path);
        Assert.DoesNotContain("ApplicationPlugins", path);
    }

    [Fact]
    public void FoldersThatDoNotExistAreIgnored()
    {
        var report = NodePacks.Load(new NodeRegistry(), new[] { Path.Combine(_root, "nothing-here") }, FixturePack);

        Assert.Empty(report.Results);
        Assert.Empty(report.Folders);
        Assert.Equal("No node packs are installed.", report.Summary());
    }

    [Fact]
    public void ADllInASubfolderIsLoadedAndItsNodesAreCounted()
    {
        Dll("user", @"Rebar\RebarToolkit.dll");
        var registry = new NodeRegistry();

        var report = NodePacks.Load(registry, new[] { Path.Combine(_root, "user") }, FixturePack);

        var result = Assert.Single(report.Results);
        Assert.Equal(NodePackStatus.Loaded, result.Status);
        Assert.True(result.Nodes > 0);
        Assert.Equal(result.Nodes, report.NodeCount);
        Assert.Equal(1, report.LoadedCount);
        Assert.Contains(registry.Definitions, d => d.Id.StartsWith("CamelGraph.Core.Tests.Fixtures.MathFixtures.", StringComparison.Ordinal));
        Assert.StartsWith("1 node pack loaded (", report.Summary());
        Assert.Contains("RebarToolkit.dll: " + result.Nodes, report.Lines().Single());
    }

    [Fact]
    public void ABrokenPackIsReportedAndTheOthersStillLoad()
    {
        Dll("user", "A-Broken.dll");
        Dll("user", "B-Good.dll");

        var report = NodePacks.Load(
            new NodeRegistry(),
            new[] { Path.Combine(_root, "user") },
            path => Path.GetFileName(path) == "A-Broken.dll" ? throw new BadImageFormatException("not a .NET assembly") : typeof(MathFixtures).Assembly);

        Assert.Equal(1, report.FailedCount);
        Assert.Equal(1, report.LoadedCount);
        var failed = report.Results.Single(r => r.Status == NodePackStatus.Failed);
        Assert.Contains("BadImageFormatException", failed.Problem);
        Assert.Contains("A-Broken.dll: NOT LOADED.", report.Lines()[0]);
        Assert.Contains("1 could not be loaded", report.Summary());
    }

    [Fact]
    public void AFileNameMetInAnEarlierFolderWinsOverALaterOne()
    {
        var user = Dll("user", "Pack.dll");
        Dll("beside", "Pack.dll");

        var report = NodePacks.Load(
            new NodeRegistry(),
            new[] { Path.Combine(_root, "user"), Path.Combine(_root, "beside") },
            FixturePack);

        Assert.Equal(2, report.Results.Count);
        Assert.Equal(NodePackStatus.Loaded, report.Results[0].Status);
        Assert.Equal(user, report.Results[0].Path);
        Assert.Equal(NodePackStatus.Skipped, report.Results[1].Status);
        Assert.Contains("same name", report.Results[1].Problem);
    }

    [Theory]
    [InlineData("CamelGraph.Core.dll")]
    [InlineData("Autodesk.Navisworks.Api.dll")]
    [InlineData("Newtonsoft.Json.dll")]
    [InlineData("System.Memory.dll")]
    public void CopiesOfLibrariesTheHostAlreadyHasAreLeftOut(string name)
    {
        Dll("user", name);
        var loaded = false;

        var report = NodePacks.Load(new NodeRegistry(), new[] { Path.Combine(_root, "user") }, p => { loaded = true; return typeof(MathFixtures).Assembly; });

        Assert.False(loaded, "the file must not even be loaded");
        Assert.Equal(NodePackStatus.Skipped, Assert.Single(report.Results).Status);
        Assert.Contains("already have their own copy", report.Results[0].Problem);
    }

    [Fact]
    public void TheLinesSayWhatEachDllDid()
    {
        var report = new NodePackReport(
            new[] { "packs" },
            new[]
            {
                new NodePackResult(Path.Combine("packs", "Rebar.dll"), NodePackStatus.Loaded, 12, null),
                new NodePackResult(Path.Combine("packs", "One.dll"), NodePackStatus.Loaded, 1, null),
                new NodePackResult(Path.Combine("packs", "Helper.dll"), NodePackStatus.Loaded, 0, null),
                new NodePackResult(Path.Combine("packs", "Json.dll"), NodePackStatus.Skipped, 0, "Own copy."),
                new NodePackResult(Path.Combine("packs", "Bad.dll"), NodePackStatus.Failed, 0, "FileLoadException: no."),
            });

        Assert.Equal(
            new[]
            {
                "Rebar.dll: 12 nodes",
                "One.dll: 1 node",
                "Helper.dll: loaded, no nodes (a library the pack uses)",
                "Json.dll: not used. Own copy.",
                "Bad.dll: NOT LOADED. FileLoadException: no.",
            },
            report.Lines());
        Assert.Equal("2 node packs loaded (13 nodes), 1 could not be loaded.", report.Summary());
    }
}
