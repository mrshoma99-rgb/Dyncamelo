using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Navisworks.Api;

namespace Dyncamelo.Navisworks.Internal;

/// <summary>
/// Transform3D construction and composition helpers for the Navisworks.Transform
/// nodes. Internal — never surfaced as nodes.
///
/// Conventions owned here:
/// - All lengths are in DOCUMENT units (nodes convert user units before calling).
/// - Angles are DEGREES at the helper boundary (matching node ports), radians
///   inside the Navisworks API.
/// - Matrices cross the node boundary as 16 doubles, row-major, acting on column
///   vectors (p' = M·p): linear part in rows 0-2 / columns 0-2, translation in
///   the last column (indices 3, 7, 11), bottom row 0 0 0 1. Only affine
///   matrices are supported; <see cref="FromRowMajorMatrix"/> and
///   <see cref="ToRowMajorMatrix"/> are exact inverses of each other.
/// - The 2024 API has no temporary transform: overrides applied with
///   <c>Document.Models.OverridePermanentTransform</c> are permanent (undoable,
///   saved in NWF) — node docs must say so.
///
/// RUNTIME-CHECK (v0.3 Windows smoke item 11 — vector convention): everything
/// here assumes Transform3D acts on COLUMN vectors (p' = M·p). If Navisworks
/// turns out to be row-vector (p' = p·M — plausible: the COM layer stores
/// translation at elements 12–14 and the API exposes Transform3D.TranslateRight),
/// then RotationAboutAxis rotates about the NEGATED point, Compose applies in
/// reversed order, and the matrix I/O rotation part is transposed. The fix in
/// that case: swap the Multiply operand order in Compose / ComposeWithOverride /
/// RotationAboutAxis and transpose the linear 3×3 in From/ToRowMajorMatrix
/// (translation stays Transform3D.Translation). Verify by rotating a known item
/// 90° about a non-origin point and round-tripping GetTransform→SetTransform.
/// </summary>
internal static class TransformHelpers
{
    private const double AffineTolerance = 1e-9;

    // -------------------------------------------------------------- Angles

    /// <summary>Degrees → radians.</summary>
    internal static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    /// <summary>Radians → degrees.</summary>
    internal static double RadiansToDegrees(double radians)
    {
        return radians * 180.0 / Math.PI;
    }

    // -------------------------------------------------------- Construction

    /// <summary>A pure translation by <paramref name="offset"/> (document units).</summary>
    internal static Transform3D Translation(Vector3D offset)
    {
        if (offset == null)
        {
            throw new ArgumentNullException(nameof(offset), "No translation vector provided.");
        }

        return Transform3D.CreateTranslation(offset);
    }

    /// <summary>
    /// Rotation by <paramref name="angleDegrees"/> about the axis through
    /// <paramref name="origin"/> along <paramref name="axis"/>, composed as
    /// T(origin) · R · T(−origin).
    /// </summary>
    internal static Transform3D RotationAboutAxis(Point3D origin, Vector3D axis, double angleDegrees)
    {
        if (origin == null)
        {
            throw new ArgumentNullException(nameof(origin), "No rotation origin provided.");
        }

        var rotation = new Transform3D(RotationFromAxisAngle(axis, angleDegrees));
        var toOrigin = Transform3D.CreateTranslation(Point3D.Origin.Subtract(origin));
        var back = Transform3D.CreateTranslation(origin.ToVector3D());
        return Transform3D.Multiply(Transform3D.Multiply(back, rotation), toOrigin);
    }

    /// <summary>
    /// A <see cref="Rotation3D"/> from an axis vector (normalized here — need
    /// not be unit length) and an angle in degrees.
    /// </summary>
    internal static Rotation3D RotationFromAxisAngle(Vector3D axis, double angleDegrees)
    {
        if (axis == null)
        {
            throw new ArgumentNullException(nameof(axis), "No rotation axis provided.");
        }

        if (axis.Length < AffineTolerance)
        {
            throw new ArgumentException(
                "The rotation axis must be a non-zero vector — (0, 0, 0) does not define an axis.", nameof(axis));
        }

        return new Rotation3D(new UnitVector3D(axis), DegreesToRadians(angleDegrees));
    }

    // --------------------------------------------------------- Composition

    /// <summary>
    /// Composes two transforms: the result applies <paramref name="first"/>,
    /// then <paramref name="second"/> (matrix product second · first).
    /// </summary>
    internal static Transform3D Compose(Transform3D second, Transform3D first)
    {
        if (second == null)
        {
            throw new ArgumentNullException(nameof(second));
        }

        if (first == null)
        {
            throw new ArgumentNullException(nameof(first));
        }

        return Transform3D.Multiply(second, first);
    }

    /// <summary>
    /// The item's current permanent override transform, or identity when the
    /// item has no geometry / no override. Never null.
    /// </summary>
    internal static Transform3D CurrentOverride(ModelItem item)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item), "No model item provided.");
        }

        var geometry = item.Geometry;
        return geometry?.PermanentOverrideTransform ?? Transform3D.CreateIdentity();
    }

    /// <summary>
    /// Composes a new delta onto an item's existing permanent override:
    /// the returned transform applies the current override first, then
    /// <paramref name="delta"/> — pass it to
    /// <c>Document.Models.OverridePermanentTransform</c> so repeated node runs
    /// accumulate instead of silently replacing earlier moves.
    /// </summary>
    internal static Transform3D ComposeWithOverride(ModelItem item, Transform3D delta)
    {
        if (delta == null)
        {
            throw new ArgumentNullException(nameof(delta), "No transform provided.");
        }

        return Transform3D.Multiply(delta, CurrentOverride(item));
    }

    // ------------------------------------------------------------ Matrices

    /// <summary>
    /// Builds a Transform3D from 16 row-major doubles (see class conventions).
    /// The matrix must be affine: bottom row 0 0 0 1.
    /// </summary>
    internal static Transform3D FromRowMajorMatrix(IList<double> matrix)
    {
        if (matrix == null)
        {
            throw new ArgumentNullException(nameof(matrix), "No matrix provided.");
        }

        if (matrix.Count != 16)
        {
            throw new ArgumentException(
                "A transform matrix needs exactly 16 numbers (row-major 4×4); got " +
                matrix.Count.ToString(CultureInfo.InvariantCulture) + ".", nameof(matrix));
        }

        if (Math.Abs(matrix[12]) > AffineTolerance ||
            Math.Abs(matrix[13]) > AffineTolerance ||
            Math.Abs(matrix[14]) > AffineTolerance ||
            Math.Abs(matrix[15] - 1.0) > AffineTolerance)
        {
            throw new ArgumentException(
                "The matrix is not affine — the bottom row must be 0, 0, 0, 1 " +
                "(perspective/projective transforms cannot be applied to model items).", nameof(matrix));
        }

        var linear = new Matrix3(
            matrix[0], matrix[1], matrix[2],
            matrix[4], matrix[5], matrix[6],
            matrix[8], matrix[9], matrix[10]);
        var translation = new Vector3D(matrix[3], matrix[7], matrix[11]);
        return new Transform3D(linear, translation);
    }

    /// <summary>
    /// A loosely-typed port value (list of 16 numbers, flat or as four rows of
    /// four) to a Transform3D — the ModelItem.SetTransform input shape.
    /// </summary>
    internal static Transform3D FromMatrixValue(object? value)
    {
        switch (value)
        {
            case null:
                throw new ArgumentNullException(nameof(value), "No matrix provided.");
            case Transform3D transform:
                return transform;
            case IList list when !(value is string):
                return FromRowMajorMatrix(FlattenNumbers(list));
            default:
                throw new ArgumentException(
                    "Cannot interpret a value of type '" + value.GetType().Name +
                    "' as a transform matrix. Wire a list of 16 numbers (row-major) or four rows of four numbers.");
        }
    }

    /// <summary>The 16 row-major doubles of a transform (see class conventions).</summary>
    internal static List<double> ToRowMajorMatrix(Transform3D transform)
    {
        if (transform == null)
        {
            throw new ArgumentNullException(nameof(transform), "No transform provided.");
        }

        var linear = transform.Linear;
        var translation = transform.Translation;
        return new List<double>
        {
            linear.Get(0, 0), linear.Get(0, 1), linear.Get(0, 2), translation.X,
            linear.Get(1, 0), linear.Get(1, 1), linear.Get(1, 2), translation.Y,
            linear.Get(2, 0), linear.Get(2, 1), linear.Get(2, 2), translation.Z,
            0.0, 0.0, 0.0, 1.0,
        };
    }

    private static List<double> FlattenNumbers(IList list)
    {
        var numbers = new List<double>(16);
        foreach (var element in list)
        {
            if (element is IList nested && !(element is string))
            {
                foreach (var inner in nested)
                {
                    numbers.Add(ToDouble(inner));
                }
            }
            else
            {
                numbers.Add(ToDouble(element));
            }
        }

        return numbers;
    }

    private static double ToDouble(object? value)
    {
        if (value == null)
        {
            throw new ArgumentException("Matrix entries must be numbers; got an empty value.");
        }

        try
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            throw new ArgumentException(
                "Matrix entries must be numbers; '" + value + "' (" + value.GetType().Name + ") is not.");
        }
    }
}
