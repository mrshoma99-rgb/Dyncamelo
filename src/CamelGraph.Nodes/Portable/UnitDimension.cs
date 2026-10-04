using System;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Portable;

/// <summary>
/// What a unit conversion applies to: a length, an area (length squared) or a volume (length cubed). The conversion nodes know the
/// factor between two length units; an area or a volume is that factor squared or cubed, which callers used to work out by hand.
/// Pure (no Navisworks types), so it is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public static class UnitDimension
{
    /// <summary>The names the conversion nodes accept, in the order they are offered.</summary>
    public static readonly string[] Names = { "Length", "Area", "Volume" };

    /// <summary>The power a length factor is raised to for a dimension: 1 for a length, 2 for an area, 3 for a volume.</summary>
    /// <param name="dimension">"Length", "Area" or "Volume" in any case; empty means length.</param>
    /// <returns>1, 2 or 3.</returns>
    /// <exception cref="ArgumentException">The name is none of the three.</exception>
    public static int Exponent(string? dimension)
    {
        switch ((dimension ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "":
            case "length":
                return 1;
            case "area":
                return 2;
            case "volume":
                return 3;
            default:
                throw new ArgumentException(
                    "'" + dimension + "' is not a dimension. Use Length, Area or Volume.", nameof(dimension));
        }
    }

    /// <summary>The factor for a dimension, given the factor between two length units.</summary>
    /// <param name="lengthFactor">The multiplier that converts a length from one unit to the other.</param>
    /// <param name="dimension">"Length", "Area" or "Volume".</param>
    /// <returns>The length factor to the power the dimension needs.</returns>
    public static double Factor(double lengthFactor, string? dimension)
    {
        var exponent = Exponent(dimension);
        return exponent == 1 ? lengthFactor : Math.Pow(lengthFactor, exponent);
    }
}
