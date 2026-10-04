using System.Collections.Generic;
using CamelGraph.Core.Graph;
using Xunit;
using static CamelGraph.Nodes.Tests.Val2Harness;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// Gaps in a list of texts (a blank spreadsheet cell is null): String.IsBlank and String.Concat answer them, String.Join and
/// String.Format write them as empty text like String.Concat does (audit VAL-08, ENG-11).
/// </summary>
public class StringNullHandlingTests
{
    [Fact]
    public void IsBlank_OverAListWithAGap_SaysTrueForTheGap()
    {
        var node = Run(Create(Registry(), "String.IsBlank"), L("a", null, "", "  "));

        Assert.Equal(L(false, true, true, true), node.OutPorts[0].Value);
        Assert.Equal(NodeState.Executed, node.State);
        Assert.Empty(node.Messages);
    }

    [Fact]
    public void Concat_OverAListWithAGap_TreatsTheGapAsEmptyText()
    {
        var node = Run(Create(Registry(), "String.Concat"), L("a", null), "x");

        Assert.Equal(L("ax", "x"), node.OutPorts[0].Value);
        Assert.Equal(NodeState.Executed, node.State);
    }

    [Fact]
    public void Concat_AGapInTheSecondInputIsEmptyTextToo()
    {
        var node = Run(Create(Registry(), "String.Concat"), "a", L(null, "y"));

        Assert.Equal(L("a", "ay"), node.OutPorts[0].Value);
        Assert.Equal(NodeState.Executed, node.State);
    }

    [Fact]
    public void Join_WritesAGapAsEmptyText_NotTheWordNull()
    {
        Assert.Equal("a,,c", StringNodes.Join(",", L("a", null, "c")));
        Assert.Equal("a|b", StringNodes.Join("|", L("a", "b")));
    }

    [Fact]
    public void Format_WritesAGapAsEmptyText_NotTheWordNull()
    {
        Assert.Equal("a|", StringExtraNodes.Format("{0}|{1}", L("a", null)));
        Assert.Equal("[  ]", StringExtraNodes.Format("[{0,2}]", L((object?)null)));
    }

    [Fact]
    public void FromObject_StillShowsNullAsTheWordNull()
    {
        // String.FromObject is the display function (Watch shows null the same way); only Join and Format changed.
        Assert.Equal("null", StringNodes.FromObject(null));
    }

    [Fact]
    public void Format_ListOfRowsOnOneWire_MakesOneTextUnlessListLevelsAreSet()
    {
        var rows = L(L("W1", 3.0), L("W2", 4.0));

        var flat = Run(Create(Registry(), "String.Format"), "{0} is {1}", rows);
        Assert.Equal("[W1, 3] is [W2, 4]", flat.OutPorts[0].Value);

        // The recipe in the node description: List Levels @L2 on 'values' gives one text per row.
        var perRow = Create(Registry(), "String.Format");
        perRow.InPorts[1].SetLevels(true, 2, false);
        Run(perRow, "{0} is {1}", rows);
        Assert.Equal(L("W1 is 3", "W2 is 4"), perRow.OutPorts[0].Value);
    }

    [Fact]
    public void Format_Description_ExplainsTheOneTextPerRowRecipe()
    {
        var description = Create(Registry(), "String.Format").Description;

        Assert.Contains("one text", description);
        Assert.Contains("@L2", description);
        Assert.Contains("String.Template", description);
    }
}
