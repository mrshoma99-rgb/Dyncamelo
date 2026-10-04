using System;
using CamelGraph.Nodes.Coordination;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// ClashTest.Create used to replace an existing test of the same name with a new, empty one (and with it every status, assignment,
/// comment and group of the last cycle). It now asks ifExists, and the safe default reuses the test (NVC-11).
/// </summary>
public class ClashCreateRulesTests
{
    [Theory]
    [InlineData("reuse", ClashIfExists.Reuse)]
    [InlineData("Reuse", ClashIfExists.Reuse)]
    [InlineData("  UPDATE ", ClashIfExists.Update)]
    [InlineData("replace", ClashIfExists.Replace)]
    [InlineData("error", ClashIfExists.Error)]
    [InlineData("", ClashIfExists.Reuse)]
    [InlineData(null, ClashIfExists.Reuse)]
    public void TheChoiceIsReadInAnyCaseAndBlankMeansTheSafeDefault(string? text, ClashIfExists expected)
    {
        Assert.Equal(expected, ClashCreateRules.ParseIfExists(text));
    }

    [Fact]
    public void AnUnknownChoiceListsTheValidOnes()
    {
        var ex = Assert.Throws<ArgumentException>(() => ClashCreateRules.ParseIfExists("overwrite"));

        Assert.Contains("'overwrite'", ex.Message);
        Assert.Contains("reuse, update, replace or error", ex.Message);
    }

    [Fact]
    public void TheReuseWarningNamesTheTestAndTheWayOut()
    {
        var message = ClashCreateRules.ReusedMessage("MEP vs Structure");

        Assert.Contains("'MEP vs Structure'", message);
        Assert.Contains("reused as it is", message);
        Assert.Contains("update", message);
        Assert.Contains("replace", message);
    }

    [Fact]
    public void TheErrorTellsWhatToSetInstead()
    {
        var message = ClashCreateRules.ExistsMessage("MEP vs Structure");

        Assert.Contains("'MEP vs Structure' already exists", message);
        Assert.Contains("reuse", message);
        Assert.Contains("another name", message);
    }
}
