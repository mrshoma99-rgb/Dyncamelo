using System;
using System.Globalization;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes.Spatial;

/// <summary>
/// Pure matrix maths for the ModelItem.Scale and ModelItem.MoveTo nodes, free of
/// Navisworks types so it is fully unit-testable. Matrices are 16 numbers, row-major,
/// acting on column vectors (p' = M·p) with the translation in indices 3, 7 and 11 and
/// the bottom row 0 0 0 1 — the convention of the existing ModelItem.SetTransform /
/// ModelItem.GetTransform nodes, so a result can be fed straight to the same
/// Transform3D builder.
/// </summary>
[IsVisibleInLibrary(false)]
public static class ItemTransformMath
{
    /// <summary>
    /// Checks a uniform scale factor: finite and strictly positive. Zero would collapse the
    /// items to a point and a negative factor would mirror them (and flip their winding),
    /// neither of which a scale node should do silently.
    /// </summary>
    /// <param name="factor">The factor typed or wired in.</param>
    /// <exception cref="ArgumentException">The factor is NaN, infinite, zero or negative.</exception>
    public static void RequireScaleFactor(double factor)
    {
        if (double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0.0)
        {
            throw new ArgumentException(
                "ModelItem.Scale needs a positive scale factor (1 = unchanged, 2 = twice as large, 0.5 = half); got " +
                factor.ToString(CultureInfo.InvariantCulture) +
                ". To mirror items use ModelItem.SetTransform with an explicit matrix.",
                nameof(factor));
        }
    }

    /// <summary>Whether a factor leaves the items exactly as they are (so the node can skip creating overrides).</summary>
    /// <param name="factor">A valid scale factor.</param>
    /// <returns>True for a factor of 1 within rounding.</returns>
    public static bool IsIdentityScale(double factor)
    {
        return Math.Abs(factor - 1.0) < 1e-12;
    }

    /// <summary>
    /// The matrix of a uniform scale about a point: T(c) · S(f) · T(-c). The point stays where it is;
    /// every other point moves along the line from the point by the factor.
    /// </summary>
    /// <param name="factor">A valid scale factor (see <see cref="RequireScaleFactor"/>).</param>
    /// <param name="aboutX">Fixed point X.</param>
    /// <param name="aboutY">Fixed point Y.</param>
    /// <param name="aboutZ">Fixed point Z.</param>
    /// <returns>16 numbers, row-major.</returns>
    public static double[] ScaleAboutPoint(double factor, double aboutX, double aboutY, double aboutZ)
    {
        RequireScaleFactor(factor);
        var k = 1.0 - factor;
        return new[]
        {
            factor, 0.0, 0.0, k * aboutX,
            0.0, factor, 0.0, k * aboutY,
            0.0, 0.0, factor, k * aboutZ,
            0.0, 0.0, 0.0, 1.0,
        };
    }

    /// <summary>The matrix of a pure translation.</summary>
    /// <param name="dx">Offset along X.</param>
    /// <param name="dy">Offset along Y.</param>
    /// <param name="dz">Offset along Z.</param>
    /// <returns>16 numbers, row-major.</returns>
    public static double[] Translation(double dx, double dy, double dz)
    {
        return new[]
        {
            1.0, 0.0, 0.0, dx,
            0.0, 1.0, 0.0, dy,
            0.0, 0.0, 1.0, dz,
            0.0, 0.0, 0.0, 1.0,
        };
    }

    /// <summary>The centre of an axis-aligned box.</summary>
    /// <param name="minX">Box minimum X.</param>
    /// <param name="minY">Box minimum Y.</param>
    /// <param name="minZ">Box minimum Z.</param>
    /// <param name="maxX">Box maximum X.</param>
    /// <param name="maxY">Box maximum Y.</param>
    /// <param name="maxZ">Box maximum Z.</param>
    /// <returns>The midpoint of the box.</returns>
    public static (double X, double Y, double Z) BoxCentre(
        double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        return ((minX + maxX) * 0.5, (minY + maxY) * 0.5, (minZ + maxZ) * 0.5);
    }

    /// <summary>The offset that moves a point onto another one.</summary>
    /// <param name="fromX">Current X.</param>
    /// <param name="fromY">Current Y.</param>
    /// <param name="fromZ">Current Z.</param>
    /// <param name="toX">Wanted X.</param>
    /// <param name="toY">Wanted Y.</param>
    /// <param name="toZ">Wanted Z.</param>
    /// <returns>The offset from the first point to the second.</returns>
    public static (double X, double Y, double Z) MoveDelta(
        double fromX, double fromY, double fromZ, double toX, double toY, double toZ)
    {
        return (toX - fromX, toY - fromY, toZ - fromZ);
    }

    /// <summary>Whether an offset is too small to be worth an override (below 1e-9 on every axis).</summary>
    /// <param name="dx">Offset along X.</param>
    /// <param name="dy">Offset along Y.</param>
    /// <param name="dz">Offset along Z.</param>
    /// <returns>True when the move is negligible.</returns>
    public static bool IsNegligibleMove(double dx, double dy, double dz)
    {
        return Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9 && Math.Abs(dz) < 1e-9;
    }

    /// <summary>Checks that a point has finite coordinates (NaN or infinity would poison every override).</summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <param name="z">Z coordinate.</param>
    /// <param name="what">What the point is, for the message ("scale centre", "target").</param>
    /// <exception cref="ArgumentException">A coordinate is NaN or infinite.</exception>
    public static void RequireFinitePoint(double x, double y, double z, string what)
    {
        if (double.IsNaN(x) || double.IsInfinity(x) ||
            double.IsNaN(y) || double.IsInfinity(y) ||
            double.IsNaN(z) || double.IsInfinity(z))
        {
            throw new ArgumentException(
                "The " + what + " has a coordinate that is not a finite number (" +
                x.ToString(CultureInfo.InvariantCulture) + ", " +
                y.ToString(CultureInfo.InvariantCulture) + ", " +
                z.ToString(CultureInfo.InvariantCulture) + ").");
        }
    }

    /// <summary>Applies a row-major affine matrix to a point (p' = M·p).</summary>
    /// <param name="matrix">16 numbers, row-major.</param>
    /// <param name="x">Point X.</param>
    /// <param name="y">Point Y.</param>
    /// <param name="z">Point Z.</param>
    /// <returns>The transformed point.</returns>
    public static (double X, double Y, double Z) TransformPoint(double[] matrix, double x, double y, double z)
    {
        if (matrix == null)
        {
            throw new ArgumentNullException(nameof(matrix));
        }

        if (matrix.Length != 16)
        {
            throw new ArgumentException("A matrix needs exactly 16 numbers (row-major 4x4).", nameof(matrix));
        }

        return (
            (matrix[0] * x) + (matrix[1] * y) + (matrix[2] * z) + matrix[3],
            (matrix[4] * x) + (matrix[5] * y) + (matrix[6] * z) + matrix[7],
            (matrix[8] * x) + (matrix[9] * y) + (matrix[10] * z) + matrix[11]);
    }
}
