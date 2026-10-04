using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The arithmetic nodes keep their IEEE results but say so when a division by zero or an impossible power makes a number that
/// is not a finite number (VAL-32, ENG-14); Math.IsClose answers "equal within a tolerance" (VAL-36).
/// </summary>
public class MathWarningsTests
{
    private static IList<object?> L(params object?[] items) => new List<object?>(items);

    private static string Warnings(NodeModel node) =>
        string.Join(" | ", node.Messages.Where(m => m.Severity == MessageSeverity.Warning).Select(m => m.Text));

    [Fact]
    public void DivideByZeroGivesInfinityAndAWarningThatNamesTheDivisor()
    {
        var node = EngineRun.Run("Divide", -1, 2.5, 0.0);

        Assert.Equal(double.PositiveInfinity, node.OutPorts[0].Value);
        Assert.Equal(NodeState.Warning, node.State);
        Assert.Contains("Dividing by zero gives Infinity", Warnings(node));
        Assert.Contains("'b'", Warnings(node));
    }

    [Fact]
    public void ZeroOverZeroIsNaNWithAWarning()
    {
        var node = EngineRun.Run("Divide", -1, 0.0, 0.0);

        Assert.True(double.IsNaN((double)node.OutPorts[0].Value!));
        Assert.Contains("NaN", Warnings(node));
    }

    [Fact]
    public void AnOrdinaryDivisionIsClean()
    {
        var node = EngineRun.Run("Divide", -1, 10.0, 4.0);

        Assert.Equal(2.5, node.OutPorts[0].Value);
        Assert.Equal(NodeState.Executed, node.State);
        Assert.Empty(node.Messages);
    }

    [Fact]
    public void ADivisionByZeroInAColumnIsOneLineWithTheCount()
    {
        var node = EngineRun.Run("Divide", -1, L(1.0, 2.0, 3.0), L(1.0, 0.0, 0.0));

        Assert.Equal(L(1.0, double.PositiveInfinity, double.PositiveInfinity), node.OutPorts[0].Value);
        Assert.Contains("2 of 3 calls", Warnings(node));
    }

    [Fact]
    public void ANonFiniteInputIsNotBlamedOnThisNode()
    {
        var node = EngineRun.Run("Divide", -1, double.PositiveInfinity, 2.0);

        Assert.Equal(double.PositiveInfinity, node.OutPorts[0].Value);
        Assert.Equal(NodeState.Executed, node.State);
    }

    [Fact]
    public void ModuloByZeroWarns_AndTheSignFollowsTheFirstNumber()
    {
        var zero = EngineRun.Run("Modulo", -1, 7.0, 0.0);
        Assert.True(double.IsNaN((double)zero.OutPorts[0].Value!));
        Assert.Contains("remainder by zero", Warnings(zero));

        var negative = EngineRun.Run("Modulo", -1, -1.0, 3.0);
        Assert.Equal(-1.0, negative.OutPorts[0].Value);
        Assert.Equal(NodeState.Executed, negative.State);

        Assert.Contains("a - b * floor(a / b)", EngineRun.Create(EngineRun.Registry(), "Modulo").Description);
    }

    [Fact]
    public void PowWarnsForANegativeBaseWithAFractionalExponentAndForOverflow()
    {
        var nan = EngineRun.Run("Math.Pow", -1, -8.0, 0.5);
        Assert.True(double.IsNaN((double)nan.OutPorts[0].Value!));
        Assert.Contains("negative base needs a whole exponent", Warnings(nan));

        var huge = EngineRun.Run("Math.Pow", -1, 10.0, 400.0);
        Assert.Equal(double.PositiveInfinity, huge.OutPorts[0].Value);
        Assert.Contains("too large", Warnings(huge));

        var fine = EngineRun.Run("Math.Pow", -1, 2.0, 10.0);
        Assert.Equal(1024.0, fine.OutPorts[0].Value);
        Assert.Empty(fine.Messages);
    }

    [Fact]
    public void SignOfNaNIsZeroWithAWarning()
    {
        var node = EngineRun.Run("Math.Sign", -1, double.NaN);

        Assert.Equal(0, node.OutPorts[0].Value);
        Assert.Contains("not a number", Warnings(node));
        Assert.Empty(EngineRun.Run("Math.Sign", -1, -3.0).Messages);
    }

    [Fact]
    public void PercentOfZeroStaysAZeroByDesignAndOverflowWarns()
    {
        var empty = EngineRun.Run("Math.Percent", -1, 2.5, 0.0);
        Assert.Equal(0.0, empty.OutPorts[0].Value);
        Assert.Equal(NodeState.Executed, empty.State);

        var overflow = EngineRun.Run("Math.Percent", -1, 1e308, 1e-10);
        Assert.Equal(double.PositiveInfinity, overflow.OutPorts[0].Value);
        Assert.Contains("not a finite number", Warnings(overflow));
    }

    [Fact]
    public void FormulaAndMapRangeAndExpWarnWhenTheyMakeANonFiniteNumber()
    {
        var formula = EngineRun.Run("Math.Formula", -1, "a / b", 1.0, 0.0);
        Assert.Equal(double.PositiveInfinity, formula.OutPorts[0].Value);
        Assert.Contains("The formula gave Infinity", Warnings(formula));

        var exp = EngineRun.Run("Math.Exp", -1, 1000.0);
        Assert.Contains("not a finite number", Warnings(exp));

        var map = EngineRun.Run("Math.MapRange", -1, 1e308, 0.0, 1e-10, 0.0, 1e308);
        Assert.Contains("mapped value", Warnings(map));

        Assert.Empty(EngineRun.Run("Math.Formula", -1, "a + b", 1.0, 2.0).Messages);
    }

    [Fact]
    public void LerpKeepsTheValuesButScrubsBetweenZeroAndOne()
    {
        Assert.Equal(15.0, MathExtraNodes.Lerp(10, 20, 0.5));
        Assert.Equal(30.0, MathExtraNodes.Lerp(10, 20, 2));

        var range = EngineRun.Create(EngineRun.Registry(), "Math.Lerp").InPorts[2].Range;
        Assert.NotNull(range);
        Assert.Equal(0.0, range!.SoftMin);
        Assert.Equal(1.0, range.SoftMax);
        Assert.Equal(0.05, range.Step);
        Assert.True(range.Min < -1e6 && range.Max > 1e6);
    }

    // ------------------------------------------------------------ Math.IsClose (VAL-36)

    [Theory]
    [InlineData(0.30000000000000004, 0.3, 0.001, true)]
    [InlineData(0.30000000000000004, 0.3, 0.0, false)]
    [InlineData(5.0, 5.0, 0.0, true)]
    [InlineData(10.0, 10.0005, 0.001, true)]
    [InlineData(10.0, 10.002, 0.001, false)]
    [InlineData(-3.0, 3.0, 5.0, false)]
    public void IsCloseTestsTheDifferenceAgainstTheTolerance(double a, double b, double tolerance, bool expected)
    {
        Assert.Equal(expected, MathExtraNodes.IsClose(a, b, tolerance));
    }

    [Fact]
    public void IsCloseOnlyTrustsNumbersThatAreFiniteOrIdentical()
    {
        Assert.True(MathExtraNodes.IsClose(double.PositiveInfinity, double.PositiveInfinity, 0.1));
        Assert.False(MathExtraNodes.IsClose(double.PositiveInfinity, 5, 1e300));
        Assert.False(MathExtraNodes.IsClose(double.NaN, double.NaN, 1));
    }

    [Fact]
    public void IsCloseNeedsATolerance_AndMapsOverAColumn()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => MathExtraNodes.IsClose(1, 1, -0.5));
        Assert.Contains("tolerance of 0 or more", ex.Message);

        var node = EngineRun.Run("Math.IsClose", -1, L(1.0, 1.0004, 1.5), 1.0, 0.001);
        Assert.Equal(L(true, true, false), node.OutPorts[0].Value);
    }

    [Fact]
    public void IsCloseIsFindableAndDescribedAsTheToleranceTest()
    {
        var definition = EngineRun.Registry().Definitions.Single(d => d.Name == "Math.IsClose");
        Assert.Contains("tolerance", definition.Description);
        Assert.Equal("Math", definition.Category);
        foreach (var tag in new[] { "tolerance", "approximately", "epsilon", "equal" })
        {
            Assert.Contains(tag, definition.SearchTags);
        }
    }
}
