using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Types;

namespace CamelGraph.Nodes;

/// <summary>
/// More point nodes over <see cref="CamelGraphPoint"/>: midpoint,
/// linear interpolation, the centroid of many points, plan (XY) distance and
/// rounding. Every node is pure and returns a new value.
/// </summary>
[NodeCategory("Geometry")]
public static class PointExtraNodes
{
    /// <summary>The point halfway between two points.</summary>
    /// <param name="a">The first point.</param>
    /// <param name="b">The second point.</param>
    /// <returns>The midpoint.</returns>
    [NodeName("Point.Midpoint")]
    [return: NodeName("point")]
    [NodeDescription("The point halfway between two points.")]
    [NodeSearchTags("middle", "center", "halfway", "average", "between")]
    public static CamelGraphPoint Midpoint(CamelGraphPoint a, CamelGraphPoint b)
    {
        RequireTwo(a, b, "Point.Midpoint");

        return new CamelGraphPoint((a.X + b.X) / 2d, (a.Y + b.Y) / 2d, (a.Z + b.Z) / 2d);
    }

    /// <summary>
    /// Linear interpolation between two points: t = 0 gives the first point, t = 1 the second, 0.5 the
    /// midpoint. The parameter is not clamped, so values below 0 or above 1 extend the line beyond the
    /// two points.
    /// </summary>
    /// <param name="a">The point at t = 0.</param>
    /// <param name="b">The point at t = 1.</param>
    /// <param name="t">The interpolation parameter (not clamped): 0 to 1 lies between the points, other values extend the line.</param>
    /// <returns>The interpolated point.</returns>
    [NodeName("Point.Lerp")]
    [return: NodeName("point")]
    [NodeDescription("Interpolates between two points: t = 0 is the first, t = 1 the second; t is not clamped, so other values extend the line beyond them.")]
    [NodeSearchTags("interpolate", "blend", "between", "fraction", "along", "mix", "extrapolate")]
    public static CamelGraphPoint Lerp(
        CamelGraphPoint a,
        CamelGraphPoint b,
        [NodeRange(-1000000000, 1000000000, SoftMin = 0, SoftMax = 1, Step = 0.05)] double t)
    {
        RequireTwo(a, b, "Point.Lerp");
        GeometryWarnings.NotFinite("Point.Lerp", "the point", ("t", t));

        // a*(1-t) + b*t is exact at both ends (t = 0 -> a, t = 1 -> b), unlike a + (b-a)*t.
        var s = 1d - t;
        return new CamelGraphPoint(a.X * s + b.X * t, a.Y * s + b.Y * t, a.Z * s + b.Z * t);
    }

    /// <summary>The centroid (coordinate-wise mean) of a list of points.</summary>
    /// <param name="points">The points to average (several wires can feed this one input); at least one, and every item must be a point.</param>
    /// <returns>The mean point.</returns>
    [NodeName("Point.Centroid")]
    [return: NodeName("point")]
    [NodeDescription("The centroid (average position) of a list of points; several wires can feed the one input. An empty list, a null or a non-point item is an error naming the item.")]
    [NodeSearchTags("average", "mean", "center", "centre", "middle", "barycenter", "cluster", "list")]
    public static CamelGraphPoint Centroid([MultiInput] IList<object?> points)
    {
        var list = ReadPoints(points, "Point.Centroid", nameof(points));

        double x = 0d, y = 0d, z = 0d;
        foreach (var point in list)
        {
            x += point.X;
            y += point.Y;
            z += point.Z;
        }

        var count = (double)list.Count;
        return new CamelGraphPoint(x / count, y / count, z / count);
    }

    /// <summary>Straight-line distance between two points in plan: the Z coordinates are ignored.</summary>
    /// <param name="a">The first point.</param>
    /// <param name="b">The second point.</param>
    /// <returns>The distance measured in the XY plane.</returns>
    [NodeName("Point.Distance2D")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("distance")]
    [NodeDescription("The distance between two points measured in plan (XY only, Z ignored) — for horizontal run lengths and offsets.")]
    [NodeSearchTags("plan", "horizontal", "xy", "flat", "length", "measure", "between", "ignore z")]
    public static double Distance2D(CamelGraphPoint a, CamelGraphPoint b)
    {
        RequireTwo(a, b, "Point.Distance2D");

        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Rounds all three coordinates of a point to a number of decimal digits.</summary>
    /// <param name="point">The point to round.</param>
    /// <param name="digits">Number of decimal digits to keep (0–15).</param>
    /// <returns>The rounded point.</returns>
    [NodeName("Point.Round")]
    [return: NodeName("point")]
    [NodeDescription("Rounds a point's X, Y and Z to the given number of decimal digits (midpoints round away from zero) — tidies float noise before comparing or grouping points.")]
    [NodeSearchTags("round", "snap", "tidy", "precision", "decimals", "clean")]
    public static CamelGraphPoint Round(CamelGraphPoint point, [NodeRange(0, 15)] int digits = 3)
    {
        if (point == null)
        {
            throw new ArgumentNullException(nameof(point), "Point.Round requires a point. Wire a point into the 'point' input.");
        }

        if (digits < 0 || digits > 15)
        {
            throw new ArgumentOutOfRangeException(
                nameof(digits),
                "Point.Round needs 0 to 15 decimal digits (got " + digits.ToString(CultureInfo.InvariantCulture) + ").");
        }

        return new CamelGraphPoint(RoundCoordinate(point.X, digits), RoundCoordinate(point.Y, digits), RoundCoordinate(point.Z, digits));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Reads a node's list input as points: each item must be a <see cref="CamelGraphPoint"/> (or something
    /// the registered converters turn into one). The error names the node and the zero-based item index.
    /// </summary>
    internal static List<CamelGraphPoint> ReadPoints(IList<object?>? items, string node, string parameter)
    {
        if (items == null)
        {
            throw new ArgumentNullException(parameter, node + " requires a list of points. Wire a list into the '" + parameter + "' input.");
        }

        if (items.Count == 0)
        {
            throw new ArgumentException(node + " needs at least one point, but the list is empty.", parameter);
        }

        var points = new List<CamelGraphPoint>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item is CamelGraphPoint point)
            {
                points.Add(point);
            }
            else if (item != null &&
                     TypeCoercion.TryCoerce(item, typeof(CamelGraphPoint), out var coerced) &&
                     coerced is CamelGraphPoint converted)
            {
                points.Add(converted);
            }
            else
            {
                throw new ArgumentException(
                    node + " expects only points, but item " + i.ToString(CultureInfo.InvariantCulture) +
                    " of the list (counting from 0) is " + Describe(item) + ". Remove it or replace it with a point.",
                    parameter);
            }
        }

        return points;
    }

    private static string Describe(object? item)
    {
        switch (item)
        {
            case null:
                return "null";
            case string text:
                return "the text \"" + text + "\"";
            case IList _:
                return "a list (flatten nested lists first, e.g. with List.Flatten)";
            default:
                return "a " + item.GetType().Name + " (" + TypeCoercion.FormatValue(item) + ")";
        }
    }

    private static void RequireTwo(CamelGraphPoint? a, CamelGraphPoint? b, string node)
    {
        if (a == null)
        {
            throw new ArgumentNullException(nameof(a), node + " requires two points. Wire a point into the 'a' input.");
        }

        if (b == null)
        {
            throw new ArgumentNullException(nameof(b), node + " requires two points. Wire a point into the 'b' input.");
        }
    }

    private static double RoundCoordinate(double value, int digits)
    {
        var rounded = Math.Round(value, digits, MidpointRounding.AwayFromZero);

        // Adding 0 turns a negative zero (-0.0004 rounded to 3 digits) into a plain 0.
        return rounded + 0d;
    }
}
