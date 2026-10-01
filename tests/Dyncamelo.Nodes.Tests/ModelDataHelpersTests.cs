using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dyncamelo.Nodes.Portable;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// The pure halves of the Navisworks model-data nodes: Properties.Discover (catalog), Model.Statistics (group counts),
/// Model.Snapshot (keying and the shape Snapshot.Diff reads), Search.ByGuid (GUID list and lookup order) and
/// ModelItem.IfcGuid (which property names and values count as an IFC id).
/// </summary>
public class ModelDataHelpersTests
{
    private static IList<object?> L(params object?[] items) => new List<object?>(items);

    private static object?[] Row(DyncameloTable table, string category, string property) =>
        table.Rows.Single(r => (string?)r[0] == category && (string?)r[1] == property);

    // ===================================================================== Properties.Discover

    [Fact]
    public void Catalog_CountsItemsDistinctValuesAndSamples()
    {
        var catalog = new PropertyCatalog(3);
        catalog.Add(0, "Element", "Category", "Walls");
        catalog.Add(1, "Element", "Category", "Walls");
        catalog.Add(2, "Element", "Category", "Doors");
        catalog.Add(3, "Element", "Category", "Floors");
        catalog.Add(4, "Element", "Category", "Pipes");
        catalog.Add(1, "Item", "Name", "W-01");

        var table = catalog.ToTable();

        Assert.Equal(new[] { "Category", "Property", "Items", "Distinct", "Samples" }, table.Headers);
        Assert.Equal(2, table.RowCount);
        var category = Row(table, "Element", "Category");
        Assert.Equal(5d, category[2]);
        Assert.Equal(4d, category[3]);
        Assert.Equal("Walls | Doors | Floors", category[4]);
        var name = Row(table, "Item", "Name");
        Assert.Equal(1d, name[2]);
        Assert.Equal(1d, name[3]);
        Assert.Equal("W-01", name[4]);
    }

    [Fact]
    public void Catalog_RowsAreOrderedByCategoryThenProperty_IgnoringCase()
    {
        var catalog = new PropertyCatalog(1);
        catalog.Add(0, "item", "Name", "a");
        catalog.Add(0, "Element", "Level", "L1");
        catalog.Add(0, "Element", "category", "Wall");
        catalog.Add(0, "Element", "Area", 12.5);
        catalog.Add(0, "Zone", "Id", "z");

        var table = catalog.ToTable();

        Assert.Equal(
            new[] { "Element/Area", "Element/category", "Element/Level", "item/Name", "Zone/Id" },
            table.Rows.Select(r => r[0] + "/" + r[1]));
    }

    [Fact]
    public void Catalog_CountsAnItemOnceEvenWhenItListsThePropertyTwice()
    {
        var catalog = new PropertyCatalog(5);
        catalog.Add(0, "Element", "Tag", "A");
        catalog.Add(0, "Element", "Tag", "B");
        catalog.Add(1, "Element", "Tag", "A");

        var row = Row(catalog.ToTable(), "Element", "Tag");

        Assert.Equal(2d, row[2]);
        Assert.Equal(2d, row[3]);
    }

    [Fact]
    public void Catalog_EmptyValuesCountAsHavingThePropertyButNotAsAValue()
    {
        var catalog = new PropertyCatalog(3);
        catalog.Add(0, "Element", "Comment", null);
        catalog.Add(1, "Element", "Comment", string.Empty);
        catalog.Add(2, "Element", "Comment", "check");

        var row = Row(catalog.ToTable(), "Element", "Comment");

        Assert.Equal(3d, row[2]);
        Assert.Equal(1d, row[3]);
        Assert.Equal("check", row[4]);
    }

    [Fact]
    public void Catalog_KeepsOnlyMaxSamplesInTheOrderTheyAppeared()
    {
        var catalog = new PropertyCatalog(2);
        foreach (var (value, index) in new[] { "c", "a", "b", "a", "d" }.Select((v, i) => (v, i)))
        {
            catalog.Add(index, "E", "P", value);
        }

        var row = Row(catalog.ToTable(), "E", "P");

        Assert.Equal("c | a", row[4]);
        Assert.Equal(4d, row[3]);
    }

    [Fact]
    public void Catalog_ZeroSamplesLeavesTheColumnEmpty()
    {
        var catalog = new PropertyCatalog(0);
        catalog.Add(0, "E", "P", "x");

        var row = Row(catalog.ToTable(), "E", "P");

        Assert.Equal(string.Empty, row[4]);
        Assert.Equal(1d, row[3]);
    }

    [Fact]
    public void Catalog_CountsDistinctValuesOnlyUpToTheCap()
    {
        var catalog = new PropertyCatalog(2, distinctCap: 10);
        for (int i = 0; i < 25; i++)
        {
            catalog.Add(i, "E", "P", "v" + i.ToString(CultureInfo.InvariantCulture));
        }

        var row = Row(catalog.ToTable(), "E", "P");

        Assert.Equal(25d, row[2]);
        Assert.Equal(10d, row[3]);
        Assert.Equal("v0 | v1", row[4]);
    }

    [Fact]
    public void Catalog_TheDefaultCapIsTenThousand()
    {
        Assert.Equal(10000, PropertyCatalog.DefaultDistinctCap);
        var catalog = new PropertyCatalog(1);
        for (int i = 0; i < 10050; i++)
        {
            catalog.Add(i, "E", "P", i);
        }

        Assert.Equal(10000d, Row(catalog.ToTable(), "E", "P")[3]);
    }

    [Fact]
    public void Catalog_ValuesAreCountedByTheirInvariantText()
    {
        var catalog = new PropertyCatalog(5);
        catalog.Add(0, "E", "Length", 1.5);
        catalog.Add(1, "E", "Length", 1.5);
        catalog.Add(2, "E", "Length", 2);
        catalog.Add(3, "E", "Length", true);
        catalog.Add(4, "E", "Length", new DateTime(2024, 3, 5, 14, 30, 0, DateTimeKind.Unspecified));

        var row = Row(catalog.ToTable(), "E", "Length");

        Assert.Equal(4d, row[3]);
        Assert.Equal("1.5 | 2 | True | 2024-03-05 14:30:00", row[4]);
    }

    [Fact]
    public void Catalog_UsesInvariantCultureForNumbers()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1234.5", PropertyCatalog.ValueText(1234.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Catalog_CutsLongSamples()
    {
        var catalog = new PropertyCatalog(1);
        catalog.Add(0, "E", "P", new string('x', 300));

        var sample = (string)Row(catalog.ToTable(), "E", "P")[4]!;

        Assert.Equal(PropertyCatalog.MaxSampleLength, sample.Length);
        Assert.EndsWith("...", sample);
    }

    [Fact]
    public void Catalog_EmptyCatalogIsAnEmptyTableWithTheHeaders()
    {
        var table = new PropertyCatalog(3).ToTable();

        Assert.Equal(0, table.RowCount);
        Assert.Equal(5, table.ColumnCount);
        Assert.Equal(0, new PropertyCatalog(3).PropertyCount);
    }

    [Fact]
    public void Catalog_RejectsNegativeLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PropertyCatalog(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PropertyCatalog(1, 0));
    }

    // ===================================================================== Model.Statistics

    [Fact]
    public void Statistics_CountsItemsAndGeometryPerGroup_BiggestFirst()
    {
        var stats = new GroupStatistics();
        stats.Add("Walls.nwc", true);
        stats.Add("Walls.nwc", true);
        stats.Add("Walls.nwc", false);
        stats.Add("MEP.nwc", true);

        var table = stats.ToTable();

        Assert.Equal(new[] { "Group", "Items", "WithGeometry", "Share" }, table.Headers);
        Assert.Equal(2, table.RowCount);
        Assert.Equal(new object?[] { "Walls.nwc", 3d, 2d, 75.0 }, table.Rows[0]);
        Assert.Equal(new object?[] { "MEP.nwc", 1d, 1d, 25.0 }, table.Rows[1]);
        Assert.Equal(4, stats.Total);
    }

    [Fact]
    public void Statistics_SharesAreRoundedToOneDecimal()
    {
        var stats = new GroupStatistics();
        stats.Add("A", false);
        stats.Add("B", false);
        stats.Add("C", false);

        var table = stats.ToTable();

        Assert.All(table.Rows, r => Assert.Equal(33.3, r[3]));
        Assert.Equal(66.7, GroupStatistics.Share(2, 3));
        Assert.Equal(0.1, GroupStatistics.Share(1, 1000));
        Assert.Equal(0.1, GroupStatistics.Share(1, 1600));
        Assert.Equal(100.0, GroupStatistics.Share(7, 7));
        Assert.Equal(0.0, GroupStatistics.Share(0, 0));
    }

    [Fact]
    public void Statistics_EqualGroupsAreOrderedByName()
    {
        var stats = new GroupStatistics();
        stats.Add("beta", false);
        stats.Add("Alpha", false);
        stats.Add("gamma", false);

        Assert.Equal(new object?[] { "Alpha", "beta", "gamma" }, stats.ToTable().Rows.Select(r => r[0]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Statistics_ItemsWithoutAValueFallIntoNone(string? group)
    {
        var stats = new GroupStatistics();
        stats.Add(group, false);
        stats.Add("(none)", true);

        var table = stats.ToTable();

        Assert.Equal(1, table.RowCount);
        Assert.Equal(new object?[] { "(none)", 2d, 1d, 100.0 }, table.Rows[0]);
        Assert.Equal(1, stats.GroupCount);
    }

    [Fact]
    public void Statistics_NothingCountedGivesAnEmptyTable()
    {
        var table = new GroupStatistics().ToTable();

        Assert.Equal(0, table.RowCount);
        Assert.Equal(4, table.ColumnCount);
    }

    // ===================================================================== Model.Snapshot

    private static readonly Guid GuidA = new Guid("3f81e10a-25b0-49ff-9520-63f2a763150a");
    private static readonly Guid GuidB = new Guid("22e66ddf-794d-40bb-8aa5-3dda450d8255");

    [Fact]
    public void SnapshotKey_IsTheLowerCaseHyphenatedGuid()
    {
        var snapshot = new SnapshotBuilder();

        var key = snapshot.Add(Guid.Parse("3F81E10A-25B0-49FF-9520-63F2A763150A"), () => "unused", new[] { "P" }, new object?[] { 1 });

        Assert.Equal("3f81e10a-25b0-49ff-9520-63f2a763150a", key);
        Assert.Single(snapshot.Snapshot);
        Assert.True(snapshot.Snapshot.ContainsKey(key));
    }

    [Fact]
    public void SnapshotKey_FallsBackToThePathWhenThereIsNoGuid()
    {
        var snapshot = new SnapshotBuilder();

        var key = snapshot.Add(Guid.Empty, () => "Site.nwc > Level 1 > Wall", new[] { "P" }, new object?[] { 1 });

        Assert.Equal("path:Site.nwc > Level 1 > Wall", key);
    }

    [Fact]
    public void SnapshotKey_ThePathIsOnlyComputedWhenNeeded()
    {
        var snapshot = new SnapshotBuilder();
        var calls = 0;

        snapshot.Add(GuidA, () => { calls++; return "p"; }, new[] { "P" }, new object?[] { 1 });
        snapshot.Add(Guid.Empty, () => { calls++; return "p"; }, new[] { "P" }, new object?[] { 1 });

        Assert.Equal(1, calls);
    }

    [Fact]
    public void SnapshotKey_DuplicatesGetANumberedSuffixSoNoItemIsLost()
    {
        var snapshot = new SnapshotBuilder();

        var first = snapshot.Add(GuidA, () => "x", new[] { "P" }, new object?[] { 1 });
        var second = snapshot.Add(GuidA, () => "x", new[] { "P" }, new object?[] { 2 });
        var third = snapshot.Add(GuidA, () => "x", new[] { "P" }, new object?[] { 3 });
        var path1 = snapshot.Add(Guid.Empty, () => "Same", new[] { "P" }, new object?[] { 4 });
        var path2 = snapshot.Add(Guid.Empty, () => "Same", new[] { "P" }, new object?[] { 5 });

        Assert.Equal("3f81e10a-25b0-49ff-9520-63f2a763150a", first);
        Assert.Equal(first + " #2", second);
        Assert.Equal(first + " #3", third);
        Assert.Equal("path:Same", path1);
        Assert.Equal("path:Same #2", path2);
        Assert.Equal(5, snapshot.Snapshot.Count);
    }

    [Fact]
    public void SnapshotValue_IsADictionaryOfHeaderToValue()
    {
        var snapshot = new SnapshotBuilder();

        var key = snapshot.Add(GuidA, () => "x", new[] { "Element.Category", "Item|Layer", "@Name" }, new object?[] { "Walls", null, "W-01" });

        var properties = Assert.IsType<Dictionary<string, object?>>(snapshot.Snapshot[key]);
        Assert.Equal(new[] { "Element.Category", "Item|Layer", "@Name" }, properties.Keys);
        Assert.Equal("Walls", properties["Element.Category"]);
        Assert.Null(properties["Item|Layer"]);
        Assert.Equal("W-01", properties["@Name"]);
    }

    [Fact]
    public void SnapshotValue_PlainValuesStayAndOthersBecomeInvariantText()
    {
        var date = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        Assert.Null(SnapshotBuilder.Normalize(null));
        Assert.Equal("text", SnapshotBuilder.Normalize("text"));
        Assert.Equal(true, SnapshotBuilder.Normalize(true));
        Assert.Equal(7, SnapshotBuilder.Normalize(7));
        Assert.Equal(7L, SnapshotBuilder.Normalize(7L));
        Assert.Equal(1.25, SnapshotBuilder.Normalize(1.25));
        Assert.Equal(date, SnapshotBuilder.Normalize(date));
        Assert.Equal(2.5, SnapshotBuilder.Normalize(2.5f));
        Assert.Equal(3.5, SnapshotBuilder.Normalize(3.5m));
        Assert.Equal("[1, 2]", SnapshotBuilder.Normalize(new List<int> { 1, 2 }));
    }

    [Fact]
    public void SnapshotAdd_RequiresOneValuePerHeader()
    {
        var snapshot = new SnapshotBuilder();

        Assert.Throws<ArgumentException>(() => snapshot.Add(GuidA, () => "x", new[] { "A", "B" }, new object?[] { 1 }));
    }

    [Fact]
    public void Snapshot_FeedsSnapshotDiffDirectly()
    {
        var before = new SnapshotBuilder();
        before.Add(GuidA, () => "x", new[] { "Element.Category", "Element.Length" }, new object?[] { "Walls", 3000.0 });
        before.Add(GuidB, () => "x", new[] { "Element.Category", "Element.Length" }, new object?[] { "Doors", 900.0 });
        before.Add(Guid.Empty, () => "Site.nwc > Pipe", new[] { "Element.Category", "Element.Length" }, new object?[] { "Pipes", 100.0 });

        var after = new SnapshotBuilder();
        after.Add(GuidA, () => "x", new[] { "Element.Category", "Element.Length" }, new object?[] { "Walls", 3200.0 });
        after.Add(Guid.Empty, () => "Site.nwc > Pipe", new[] { "Element.Category", "Element.Length" }, new object?[] { "Pipes", 100.0 });
        after.Add(new Guid("e67aebc3-0bcc-49d1-b719-e4b4dff208b3"), () => "x", new[] { "Element.Category", "Element.Length" }, new object?[] { "Floors", 50.0 });

        var diff = SnapshotNodes.Diff(before.Snapshot, after.Snapshot);

        Assert.Equal(new[] { "e67aebc3-0bcc-49d1-b719-e4b4dff208b3" }, Assert.IsType<List<string>>(diff["addedKeys"]));
        Assert.Equal(new[] { "22e66ddf-794d-40bb-8aa5-3dda450d8255" }, Assert.IsType<List<string>>(diff["removedKeys"]));
        Assert.Equal(new[] { "3f81e10a-25b0-49ff-9520-63f2a763150a" }, Assert.IsType<List<string>>(diff["changedKeys"]));
    }

    [Fact]
    public void Snapshot_ReportsNothingWhenNothingChanged()
    {
        SnapshotBuilder Build()
        {
            var builder = new SnapshotBuilder();
            builder.Add(GuidA, () => "x", new[] { "P", "Q" }, new object?[] { "a", 2.0 });
            builder.Add(Guid.Empty, () => "path", new[] { "P", "Q" }, new object?[] { "b", null });
            return builder;
        }

        var diff = SnapshotNodes.Diff(Build().Snapshot, Build().Snapshot);

        Assert.Empty(Assert.IsType<List<string>>(diff["addedKeys"]));
        Assert.Empty(Assert.IsType<List<string>>(diff["removedKeys"]));
        Assert.Empty(Assert.IsType<List<string>>(diff["changedKeys"]));
    }

    // ===================================================================== Search.ByGuid

    [Fact]
    public void ParseRequests_ReadsTextGuidsAndIfcIds()
    {
        var requests = GuidLookup.ParseRequests(
            L("3f81e10a-25b0-49ff-9520-63f2a763150a", GuidB, " 3F81E10A25B049FF952063F2A763150A ", "3cUkl32yn9qRSPvBJVyWYp"),
            "Search.ByGuid");

        Assert.Equal(4, requests.Count);
        Assert.Equal(GuidA, requests[0].Guid);
        Assert.Equal("3f81e10a-25b0-49ff-9520-63f2a763150a", requests[0].Text);
        Assert.Equal(GuidB, requests[1].Guid);
        Assert.Equal("22e66ddf-794d-40bb-8aa5-3dda450d8255", requests[1].Text);
        Assert.Equal(GuidA, requests[2].Guid);
        Assert.Equal("3F81E10A25B049FF952063F2A763150A", requests[2].Text);
        Assert.Equal(new Guid("e67aebc3-0bcc-49d1-b719-e4b4dff208b3"), requests[3].Guid);
        Assert.Equal("3cUkl32yn9qRSPvBJVyWYp", requests[3].Text);
    }

    [Fact]
    public void ParseRequests_RequiresTheList_AndAcceptsAnEmptyOne()
    {
        var error = Assert.Throws<ArgumentNullException>(() => GuidLookup.ParseRequests(null!, "Search.ByGuid"));
        Assert.Contains("Search.ByGuid requires the GUIDs", error.Message);
        Assert.Empty(GuidLookup.ParseRequests(L(), "Search.ByGuid"));
    }

    [Fact]
    public void ParseRequests_NamesThePositionOfABadGuid()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            GuidLookup.ParseRequests(L("3f81e10a-25b0-49ff-9520-63f2a763150a", GuidB, "3f81e10a-25b0"), "Search.ByGuid"));

        Assert.Contains("Search.ByGuid", error.Message);
        Assert.Contains("index 2", error.Message);
        Assert.Contains("is not a GUID", error.Message);
        Assert.Contains("hexadecimal digit(s)", error.Message);
    }

    [Fact]
    public void ParseRequests_NamesThePositionOfAnEmptyOrWrongTypedEntry()
    {
        var empty = Assert.Throws<ArgumentException>(() => GuidLookup.ParseRequests(L("3f81e10a-25b0-49ff-9520-63f2a763150a", null), "Search.ByGuid"));
        Assert.Contains("index 1", empty.Message);
        Assert.Contains("is empty", empty.Message);

        var blank = Assert.Throws<ArgumentException>(() => GuidLookup.ParseRequests(L(" "), "Search.ByGuid"));
        Assert.Contains("index 0", blank.Message);
        Assert.Contains("is not a GUID", blank.Message);

        var number = Assert.Throws<ArgumentException>(() => GuidLookup.ParseRequests(L(12.5), "Search.ByGuid"));
        Assert.Contains("index 0", number.Message);
        Assert.Contains("not text or a GUID", number.Message);
    }

    [Fact]
    public void Collect_ReturnsItemsInTheOrderOfTheGuids_AndListsTheMissingOnes()
    {
        var requests = GuidLookup.ParseRequests(L(GuidB, "00000000-0000-0000-0000-000000000001", GuidA), "Search.ByGuid");
        var found = new Dictionary<Guid, List<string>>
        {
            [GuidA] = new List<string> { "wall-1" },
            [GuidB] = new List<string> { "door-1" },
        };

        var items = GuidLookup.Collect(requests, found, out var missing);

        Assert.Equal(new[] { "door-1", "wall-1" }, items);
        Assert.Equal(new[] { "00000000-0000-0000-0000-000000000001" }, missing);
    }

    [Fact]
    public void Collect_AGuidAskedTwiceYieldsItsItemsOnce_AndAMissingOneIsListedOnce()
    {
        var requests = GuidLookup.ParseRequests(
            L(GuidA, GuidA.ToString("N"), "00000000-0000-0000-0000-000000000001", "00000000000000000000000000000001"),
            "Search.ByGuid");
        var found = new Dictionary<Guid, List<string>> { [GuidA] = new List<string> { "a1", "a2" } };

        var items = GuidLookup.Collect(requests, found, out var missing);

        Assert.Equal(new[] { "a1", "a2" }, items);
        Assert.Single(missing);
        Assert.Equal("00000000-0000-0000-0000-000000000001", missing[0]);
    }

    [Fact]
    public void Collect_KeepsEveryItemThatSharesAGuid()
    {
        var requests = GuidLookup.ParseRequests(L(GuidA), "Search.ByGuid");
        var found = new Dictionary<Guid, List<string>> { [GuidA] = new List<string> { "first", "second", "third" } };

        Assert.Equal(new[] { "first", "second", "third" }, GuidLookup.Collect(requests, found, out var missing));
        Assert.Empty(missing);
    }

    [Fact]
    public void Collect_NothingFoundListsAllAsMissing()
    {
        var requests = GuidLookup.ParseRequests(L(GuidA, GuidB), "Search.ByGuid");

        var items = GuidLookup.Collect(requests, new Dictionary<Guid, List<string>>(), out var missing);

        Assert.Empty(items);
        Assert.Equal(2, missing.Count);
    }

    // ===================================================================== ModelItem.IfcGuid

    [Theory]
    [InlineData("GlobalId", 1)]
    [InlineData("globalid", 1)]
    [InlineData("Global Id", 1)]
    [InlineData("IfcGlobalId", 1)]
    [InlineData("IfcGUID", 2)]
    [InlineData("IFC GUID", 2)]
    [InlineData("Ifc_Guid", 2)]
    [InlineData("Guid", 3)]
    [InlineData("GUID", 3)]
    [InlineData("Name", 0)]
    [InlineData("GUIDs", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void Rank_PrefersGlobalIdThenIfcGuidThenGuid(string? name, int expected)
    {
        Assert.Equal(expected, IfcGuidLookup.Rank(name));
    }

    [Fact]
    public void TryNormalize_KeepsAGlobalIdAndEncodesAGuid()
    {
        Assert.True(IfcGuidLookup.TryNormalize("0$WU4A9R19$vKWO$AdOnKA", out var id));
        Assert.Equal("0$WU4A9R19$vKWO$AdOnKA", id);

        Assert.True(IfcGuidLookup.TryNormalize("  0$WU4A9R19$vKWO$AdOnKA ", out id));
        Assert.Equal("0$WU4A9R19$vKWO$AdOnKA", id);

        Assert.True(IfcGuidLookup.TryNormalize("3f81e10a-25b0-49ff-9520-63f2a763150a", out id));
        Assert.Equal("0$WU4A9R19$vKWO$AdOnKA", id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not an id")]
    [InlineData("4$WU4A9R19$vKWO$AdOnKA")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void TryNormalize_RejectsEverythingElse(string? value)
    {
        Assert.False(IfcGuidLookup.TryNormalize(value, out var id));
        Assert.Null(id);
    }
}
