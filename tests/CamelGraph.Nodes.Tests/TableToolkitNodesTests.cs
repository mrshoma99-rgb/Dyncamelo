using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace CamelGraph.Nodes.Tests;

public class TableToolkitNodesTests
{
    private static IList<object?> L(params object?[] items) => new List<object?>(items);

    private static IList<object?> Rows(params object?[][] rows) => rows.Select(r => (object?)new List<object?>(r)).ToList();

    // Elements: level, category, length, status
    private static CamelGraphTable Elements() => TableToolkitNodes.FromRows(
        Rows(
            new object?[] { "L01", "Wall", 3000.0, "New" },
            new object?[] { "L02", "Door", 900.0, "Active" },
            new object?[] { "L01", "Wall", 4500.0, "Active" },
            new object?[] { "L02", "Wall", 2500.0, "New" },
            new object?[] { "L01", "Pipe", null, "New" }),
        L("Level", "Category", "Length", "Status"));

    private static object?[] Cells(CamelGraphTable table, int row) => table.Rows[row];

    // ------------------------------------------------------------- Building

    [Fact]
    public void FromRows_UsesTheGivenHeaders()
    {
        var table = Elements();
        Assert.Equal(new[] { "Level", "Category", "Length", "Status" }, table.Headers);
        Assert.Equal(5, table.RowCount);
        Assert.Equal(4, table.ColumnCount);
    }

    [Fact]
    public void FromRows_CanTakeTheFirstRowAsHeaders_OrInventNames()
    {
        var withHeader = TableToolkitNodes.FromRows(Rows(new object?[] { "A", "B" }, new object?[] { 1.0, 2.0 }), null, firstRowIsHeader: true);
        Assert.Equal(new[] { "A", "B" }, withHeader.Headers);
        Assert.Equal(1, withHeader.RowCount);

        var invented = TableToolkitNodes.FromRows(Rows(new object?[] { 1.0, 2.0, 3.0 }));
        Assert.Equal(new[] { "Column 1", "Column 2", "Column 3" }, invented.Headers);
    }

    [Fact]
    public void FromRows_MakesBlankAndRepeatedHeadersUnique()
    {
        var table = TableToolkitNodes.FromRows(Rows(new object?[] { 1.0, 2.0, 3.0, 4.0 }), L("Name", "Name", "", "name"));
        Assert.Equal(new[] { "Name", "Name (2)", "Column 3", "name (3)" }, table.Headers);
    }

    [Fact]
    public void FromRows_PadsShortRows_AndRejectsRowsWithRealExtraData()
    {
        var table = TableToolkitNodes.FromRows(Rows(new object?[] { 1.0 }), L("A", "B"));
        Assert.Null(table.Rows[0][1]);

        var trailingEmpty = TableToolkitNodes.FromRows(Rows(new object?[] { 1.0, 2.0, null, "" }), L("A", "B"));
        Assert.Equal(2, trailingEmpty.ColumnCount);

        var ex = Assert.Throws<ArgumentException>(() => TableToolkitNodes.FromRows(Rows(new object?[] { 1.0, 2.0, 3.0 }), L("A", "B")));
        Assert.Contains("Row 1", ex.Message);
    }

    [Fact]
    public void FromRows_TreatsScalarRowsAsSingleCellRows()
    {
        var table = TableToolkitNodes.FromRows(L("a", "b"), L("Name"));
        Assert.Equal(2, table.RowCount);
        Assert.Equal("b", table.Rows[1][0]);
    }

    [Fact]
    public void FromDictionaries_UsesTheUnionOfKeysInOrderOfAppearance()
    {
        var table = TableToolkitNodes.FromDictionaries(L(
            new Dictionary<string, object?> { ["Name"] = "W1", ["Length"] = 3.0 },
            new Dictionary<string, object?> { ["Name"] = "W2", ["Fire"] = "EI60" }));
        Assert.Equal(new[] { "Name", "Length", "Fire" }, table.Headers);
        Assert.Equal(new object?[] { "W2", null, "EI60" }, Cells(table, 1));
    }

    [Fact]
    public void FromDictionaries_NamesTheItemThatIsNotADictionary()
    {
        var ex = Assert.Throws<ArgumentException>(() => TableToolkitNodes.FromDictionaries(L(new Dictionary<string, object?>(), "oops")));
        Assert.Contains("item 1", ex.Message);
    }

    [Fact]
    public void FromColumns_PadsShortColumns()
    {
        var table = TableToolkitNodes.FromColumns(L(L("a", "b", "c"), L(1.0, 2.0)), L("Name", "Qty"));
        Assert.Equal(3, table.RowCount);
        Assert.Equal(new object?[] { "c", null }, Cells(table, 2));
        Assert.Throws<ArgumentException>(() => TableToolkitNodes.FromColumns(L("not a list")));
    }

    // ------------------------------------------------------------- Reading back

    [Fact]
    public void RowsHeadersInfoAndDictionaries_ReadTheTableBack()
    {
        var table = Elements();
        var rows = TableToolkitNodes.Rows(table);
        Assert.Equal(5, rows.Count);
        Assert.Equal(new object?[] { "L02", "Door", 900.0, "Active" }, (List<object?>)rows[1]!);

        Assert.Equal(new[] { "Level", "Category", "Length", "Status" }, TableToolkitNodes.Headers(table).ToArray());

        var info = TableToolkitNodes.Info(table);
        Assert.Equal(5, info["rowCount"]);
        Assert.Equal(4, info["columnCount"]);

        var dictionaries = TableToolkitNodes.ToDictionaries(table);
        Assert.Equal("Door", ((Dictionary<string, object?>)dictionaries[1]!)["Category"]);
    }

    [Fact]
    public void Column_ReturnsOneColumn_AndListsTheAvailableOnesWhenMissing()
    {
        var table = Elements();
        Assert.Equal(new object?[] { "L01", "L02", "L01", "L02", "L01" }, TableToolkitNodes.Column(table, "Level").ToArray());
        Assert.Equal(new object?[] { "L01", "L02", "L01", "L02", "L01" }, TableToolkitNodes.Column(table, "  level ").ToArray());
        var ex = Assert.Throws<ArgumentException>(() => TableToolkitNodes.Column(table, "Floor"));
        Assert.Contains("Floor", ex.Message);
        Assert.Contains("Level, Category, Length, Status", ex.Message);
    }

    [Fact]
    public void Row_CountsFromEitherEnd_AndChecksTheRange()
    {
        var table = Elements();
        Assert.Equal("Door", TableToolkitNodes.Row(table, 1)["Category"]);
        Assert.Equal("Pipe", TableToolkitNodes.Row(table, -1)["Category"]);
        Assert.Throws<ArgumentOutOfRangeException>(() => TableToolkitNodes.Row(table, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => TableToolkitNodes.Row(table, -6));
    }

    // ------------------------------------------------------------- Columns

    [Fact]
    public void SelectColumns_KeepsAndReorders_AcceptingACommaSeparatedText()
    {
        var table = Elements();
        var picked = TableToolkitNodes.SelectColumns(table, L("Status", "Level"));
        Assert.Equal(new[] { "Status", "Level" }, picked.Headers);
        Assert.Equal(new object?[] { "New", "L01" }, Cells(picked, 0));

        var commas = TableToolkitNodes.SelectColumns(table, L("Category, Length"));
        Assert.Equal(new[] { "Category", "Length" }, commas.Headers);
    }

    [Fact]
    public void RemoveColumns_DropsTheListedOnes()
    {
        var table = TableToolkitNodes.RemoveColumns(Elements(), L("Length", "Status"));
        Assert.Equal(new[] { "Level", "Category" }, table.Headers);
        Assert.Equal(5, table.RowCount);
    }

    [Fact]
    public void RenameColumn_ChangesOneName_AndKeepsNamesUnique()
    {
        var table = TableToolkitNodes.RenameColumn(Elements(), "Length", "Size");
        Assert.Equal(new[] { "Level", "Category", "Size", "Status" }, table.Headers);
        Assert.Equal(new[] { "Level", "Level (2)", "Length", "Status" }, TableToolkitNodes.RenameColumn(Elements(), "Category", "Level").Headers);
        Assert.Throws<ArgumentException>(() => TableToolkitNodes.RenameColumn(Elements(), "Length", " "));
    }

    [Fact]
    public void AddColumn_TakesAListPerRow_OrOneRepeatedValue()
    {
        var perRow = TableToolkitNodes.AddColumn(Elements(), "No", L(1.0, 2.0, 3.0, 4.0, 5.0));
        Assert.Equal(5.0, perRow.Rows[4][4]);

        var constant = TableToolkitNodes.AddColumn(Elements(), "Source", "Revit");
        Assert.All(constant.Rows, r => Assert.Equal("Revit", r[4]));

        var ex = Assert.Throws<ArgumentException>(() => TableToolkitNodes.AddColumn(Elements(), "No", L(1.0, 2.0)));
        Assert.Contains("5 row(s)", ex.Message);
    }

    [Fact]
    public void AddFormulaColumn_ComputesPerRowFromTheOtherColumns()
    {
        var table = TableToolkitNodes.AddFormulaColumn(Elements(), "Meters", "Length / 1000");
        Assert.Equal(3.0, table.Rows[0][4]);
        Assert.Equal(0.9, (double)table.Rows[1][4]!, 9);
        Assert.Equal(0.0, table.Rows[4][4]);   // a blank cell counts as 0

        var spaced = TableToolkitNodes.FromRows(Rows(new object?[] { 120.0, 2.0 }), L("Fire Rating", "Factor"));
        var calc = TableToolkitNodes.AddFormulaColumn(spaced, "Hours", "[Fire Rating] / 60 * factor");
        Assert.Equal(4.0, calc.Rows[0][2]);
    }

    [Fact]
    public void AddFormulaColumn_ExplainsMistakes()
    {
        var unknown = Assert.Throws<FormatException>(() => TableToolkitNodes.AddFormulaColumn(Elements(), "X", "Width * 2"));
        Assert.Contains("unknown name 'Width'", unknown.Message);
        Assert.Contains("Length", unknown.Message);
        Assert.Throws<ArgumentException>(() => TableToolkitNodes.AddFormulaColumn(Elements(), "X", " "));
    }

    // ------------------------------------------------------------- Rows

    [Fact]
    public void Filter_SplitsIntoMatchedAndRejected()
    {
        var result = TableToolkitNodes.Filter(Elements(), "Category", "==", "wall");
        var matched = (CamelGraphTable)result["matched"];
        var rejected = (CamelGraphTable)result["rejected"];
        Assert.Equal(3, matched.RowCount);
        Assert.Equal(2, rejected.RowCount);
        Assert.Equal(Elements().Headers, matched.Headers);
    }

    [Fact]
    public void Filter_NumbersTextAndNullTests()
    {
        var longer = (CamelGraphTable)TableToolkitNodes.Filter(Elements(), "Length", ">=", 3000.0)["matched"];
        Assert.Equal(2, longer.RowCount);

        var blanks = (CamelGraphTable)TableToolkitNodes.Filter(Elements(), "Length", "isNull")["matched"];
        Assert.Equal("Pipe", blanks.Rows[0][1]);

        var pattern = (CamelGraphTable)TableToolkitNodes.Filter(Elements(), "Category", "matches", "?all")["matched"];
        Assert.Equal(3, pattern.RowCount);
    }

    [Fact]
    public void Sort_OrdersByColumns_StablyWithBlanksLast()
    {
        var byLength = TableToolkitNodes.Sort(Elements(), "Length");
        Assert.Equal(new object?[] { 900.0, 2500.0, 3000.0, 4500.0, null }, byLength.Rows.Select(r => r[2]).ToArray());

        var descending = TableToolkitNodes.Sort(Elements(), "Length", descending: true);
        Assert.Equal(new object?[] { 4500.0, 3000.0, 2500.0, 900.0, null }, descending.Rows.Select(r => r[2]).ToArray());
    }

    [Fact]
    public void Sort_SupportsSeveralColumnsAndPerColumnDirection()
    {
        var table = TableToolkitNodes.Sort(Elements(), "Level, -Length");
        Assert.Equal(new object?[] { "L01", "L01", "L01", "L02", "L02" }, table.Rows.Select(r => r[0]).ToArray());
        Assert.Equal(new object?[] { 4500.0, 3000.0, null, 2500.0, 900.0 }, table.Rows.Select(r => r[2]).ToArray());

        var viaWord = TableToolkitNodes.Sort(Elements(), "Level asc, Length desc");
        Assert.Equal(table.Rows.Select(r => r[2]), viaWord.Rows.Select(r => r[2]));
    }

    [Fact]
    public void Sort_NeedsAColumn()
    {
        Assert.Throws<ArgumentException>(() => TableToolkitNodes.Sort(Elements(), " "));
        Assert.Throws<ArgumentException>(() => TableToolkitNodes.Sort(Elements(), "Nope"));
    }

    [Fact]
    public void Sort_MixedNumbersAndTextDoNotThrow()
    {
        var table = TableToolkitNodes.FromRows(Rows(new object?[] { "b" }, new object?[] { 2.0 }, new object?[] { "a" }, new object?[] { 10.0 }), L("V"));
        var sorted = TableToolkitNodes.Sort(table, "V");
        Assert.Equal(4, sorted.RowCount);
    }

    [Fact]
    public void Distinct_KeepsTheFirstOfEachKind()
    {
        Assert.Equal(3, TableToolkitNodes.Distinct(Elements(), "Category").RowCount);
        Assert.Equal(2, TableToolkitNodes.Distinct(Elements(), "Level").RowCount);
        Assert.Equal(5, TableToolkitNodes.Distinct(Elements()).RowCount);

        var doubled = TableToolkitNodes.Concat(L(Elements(), Elements()));
        Assert.Equal(10, doubled.RowCount);
        Assert.Equal(5, TableToolkitNodes.Distinct(doubled).RowCount);
    }

    [Fact]
    public void Concat_MatchesColumnsByName_AndFillsGaps()
    {
        var a = TableToolkitNodes.FromRows(Rows(new object?[] { 1.0, "x" }), L("Id", "Name"));
        var b = TableToolkitNodes.FromRows(Rows(new object?[] { "y", 2.0, "extra" }), L("name", "Id", "Note"));
        var table = TableToolkitNodes.Concat(L(a, b));
        Assert.Equal(new[] { "Id", "Name", "Note" }, table.Headers);
        Assert.Equal(new object?[] { 1.0, "x", null }, Cells(table, 0));
        Assert.Equal(new object?[] { 2.0, "y", "extra" }, Cells(table, 1));
        Assert.Throws<ArgumentException>(() => TableToolkitNodes.Concat(L(a, "no")));
    }

    [Fact]
    public void Slice_TakesABlockOfRows()
    {
        Assert.Equal(2, TableToolkitNodes.Slice(Elements(), 1, 2).RowCount);
        Assert.Equal("Door", TableToolkitNodes.Slice(Elements(), 1, 2).Rows[0][1]);
        Assert.Equal(3, TableToolkitNodes.Slice(Elements(), 2).RowCount);
        Assert.Equal(0, TableToolkitNodes.Slice(Elements(), 9).RowCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => TableToolkitNodes.Slice(Elements(), -1));
    }

    // ------------------------------------------------------------- Analysis

    [Fact]
    public void GroupBy_CountsAndSumsPerGroup_InOrderOfFirstAppearance()
    {
        var table = TableToolkitNodes.GroupBy(Elements(), "Category", L("count", "sum:Length", "average:Length as Mean"));
        Assert.Equal(new[] { "Category", "count", "sum(Length)", "Mean" }, table.Headers);
        Assert.Equal(new object?[] { "Wall", 3.0, 10000.0, 3333.3333333333335 }, Cells(table, 0));
        Assert.Equal(new object?[] { "Door", 1.0, 900.0, 900.0 }, Cells(table, 1));
        Assert.Equal("Pipe", table.Rows[2][0]);
        Assert.Equal(0.0, table.Rows[2][2]);     // sum of nothing
        Assert.Null(table.Rows[2][3]);           // average of nothing
    }

    [Fact]
    public void GroupBy_SeveralKeysAndAllTheFunctions()
    {
        var table = TableToolkitNodes.GroupBy(
            Elements(), "Level, Status", L("count", "min:Length", "max:Length", "median:Length", "first:Category", "last:Category", "distinct:Category"));
        Assert.Equal(4, table.RowCount);   // L01/New (the pipe shares it), L02/Active, L01/Active, L02/New
        var l01New = table.Rows[0];
        Assert.Equal("L01", l01New[0]);
        Assert.Equal("New", l01New[1]);
        Assert.Equal(2.0, l01New[2]);
        Assert.Equal(3000.0, l01New[3]);
        Assert.Equal(3000.0, l01New[4]);
        Assert.Equal(3000.0, l01New[5]);
        Assert.Equal("Wall", l01New[6]);
        Assert.Equal("Pipe", l01New[7]);
        Assert.Equal(2.0, l01New[8]);
    }

    [Fact]
    public void GroupBy_ListCollectsTheCells()
    {
        var table = TableToolkitNodes.GroupBy(Elements(), "Level", L("list:Category"));
        var cells = (List<object?>)table.Rows[0][1]!;
        Assert.Equal(new object?[] { "Wall", "Wall", "Pipe" }, cells.ToArray());
    }

    [Fact]
    public void GroupBy_WithoutKeys_GivesOneTotalRow_EvenForAnEmptyTable()
    {
        var totals = TableToolkitNodes.GroupBy(Elements(), "", L("count", "sum:Length"));
        Assert.Equal(1, totals.RowCount);
        Assert.Equal(new object?[] { 5.0, 10900.0 }, Cells(totals, 0));

        var empty = (CamelGraphTable)TableToolkitNodes.Filter(Elements(), "Level", "==", "L99")["matched"];
        var none = TableToolkitNodes.GroupBy(empty, "", L("count", "sum:Length"));
        Assert.Equal(new object?[] { 0.0, 0.0 }, Cells(none, 0));
    }

    [Fact]
    public void GroupBy_ExplainsMistakes()
    {
        Assert.Contains("needs a column", Assert.Throws<ArgumentException>(() => TableToolkitNodes.GroupBy(Elements(), "Level", L("sum"))).Message);
        Assert.Contains("not an aggregation", Assert.Throws<ArgumentException>(() => TableToolkitNodes.GroupBy(Elements(), "Level", L("total:Length"))).Message);
        Assert.Contains("no column named 'Width'", Assert.Throws<ArgumentException>(() => TableToolkitNodes.GroupBy(Elements(), "Level", L("sum:Width"))).Message);
        Assert.Throws<ArgumentException>(() => TableToolkitNodes.GroupBy(Elements(), "", L()));

        var text = Assert.Throws<ArgumentException>(() => TableToolkitNodes.GroupBy(Elements(), "Level", L("sum:Category")));
        Assert.Contains("'Wall'", text.Message);
        Assert.Contains("not a number", text.Message);
    }

    [Fact]
    public void GroupBy_MinAndMaxOfNumbersWrittenAsText_AreNumbers_NotTextOrdering()
    {
        var table = TableToolkitNodes.FromRows(Rows(new object?[] { "a", "9" }, new object?[] { "a", "100" }, new object?[] { "a", " 25 " }), L("K", "V"));
        var result = TableToolkitNodes.GroupBy(table, "K", L("min:V", "max:V"));
        Assert.Equal(9.0, result.Rows[0][1]);
        Assert.Equal(100.0, result.Rows[0][2]);

        // text that is not numeric still compares as text
        var words = TableToolkitNodes.FromRows(Rows(new object?[] { "a", "pear" }, new object?[] { "a", "apple" }), L("K", "V"));
        Assert.Equal("apple", TableToolkitNodes.GroupBy(words, "K", L("min:V")).Rows[0][1]);
    }

    [Fact]
    public void GroupBy_ReadsNumericTextAsNumbers()
    {
        var table = TableToolkitNodes.FromRows(Rows(new object?[] { "a", "10" }, new object?[] { "a", "5.5" }), L("K", "V"));
        Assert.Equal(15.5, TableToolkitNodes.GroupBy(table, "K", L("sum:V")).Rows[0][1]);
    }

    [Fact]
    public void Pivot_CrossTabulates()
    {
        var table = TableToolkitNodes.Pivot(Elements(), "Level", "Status", "Length", "sum");
        Assert.Equal(new[] { "Level", "New", "Active" }, table.Headers);
        Assert.Equal(new object?[] { "L01", 3000.0, 4500.0 }, Cells(table, 0));
        Assert.Equal(new object?[] { "L02", 2500.0, 900.0 }, Cells(table, 1));
    }

    [Fact]
    public void Pivot_CountNeedsNoValueColumn_AndMissingCombinationsAreZero()
    {
        var table = TableToolkitNodes.Pivot(Elements(), "Category", "Level", "", "count");
        Assert.Equal(new[] { "Category", "L01", "L02" }, table.Headers);
        Assert.Equal(new object?[] { "Wall", 2.0, 1.0 }, Cells(table, 0));
        Assert.Equal(new object?[] { "Pipe", 1.0, 0.0 }, Cells(table, 2));
    }

    [Fact]
    public void Pivot_MissingCombinationsAreEmptyForAverage()
    {
        var table = TableToolkitNodes.Pivot(Elements(), "Category", "Level", "Length", "average");
        Assert.Null(table.Rows[1][1]);   // Door has no L01 rows
    }

    [Fact]
    public void Pivot_ChecksItsArguments()
    {
        Assert.Throws<ArgumentException>(() => TableToolkitNodes.Pivot(Elements(), "Level", "Status", "", "sum"));
        Assert.Throws<ArgumentException>(() => TableToolkitNodes.Pivot(Elements(), "Level", "Status", "Length", "total"));
    }

    // ------------------------------------------------------------- Join

    private static CamelGraphTable Fire() => TableToolkitNodes.FromRows(
        Rows(new object?[] { "Wall", "EI60", 5.0 }, new object?[] { "Door", "EI30", 7.0 }, new object?[] { "Window", "E15", 9.0 }),
        L("Category", "Fire", "Cost"));

    [Fact]
    public void Join_Inner_KeepsOnlyMatches_AndDropsTheRepeatedKeyColumn()
    {
        var table = TableToolkitNodes.Join(Elements(), Fire(), "Category");
        Assert.Equal(new[] { "Level", "Category", "Length", "Status", "Fire", "Cost" }, table.Headers);
        Assert.Equal(4, table.RowCount);   // the pipe has no match
        Assert.Equal("EI60", table.Rows[0][4]);
    }

    [Fact]
    public void Join_Left_KeepsEveryLeftRow()
    {
        var table = TableToolkitNodes.Join(Elements(), Fire(), "Category", "", "left");
        Assert.Equal(5, table.RowCount);
        Assert.Null(table.Rows[4][4]);
    }

    [Fact]
    public void Join_Outer_AlsoKeepsUnmatchedRightRows()
    {
        var table = TableToolkitNodes.Join(Elements(), Fire(), "Category", "Category", "outer");
        Assert.Equal(6, table.RowCount);
        var window = table.Rows.Single(r => (string?)r[1] == "Window");
        Assert.Null(window[0]);
        Assert.Equal("E15", window[4]);
    }

    [Fact]
    public void Join_UsesDifferentKeyNames_AndRenamesCollidingColumns()
    {
        var other = TableToolkitNodes.FromRows(Rows(new object?[] { "Wall", "Status!" }), L("Kind", "Status"));
        var table = TableToolkitNodes.Join(Elements(), other, "Category", "Kind", "left");
        Assert.Equal(new[] { "Level", "Category", "Length", "Status", "Kind", "Status (right)" }, table.Headers);
    }

    [Fact]
    public void Join_MatchesNumbersAndTextOnTheirText_AndMakesAProductForRepeatedKeys()
    {
        var left = TableToolkitNodes.FromRows(Rows(new object?[] { 42.0, "L" }), L("Id", "A"));
        var right = TableToolkitNodes.FromRows(Rows(new object?[] { "42", "r1" }, new object?[] { "42", "r2" }), L("Id", "B"));
        var table = TableToolkitNodes.Join(left, right, "Id");
        Assert.Equal(2, table.RowCount);
        Assert.Equal(new object?[] { "r1", "r2" }, table.Rows.Select(r => r[2]).ToArray());
    }

    [Fact]
    public void Join_ChecksItsArguments()
    {
        Assert.Throws<ArgumentNullException>(() => TableToolkitNodes.Join(Elements(), null!, "Category"));
        Assert.Throws<ArgumentException>(() => TableToolkitNodes.Join(Elements(), Fire(), "Category", "", "cross"));
        Assert.Throws<ArgumentException>(() => TableToolkitNodes.Join(Elements(), Fire(), "Nope"));
    }

    // ------------------------------------------------------------- Output

    [Fact]
    public void ToText_Markdown_AlignsNumbersAndEscapesPipes()
    {
        var table = TableToolkitNodes.FromRows(Rows(new object?[] { "a|b", 1.5 }, new object?[] { "c", 20.0 }), L("Name", "Qty"));
        var text = TableToolkitNodes.ToText(table).Replace("\r\n", "\n");
        Assert.Equal("| Name | Qty |\n| --- | ---: |\n| a\\|b | 1.5 |\n| c | 20 |\n", text);
    }

    [Fact]
    public void ToText_CsvAndTsv_QuoteWhenNeeded()
    {
        var table = TableToolkitNodes.FromRows(Rows(new object?[] { "a,b", "say \"hi\"" }), L("X", "Y"));
        Assert.Equal("X,Y\n\"a,b\",\"say \"\"hi\"\"\"\n", TableToolkitNodes.ToText(table, "csv").Replace("\r\n", "\n"));
        Assert.Equal("X\tY\na,b\t\"say \"\"hi\"\"\"\n", TableToolkitNodes.ToText(table, "tsv").Replace("\r\n", "\n"));
    }

    [Fact]
    public void ToText_Html_EscapesAndMarksNumberColumns()
    {
        var table = TableToolkitNodes.FromRows(Rows(new object?[] { "<b>", 2.0 }), L("Name", "Qty"));
        var html = TableToolkitNodes.ToText(table, "html");
        Assert.Contains("<th>Name</th>", html);
        Assert.Contains("<td>&lt;b&gt;</td>", html);
        Assert.Contains("<td class=\"num\">2</td>", html);
    }

    [Fact]
    public void ToText_RejectsAnUnknownFormat()
    {
        Assert.Throws<ArgumentException>(() => TableToolkitNodes.ToText(Elements(), "pdf"));
    }

    [Fact]
    public void CsvRoundTrip_ThroughTheFileNodes()
    {
        var folder = Path.Combine(Path.GetTempPath(), "camelgraph-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(folder, "elements.csv");
            Assert.Equal(path, TableToolkitNodes.ToCsvFile(Elements(), path));
            var back = TableToolkitNodes.FromCsvFile(path);
            Assert.Equal(Elements().Headers, back.Headers);
            Assert.Equal(5, back.RowCount);
            Assert.Equal(3000.0, back.Rows[0][2]);
            Assert.Equal("Wall", back.Rows[0][1]);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Fact]
    public void ExcelRoundTrip_ThroughTheFileNodes()
    {
        var folder = Path.Combine(Path.GetTempPath(), "camelgraph-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(folder, "elements.xlsx");
            TableToolkitNodes.ToExcelFile(Elements(), path, "Elements");
            var back = TableToolkitNodes.FromExcelFile(path, "Elements");
            Assert.Equal(Elements().Headers, back.Headers);
            Assert.Equal(5, back.RowCount);
            Assert.Equal("Door", back.Rows[1][1]);
            Assert.Equal(900.0, back.Rows[1][2]);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Fact]
    public void EveryNodeNamesTheTableWhenNoneIsWired()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => TableToolkitNodes.Column(null!, "x"));
        Assert.Contains("Table.Column", ex.Message);
        Assert.Contains("Table.FromRows", ex.Message);
        Assert.Throws<ArgumentNullException>(() => TableToolkitNodes.Rows(null!));
        Assert.Throws<ArgumentNullException>(() => TableToolkitNodes.Sort(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => TableToolkitNodes.FromRows(null!));
    }

    [Fact]
    public void ATableDescribesItself()
    {
        Assert.Equal("Table 5 row(s) × 4 column(s): Level, Category, Length, Status", Elements().ToString());
    }
}

public class ReportNodesTests
{
    private static IList<object?> L(params object?[] items) => new List<object?>(items);

    private static CamelGraphTable Small() =>
        TableToolkitNodes.FromRows(new List<object?> { new List<object?> { "Wall", 3.0 } }, L("Category", "Count"));

    [Fact]
    public void Html_BuildsAPageWithTitleHeadingsParagraphsAndTables()
    {
        var html = ReportNodes.Html("Weekly <report>", L("# Clashes", "Open clashes are down.", Small(), "## Detail", Small()), "Model A — 2026-10-01");
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<title>Weekly &lt;report&gt;</title>", html);
        Assert.Contains("<h1>Weekly &lt;report&gt;</h1>", html);
        Assert.Contains("<p class=\"meta\">Model A — 2026-10-01</p>", html);
        Assert.Contains("<h2>Clashes</h2>", html);
        Assert.Contains("<h3>Detail</h3>", html);
        Assert.Contains("<p>Open clashes are down.</p>", html);
        Assert.Equal(2, html.Split(new[] { "<table>" }, StringSplitOptions.None).Length - 1);
        Assert.Contains("prefers-color-scheme:dark", html);
    }

    [Fact]
    public void Html_ShowsOtherValuesAsText_AndEscapesThem()
    {
        var html = ReportNodes.Html("T", L(new Dictionary<string, object?> { ["a"] = "<x>" }, L(1.0, 2.0)));
        Assert.Contains("<pre>a: &lt;x&gt;</pre>", html);
        Assert.Contains("[1, 2]", html.Replace("\r\n", "\n").Replace("1\n2", "[1, 2]").Replace("[1, 2]", "[1, 2]"));
    }

    [Fact]
    public void Html_AcceptsAListOfTablesAndTextAsOneSection()
    {
        var html = ReportNodes.Html("T", L(L(Small(), "after")));
        Assert.Contains("<table>", html);
        Assert.Contains("<p>after</p>", html);
    }

    [Fact]
    public void Html_NeedsSections()
    {
        Assert.Throws<ArgumentNullException>(() => ReportNodes.Html("T", null!));
    }

    [Fact]
    public void Markdown_BuildsTheSameReportAsText()
    {
        var text = ReportNodes.Markdown("Weekly", L("# Clashes", Small(), 42.0), "Model A").Replace("\r\n", "\n");
        Assert.StartsWith("# Weekly\n\n_Model A_\n", text);
        Assert.Contains("# Clashes", text);
        Assert.Contains("| Category | Count |", text);
        Assert.Contains("```\n42\n```", text);
    }
}
