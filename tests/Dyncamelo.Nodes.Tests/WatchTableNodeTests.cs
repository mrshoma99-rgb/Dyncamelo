using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

public class WatchTableNodeTests
{
    private static IList<object?> L(params object?[] items) => new List<object?>(items);

    private static WatchTableNode Run(object? value)
    {
        var node = new WatchTableNode();
        var outputs = node.Evaluate(new[] { value }, new EvaluationContext());
        Assert.Same(value, outputs[0]);
        return node;
    }

    [Fact]
    public void ATableIsShownWithItsHeadersAndRows()
    {
        var table = TableToolkitNodes.FromRows(
            L(L("Wall", 3000.0), L("Door", null)),
            L("Category", "Length"));

        var node = Run(table);

        Assert.Equal(new[] { "Category", "Length" }, node.Headers);
        Assert.Equal(2, node.Rows.Count);
        Assert.Equal(new[] { "Wall", "3000" }, node.Rows[0].Cells);
        Assert.Equal(new[] { "Door", string.Empty }, node.Rows[1].Cells);
        Assert.Equal("2 rows × 2 columns", node.Summary);
    }

    [Fact]
    public void RowsWithoutHeadersGetNumberedColumns()
    {
        var node = Run(L(L(1.0, 2.0, 3.0), L(4.0, 5.0, 6.0)));
        Assert.Equal(new[] { "Column 1", "Column 2", "Column 3" }, node.Headers);
        Assert.Equal(2, node.Rows.Count);
    }

    [Fact]
    public void DictionariesBecomeRowsWithTheirKeysAsColumns()
    {
        var node = Run(L(
            new Dictionary<string, object?> { ["Name"] = "W1", ["Fire"] = "EI60" },
            new Dictionary<string, object?> { ["Name"] = "W2" }));
        Assert.Equal(new[] { "Name", "Fire" }, node.Headers);
        Assert.Equal(new[] { "W2", string.Empty }, node.Rows[1].Cells);

        var single = Run(new Dictionary<string, object?> { ["a"] = 1.0 });
        Assert.Equal(1, single.Rows.Count);
    }

    [Fact]
    public void AFlatListIsOneColumn_AndAScalarIsOneCell()
    {
        var list = Run(L("a", "b", "c"));
        Assert.Equal(new[] { "value" }, list.Headers);
        Assert.Equal(3, list.Rows.Count);

        var scalar = Run(42.0);
        Assert.Equal("1 row × 1 column", scalar.Summary);
        Assert.Equal("42", scalar.Rows[0].Cells[0]);

        var nothing = Run(null);
        Assert.Equal(1, nothing.Rows.Count);
    }

    [Fact]
    public void OnlyTheFirstRowsAreDrawn_AndTheSummarySaysSo()
    {
        var rows = Enumerable.Range(0, WatchTableNode.MaxRows + 50).Select(i => (object?)L((double)i)).ToList();
        var node = Run(rows);
        Assert.Equal(WatchTableNode.MaxRows, node.Rows.Count);
        Assert.Contains("first 2000 shown", node.Summary);
        Assert.StartsWith("2050 rows", node.Summary);
    }

    [Fact]
    public void ThePlayerShowsTheTableAsMarkdown()
    {
        var node = Run(TableToolkitNodes.FromRows(L(L("Wall", 3.0)), L("Category", "Count")));
        Assert.Contains("| Category | Count |", node.PlayerText.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ViewSizeIsSavedButNeverDirtiesTheNode()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var node = (WatchTableNode)registry.CreateNode(WatchTableNode.TypeName)!;
        Run(L(L(1.0)));
        var data = new Newtonsoft.Json.Linq.JObject();
        node.ViewWidth = 300;
        node.ViewHeight = 120;
        node.SerializeData(data);

        var copy = new WatchTableNode();
        copy.DeserializeData(data);
        Assert.Equal(300, copy.ViewWidth);
        Assert.Equal(120, copy.ViewHeight);
        Assert.Equal(NodeFunction.Info, copy.Function);
        Assert.Equal("Display", copy.Category);
    }

    [Fact]
    public void ItIsRegisteredWithTheLibrary()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        Assert.NotNull(registry.CreateNode(WatchTableNode.TypeName));
    }
}
