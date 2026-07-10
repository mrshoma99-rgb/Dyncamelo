using System;
using System.Globalization;

namespace Dyncamelo.Nodes;

/// <summary>
/// A simple immutable 3D direction vector used by the Geometry nodes
/// (e.g. camera up/forward directions). Host layers convert to their native
/// vector types (such as <c>Autodesk.Navisworks.Api.Vector3D</c>) at the
/// boundary.
/// </summary>
public sealed class DyncameloVector : IEquatable<DyncameloVector>
{
    /// <summary>Creates a vector from cartesian components.</summary>
    /// <param name="x">X component.</param>
    /// <param name="y">Y component.</param>
    /// <param name="z">Z component.</param>
    public DyncameloVector(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>X component.</summary>
    public double X { get; }

    /// <summary>Y component.</summary>
    public double Y { get; }

    /// <summary>Z component.</summary>
    public double Z { get; }

    /// <summary>Euclidean length of the vector.</summary>
    public double Length
    {
        get { return Math.Sqrt(X * X + Y * Y + Z * Z); }
    }

    /// <inheritdoc />
    public bool Equals(DyncameloVector? other)
    {
        return other != null && X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DyncameloVector);

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
        return string.Format(CultureInfo.InvariantCulture, "Vector({0}, {1}, {2})", X, Y, Z);
    }
}
