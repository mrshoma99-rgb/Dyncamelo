using System.Collections.Generic;
using CamelGraph.Nodes.Portable;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// A list that arrives at a socket taking ONE point, vector or matrix: the sockets accept [x, y, z] as one point, so the node has to
/// recognise a list of points itself and say how to run once per point.
/// </summary>
public class SeveralValuesTests
{
    private static List<object?> L(params object?[] items) => new List<object?>(items);

    [Fact]
    public void AFlatListOfNumbersIsOnePoint()
    {
        Assert.False(SeveralValues.HoldsSeveral(L(1.0, 2.0, 3.0)));
        Assert.False(SeveralValues.HoldsSeveral(L(1, 2L, 3.5f)));
    }

    [Fact]
    public void NumericTextCountsAsANumberBecauseTheConvertersReadIt()
    {
        Assert.False(SeveralValues.HoldsSeveral(L("1", "2", "3")));
    }

    [Fact]
    public void AListOfListsIsSeveralPoints()
    {
        Assert.True(SeveralValues.HoldsSeveral(L(L(1.0, 2.0, 3.0), L(4.0, 5.0, 6.0))));
    }

    [Fact]
    public void AListOfObjectsIsSeveralPoints()
    {
        Assert.True(SeveralValues.HoldsSeveral(L(new CamelGraphPoint(1, 2, 3), new CamelGraphPoint(4, 5, 6))));
    }

    [Fact]
    public void ANullEntryDoesNotMakeANumberListSeveral()
    {
        Assert.False(SeveralValues.HoldsSeveral(L(1.0, null, 3.0)));
    }

    [Fact]
    public void ABoolOrADateIsNotANumber()
    {
        Assert.True(SeveralValues.HoldsSeveral(L(true, false, true)));
    }

    [Fact]
    public void TheMessageForAListOfObjectsSaysL1()
    {
        var text = SeveralValues.Describe("point", L(new CamelGraphPoint(1, 2, 3), new CamelGraphPoint(4, 5, 6)));

        Assert.Contains("takes one point", text);
        Assert.Contains("list of 2 values", text);
        Assert.Contains("List Levels", text);
        Assert.Contains("L1", text);
        Assert.DoesNotContain("L2", text);
    }

    [Fact]
    public void TheMessageForAListOfNumberListsSaysL2BecauseL1WouldCutEachListIntoNumbers()
    {
        var text = SeveralValues.Describe("vector", L(L(1.0, 0.0, 0.0), L(0.0, 1.0, 0.0), L(0.0, 0.0, 1.0)));

        Assert.Contains("takes one vector", text);
        Assert.Contains("list of 3 values", text);
        Assert.Contains("L2", text);
        Assert.Contains("L1 would cut each list into single numbers", text);
    }

    [Fact]
    public void TheMessageNamesNoTypes()
    {
        var text = SeveralValues.Describe("point", L(new CamelGraphPoint(1, 2, 3)));

        Assert.DoesNotContain("List`1", text);
        Assert.DoesNotContain("System.", text);
        Assert.DoesNotContain("Object", text);
    }

    [Fact]
    public void ARowMajorMatrixOfSixteenNumbersAndFourRowsOfFourAreOneMatrix()
    {
        var flat = new List<object?>();
        for (var i = 0; i < 16; i++)
        {
            flat.Add((double)i);
        }

        Assert.False(SeveralValues.HoldsSeveralMatrices(flat));
        Assert.False(SeveralValues.HoldsSeveralMatrices(L(L(1.0, 0.0, 0.0, 0.0), L(0.0, 1.0, 0.0, 0.0), L(0.0, 0.0, 1.0, 0.0), L(0.0, 0.0, 0.0, 1.0))));
    }

    [Fact]
    public void ListsOfSixteenNumbersAreSeveralMatrices()
    {
        var one = new List<object?>();
        for (var i = 0; i < 16; i++)
        {
            one.Add((double)i);
        }

        Assert.True(SeveralValues.HoldsSeveralMatrices(L(one, one)));
        Assert.True(SeveralValues.HoldsSeveralMatrices(L(one, one, one, one)));
    }

    [Fact]
    public void AListOfMatrixObjectsIsSeveralMatrices()
    {
        Assert.True(SeveralValues.HoldsSeveralMatrices(L(new object(), new object())));
    }
}
