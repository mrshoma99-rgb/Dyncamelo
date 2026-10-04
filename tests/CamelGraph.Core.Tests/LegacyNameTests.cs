using System;
using System.IO;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Tests.Fixtures;
using Xunit;

namespace CamelGraph.Core.Tests;

/// <summary>
/// The product was called Dyncamelo up to version 0.48. Graphs saved then carry definition ids in the old namespace, and the
/// settings and scripts of those versions sit in folders with the old name. These tests keep both working after the rename.
/// </summary>
public class LegacyNameTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "camelgraph-legacy-" + Guid.NewGuid().ToString("N"));

    public LegacyNameTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData("Dyncamelo.Nodes.MathNodes.Atan2@double,double", "CamelGraph.Nodes.MathNodes.Atan2@double,double")]
    [InlineData("Dyncamelo.Nodes.TableToolkitNodes.Sort@Dyncamelo.Nodes.DyncameloTable,string,bool", "CamelGraph.Nodes.TableToolkitNodes.Sort@CamelGraph.Nodes.CamelGraphTable,string,bool")]
    [InlineData("Dyncamelo.Navisworks.SearchNodes.HasCategory@string,string,Autodesk.Navisworks.Api.Document", "CamelGraph.Navisworks.SearchNodes.HasCategory@string,string,Autodesk.Navisworks.Api.Document")]
    public void UpgradeLegacyId_RenamesTheNamespaceAndTheValueTypes(string legacy, string expected)
    {
        Assert.Equal(expected, NodeRegistry.UpgradeLegacyId(legacy));
    }

    [Theory]
    [InlineData("CamelGraph.Nodes.MathNodes.Atan2@double,double")]
    [InlineData("StringInput")]
    [InlineData("MyPack.Dyncamelo.Thing@int")]
    [InlineData("")]
    public void UpgradeLegacyId_LeavesOtherIdsAlone(string id)
    {
        Assert.Same(id, NodeRegistry.UpgradeLegacyId(id));
    }

    [Fact]
    public void Registry_ResolvesAnIdSavedUnderTheOldName()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterDefinitions(ZT.All);

        // Written by 0.48: the same method in the old namespace.
        var saved = "Dyncamelo.Core.Tests.Fixtures.MathFixtures.Add@double,double";
        Assert.True(registry.TryGetDefinition(saved, out var definition));
        Assert.Equal("CamelGraph.Core.Tests.Fixtures.MathFixtures.Add@double,double", definition!.Id);
        Assert.NotNull(registry.CreateZeroTouchNode(saved));
    }

    [Fact]
    public void Registry_ResolvesAnOldAliasToo()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterDefinitions(ZT.All);

        // An id from before a signature change AND before the rename.
        Assert.True(registry.TryGetDefinition("Dyncamelo.Core.Tests.Fixtures.MathFixtures.Doubler@double", out var definition));
        Assert.Equal("CamelGraph.Core.Tests.Fixtures.MathFixtures.Doubler@double,double", definition!.Id);
    }

    [Fact]
    public void Registry_StillRejectsAnUnknownOldId()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterDefinitions(ZT.All);

        Assert.Null(registry.CreateZeroTouchNode("Dyncamelo.Core.Tests.Fixtures.MathFixtures.NoSuchMethod@double"));
    }

    [Fact]
    public void MigrateDirectory_MovesTheOldFolderWhenOnlyItExists()
    {
        var old = Path.Combine(_root, "Dyncamelo");
        var current = Path.Combine(_root, "CamelGraph");
        Directory.CreateDirectory(Path.Combine(old, "recovery"));
        File.WriteAllText(Path.Combine(old, "ui-settings.json"), "{}");
        File.WriteAllText(Path.Combine(old, "recovery", "a.dyc"), "graph");

        Assert.True(LegacyData.MigrateDirectory(old, current));

        Assert.False(Directory.Exists(old));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(current, "ui-settings.json")));
        Assert.Equal("graph", File.ReadAllText(Path.Combine(current, "recovery", "a.dyc")));
    }

    [Fact]
    public void MigrateDirectory_CopiesOnlyWhatTheNewFolderLacks()
    {
        var old = Path.Combine(_root, "Dyncamelo");
        var current = Path.Combine(_root, "CamelGraph");
        Directory.CreateDirectory(Path.Combine(old, "recovery"));
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(old, "ui-settings.json"), "old settings");
        File.WriteAllText(Path.Combine(old, "errors.log"), "old log");
        File.WriteAllText(Path.Combine(old, "recovery", "a.dyc"), "old graph");
        File.WriteAllText(Path.Combine(current, "ui-settings.json"), "new settings");

        Assert.True(LegacyData.MigrateDirectory(old, current));

        Assert.Equal("new settings", File.ReadAllText(Path.Combine(current, "ui-settings.json")));
        Assert.Equal("old log", File.ReadAllText(Path.Combine(current, "errors.log")));
        Assert.Equal("old graph", File.ReadAllText(Path.Combine(current, "recovery", "a.dyc")));
        Assert.True(Directory.Exists(old), "a merge leaves the old folder where it is");

        // Nothing left to bring across the second time.
        Assert.False(LegacyData.MigrateDirectory(old, current));
    }

    [Fact]
    public void MigrateDirectory_DoesNothingWithoutAnOldFolderOrForTheSameFolder()
    {
        var current = Path.Combine(_root, "CamelGraph");
        Assert.False(LegacyData.MigrateDirectory(Path.Combine(_root, "Dyncamelo"), current));
        Assert.False(Directory.Exists(current));

        Directory.CreateDirectory(current);
        Assert.False(LegacyData.MigrateDirectory(current, current));
        Assert.False(LegacyData.MigrateDirectory(string.Empty, current));
    }

    [Fact]
    public void TheOldFolderNamesAreTheOnesTheLastDyncameloVersionUsed()
    {
        Assert.EndsWith("Dyncamelo", LegacyData.OldDataDirectory);
        Assert.EndsWith(Path.Combine("Dyncamelo", "Scripts"), LegacyData.OldScriptsFolder);
    }
}
