using System;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes.Spatial;

/// <summary>
/// Box-shape classification for clash-orientation nodes: an axis-aligned
/// bounding box reads as one of five shapes — "slab" (thin and horizontal),
/// "wall" (thin and upright), "riser" (long and vertical), "run" (long and
/// horizontal) or "block" (none dominant) — which is what tells a
/// pipe-through-wall clash apart from a pipe-through-floor one when the
/// crossing angle alone is 90° in both cases. Box-based on purpose (cheap,
/// works on every item); the honest limitation is that a strongly diagonal
/// linear element has a thin diagonal-plane box and can read as planar.
/// Pure math — fully unit-testable.
/// </summary>
[IsVisibleInLibrary(false)]
public static class BoxOrientation
{
    /// <summary>A box is planar when its smallest extent is at most this fraction of the middle one.</summary>
    public const double PlanarRatio = 0.25;

    /// <summary>A box is linear when its largest extent is at least this multiple of the middle one.</summary>
    public const double LinearRatio = 2.0;

    /// <summary>
    /// Classifies box extents (dx, dy, dz) as "slab", "wall", "riser", "run"
    /// or "block". Planar wins over linear: thin boxes are plates first.
    /// </summary>
    public static string Classify(double dx, double dy, double dz)
    {
        dx = Math.Abs(dx);
        dy = Math.Abs(dy);
        dz = Math.Abs(dz);

        var small = Math.Min(dx, Math.Min(dy, dz));
        var large = Math.Max(dx, Math.Max(dy, dz));
        var mid = dx + dy + dz - small - large;

        if (large <= 0)
        {
            return "block"; // a point — nothing to classify
        }

        if (mid <= 0)
        {
            // A degenerate line only has ONE real extent — classify as linear.
            return LargestAxis(dx, dy, dz, large) == 'z' ? "riser" : "run";
        }

        if (small <= PlanarRatio * mid)
        {
            return SmallestAxis(dx, dy, dz, small) == 'z' ? "slab" : "wall";
        }

        if (large >= LinearRatio * mid)
        {
            return LargestAxis(dx, dy, dz, large) == 'z' ? "riser" : "run";
        }

        return "block";
    }

    /// <summary>
    /// The slope of a direction (dx, dy, dz) measured from the horizontal
    /// plane: 0 = horizontal, 90 = vertical. NaN for a zero-length direction.
    /// </summary>
    public static double SlopeDegrees(double dx, double dy, double dz)
    {
        var length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (length < 1e-12)
        {
            return double.NaN;
        }

        var sine = Math.Min(1.0, Math.Abs(dz) / length);
        return Math.Asin(sine) * (180.0 / Math.PI);
    }

    /// <summary>Whether an unordered shape pair matches an unordered filter pair ("any" matches everything).</summary>
    public static bool PairMatches(string shapeA, string shapeB, string filterA, string filterB)
    {
        return (Matches(shapeA, filterA) && Matches(shapeB, filterB)) ||
               (Matches(shapeA, filterB) && Matches(shapeB, filterA));
    }

    private static bool Matches(string shape, string filter)
    {
        return string.IsNullOrEmpty(filter) ||
               string.Equals(filter, "any", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(shape, filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The first axis (x → y → z) whose extent equals the smallest value.</summary>
    private static char SmallestAxis(double dx, double dy, double dz, double value)
    {
        if (dx == value)
        {
            return 'x';
        }

        return dy == value ? 'y' : 'z';
    }

    /// <summary>The last axis (x → y → z) whose extent equals the largest value — a vertical tie prefers z.</summary>
    private static char LargestAxis(double dx, double dy, double dz, double value)
    {
        if (dz == value)
        {
            return 'z';
        }

        return dy == value ? 'y' : 'x';
    }
}
