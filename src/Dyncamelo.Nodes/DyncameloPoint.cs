using System;
using System.Globalization;

namespace Dyncamelo.Nodes;

/// <summary>
/// A simple immutable 3D point used by the Geometry nodes. Host layers
/// (e.g. the Navisworks node pack) convert to their native geometry types
/// at the boundary.
/// </summary>
public sealed class DyncameloPoint : IEquatable<DyncameloPoint>
{
    /// <summary>Creates a point from cartesian coordinates.</summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <param name="z">Z coordinate.</param>
    public DyncameloPoint(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>X coordinate.</summary>
    public double X { get; }

    /// <summary>Y coordinate.</summary>
    public double Y { get; }

    /// <summary>Z coordinate.</summary>
    public double Z { get; }

    /// <inheritdoc />
    public bool Equals(DyncameloPoint? other)
    {
        return other != null && X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DyncameloPoint);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = X.GetHashCode();
            hash = (hash * 397) ^ Y.GetHashCode();
            hash = (hash * 397) ^ Z.GetHashCode();
            return hash;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "Point({0}, {1}, {2})", X, Y, Z);
    }
}
