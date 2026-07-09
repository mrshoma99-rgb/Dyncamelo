using System;
using System.Globalization;

namespace Dyncamelo.Nodes;

/// <summary>
/// A simple immutable axis-aligned bounding box used by the Geometry nodes.
/// The constructor normalizes the two corners so <see cref="Min"/> always holds
/// the component-wise minimum and <see cref="Max"/> the component-wise maximum.
/// </summary>
public sealed class DyncameloBoundingBox : IEquatable<DyncameloBoundingBox>
{
    /// <summary>Creates a bounding box spanning two opposite corners (any order).</summary>
    /// <param name="cornerA">One corner of the box.</param>
    /// <param name="cornerB">The opposite corner of the box.</param>
    public DyncameloBoundingBox(DyncameloPoint cornerA, DyncameloPoint cornerB)
    {
        if (cornerA == null)
        {
            throw new ArgumentNullException(nameof(cornerA));
        }

        if (cornerB == null)
        {
            throw new ArgumentNullException(nameof(cornerB));
        }

        Min = new DyncameloPoint(
            Math.Min(cornerA.X, cornerB.X),
            Math.Min(cornerA.Y, cornerB.Y),
            Math.Min(cornerA.Z, cornerB.Z));
        Max = new DyncameloPoint(
            Math.Max(cornerA.X, cornerB.X),
            Math.Max(cornerA.Y, cornerB.Y),
            Math.Max(cornerA.Z, cornerB.Z));
    }

    /// <summary>Corner with the smallest X, Y and Z.</summary>
    public DyncameloPoint Min { get; }

    /// <summary>Corner with the largest X, Y and Z.</summary>
    public DyncameloPoint Max { get; }

    /// <summary>Geometric center of the box.</summary>
    public DyncameloPoint Center
    {
        get
        {
            return new DyncameloPoint(
                (Min.X + Max.X) / 2d,
                (Min.Y + Max.Y) / 2d,
                (Min.Z + Max.Z) / 2d);
        }
    }

    /// <inheritdoc />
    public bool Equals(DyncameloBoundingBox? other)
    {
        return other != null && Min.Equals(other.Min) && Max.Equals(other.Max);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DyncameloBoundingBox);

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
