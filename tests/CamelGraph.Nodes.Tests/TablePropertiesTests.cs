using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Nodes.Portable;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// Properties.SetCustomFromTable: the spreadsheet-to-model step as one node, where the old way needed two list-level badges and one
/// forgotten badge stamped the wrong values on every item.
/// </summary>
public class TablePropertyPlanTests
{
    private static readonly string G1 = "3f81e10a-25b0-49ff-9520-63f2a763150a";
    private static readonly string G2 = "4a81e10a-25b0-49ff-9520-63f2a763150b";

    private static CamelGraphTable Table(params object?[][] rows) =>
        new CamelGraphTable(new[] { "@Guid", "Cost", "Supplier" }, rows);

    private static IList<object?> L(params object?[] items) => new List<object?>(items);

    [Fact]
    public void ByDefaultEveryColumnExceptTheAtColumnsAndTheKeyIsWritten()
    {
        var plan = TablePropertyPlan.Create(Table(new object?[] { G1, 5.0, "ACME" }), null, null, "Properties.SetCustomFromTable");

        Assert.Equal(new[] { "Cost", "Supplier" }, plan.Columns);
        Assert.False(plan.IsKeyed);
        Assert.Equal(new[] { "Cost", "Supplier" }, plan.PairsOf(0).Select(p => p.Key));
        Assert.Equal(new object?[] { 5.0, "ACME" }, plan.PairsOf(0).Select(p => p.Value));
    }

    [Fact]
    public void TheKeyColumnIsNotWrittenAsAPropertyUnlessNamed()
    {
        var table = new CamelGraphTable(new[] { "GUID", "Cost" }, new[] { new object?[] { G1, 1.0 } });

        var keyed = TablePropertyPlan.Create(table, null, "GUID", "Properties.SetCustomFromTable");
        var named = TablePropertyPlan.Create(table, L("GUID", "Cost"), "GUID", "Properties.SetCustomFromTable");

        Assert.Equal(new[] { "Cost" }, keyed.Columns);
        Assert.Equal(new[] { "GUID", "Cost" }, named.Columns);
    }

    [Fact]
    public void NamedColumnsKeepTheOrderGivenAndRepeatsAreIgnored()
    {
        var plan = TablePropertyPlan.Create(Table(new object?[] { G1, 5.0, "ACME" }), L("Supplier", "Cost", "Supplier"), null, "Properties.SetCustomFromTable");

        Assert.Equal(new[] { "Supplier", "Cost" }, plan.Columns);
    }

    [Fact]
    public void AColumnThatIsNotThereListsTheOnesThatAre()
    {
        var error = Assert.Throws<ArgumentException>(
            () => TablePropertyPlan.Create(Table(new object?[] { G1, 1.0, "x" }), L("Price"), null, "Properties.SetCustomFromTable"));

        Assert.Contains("'Price'", error.Message);
        Assert.Contains("Cost", error.Message);
    }

    [Fact]
    public void ATableWithNothingLeftToWriteIsRefused()
    {
        var table = new CamelGraphTable(new[] { "@Guid", "@Name" }, new[] { new object?[] { G1, "Wall" } });

        var error = Assert.Throws<ArgumentException>(() => TablePropertyPlan.Create(table, null, null, "Properties.SetCustomFromTable"));

        Assert.Contains("no column is left", error.Message);
    }

    [Fact]
    public void WithoutAKeyTheRowsMustMatchTheItemsOneForOne()
    {
        var plan = TablePropertyPlan.Create(Table(new object?[] { G1, 1.0, "a" }, new object?[] { G2, 2.0, "b" }), null, null, "Properties.SetCustomFromTable");

        plan.RequireItemCount(2);
        var error = Assert.Throws<ArgumentException>(() => plan.RequireItemCount(3));

        Assert.Contains("2 row(s) but 3 item(s)", error.Message);
        Assert.Contains("keyColumn", error.Message);
    }

    [Fact]
    public void ARowFindsItsItemByTheGuidInTheKeyColumn()
    {
        var plan = TablePropertyPlan.Create(
            Table(new object?[] { G1, 1.0, "a" }, new object?[] { G2.ToUpperInvariant(), 2.0, "b" }), null, "@Guid", "Properties.SetCustomFromTable");

        Assert.True(plan.IsKeyed);
        Assert.Equal(new Guid(G1), plan.Keys![0].Guid);
        Assert.Equal(new Guid(G2), plan.Keys![1].Guid);
    }

    [Fact]
    public void AnIfcIdInTheKeyColumnIsDecoded()
    {
        var ifc = IfcGuidCodec.Encode(new Guid(G1));
        var plan = TablePropertyPlan.Create(Table(new object?[] { ifc, 1.0, "a" }), null, "@Guid", "Properties.SetCustomFromTable");

        Assert.Equal(new Guid(G1), plan.Keys![0].Guid);
        Assert.Equal(ifc, plan.Keys![0].Text);
    }

    [Fact]
    public void ABlankOrBadKeyNamesItsRow()
    {
        var blank = Assert.Throws<ArgumentException>(
            () => TablePropertyPlan.Create(Table(new object?[] { G1, 1.0, "a" }, new object?[] { null, 2.0, "b" }), null, "@Guid", "Properties.SetCustomFromTable"));
        var bad = Assert.Throws<ArgumentException>(
            () => TablePropertyPlan.Create(Table(new object?[] { "nonsense", 1.0, "a" }), null, "@Guid", "Properties.SetCustomFromTable"));

        Assert.Contains("index 1", blank.Message);
        Assert.Contains("index 0", bad.Message);
    }

    [Fact]
    public void AGuidInTwoRowsIsRefusedBecauseOneItemTakesOneRow()
    {
        var error = Assert.Throws<ArgumentException>(
            () => TablePropertyPlan.Create(Table(new object?[] { G1, 1.0, "a" }, new object?[] { G2, 2.0, "b" }, new object?[] { G1, 3.0, "c" }), null, "@Guid", "Properties.SetCustomFromTable"));

        Assert.Contains("row 1 and again in row 3", error.Message);
        Assert.Contains("Table.Distinct", error.Message);
    }

    [Fact]
    public void ACellThatCannotBeAPropertyIsRefusedWithThePropertyName()
    {
        var plan = TablePropertyPlan.Create(Table(new object?[] { G1, new List<object?> { 1, 2 }, "a" }), null, null, "Properties.SetCustomFromTable");

        var error = Assert.Throws<ArgumentException>(() => plan.PairsOf(0));

        Assert.Contains("'Cost'", error.Message);
        Assert.DoesNotContain("System.Collections", error.Message);
    }

    [Fact]
    public void AnEmptyCellIsAStorableEmptyValue()
    {
        var plan = TablePropertyPlan.Create(Table(new object?[] { G1, null, "" }), null, null, "Properties.SetCustomFromTable");

        var pairs = plan.PairsOf(0);

        Assert.Equal(2, pairs.Count);
        Assert.Null(pairs[0].Value);
    }

    [Fact]
    public void NoTableIsAnError()
    {
        Assert.Throws<ArgumentNullException>(() => TablePropertyPlan.Create(null!, null, null, "Properties.SetCustomFromTable"));
    }
}

/// <summary>Properties.ToTable with no property list: every property found, in the order first met.</summary>
public class WideTableBuilderTests
{
    [Fact]
    public void ColumnsAreTheNamesInTheOrderTheyWereFirstSeenAndMissingCellsAreEmpty()
    {
        var builder = new WideTableBuilder();
        builder.StartRow();
        builder.Set("Item.Name", "Wall 1");
        builder.Set("Element.Level", "L1");
        builder.StartRow();
        builder.Set("Element.Level", "L2");
        builder.Set("Element.Area", 12.5);

        var table = builder.Build();

        Assert.Equal(new[] { "Item.Name", "Element.Level", "Element.Area" }, table.Headers);
        Assert.Equal(2, table.RowCount);
        Assert.Equal(new object?[] { "Wall 1", "L1", null }, table.Rows[0]);
        Assert.Equal(new object?[] { null, "L2", 12.5 }, table.Rows[1]);
    }

    [Fact]
    public void ANameMetTwiceInOneRowKeepsTheFirstValue()
    {
        var builder = new WideTableBuilder();
        builder.StartRow();
        builder.Set("Element.Level", "L1");
        builder.Set("Element.Level", "L9");

        Assert.Equal("L1", builder.Build().Rows[0][0]);
    }

    [Fact]
    public void NoItemsGiveAnEmptyTable()
    {
        var table = new WideTableBuilder().Build();

        Assert.Equal(0, table.RowCount);
        Assert.Equal(0, table.ColumnCount);
    }

    [Fact]
    public void AValueNeedsARow()
    {
        Assert.Throws<InvalidOperationException>(() => new WideTableBuilder().Set("a", 1));
    }

    [Fact]
    public void TheTableStopsAtOneMillionCellsWithAnAdvice()
    {
        // 2,000 columns, then rows: the 501st row passes a million cells.
        var builder = new WideTableBuilder();
        builder.StartRow();
        for (var column = 0; column < 2000; column++)
        {
            builder.Set("Category.Property" + column, column);
        }

        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            for (var row = 0; row < 600; row++)
            {
                builder.StartRow();
            }
        });

        Assert.Contains("1,000,000 cells", error.Message);
        Assert.Contains("'properties'", error.Message);
    }
}

/// <summary>The wiring of SetCustomFromTable, Properties.ToTable and Property.Info.</summary>
public class TablePropertiesWiringTests
{
    [Fact]
    public void SetCustomFromTableChecksEveryValueBeforeItWritesToAnItemAndWritesInOnePass()
    {
        var body = NavisworksMethodText.Body("CustomPropertyNodes.cs", "SetCustomFromTable");
        var declaration = NavisworksMethodText.Declaration("CustomPropertyNodes.cs", "SetCustomFromTable");

        Assert.Contains("NodeEffects.ChangesModel", declaration);
        Assert.Contains("CamelGraphTable table,", declaration);
        Assert.Contains("string? keyColumn = null", declaration);
        Assert.True(body.IndexOf("plan.PairsOf(row)", StringComparison.Ordinal) < body.IndexOf("SetUserDefinedTab(", StringComparison.Ordinal));
        Assert.Contains("plan.RequireItemCount(items.Count)", body);
        // The items are found by GUID in one pass over the wired items (or the whole model), not one search per key.
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(body, "foreach \\(var item in source\\)").Count);
        Assert.Contains("ModelDataReader.AllItems(", body);
    }

    [Fact]
    public void SetCustomPointsToTheTableVersionAndTheTableVersionNamesTheGuidKey()
    {
        Assert.Contains("Properties.SetCustomFromTable", NavisworksMethodText.Declaration("CustomPropertyNodes.cs", "SetCustom"));
        Assert.Contains("keyColumn", NavisworksMethodText.Declaration("CustomPropertyNodes.cs", "SetCustomFromTable"));
    }

    [Fact]
    public void ToTableTakesNoPropertiesAsEveryPropertyAndSeveralWires()
    {
        var declaration = NavisworksMethodText.Declaration("ModelDataNodes.cs", "ToTable");
        var body = NavisworksMethodText.Body("ModelDataNodes.cs", "ToTable");

        Assert.Contains("[MultiInput] IList<object?>? properties = null", declaration);
        Assert.Contains("ReadEveryProperty(list)", body);
        Assert.Contains("new WideTableBuilder()", NavisworksSourceText.Source("ModelDataNodes.cs"));
    }

    [Fact]
    public void TheListInputsOfTheSnapshotAndTheGuidSearchTakeSeveralWires()
    {
        Assert.Contains("[MultiInput] IList<object?> properties", NavisworksMethodText.Declaration("ModelDataNodes.cs", "Snapshot"));
        Assert.Contains("[MultiInput] IList<object?> guids", NavisworksMethodText.Declaration("SelectionExtraNodes.cs", "ByGuid"));
    }

    [Fact]
    public void PropertyInfoIsRetiredBecauseNothingFeedsIt()
    {
        var declaration = NavisworksMethodText.Declaration("PropertyNodes.cs", "Info");

        Assert.Contains("[NodeDeprecated(\"Properties.InCategory\")]", declaration);
        Assert.Contains("DataProperty property", declaration);
    }
}
