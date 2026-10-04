using System;
using System.Globalization;

namespace CamelGraph.Nodes;

/// <summary>
/// A simple immutable axis-aligned bounding box used by the Geometry nodes.
/// The constructor normalizes the two corners so <see cref="Min"/> always holds
/// the component-wise minimum and <see cref="Max"/> the component-wise maximum.
/// </summary>
public sealed class CamelGraphBoundingBox : IEquatable<CamelGraphBoundingBox>
{
    /// <summary>Creates a bounding box spanning two opposite corners (any order).</summary>
    /// <param name="cornerA">One corner of the box.</param>
    /// <param name="cornerB">The opposite corner of the box.</param>
    public CamelGraphBoundingBox(CamelGraphPoint cornerA, CamelGraphPoint cornerB)
    {
        if (cornerA == null)
        {
            throw new ArgumentNullException(nameof(cornerA));
        }

        if (cornerB == null)
        {
            throw new ArgumentNullException(nameof(cornerB));
        }

        Min = new CamelGraphPoint(
            Math.Min(cornerA.X, cornerB.X),
            Math.Min(cornerA.Y, cornerB.Y),
            Math.Min(cornerA.Z, cornerB.Z));
        Max = new CamelGraphPoint(
            Math.Max(cornerA.X, cornerB.X),
            Math.Max(cornerA.Y, cornerB.Y),
            Math.Max(cornerA.Z, cornerB.Z));
    }

    /// <summary>Corner with the smallest X, Y and Z.</summary>
    public CamelGraphPoint Min { get; }

    /// <summary>Corner with the largest X, Y and Z.</summary>
    public CamelGraphPoint Max { get; }

    /// <summary>Geometric center of the box.</summary>
    public CamelGraphPoint Center
    {
        get
        {
            return new CamelGraphPoint(
                (Min.X + Max.X) / 2d,
                (Min.Y + Max.Y) / 2d,
                (Min.Z + Max.Z) / 2d);
        }
    }

    /// <inheritdoc />
    public bool Equals(CamelGraphBoundingBox? other)
    {
        return other != null && Min.Equals(other.Min) && Max.Equals(other.Max);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as CamelGraphBoundingBox);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            return (Min.GetHashCode() * 397) ^ Max.GetHashCode();
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "BoundingBox({0} .. {1})", Min, Max);
    }
}
