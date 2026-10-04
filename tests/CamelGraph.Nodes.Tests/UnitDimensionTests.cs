using System;
using CamelGraph.Nodes.Portable;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>Units.Convert and Units.ScaleFactor for areas and volumes: the length factor squared or cubed.</summary>
public class UnitDimensionTests
{
    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("Length", 1)]
    [InlineData("length", 1)]
    [InlineData("Area", 2)]
    [InlineData(" area ", 2)]
    [InlineData("Volume", 3)]
    public void TheExponentFollowsTheDimension(string? name, int expected)
    {
        Assert.Equal(expected, UnitDimension.Exponent(name));
    }

    [Fact]
    public void AnUnknownDimensionNamesTheThreeThatExist()
    {
        var error = Assert.Throws<ArgumentException>(() => UnitDimension.Exponent("Weight"));

        Assert.Contains("Length, Area or Volume", error.Message);
    }

    [Fact]
    public void SquareFeetToSquareMetersIsTheLengthFactorSquared()
    {
        const double feetToMeters = 0.3048;

        Assert.Equal(feetToMeters, UnitDimension.Factor(feetToMeters, "Length"), 12);
        Assert.Equal(0.09290304, UnitDimension.Factor(feetToMeters, "Area"), 10);
        Assert.Equal(0.028316846592, UnitDimension.Factor(feetToMeters, "Volume"), 12);
    }

    [Fact]
    public void ALengthFactorIsReturnedUnchanged()
    {
        Assert.Equal(25.4, UnitDimension.Factor(25.4, null));
    }
}
