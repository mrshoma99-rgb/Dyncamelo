using System;
using System.Collections.Generic;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

public class TableNodesTests
{
    private static IList<object?> RowList(params object?[][] rows)
    {
        var list = new List<object?>();
        foreach (var row in rows)
        {
            list.Add(new List<object?>(row));
        }

        return list;
    }

    private static IList<object?> Cells(params object?[] cells) => new List<object?>(cells);

    // ------------------------------------------------------------- Joining

    [Fact]
    public void JoinByKey_ReturnsRowsParallelToKeys_NullWhenUnmatched()
    {
        var rows = RowList(
            new object?[] { "g1", "Wall" },
            new object?[] { "g2", "Door" });
        var headers = Cells("GUID", "Name");
        var keys = Cells("g2", "missing", "g1");

        var result = TableNodes.JoinByKey(rows, headers, keys, "GUID");

        var matched = Assert.IsType<List<object?>>(result["matchedRows"]);
        Assert.Equal(3, matched.Count);
        Assert.Same(rows[1], matched[0]);
        Assert.Null(matched[1]);
        Assert.Same(rows[0], matched[2]);

        var unmatched = Assert.IsType<List<object?>>(result["unmatchedKeys"]);
        Assert.Equal(new object?[] { "missing" }, unmatched);
    }

    [Fact]
    public void JoinByKey_NumbersAndTextCompareInvariantly()
    {
        var rows = RowList(
            new object?[] { 42.0, "numeric cell" },
            new object?[] { "7", "text cell" });
        var headers = Cells("Mark", "Name");

        var result = TableNodes.JoinByKey(rows, headers, Cells("42", 7.0), "Mark");

        var matched = Assert.IsType<List<object?>>(result["matchedRows"]);
        Assert.Same(rows[0], matched[0]); // string key "42" matches numeric cell 42.0
        Assert.Same(rows[1], matched[1]); // numeric key 7.0 matches text cell "7"
        Assert.Empty(Assert.IsType<List<object?>>(result["unmatchedKeys"]));
    }

    [Fact]
    public void JoinByKey_DuplicateKeyValues_FirstRowWins()
    {
        var rows = RowList(
            new object?[] { "g1", "first" },
            new object?[] { "g1", "second" });

        var result = TableNodes.JoinByKey(rows, Cells("GUID", "Name"), Cells("g1"), "GUID");

        var matched = Assert.IsType<List<object?>>(result["matchedRows"]);
        Assert.Same(rows[0], matched[0]);
    }

    [Fact]
    public void JoinByKey_KeysAreCaseSensitive()
    {
        var rows = RowList(new object?[] { "abc" });

        var result = TableNodes.JoinByKey(rows, Cells("Key"), Cells("ABC"), "Key");

        var matched = Assert.IsType<List<object?>>(result["matchedRows"]);
        Assert.Null(matched[0]);
        Assert.Single(Assert.IsType<List<object?>>(result["unmatchedKeys"]));
    }

    // ------------------------------------------------------ Column resolution

    [Fact]
    public void JoinByKey_ColumnLookupFallsBackToCaseInsensitive()
    {
        var rows = RowList(new object?[] { "g1", "Wall" });

        var result = TableNodes.JoinByKey(rows, Cells("GUID", "Name"), Cells("g1"), "guid");

        var matched = Assert.IsType<List<object?>>(result["matchedRows"]);
        Assert.Same(rows[0], matched[0]);
    }

    [Fact]
    public void JoinByKey_ExactColumnNameBeatsCaseInsensitiveMatch()
    {
        var rows = RowList(new object?[] { "upper", "lower" });

        var result = TableNodes.JoinByKey(rows, Cells("KEY", "key"), Cells("lower"), "key");

        var matched = Assert.IsType<List<object?>>(result["matchedRows"]);
        Assert.Same(rows[0], matched[0]); // matched via the exact "key" column (index 1)
    }

    [Fact]
    public void JoinByKey_UnknownColumn_ThrowsListingAvailableColumns()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => TableNodes.JoinByKey(RowList(), Cells("GUID", "Name"), Cells("g1"), "Mark"));
        Assert.Contains("Mark", ex.Message);
        Assert.Contains("GUID, Name", ex.Message);
    }

    // ------------------------------------------------------------ Row shapes

    [Fact]
    public void JoinByKey_RowsShorterThanKeyColumn_AreSkipped()
    {
        var rows = RowList(
            new object?[] { "g1" }, // too short: no Name-column cell
            new object?[] { "g2", "match me" });

        var result = TableNodes.JoinByKey(rows, Cells("GUID", "Name"), Cells("match me"), "Name");

        var matched = Assert.IsType<List<object?>>(result["matchedRows"]);
        Assert.Same(rows[1], matched[0]);
    }

    [Fact]
    public void JoinByKey_ScalarRows_ActAsSingleCellRows()
    {
        var rows = new List<object?> { "g1", "g2" };

        var result = TableNodes.JoinByKey(rows, Cells("GUID"), Cells("g2"), "GUID");

        var matched = Assert.IsType<List<object?>>(result["matchedRows"]);
        Assert.Equal("g2", matched[0]);
    }

    // --------------------------------------------------------------- Errors

    [Fact]
    public void JoinByKey_NullArguments_ThrowWithNodeName()
    {
        Assert.Contains("Table.JoinByKey", Assert.Throws<ArgumentNullException>(
            () => TableNodes.JoinByKey(null!, Cells("A"), Cells("k"), "A")).Message);
        Assert.Contains("Table.JoinByKey", Assert.Throws<ArgumentNullException>(
            () => TableNodes.JoinByKey(RowList(), null!, Cells("k"), "A")).Message);
        Assert.Contains("Table.JoinByKey", Assert.Throws<ArgumentNullException>(
            () => TableNodes.JoinByKey(RowList(), Cells("A"), null!, "A")).Message);
    }

    [Fact]
    public void JoinByKey_EmptyKeyColumn_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => TableNodes.JoinByKey(RowList(), Cells("A"), Cells("k"), " "));
        Assert.Contains("key column", ex.Message);
    }
}
