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
}
