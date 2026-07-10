using System;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

public class MathNodesTests
{
    [Theory]
    [InlineData(2, 3, 5)]
    [InlineData(-1.5, 0.5, -1)]
    [InlineData(0, 0, 0)]
    public void Add_ReturnsSum(double a, double b, double expected)
    {
        Assert.Equal(expected, MathNodes.Add(a, b), 12);
    }

    [Fact]
    public void Subtract_ReturnsDifference()
    {
        Assert.Equal(1.5, MathNodes.Subtract(4, 2.5), 12);
    }

    [Fact]
    public void Multiply_ReturnsProduct()
    {
        Assert.Equal(-8, MathNodes.Multiply(4, -2), 12);
    }

    [Fact]
    public void Divide_ReturnsQuotient()
    {
        Assert.Equal(2.5, MathNodes.Divide(5, 2), 12);
    }

    [Fact]
    public void Divide_ByZero_FollowsIeeeSemantics()
    {
        Assert.Equal(double.PositiveInfinity, MathNodes.Divide(1, 0));
        Assert.Equal(double.NegativeInfinity, MathNodes.Divide(-1, 0));
        Assert.True(double.IsNaN(MathNodes.Divide(0, 0)));
    }

    [Theory]
    [InlineData(7, 3, 1)]
    [InlineData(-7, 3, -1)]
    [InlineData(7.5, 2, 1.5)]
    public void Modulo_UsesCSharpRemainderSemantics(double a, double b, double expected)
    {
        Assert.Equal(expected, MathNodes.Modulo(a, b), 12);
    }

    [Theory]
    [InlineData(0.5, 0, 1)]
    [InlineData(-0.5, 0, -1)]
    [InlineData(2.345, 2, 2.35)]
    [InlineData(2.344, 2, 2.34)]
    public void Round_RoundsMidpointsAwayFromZero(double number, int digits, double expected)
    {
        Assert.Equal(expected, MathNodes.Round(number, digits), 12);
    }

    [Fact]
    public void Round_DefaultsToZeroDigits()
    {
        Assert.Equal(3, MathNodes.Round(3.4));
    }

    [Fact]
    public void MinAndMax_ReturnExtremes()
    {
        Assert.Equal(-2, MathNodes.Min(-2, 5));
        Assert.Equal(5, MathNodes.Max(-2, 5));
    }

    [Theory]
    [InlineData(-3.5, 3.5)]
    [InlineData(3.5, 3.5)]
    [InlineData(0, 0)]
    public void Abs_ReturnsAbsoluteValue(double number, double expected)
    {
        Assert.Equal(expected, MathNodes.Abs(number), 12);
    }

    [Theory]
    [InlineData(2, 10, 1024)]
    [InlineData(9, 0.5, 3)]
    [InlineData(5, 0, 1)]
    public void Pow_RaisesToPower(double b, double exponent, double expected)
    {
        Assert.Equal(expected, MathNodes.Pow(b, exponent), 12);
    }

    [Fact]
    public void Sqrt_ReturnsRoot()
    {
        Assert.Equal(4, MathNodes.Sqrt(16), 12);
        Assert.Equal(0, MathNodes.Sqrt(0), 12);
    }

    [Fact]
    public void Sqrt_NegativeInput_ThrowsWithClearMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => MathNodes.Sqrt(-4));
        Assert.Contains("non-negative", ex.Message);
        Assert.Contains("-4", ex.Message);
    }

    [Theory]
    [InlineData(2.7, 2)]
    [InlineData(-2.1, -3)]
    [InlineData(5, 5)]
    public void Floor_RoundsDown(double number, double expected)
    {
        Assert.Equal(expected, MathNodes.Floor(number));
    }

    [Theory]
    [InlineData(2.1, 3)]
    [InlineData(-2.7, -2)]
    [InlineData(5, 5)]
    public void Ceiling_RoundsUp(double number, double expected)
    {
        Assert.Equal(expected, MathNodes.Ceiling(number));
    }

    [Theory]
    [InlineData(5, 0, 10, 0, 1, 0.5)]
    [InlineData(0, 0, 10, 100, 200, 100)]
    [InlineData(10, 0, 10, 100, 200, 200)]
    [InlineData(15, 0, 10, 0, 1, 1.5)] // extrapolates beyond the target range
    [InlineData(5, 10, 0, 0, 1, 0.5)] // reversed source range
    public void MapRange_RemapsLinearly(double value, double fromLow, double fromHigh, double toLow, double toHigh, double expected)
    {
        Assert.Equal(expected, MathNodes.MapRange(value, fromLow, fromHigh, toLow, toHigh), 12);
    }

    [Fact]
    public void MapRange_DegenerateSourceRange_ThrowsWithClearMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => MathNodes.MapRange(1, 5, 5, 0, 1));
        Assert.Contains("fromLow", ex.Message);
    }

    [Fact]
    public void Random_StaysWithinRange()
    {
        for (int i = 0; i < 100; i++)
        {
            var value = MathNodes.Random(2, 3);
            Assert.InRange(value, 2d, 3d);
        }
    }

    [Fact]
    public void Random_SameSeed_IsDeterministic()
    {
        Assert.Equal(MathNodes.Random(0, 10, seed: 42), MathNodes.Random(0, 10, seed: 42));
        Assert.NotEqual(MathNodes.Random(0, 10, seed: 1), MathNodes.Random(0, 10, seed: 2));
    }

    [Fact]
    public void Random_UnseededCallsInQuickSuccession_Vary()
    {
        var first = MathNodes.Random();
        var anyDifferent = false;
        for (int i = 0; i < 20 && !anyDifferent; i++)
        {
            anyDifferent = !MathNodes.Random().Equals(first);
        }

        Assert.True(anyDifferent, "20 unseeded draws all returned the same value.");
    }

    [Fact]
    public void Random_MaxBelowMin_ThrowsWithClearMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => MathNodes.Random(5, 1));
        Assert.Contains("max", ex.Message);
    }
}
