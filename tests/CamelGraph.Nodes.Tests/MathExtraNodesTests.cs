using System;
using System.Linq;
using Xunit;

namespace CamelGraph.Nodes.Tests;

public class MathExtraNodesTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(30, 0.5)]
    [InlineData(90, 1)]
    [InlineData(180, 0)]
    public void Sin_UsesDegreesByDefault(double angle, double expected)
    {
        Assert.Equal(expected, MathExtraNodes.Sin(angle), 9);
    }

    [Fact]
    public void Sin_AcceptsRadians()
    {
        Assert.Equal(1d, MathExtraNodes.Sin(Math.PI / 2, "radians"), 12);
        Assert.Equal(1d, MathExtraNodes.Sin(Math.PI / 2, "Radians"), 12);
    }

    [Fact]
    public void Sin_RejectsAnUnknownUnit()
    {
        var ex = Assert.Throws<ArgumentException>(() => MathExtraNodes.Sin(1, "grads"));
        Assert.Contains("degrees", ex.Message);
    }

    [Fact]
    public void CosAndTan_MatchTheirDefinitions()
    {
        Assert.Equal(0.5, MathExtraNodes.Cos(60), 9);
        Assert.Equal(1d, MathExtraNodes.Tan(45), 9);
        Assert.Equal(1d, MathExtraNodes.Cos(0, "radians"), 12);
    }

    [Fact]
    public void InverseFunctions_ReturnDegreesByDefault()
    {
        Assert.Equal(30d, MathExtraNodes.Asin(0.5), 9);
        Assert.Equal(60d, MathExtraNodes.Acos(0.5), 9);
        Assert.Equal(45d, MathExtraNodes.Atan(1), 9);
        Assert.Equal(Math.PI / 4, MathExtraNodes.Atan(1, "radians"), 12);
    }

    [Fact]
    public void AsinAndAcos_RejectValuesOutsideTheUnitRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MathExtraNodes.Asin(1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => MathExtraNodes.Acos(-2));
    }

    [Theory]
    [InlineData(1, 1, 45)]
    [InlineData(1, 0, 90)]
    [InlineData(0, -1, 180)]
    [InlineData(-1, 0, -90)]
    public void Atan2_FindsTheQuadrant(double y, double x, double expected)
    {
        Assert.Equal(expected, MathExtraNodes.Atan2(y, x), 9);
    }

    [Fact]
    public void RadiansAndDegrees_AreInverses()
    {
        Assert.Equal(Math.PI, MathExtraNodes.Radians(180), 12);
        Assert.Equal(90d, MathExtraNodes.Degrees(Math.PI / 2), 9);
        Assert.Equal(Math.PI, MathExtraNodes.Pi());
    }

    [Fact]
    public void LogarithmsAndExponent()
    {
        Assert.Equal(1d, MathExtraNodes.Ln(Math.E), 12);
        Assert.Equal(2d, MathExtraNodes.Log(100), 12);
        Assert.Equal(3d, MathExtraNodes.Log(8, 2), 12);
        Assert.Equal(Math.E, MathExtraNodes.Exp(1), 12);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Logarithms_RejectNonPositiveNumbers(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MathExtraNodes.Ln(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => MathExtraNodes.Log(value));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-2)]
    public void Log_RejectsAnInvalidBase(double logBase)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MathExtraNodes.Log(10, logBase));
    }

    [Theory]
    [InlineData(-4.2, -1)]
    [InlineData(0, 0)]
    [InlineData(7, 1)]
    [InlineData(double.NaN, 0)]
    public void Sign_ReturnsMinusOneZeroOrOne(double number, int expected)
    {
        Assert.Equal(expected, MathExtraNodes.Sign(number));
    }

    [Fact]
    public void NegateAndTruncate()
    {
        Assert.Equal(-5d, MathExtraNodes.Negate(5));
        Assert.Equal(5d, MathExtraNodes.Negate(-5));
        Assert.Equal(2d, MathExtraNodes.Truncate(2.9));
        Assert.Equal(-2d, MathExtraNodes.Truncate(-2.9));
    }

    [Theory]
    [InlineData(5, 0, 10, 5)]
    [InlineData(-5, 0, 10, 0)]
    [InlineData(50, 0, 10, 10)]
    public void Clamp_LimitsToTheRange(double value, double min, double max, double expected)
    {
        Assert.Equal(expected, MathExtraNodes.Clamp(value, min, max));
    }

    [Fact]
    public void Clamp_RejectsAnInvertedRange()
    {
        var ex = Assert.Throws<ArgumentException>(() => MathExtraNodes.Clamp(1, 10, 0));
        Assert.Contains("min <= max", ex.Message);
    }

    [Fact]
    public void Lerp_InterpolatesAndExtrapolates()
    {
        Assert.Equal(15d, MathExtraNodes.Lerp(10, 20, 0.5));
        Assert.Equal(10d, MathExtraNodes.Lerp(10, 20, 0));
        Assert.Equal(30d, MathExtraNodes.Lerp(10, 20, 2));
    }

    [Fact]
    public void Percent_IsZeroForAnEmptyTotal()
    {
        Assert.Equal(25d, MathExtraNodes.Percent(1, 4));
        Assert.Equal(0d, MathExtraNodes.Percent(5, 0));
        Assert.Equal(10.882352941, MathExtraNodes.Percent(37, 340), 6);
    }

    [Theory]
    [InlineData(23, 5, 25)]
    [InlineData(22, 5, 20)]
    [InlineData(2.5, 5, 5)]
    [InlineData(-2.5, 5, -5)]
    [InlineData(1.13, 0.25, 1.25)]
    public void RoundToMultiple_SnapsToTheStep(double value, double step, double expected)
    {
        Assert.Equal(expected, MathExtraNodes.RoundToMultiple(value, step), 9);
    }

    [Fact]
    public void RoundToMultiple_NeedsAPositiveStep()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MathExtraNodes.RoundToMultiple(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MathExtraNodes.RoundToMultiple(1, -1));
    }

    [Fact]
    public void Sequence_CountsFromStartByStep()
    {
        Assert.Equal(new[] { 2d, 4d, 6d, 8d }, MathExtraNodes.Sequence(2, 4, 2).ToArray());
        Assert.Equal(new[] { 1d, 0.5, 0d }, MathExtraNodes.Sequence(1, 3, -0.5).ToArray());
        Assert.Empty(MathExtraNodes.Sequence(0, 0));
    }

    [Fact]
    public void Sequence_RefusesAnAbsurdCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MathExtraNodes.Sequence(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MathExtraNodes.Sequence(0, 2_000_000));
    }
}
