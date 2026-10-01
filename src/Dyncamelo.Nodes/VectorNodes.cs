using System;
using System.Collections.Generic;
using System.Globalization;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes;

/// <summary>
/// Vector arithmetic and tests over <see cref="DyncameloVector"/>: components,
/// add / subtract / scale / negate, length and normalisation, dot and cross
/// products, the angle between two vectors, the unit axes, and parallel /
/// perpendicular tests. Every node is pure and returns a new value.
/// </summary>
[NodeCategory("Geometry")]
public static class VectorNodes
{
    /// <summary>Decomposes a vector into its X, Y and Z components.</summary>
    /// <param name="vector">The vector to decompose.</param>
    /// <returns>Dictionary with "x", "y" and "z" values.</returns>
    [NodeName("Vector.Components")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [MultiReturn("x", "y", "z")]
    [PortKinds("number", "number", "number")]
    [NodeDescription("Splits a vector into its X, Y and Z components.")]
    [NodeSearchTags("deconstruct", "xyz", "coordinates", "components")]
    public static Dictionary<string, object> Components(DyncameloVector vector)
    {
        Require(vector, "Vector.Components", nameof(vector), "a vector");

        return new Dictionary<string, object>
        {
            ["x"] = vector.X,
            ["y"] = vector.Y,
            ["z"] = vector.Z,
        };
    }

    /// <summary>Adds two vectors component by component.</summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>The sum a + b.</returns>
    [NodeName("Vector.Add")]
    [return: NodeName("vector")]
    [NodeDescription("Adds two vectors (a + b).")]
    [NodeSearchTags("sum", "plus", "combine", "resultant")]
    public static DyncameloVector Add(DyncameloVector a, DyncameloVector b)
    {
        Require(a, "Vector.Add", nameof(a), "two vectors");
        Require(b, "Vector.Add", nameof(b), "two vectors");

        return new DyncameloVector(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    }

    /// <summary>Subtracts the second vector from the first, component by component.</summary>
    /// <param name="a">The vector to subtract from.</param>
    /// <param name="b">The vector to subtract.</param>
    /// <returns>The difference a - b.</returns>
    [NodeName("Vector.Subtract")]
    [return: NodeName("vector")]
    [NodeDescription("Subtracts one vector from another (a - b).")]
    [NodeSearchTags("minus", "difference", "delta")]
    public static DyncameloVector Subtract(DyncameloVector a, DyncameloVector b)
    {
        Require(a, "Vector.Subtract", nameof(a), "two vectors");
        Require(b, "Vector.Subtract", nameof(b), "two vectors");

        return new DyncameloVector(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }

    /// <summary>Multiplies every component of a vector by a number.</summary>
    /// <param name="vector">The vector to scale.</param>
    /// <param name="factor">The scale factor (2 = twice as long, -1 = reversed, 0 = the zero vector).</param>
    /// <returns>The scaled vector.</returns>
    [NodeName("Vector.Scale")]
    [return: NodeName("vector")]
    [NodeDescription("Multiplies a vector by a number (2 doubles its length, -1 reverses it).")]
    [NodeSearchTags("multiply", "stretch", "factor", "times", "resize")]
    public static DyncameloVector Scale(DyncameloVector vector, double factor)
    {
        Require(vector, "Vector.Scale", nameof(vector), "a vector");

        return new DyncameloVector(vector.X * factor, vector.Y * factor, vector.Z * factor);
    }

    /// <summary>Reverses a vector (every component changes sign).</summary>
    /// <param name="vector">The vector to reverse.</param>
    /// <returns>The vector pointing the opposite way.</returns>
    [NodeName("Vector.Negate")]
    [return: NodeName("vector")]
    [NodeDescription("Reverses a vector so it points the opposite way.")]
    [NodeSearchTags("reverse", "flip", "opposite", "invert", "minus")]
    public static DyncameloVector Negate(DyncameloVector vector)
    {
        Require(vector, "Vector.Negate", nameof(vector), "a vector");

        // 0 - x (rather than -x) keeps a zero component +0 instead of -0.
        return new DyncameloVector(0d - vector.X, 0d - vector.Y, 0d - vector.Z);
    }

    /// <summary>The length (magnitude) of a vector.</summary>
    /// <param name="vector">The vector to measure.</param>
    /// <returns>The Euclidean length.</returns>
    [NodeName("Vector.Length")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("length")]
    [NodeDescription("Returns the length (magnitude) of a vector.")]
    [NodeSearchTags("magnitude", "norm", "size", "measure")]
    public static double Length(DyncameloVector vector)
    {
        Require(vector, "Vector.Length", nameof(vector), "a vector");

        return vector.Length;
    }

    /// <summary>Scales a vector to length 1 while keeping its direction.</summary>
    /// <param name="vector">The vector to normalise; it must not have zero length.</param>
    /// <returns>The unit vector with the same direction.</returns>
    [NodeName("Vector.Normalize")]
    [return: NodeName("vector")]
    [NodeDescription("Scales a vector to length 1, keeping its direction (a zero-length vector has no direction and is an error).")]
    [NodeSearchTags("unit", "normalise", "normalize", "direction", "unitize")]
    public static DyncameloVector Normalize(DyncameloVector vector)
    {
        Require(vector, "Vector.Normalize", nameof(vector), "a vector");
        var length = RequireDirection(vector, "Vector.Normalize", nameof(vector));

        return new DyncameloVector(vector.X / length, vector.Y / length, vector.Z / length);
    }

    /// <summary>
    /// The dot (scalar) product of two vectors: positive when they point roughly the same way, 0 when
    /// perpendicular, negative when they point roughly opposite ways.
    /// </summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>a · b.</returns>
    [NodeName("Vector.Dot")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("dot")]
    [NodeDescription("The dot product of two vectors: positive when they point the same way, 0 when perpendicular, negative when opposed.")]
    [NodeSearchTags("scalar", "inner", "product", "projection", "multiply")]
    public static double Dot(DyncameloVector a, DyncameloVector b)
    {
        Require(a, "Vector.Dot", nameof(a), "two vectors");
        Require(b, "Vector.Dot", nameof(b), "two vectors");

        return DotProduct(a, b);
    }

    /// <summary>
    /// The cross (vector) product of two vectors: a vector perpendicular to both, following the
    /// right-hand rule; the zero vector when they are parallel.
    /// </summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>a × b.</returns>
    [NodeName("Vector.Cross")]
    [return: NodeName("vector")]
    [NodeDescription("The cross product of two vectors: a vector perpendicular to both (right-hand rule), zero when they are parallel.")]
    [NodeSearchTags("perpendicular", "normal", "orthogonal", "product", "right hand")]
    public static DyncameloVector Cross(DyncameloVector a, DyncameloVector b)
    {
        Require(a, "Vector.Cross", nameof(a), "two vectors");
        Require(b, "Vector.Cross", nameof(b), "two vectors");

        return CrossProduct(a, b);
    }

    /// <summary>
    /// The angle between two vectors in degrees, from 0 (same direction) to 180 (opposite directions).
    /// It is unsigned: it does not say which way to turn.
    /// </summary>
    /// <param name="a">The first vector; it must not have zero length.</param>
    /// <param name="b">The second vector; it must not have zero length.</param>
    /// <returns>The angle in degrees, 0 to 180.</returns>
    [NodeName("Vector.Angle")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("degrees")]
    [NodeDescription("The angle between two vectors in degrees, 0 (same direction) to 180 (opposite); a zero-length vector has no direction and is an error.")]
    [NodeSearchTags("between", "degrees", "bearing", "deviation", "measure")]
    public static double Angle(DyncameloVector a, DyncameloVector b)
    {
        Require(a, "Vector.Angle", nameof(a), "two vectors");
        Require(b, "Vector.Angle", nameof(b), "two vectors");
        RequireDirection(a, "Vector.Angle", nameof(a));
        RequireDirection(b, "Vector.Angle", nameof(b));

        // atan2(|a x b|, a . b) stays accurate for nearly parallel vectors, where acos of the cosine does not.
        var radians = Math.Atan2(CrossProduct(a, b).Length, DotProduct(a, b));
        return radians * (180d / Math.PI);
    }

    /// <summary>The vector that leads from one point to another (to minus from).</summary>
    /// <param name="from">The start point.</param>
    /// <param name="to">The end point.</param>
    /// <returns>The vector from the start point to the end point.</returns>
    [NodeName("Vector.ByPoints")]
    [return: NodeName("vector")]
    [NodeDescription("Creates the vector that leads from one point to another (end minus start).")]
    [NodeSearchTags("between", "direction", "from", "to", "two points", "displacement")]
    public static DyncameloVector ByPoints(DyncameloPoint from, DyncameloPoint to)
    {
        if (from == null)
        {
            throw new ArgumentNullException(nameof(from), "Vector.ByPoints requires a start and an end point. Wire a point into the 'from' input.");
        }

        if (to == null)
        {
            throw new ArgumentNullException(nameof(to), "Vector.ByPoints requires a start and an end point. Wire a point into the 'to' input.");
        }

        return new DyncameloVector(to.X - from.X, to.Y - from.Y, to.Z - from.Z);
    }

    /// <summary>The unit vector along the X axis, (1, 0, 0).</summary>
    /// <returns>The X axis direction.</returns>
    [NodeName("Vector.XAxis")]
    [return: NodeName("vector")]
    [NodeDescription("The unit vector along the X axis, (1, 0, 0).")]
    [NodeSearchTags("axis", "unit", "east", "right", "direction")]
    public static DyncameloVector XAxis()
    {
        return new DyncameloVector(1d, 0d, 0d);
    }

    /// <summary>The unit vector along the Y axis, (0, 1, 0).</summary>
    /// <returns>The Y axis direction.</returns>
    [NodeName("Vector.YAxis")]
    [return: NodeName("vector")]
    [NodeDescription("The unit vector along the Y axis, (0, 1, 0).")]
    [NodeSearchTags("axis", "unit", "north", "forward", "direction")]
    public static DyncameloVector YAxis()
    {
        return new DyncameloVector(0d, 1d, 0d);
    }

    /// <summary>The unit vector along the Z axis, (0, 0, 1).</summary>
    /// <returns>The Z axis direction.</returns>
    [NodeName("Vector.ZAxis")]
    [return: NodeName("vector")]
    [NodeDescription("The unit vector along the Z axis, (0, 0, 1) — straight up.")]
    [NodeSearchTags("axis", "unit", "up", "vertical", "direction")]
    public static DyncameloVector ZAxis()
    {
        return new DyncameloVector(0d, 0d, 1d);
    }

    /// <summary>
    /// Tests whether two vectors lie along the same line, pointing the same way or opposite ways. The
    /// tolerance is dimensionless: the sine of the angle between them may not exceed it, so the test does
    /// not depend on how long the vectors are.
    /// </summary>
    /// <param name="a">The first vector; it must not have zero length.</param>
    /// <param name="b">The second vector; it must not have zero length.</param>
    /// <param name="tolerance">Largest allowed sine of the angle between them (0 = exactly parallel).</param>
    /// <returns>True when the vectors are parallel or anti-parallel within the tolerance.</returns>
    [NodeName("Vector.IsParallel")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("isParallel")]
    [NodeDescription("True when two vectors lie along the same line (same or opposite direction) within a length-independent tolerance; zero-length vectors are an error.")]
    [NodeSearchTags("parallel", "collinear", "same direction", "aligned", "test")]
    public static bool IsParallel(DyncameloVector a, DyncameloVector b, double tolerance = 1e-9)
    {
        Require(a, "Vector.IsParallel", nameof(a), "two vectors");
        Require(b, "Vector.IsParallel", nameof(b), "two vectors");
        RequireTolerance(tolerance, "Vector.IsParallel");
        var lengthA = RequireDirection(a, "Vector.IsParallel", nameof(a));
        var lengthB = RequireDirection(b, "Vector.IsParallel", nameof(b));

        return CrossProduct(a, b).Length <= tolerance * lengthA * lengthB;
    }

    /// <summary>
    /// Tests whether two vectors meet at a right angle. The tolerance is dimensionless: the cosine of the
    /// angle between them may not exceed it, so the test does not depend on how long the vectors are.
    /// </summary>
    /// <param name="a">The first vector; it must not have zero length.</param>
    /// <param name="b">The second vector; it must not have zero length.</param>
    /// <param name="tolerance">Largest allowed cosine of the angle between them (0 = exactly perpendicular).</param>
    /// <returns>True when the vectors are perpendicular within the tolerance.</returns>
    [NodeName("Vector.IsPerpendicular")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("isPerpendicular")]
    [NodeDescription("True when two vectors meet at a right angle within a length-independent tolerance; zero-length vectors are an error.")]
    [NodeSearchTags("perpendicular", "orthogonal", "right angle", "normal", "square", "test")]
    public static bool IsPerpendicular(DyncameloVector a, DyncameloVector b, double tolerance = 1e-9)
    {
        Require(a, "Vector.IsPerpendicular", nameof(a), "two vectors");
        Require(b, "Vector.IsPerpendicular", nameof(b), "two vectors");
        RequireTolerance(tolerance, "Vector.IsPerpendicular");
        var lengthA = RequireDirection(a, "Vector.IsPerpendicular", nameof(a));
        var lengthB = RequireDirection(b, "Vector.IsPerpendicular", nameof(b));

        return Math.Abs(DotProduct(a, b)) <= tolerance * lengthA * lengthB;
    }

    // ------------------------------------------------------------------ helpers

    private static double DotProduct(DyncameloVector a, DyncameloVector b)
    {
        return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }

    private static DyncameloVector CrossProduct(DyncameloVector a, DyncameloVector b)
    {
        return new DyncameloVector(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);
    }

    private static void Require(DyncameloVector? vector, string node, string parameter, string what)
    {
        if (vector == null)
        {
            throw new ArgumentNullException(parameter, node + " requires " + what + ". Wire a vector into the '" + parameter + "' input.");
        }
    }

    /// <summary>Returns the vector's length, or throws when it has no usable direction (zero or non-finite length).</summary>
    private static double RequireDirection(DyncameloVector vector, string node, string parameter)
    {
        var length = vector.Length;
        if (length > 0d && !double.IsInfinity(length))
        {
            return length;
        }

        var reason = double.IsNaN(length)
            ? "has a component that is not a number"
            : (double.IsInfinity(length) ? "is too large to measure" : "has zero length, so it has no direction");
        throw new ArgumentException(
            node + " cannot use the vector wired into '" + parameter + "': it " + reason +
            " (" + vector + "). Wire a vector with a finite, non-zero length.",
            parameter);
    }

    private static void RequireTolerance(double tolerance, string node)
    {
        if (double.IsNaN(tolerance) || tolerance < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tolerance),
                node + " needs a tolerance of 0 or more (got " + tolerance.ToString(CultureInfo.InvariantCulture) + ").");
        }
    }
}
