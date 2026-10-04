using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The Table nodes after the node-library audit: column names that hold a comma, column inputs that take a list, joins that never
/// match blanks, filters that leave blanks out of ordering tests, formulas that never turn a blank into 0, Table.SetColumn, and the
/// retirement of Table.Headers and Table.JoinByKey. Where a node reports through the warning channel the test runs it in a graph.
/// </summary>
public class TableAuditTests
{
    private static readonly NodeRegistry Registry = CreateRegistry();

    private static NodeRegistry CreateRegistry()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        return registry;
    }

    private static IList<object?> L(params object?[] items) => new List<object?>(items);

    private static CamelGraphTable Make(string[] headers, params object?[][] rows) =>
        TableToolkitNodes.FromRows(rows.Select(r => (object?)new List<object?>(r)).ToList(), headers.Cast<object?>().ToList());

    // Elements: level, category, length (one row has no length), area with a comma in its name
    private static CamelGraphTable Elements() => Make(
        new[] { "Level", "Category", "Length", "Area, gross (m2)" },
        new object?[] { "L02", "Wall", 3000.0, 12.5 },
        new object?[] { "L01", "Door", 900.0, 2.0 },
        new object?[] { "L01", "Wall", 4500.0, 20.0 },
        new object?[] { "L02", "Pipe", null, null });

    /// <summary>Runs one library node in a graph with the given pinned inputs, the way the editor does.</summary>
    private static ZeroTouchNodeModel Run(string nodeName, params (string Port, object? Value)[] inputs)
    {
        var definition = Registry.Definitions.First(d => d.Name == nodeName);
        var node = new ZeroTouchNodeModel(definition);
        var graph = new GraphModel();
        graph.AddNode(node);
        foreach (var (port, value) in inputs)
        {
            node.InPorts.First(p => p.Name == port).SetUserValue(value);
        }

        new GraphEngine().Run(graph);
        return node;
    }

    private static object? Output(ZeroTouchNodeModel node, string port) => node.OutPorts.First(p => p.Name == port).Value;

    private static string[] Warnings(ZeroTouchNodeModel node) =>
        node.Messages.Where(m => m.Severity == MessageSeverity.Warning).Select(m => m.Text).ToArray();

    // ------------------------------------------------------------- COL-04: a comma inside a column name

    [Fact]
    public void ColumnsWhoseNameHasACommaAreFoundByTheirWholeName()
    {
        var table = Elements();

        var selected = TableToolkitNodes.SelectColumns(table, L("Area, gross (m2)", "Level"));
        Assert.Equal(new[] { "Area, gross (m2)", "Level" }, selected.Headers);
        Assert.Equal(new object?[] { 12.5, "L02" }, selected.Rows[0]);

        var removed = TableToolkitNodes.RemoveColumns(table, L("Area, gross (m2)"));
        Assert.Equal(new[] { "Level", "Category", "Length" }, removed.Headers);

        // typed as one text, the same column is still found whole; a real list of two names still splits
        var typed = TableToolkitNodes.SelectColumns(table, L("Area, gross (m2), Level"));
        Assert.Equal(new[] { "Area, gross (m2)", "Level" }, typed.Headers);
    }

    [Fact]
    public void ACommaTextStillSplitsWhenNoColumnHasThatWholeName()
    {
        var picked = TableToolkitNodes.SelectColumns(Elements(), L("Category, Length"));
        Assert.Equal(new[] { "Category", "Length" }, picked.Headers);
    }

    [Fact]
    public void SortDistinctAndGroupByAddressACommaColumnToo()
    {
        var table = Elements();

        var sorted = TableToolkitNodes.Sort(table, L("Area, gross (m2)"));
        Assert.Equal(new object?[] { 2.0, 12.5, 20.0, null }, sorted.Rows.Select(r => r[3]).ToArray());

        // the direction words work on the whole name as well
        var descending = TableToolkitNodes.Sort(table, L("Area, gross (m2) desc"));
        Assert.Equal(new object?[] { 20.0, 12.5, 2.0, null }, descending.Rows.Select(r => r[3]).ToArray());
        var minus = TableToolkitNodes.Sort(table, L("-Area, gross (m2), Level"));
        Assert.Equal(new object?[] { 20.0, 12.5, 2.0, null }, minus.Rows.Select(r => r[3]).ToArray());

        Assert.Equal(4, TableToolkitNodes.Distinct(table, L("Area, gross (m2)")).RowCount);

        var grouped = TableToolkitNodes.GroupBy(table, L("Area, gross (m2)"), L("count"));
        Assert.Equal(new[] { "Area, gross (m2)", "count" }, grouped.Headers);
        Assert.Equal(4, grouped.RowCount);
    }

    // ------------------------------------------------------------- COL-17: a list of names is one sort, not one table per name

    [Fact]
    public void AListOfNamesSortsOnceByAllOfThem()
    {
        var table = Elements();

        var byList = TableToolkitNodes.Sort(table, L("Level", "-Length"));
        var byText = TableToolkitNodes.Sort(table, L("Level, -Length"));

        Assert.Equal(byText.Rows.Select(r => r[2]).ToArray(), byList.Rows.Select(r => r[2]).ToArray());
        Assert.Equal(new object?[] { 4500.0, 900.0, 3000.0, null }, byList.Rows.Select(r => r[2]).ToArray());
    }

    [Fact]
    public void ARunWithAListOnTheColumnsInputGivesOneSortedTableNotOnePerName()
    {
        var node = Run("Table.Sort", ("table", Elements()), ("columns", L("Level", "Length")));

        Assert.Equal(NodeState.Executed, node.State);
        var sorted = Assert.IsType<CamelGraphTable>(node.OutPorts[0].Value);
        Assert.Equal(new object?[] { 900.0, 4500.0, 3000.0, null }, sorted.Rows.Select(r => r[2]).ToArray());

        // the text a person types into the box on the node
        var typed = Run("Table.Sort", ("table", Elements()), ("columns", "Level, -Length"));
        Assert.Equal(new object?[] { 4500.0, 900.0, 3000.0, null }, ((CamelGraphTable)typed.OutPorts[0].Value!).Rows.Select(r => r[2]).ToArray());
    }

    [Fact]
    public void GroupByAndDistinctTakeAListOfNamesAndTypedText()
    {
        var grouped = Run("Table.GroupBy", ("table", Elements()), ("by", L("Level", "Category")), ("aggregations", L("count")));
        Assert.Equal(4, ((CamelGraphTable)grouped.OutPorts[0].Value!).RowCount);

        var typed = Run("Table.GroupBy", ("table", Elements()), ("by", "Level"), ("aggregations", L("count", "sum:Length")));
        var result = (CamelGraphTable)typed.OutPorts[0].Value!;
        Assert.Equal(2, result.RowCount);
        Assert.Equal(new object?[] { "L02", 2.0, 3000.0 }, result.Rows[0]);

        var distinct = Run("Table.Distinct", ("table", Elements()), ("columns", L("Level")));
        Assert.Equal(2, ((CamelGraphTable)distinct.OutPorts[0].Value!).RowCount);

        var whole = Run("Table.Distinct", ("table", Elements()));
        Assert.Equal(4, ((CamelGraphTable)whole.OutPorts[0].Value!).RowCount);
    }

    // ------------------------------------------------------------- ENG-12, ENG-15: empty and nested column lists

    [Fact]
    public void AListInsideTheColumnListIsAnErrorThatSaysSoNotAHalfReadName()
    {
        var ex = Assert.Throws<ArgumentException>(() => TableToolkitNodes.SelectColumns(Elements(), L(L(3.0, 1.0), L(2.0), L())));

        Assert.Contains("Table.SelectColumns", ex.Message);
        Assert.Contains("'columns'", ex.Message);
        Assert.Contains("is a list, not a column name", ex.Message);
        Assert.DoesNotContain("[3", ex.Message);
        Assert.DoesNotContain("Parameter", ex.Message);
    }

    [Fact]
    public void SelectingNoColumnsIsAnErrorAndRemovingNoneSaysSoAndChangesNothing()
    {
        var empty = Assert.Throws<ArgumentException>(() => TableToolkitNodes.SelectColumns(Elements(), L()));
        Assert.Contains("nothing left to keep", empty.Message);
        Assert.Throws<ArgumentException>(() => TableToolkitNodes.SelectColumns(Elements(), L((object?)null)));

        var removed = TableToolkitNodes.RemoveColumns(Elements(), L());
        Assert.Equal(Elements().Headers, removed.Headers);
        Assert.Equal(4, removed.RowCount);

        var node = Run("Table.RemoveColumns", ("table", Elements()), ("columns", L()));
        Assert.Equal(NodeState.Warning, node.State);
        Assert.Contains("no column was removed", Assert.Single(Warnings(node)));
    }

    [Fact]
    public void AMissingColumnNamesTheNodeTheTableAndTheColumnsThereAre()
    {
        var ex = Assert.Throws<ArgumentException>(() => TableToolkitNodes.Sort(Elements(), L("Floor")));
        Assert.Contains("Table.Sort", ex.Message);
        Assert.Contains("'Floor'", ex.Message);
        Assert.Contains("Level, Category, Length, Area, gross (m2)", ex.Message);
        Assert.DoesNotContain("Parameter", ex.Message);

        var direction = Assert.Throws<ArgumentException>(() => TableToolkitNodes.Sort(Elements(), L("Floor desc")));
        Assert.Contains("'Floor'", direction.Message);
    }

    // ------------------------------------------------------------- COL-05 / COL-24: Table.Join

    private static CamelGraphTable Joined(CamelGraphTable left, CamelGraphTable right, string leftKey, string rightKey = "", string kind = "inner") =>
        TableToolkitNodes.Join(left, right, L(leftKey), L(rightKey), kind);

    [Fact]
    public void BlankKeysNeverMatchEachOther_InEveryKindOfJoin()
    {
        var left = Make(new[] { "Key", "L" }, new object?[] { "a", 1.0 }, new object?[] { null, 2.0 }, new object?[] { "", 3.0 }, new object?[] { "  ", 4.0 });
        var right = Make(new[] { "Key", "R" }, new object?[] { "a", "ra" }, new object?[] { null, "r-null" }, new object?[] { "", "r-empty" });

        var inner = Joined(left, right, "Key");
        Assert.Equal(1, inner.RowCount);
        Assert.Equal("ra", inner.Rows[0][2]);

        var leftJoin = Joined(left, right, "Key", kind: "left");
        Assert.Equal(4, leftJoin.RowCount);
        Assert.All(leftJoin.Rows.Skip(1), row => Assert.Null(row[2]));

        // outer: the two unmatched right rows are added once each, not paired with the blank left rows
        var outer = Joined(left, right, "Key", kind: "outer");
        Assert.Equal(6, outer.RowCount);
        Assert.Equal(2, outer.Rows.Count(r => r[1] == null));
    }

    [Fact]
    public void ABlankKeyIsNotAManyToManyBlowUp()
    {
        var left = Make(new[] { "Key" }, Enumerable.Range(0, 200).Select(_ => new object?[] { null }).ToArray());
        var right = Make(new[] { "Key", "R" }, Enumerable.Range(0, 50).Select(_ => new object?[] { null, "x" }).ToArray());

        Assert.Equal(0, Joined(left, right, "Key").RowCount);
        Assert.Equal(200, Joined(left, right, "Key", kind: "left").RowCount);
    }

    [Fact]
    public void AGuidMatchesWhateverItsCaseAndOtherTextMustMatchExactly()
    {
        var guid = "5F8C1B9E-0000-4A7B-9C3D-0123456789AB";
        var left = Make(new[] { "GUID", "Name" }, new object?[] { guid.ToLowerInvariant(), "Wall 1" }, new object?[] { "{" + guid + "}", "Wall 2" });
        var right = Make(new[] { "GUID", "Fire" }, new object?[] { guid.ToUpperInvariant(), "EI60" });

        // OLD: a GUID in capitals on one side and small letters on the other matched nothing
        var matched = Joined(left, right, "GUID");
        Assert.Equal(2, matched.RowCount);
        Assert.All(matched.Rows, row => Assert.Equal("EI60", row[2]));

        // anything that is not a GUID is still compared exactly
        var marks = Joined(Make(new[] { "Mark" }, new object?[] { "w-01" }), Make(new[] { "Mark", "V" }, new object?[] { "W-01", 1.0 }), "Mark");
        Assert.Equal(0, marks.RowCount);
    }

    [Fact]
    public void SeveralKeyColumnsJoinOnAllOfThem()
    {
        var left = Make(new[] { "Level", "Mark", "Qty" },
            new object?[] { "L01", "W1", 1.0 }, new object?[] { "L02", "W1", 2.0 }, new object?[] { "L01", "W2", 3.0 });
        var right = Make(new[] { "Level", "Mark", "Fire" },
            new object?[] { "L01", "W1", "EI30" }, new object?[] { "L02", "W1", "EI60" });

        var table = TableToolkitNodes.Join(left, right, L("Level", "Mark"), null, "left");

        Assert.Equal(new[] { "Level", "Mark", "Qty", "Fire" }, table.Headers);
        Assert.Equal(new object?[] { "EI30", "EI60", null }, table.Rows.Select(r => r[3]).ToArray());

        // the same two names as one comma text, and a different pair of names on the right
        var typed = TableToolkitNodes.Join(left, right, L("Level, Mark"), L(""), "inner");
        Assert.Equal(2, typed.RowCount);
        var renamed = Make(new[] { "Lvl", "Id", "Fire" }, new object?[] { "L01", "W2", "EI90" });
        var other = TableToolkitNodes.Join(left, renamed, L("Level", "Mark"), L("Lvl", "Id"));
        Assert.Equal(new[] { "Level", "Mark", "Qty", "Lvl", "Id", "Fire" }, other.Headers);
        Assert.Equal(1, other.RowCount);
        Assert.Equal("EI90", other.Rows[0][5]);
    }

    [Fact]
    public void ABlankInOneOfSeveralKeyColumnsMakesTheRowUnmatched()
    {
        var left = Make(new[] { "A", "B" }, new object?[] { "x", null });
        var right = Make(new[] { "A", "B", "R" }, new object?[] { "x", null, "r" });

        Assert.Equal(0, TableToolkitNodes.Join(left, right, L("A", "B")).RowCount);
    }

    [Fact]
    public void LeftAndRightKeyMustNameTheSameNumberOfColumns()
    {
        var ex = Assert.Throws<ArgumentException>(() => TableToolkitNodes.Join(Elements(), Elements(), L("Level", "Category"), L("Level")));
        Assert.Contains("'leftKey' names 2", ex.Message);
        Assert.Contains("'rightKey' names 1", ex.Message);

        var missing = Assert.Throws<ArgumentException>(() => TableToolkitNodes.Join(Elements(), Elements(), L("Level"), L("Floor")));
        Assert.Contains("the right table has no column named 'Floor'", missing.Message);
        var left = Assert.Throws<ArgumentException>(() => TableToolkitNodes.Join(Elements(), Elements(), L("Floor")));
        Assert.Contains("the left table has no column named 'Floor'", left.Message);
        Assert.Throws<ArgumentException>(() => TableToolkitNodes.Join(Elements(), Elements(), L()));
    }

    [Fact]
    public void UnmatchedListsTheLeftRowsThatFoundNoPartner()
    {
        var left = Make(new[] { "Id", "Name" }, new object?[] { "a", "A" }, new object?[] { "b", "B" }, new object?[] { null, "C" });
        var right = Make(new[] { "Id", "V" }, new object?[] { "a", 1.0 });

        var unmatched = TableToolkitNodes.Unmatched(left, right, L("Id"));

        Assert.Equal(new[] { "Id", "Name" }, unmatched.Headers);
        Assert.Equal(new object?[] { "B", "C" }, unmatched.Rows.Select(r => r[1]).ToArray());
        Assert.Equal(1, TableToolkitNodes.Join(left, right, L("Id")).RowCount);

        var node = Run("Table.Unmatched", ("left", left), ("right", right), ("leftKey", "Id"));
        Assert.Equal(2, ((CamelGraphTable)node.OutPorts[0].Value!).RowCount);
    }

    [Fact]
    public void UnmatchedUsesTheSameKeyRulesAsJoinAndNamesItselfInErrors()
    {
        var guid = "5F8C1B9E-0000-4A7B-9C3D-0123456789AB";
        var left = Make(new[] { "GUID" }, new object?[] { guid.ToLowerInvariant() }, new object?[] { "other" });
        var right = Make(new[] { "Id" }, new object?[] { guid });

        var unmatched = TableToolkitNodes.Unmatched(left, right, L("GUID"), L("Id"));
        Assert.Equal(new object?[] { "other" }, unmatched.Rows.Select(r => r[0]).ToArray());

        var ex = Assert.Throws<ArgumentException>(() => TableToolkitNodes.Unmatched(left, right, L("Floor")));
        Assert.Contains("Table.Unmatched", ex.Message);
        Assert.Contains("the left table has no column named 'Floor'", ex.Message);
    }

    [Fact]
    public void JoinByKeyIsRetiredAndPointsAtTableJoin()
    {
        var definition = Registry.Definitions.Single(d => d.Name == "Table.JoinByKey");
        Assert.True(definition.IsDeprecated);
        Assert.Equal("Table.Join", definition.Replacement);

        var join = Registry.Definitions.Single(d => d.Name == "Table.Join");
        Assert.False(join.IsDeprecated);
        Assert.Equal(new[] { "table" }, join.Outputs.Select(o => o.Name));
        Assert.Contains("joinbykey", join.SearchTags);
        Assert.Contains("joinbykey", Registry.Definitions.Single(d => d.Name == "Table.Unmatched").SearchTags);
    }

    [Fact]
    public void JoinByKeyNoLongerMatchesBlankKeys()
    {
        var rows = new List<object?>
        {
            new List<object?> { null, "blank row" },
            new List<object?> { "k", "key row" },
        };

        var result = TableNodes.JoinByKey(rows, L("Key", "Name"), L(null, "", "k"), "Key");

        var matched = (List<object?>)result["matchedRows"];
        Assert.Null(matched[0]);
        Assert.Null(matched[1]);
        Assert.Same(rows[1], matched[2]);
        Assert.Equal(2, ((List<object?>)result["unmatchedKeys"]).Count);
    }

    // ------------------------------------------------------------- COL-03: blanks and text in ordering tests

    private static int[] LengthsThatPass(string test, object? value)
    {
        var matched = (CamelGraphTable)TableToolkitNodes.Filter(Elements(), "Length", test, value)["matched"];
        return matched.Rows.Select(r => (int)(double)r[2]!).ToArray();
    }

    [Fact]
    public void ABlankCellPassesNoOrderingTest()
    {
        // OLD: the cell with no Length passed < and <=
        Assert.Equal(new[] { 900 }, LengthsThatPass("<", 3000.0));
        Assert.Equal(new[] { 3000, 900 }, LengthsThatPass("<=", 3000.0));
        Assert.Equal(new[] { 4500 }, LengthsThatPass(">", 3000.0));
        Assert.Equal(new[] { 3000, 4500 }, LengthsThatPass(">=", 3000.0));

        var blanks = (CamelGraphTable)TableToolkitNodes.Filter(Elements(), "Length", "<", 3000.0)["rejected"];
        Assert.Contains(blanks.Rows, r => (string?)r[1] == "Pipe");
    }

    [Fact]
    public void AnEmptyTextCellIsLeftOutOfOrderingTestsLikeAMissingOne()
    {
        var table = Make(new[] { "V" }, new object?[] { "" }, new object?[] { "  " }, new object?[] { 5.0 }, new object?[] { "7" });

        var less = (CamelGraphTable)TableToolkitNodes.Filter(table, "V", "<=", 10.0)["matched"];

        Assert.Equal(new object?[] { 5.0, "7" }, less.Rows.Select(r => r[0]).ToArray());
    }

    [Fact]
    public void TextThatIsNotANumberDoesNotAbortAnOrderingTestAndIsCounted()
    {
        var table = Make(new[] { "Length" }, new object?[] { 100.0 }, new object?[] { "TBC" }, new object?[] { 4000.0 }, new object?[] { "n/a" });

        var result = TableToolkitNodes.Filter(table, "Length", ">", 3000.0);
        Assert.Equal(1, ((CamelGraphTable)result["matched"]).RowCount);
        Assert.Equal(3, ((CamelGraphTable)result["rejected"]).RowCount);

        var node = Run("Table.Filter", ("table", table), ("column", "Length"), ("test", ">"), ("value", 3000.0));
        Assert.Equal(NodeState.Warning, node.State);
        var warning = Assert.Single(Warnings(node));
        Assert.Contains("2 row(s)", warning);
        Assert.Contains("'Length'", warning);
        Assert.Contains("'TBC' in row 2", warning);
    }

    [Fact]
    public void AnOrderingTestWithNoValueSaysSoAndPassesNothing()
    {
        var node = Run("Table.Filter", ("table", Elements()), ("column", "Length"), ("test", ">"));

        Assert.Equal(NodeState.Warning, node.State);
        Assert.Contains("needs a value", Assert.Single(Warnings(node)));
        Assert.Equal(0, ((CamelGraphTable)Output(node, "matched")!).RowCount);
        Assert.Equal(4, ((CamelGraphTable)Output(node, "rejected")!).RowCount);
    }

    [Fact]
    public void TheOtherTestsStillSelectBlanks()
    {
        Assert.Equal(1, ((CamelGraphTable)TableToolkitNodes.Filter(Elements(), "Length", "isNull")["matched"]).RowCount);
        Assert.Equal(3, ((CamelGraphTable)TableToolkitNodes.Filter(Elements(), "Length", "notNull")["matched"]).RowCount);
        Assert.Equal(1, ((CamelGraphTable)TableToolkitNodes.Filter(Elements(), "Length", "isEmpty")["matched"]).RowCount);
    }

    // ------------------------------------------------------------- COL-35: in / notIn

    [Fact]
    public void InAndNotInLookTheCellUpInASetOfValues()
    {
        var table = Elements();

        var inList = (CamelGraphTable)TableToolkitNodes.Filter(table, "Level", "in", L("L01", "L09"))["matched"];
        Assert.Equal(new object?[] { "Door", "Wall" }, inList.Rows.Select(r => r[1]).ToArray());

        var notIn = (CamelGraphTable)TableToolkitNodes.Filter(table, "Level", "notIn", L("L01"))["matched"];
        Assert.Equal(new object?[] { "Wall", "Pipe" }, notIn.Rows.Select(r => r[1]).ToArray());

        // one text with commas is a set too; case is ignored by default; numbers and numeric text agree
        var typed = (CamelGraphTable)TableToolkitNodes.Filter(table, "Category", "in", "wall, DOOR")["matched"];
        Assert.Equal(3, typed.RowCount);
        var exact = (CamelGraphTable)TableToolkitNodes.Filter(table, "Category", "in", "wall, DOOR", false)["matched"];
        Assert.Equal(0, exact.RowCount);
        var numbers = (CamelGraphTable)TableToolkitNodes.Filter(table, "Length", "in", L("900", 3000.0))["matched"];
        Assert.Equal(2, numbers.RowCount);
    }

    [Fact]
    public void AnEmptyCellIsInNoListAndAnEmptyListMatchesNothing()
    {
        var table = Elements();

        Assert.Equal(0, ((CamelGraphTable)TableToolkitNodes.Filter(table, "Length", "in", L())["matched"]).RowCount);
        Assert.Equal(4, ((CamelGraphTable)TableToolkitNodes.Filter(table, "Length", "notIn", L())["matched"]).RowCount);
        var notIn = (CamelGraphTable)TableToolkitNodes.Filter(table, "Length", "notIn", L(900.0))["matched"];
        Assert.Contains(notIn.Rows, r => r[2] == null);
    }

    [Fact]
    public void TheTestListOfTheFilterOffersInAndNotIn()
    {
        var choices = Registry.Definitions.Single(d => d.Name == "Table.Filter").Inputs.Single(i => i.Name == "test").Choices!;
        Assert.Contains("in", choices);
        Assert.Contains("notIn", choices);
    }

    // ------------------------------------------------------------- ENG-15: regex timeout

    [Fact]
    public void ARegexThatRunsAwayIsStoppedWithASentenceAboutTheRowNotTheRawFrameworkText()
    {
        var table = Make(new[] { "Name" }, new object?[] { new string('a', 40) + "!" });

        var ex = Assert.Throws<InvalidOperationException>(() => TableToolkitNodes.Filter(table, "Name", "regex", "^(a+)+$"));

        Assert.Contains("Table.Filter", ex.Message);
        Assert.Contains("2 seconds", ex.Message);
        Assert.Contains("row 1", ex.Message);
        Assert.DoesNotContain("Regex engine", ex.Message);
    }

    // ------------------------------------------------------------- COL-06: formulas never turn a blank into 0

    [Fact]
    public void ABlankOrTextCellInAUsedColumnGivesAnEmptyCellNotZero()
    {
        // OLD: the blank Length counted as 0 and the "TBC" text as 0
        var table = Make(new[] { "Width", "Height", "Note" },
            new object?[] { 2.0, 3.0, "x" }, new object?[] { 2.0, null, "x" }, new object?[] { 2.0, "TBC", "x" }, new object?[] { 2.0, "4", null });

        var result = TableToolkitNodes.AddFormulaColumn(table, "Area", "Width * Height");

        Assert.Equal(new object?[] { 6.0, null, null, 8.0 }, result.Rows.Select(r => r[3]).ToArray());
    }

    [Fact]
    public void ABlankInAColumnTheFormulaDoesNotUseIsIrrelevant()
    {
        var table = Make(new[] { "Width", "Height", "Note" }, new object?[] { 2.0, 3.0, null });

        Assert.Equal(6.0, TableToolkitNodes.AddFormulaColumn(table, "Area", "Width * Height").Rows[0][3]);
        // names in brackets, functions and numbers with exponents are read like the formula language reads them: the blank
        // column called e3 is not used by "2e3", and max( is a function, not a column
        var spaced = Make(new[] { "Fire Rating", "e3", "max" }, new object?[] { 120.0, null, null });
        Assert.Equal(3.0, TableToolkitNodes.AddFormulaColumn(spaced, "Hours", "[Fire Rating] / 60 + 2e3 * 0 + max(1, 0)").Rows[0][3]);
    }

    [Fact]
    public void ANonFiniteResultIsLeftEmptyAndCounted()
    {
        var table = Make(new[] { "Volume", "Count" }, new object?[] { 10.0, 2.0 }, new object?[] { 10.0, 0.0 });

        var result = TableToolkitNodes.AddFormulaColumn(table, "Each", "Volume / Count");

        Assert.Equal(new object?[] { 5.0, null }, result.Rows.Select(r => r[2]).ToArray());

        var node = Run("Table.AddFormulaColumn", ("table", table), ("name", "Each"), ("formula", "Volume / Count"));
        Assert.Equal(NodeState.Warning, node.State);
        var warning = Assert.Single(Warnings(node));
        Assert.Contains("1 result(s) were not a finite number", warning);
    }

    [Fact]
    public void TheFormulaWarningNamesTheColumnAndTheFirstRowThatWasLeftEmpty()
    {
        var table = Make(new[] { "Width", "Height" }, new object?[] { 2.0, 3.0 }, new object?[] { 2.0, null }, new object?[] { null, 1.0 });

        var node = Run("Table.AddFormulaColumn", ("table", table), ("name", "Area"), ("formula", "Width * Height"));

        Assert.Equal(NodeState.Warning, node.State);
        var warning = Assert.Single(Warnings(node));
        Assert.Contains("2 row(s) got an empty cell", warning);
        Assert.Contains("'Height' in row 2", warning);
        Assert.Contains("never counted as 0", warning);
    }

    [Fact]
    public void ACleanFormulaColumnHasNoWarning()
    {
        var node = Run("Table.AddFormulaColumn", ("table", Make(new[] { "A" }, new object?[] { 1.0 })), ("name", "B"), ("formula", "A * 2"));

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Empty(node.Messages);
    }

    // ------------------------------------------------------------- COL-34: Table.SetColumn

    [Fact]
    public void SetColumnReplacesInPlaceAndKeepsThePositionAndName()
    {
        var table = Elements();

        var result = TableToolkitNodes.SetColumn(table, "category", L("a", "b", "c", "d"));

        Assert.Equal(table.Headers, result.Headers);
        Assert.Equal(new object?[] { "a", "b", "c", "d" }, result.Rows.Select(r => r[1]).ToArray());
        Assert.Equal(table.Rows.Select(r => r[0]), result.Rows.Select(r => r[0]));
        Assert.Equal(table.Rows.Select(r => r[2]), result.Rows.Select(r => r[2]));
        // the input is left alone
        Assert.Equal("Wall", table.Rows[0][1]);
    }

    [Fact]
    public void SetColumnAddsTheColumnWhenThereIsNoneAndRepeatsASingleValue()
    {
        var added = TableToolkitNodes.SetColumn(Elements(), "Source", "Revit");

        Assert.Equal(new[] { "Level", "Category", "Length", "Area, gross (m2)", "Source" }, added.Headers);
        Assert.All(added.Rows, r => Assert.Equal("Revit", r[4]));

        var constant = TableToolkitNodes.SetColumn(Elements(), "Level", "L00");
        Assert.Equal(4, constant.ColumnCount);
        Assert.All(constant.Rows, r => Assert.Equal("L00", r[0]));
    }

    [Fact]
    public void SetColumnChecksTheNumberOfValues()
    {
        var ex = Assert.Throws<ArgumentException>(() => TableToolkitNodes.SetColumn(Elements(), "Level", L("a")));
        Assert.Contains("Table.SetColumn", ex.Message);
        Assert.Contains("4 row(s)", ex.Message);
        Assert.Throws<ArgumentException>(() => TableToolkitNodes.SetColumn(Elements(), " ", "x"));
        Assert.Throws<ArgumentNullException>(() => TableToolkitNodes.SetColumn(null!, "x", "y"));
    }

    [Fact]
    public void ACleanedColumnGoesBackIntoItsPlace()
    {
        var table = Elements();
        var upper = TableToolkitNodes.Column(table, "Category").Select(c => (object?)((string)c!).ToUpperInvariant()).ToList();

        var result = TableToolkitNodes.SetColumn(table, "Category", upper);

        Assert.Equal("WALL", result.Rows[0][1]);
        Assert.Equal(table.Headers, result.Headers);
    }

    // ------------------------------------------------------------- COL-25: Table.Headers retired into Table.Info

    [Fact]
    public void TableHeadersIsRetiredButStillRunsAndTableInfoCarriesItsSearchWords()
    {
        var headers = Registry.Definitions.Single(d => d.Name == "Table.Headers");
        Assert.True(headers.IsDeprecated);
        Assert.Equal("Table.Info", headers.Replacement);

        var node = Run("Table.Headers", ("table", Elements()));
        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(new[] { "Level", "Category", "Length", "Area, gross (m2)" }, ((IEnumerable<string>)node.OutPorts[0].Value!).ToArray());

        var info = Registry.Definitions.Single(d => d.Name == "Table.Info");
        Assert.False(info.IsDeprecated);
        foreach (var word in new[] { "headers", "column names", "names", "columns", "header", "fields" })
        {
            Assert.Contains(word, info.SearchTags);
        }
    }

    // ------------------------------------------------------------- COL-22: ranges on the integer inputs

    [Fact]
    public void TheIntegerInputsOfSliceAndRowCarryARange()
    {
        var slice = Registry.Definitions.Single(d => d.Name == "Table.Slice");
        var start = slice.Inputs.Single(i => i.Name == "start").Range;
        var count = slice.Inputs.Single(i => i.Name == "count").Range;
        Assert.NotNull(start);
        Assert.Equal(0, start!.Min);
        Assert.NotNull(count);
        Assert.Equal(-1, count!.Min);

        var row = Registry.Definitions.Single(d => d.Name == "Table.Row").Inputs.Single(i => i.Name == "index").Range;
        Assert.NotNull(row);
        Assert.True(row!.Min < 0);
    }

    // ------------------------------------------------------------- COL-20: the Pivot function list is complete

    [Fact]
    public void ThePivotOffersEveryFunctionItAccepts()
    {
        var choices = Registry.Definitions.Single(d => d.Name == "Table.Pivot").Inputs.Single(i => i.Name == "aggregation").Choices!;
        foreach (var function in new[] { "sum", "count", "average", "min", "max", "median", "first", "last", "list", "distinct" })
        {
            Assert.Contains(function, choices);
        }

        var pivot = TableToolkitNodes.Pivot(Elements(), "Level", "Category", "Length", "median");
        Assert.Equal(2, pivot.RowCount);
    }

    // ------------------------------------------------------------- Saved graphs: the old ids resolve to the new nodes

    [Theory]
    [InlineData("CamelGraph.Nodes.TableToolkitNodes.Sort@CamelGraph.Nodes.CamelGraphTable,string,bool", "Table.Sort")]
    [InlineData("CamelGraph.Nodes.TableToolkitNodes.Distinct@CamelGraph.Nodes.CamelGraphTable,string", "Table.Distinct")]
    [InlineData("CamelGraph.Nodes.TableToolkitNodes.GroupBy@CamelGraph.Nodes.CamelGraphTable,string,System.Collections.Generic.IList<object>", "Table.GroupBy")]
    [InlineData("CamelGraph.Nodes.TableToolkitNodes.Join@CamelGraph.Nodes.CamelGraphTable,CamelGraph.Nodes.CamelGraphTable,string,string,string", "Table.Join")]
    [InlineData("Dyncamelo.Nodes.TableToolkitNodes.Sort@Dyncamelo.Nodes.DyncameloTable,string,bool", "Table.Sort")]
    public void TheIdsSavedBeforeTheAuditStillFindTheirNode(string oldId, string name)
    {
        Assert.True(Registry.TryGetDefinition(oldId, out var definition));
        Assert.Equal(name, definition!.Name);
        Assert.NotNull(Registry.CreateZeroTouchNode(oldId));
    }
}
