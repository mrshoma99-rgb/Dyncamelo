using System;
using System.Collections.Generic;
using System.Windows;

namespace CamelGraph.UI.Services;

/// <summary>The shape of a wire: the Bézier the row-layout wires draw, shared by drawing and hit-testing.</summary>
public static class WireGeometry
{
    /// <summary>Bézier control points for a wire from <paramref name="source"/> to <paramref name="target"/>.</summary>
    public static (Point P1, Point P2) ControlPoints(Point source, Point target)
    {
        var dx = Math.Abs(target.X - source.X);
        var offset = Math.Max(40d, Math.Min(dx * 0.5d + 20d, 200d));
        return (new Point(source.X + offset, source.Y), new Point(target.X - offset, target.Y));
    }

    /// <summary>Point of the cubic at parameter <paramref name="t"/>.</summary>
    public static Point Evaluate(Point p0, Point p1, Point p2, Point p3, double t)
    {
        var u = 1d - t;
        var b0 = u * u * u;
        var b1 = 3d * u * u * t;
        var b2 = 3d * u * t * t;
        var b3 = t * t * t;
        return new Point(
            b0 * p0.X + b1 * p1.X + b2 * p2.X + b3 * p3.X,
            b0 * p0.Y + b1 * p1.Y + b2 * p2.Y + b3 * p3.Y);
    }

    /// <summary>Evenly spaced points along the wire (both endpoints included).</summary>
    /// <param name="source">Start of the wire.</param>
    /// <param name="target">End of the wire.</param>
    /// <param name="count">Number of points, at least 2.</param>
    public static IReadOnlyList<Point> Sample(Point source, Point target, int count = 32)
    {
        count = Math.Max(2, count);
        var (p1, p2) = ControlPoints(source, target);
        var points = new List<Point>(count);
        for (var i = 0; i < count; i++)
        {
            points.Add(Evaluate(source, p1, p2, target, i / (double)(count - 1)));
        }

        return points;
    }
}
