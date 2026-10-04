using System.Collections.Generic;
using CamelGraph.Nodes.Coordination;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>The shape of Clash.SummaryTable: Test, Total and one count per status, and the table it now also returns (NVC-34).</summary>
public class ClashSummaryTests
{
    private static readonly string[] Statuses = { "New", "Active", "Reviewed", "Approved", "Resolved" };

    [Fact]
    public void TheHeadersAreTestTotalAndEveryStatus()
    {
        Assert.Equal(new[] { "Test", "Total", "New", "Active", "Reviewed", "Approved", "Resolved" }, ClashSummary.Headers(Statuses));
    }

    [Fact]
    public void ARowCountsEachStatusAndTheTotal()
    {
        var row = ClashSummary.Row("Pipes vs Walls", new List<string> { "New", "New", "Approved", "Resolved", "New" }, Statuses);

        Assert.Equal(new object?[] { "Pipes vs Walls", 5, 3, 0, 0, 1, 1 }, row);
    }

    [Fact]
    public void ATestWithoutResultsIsARowOfZeros()
    {
        var row = ClashSummary.Row("Empty", new List<string>(), Statuses);

        Assert.Equal(new object?[] { "Empty", 0, 0, 0, 0, 0, 0 }, row);
    }

    [Fact]
    public void TheRowsAndHeadersMakeATableTheTableNodesCanRead()
    {
        var headers = ClashSummary.Headers(Statuses);
        var rows = new List<List<object?>>
        {
            ClashSummary.Row("A", new List<string> { "New" }, Statuses),
            ClashSummary.Row("B", new List<string> { "Active", "Active" }, Statuses),
        };

        var table = new CamelGraphTable(headers, rows);

        Assert.Equal(2, table.RowCount);
        Assert.Equal(7, table.ColumnCount);
        Assert.Equal(2, table.Rows[1][1]);
        Assert.Equal(2, table.Rows[1][table.IndexOf("Active", "test")]);
    }

    [Fact]
    public void NoTestsGiveATableWithTheHeadersAndNoRows()
    {
        var table = new CamelGraphTable(ClashSummary.Headers(Statuses), new List<List<object?>>());

        Assert.Equal(0, table.RowCount);
        Assert.Equal(7, table.ColumnCount);
    }
}
