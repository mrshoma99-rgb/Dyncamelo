using System;
using System.Collections.Generic;
using System.Globalization;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Types;

namespace Dyncamelo.Nodes;

/// <summary>
/// More bounding-box nodes over <see cref="DyncameloBoundingBox"/>: volume,
/// areas, corners, expand / translate, overlap, containment tests and a box
/// around many points. Every node is pure and returns a new value.
/// </summary>
[NodeCategory("Geometry")]
public static class BoundingBoxExtraNodes
{
    /// <summary>The volume of a bounding box (size X times size Y times size Z).</summary>
    /// <param name="box">The bounding box.</param>
    /// <returns>The volume, in cubic model units.</returns>
    [NodeName("BoundingBox.Volume")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("volume")]
    [NodeDescription("The volume of a bounding box (cubic model units); a flat box has volume 0.")]
    [NodeSearchTags("size", "cubic", "capacity", "space", "measure", "m3")]
    public static double Volume(DyncameloBoundingBox box)
    {
        Require(box, "BoundingBox.Volume", nameof(box));

        return SizeX(box) * SizeY(box) * SizeZ(box);
    }

    /// <summary>The total area of the six faces of a bounding box.</summary>
    /// <param name="box">The bounding box.</param>
    /// <returns>The surface area, in square model units.</returns>
    [NodeName("BoundingBox.SurfaceArea")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("area")]
    [NodeDescription("The total area of a bounding box's six faces (square model units) — e.g. for a coating or cladding estimate.")]
    [NodeSearchTags("area", "faces", "skin", "paint", "coating", "cladding", "measure", "m2")]
    public static double SurfaceArea(DyncameloBoundingBox box)
    {
        Require(box, "BoundingBox.SurfaceArea", nameof(box));

        var x = SizeX(box);
        var y = SizeY(box);
        var z = SizeZ(box);
        return 2d * (x * y + y * z + z * x);
    }

    /// <summary>The plan (floor) area of a bounding box: size X times size Y.</summary>
    /// <param name="box">The bounding box.</param>
    /// <returns>The footprint area, in square model units.</returns>
    [NodeName("BoundingBox.Footprint")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("area")]
    [NodeDescription("The plan (floor) area of a bounding box: size X times size Y, ignoring height.")]
    [NodeSearchTags("plan", "floor", "area", "base", "ground", "xy", "measure", "m2")]
    public static double Footprint(DyncameloBoundingBox box)
    {
        Require(box, "BoundingBox.Footprint", nameof(box));

        return SizeX(box) * SizeY(box);
    }

    /// <summary>
    /// The eight corner points of a bounding box. Order: the bottom face (at the box's minimum Z) counter-
    /// clockwise seen from above, starting at the Min corner: (minX, minY), (maxX, minY), (maxX, maxY),
    /// (minX, maxY); then the top face (at the maximum Z) in the same order.
    /// </summary>
    /// <param name="box">The bounding box.</param>
    /// <returns>The eight corners: indices 0-3 bottom face, 4-7 top face.</returns>
    [NodeName("BoundingBox.Corners")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("corners")]
    [NodeDescription("The 8 corner points of a bounding box: the bottom face counter-clockwise from the Min corner (0-3), then the top face in the same order (4-7).")]
    [NodeSearchTags("vertices", "points", "extremes", "eight", "decompose", "explode")]
    public static List<DyncameloPoint> Corners(DyncameloBoundingBox box)
    {
        Require(box, "BoundingBox.Corners", nameof(box));

        var min = box.Min;
        var max = box.Max;
        return new List<DyncameloPoint>(8)
        {
            new DyncameloPoint(min.X, min.Y, min.Z),
            new DyncameloPoint(max.X, min.Y, min.Z),
            new DyncameloPoint(max.X, max.Y, min.Z),
            new DyncameloPoint(min.X, max.Y, min.Z),
            new DyncameloPoint(min.X, min.Y, max.Z),
            new DyncameloPoint(max.X, min.Y, max.Z),
            new DyncameloPoint(max.X, max.Y, max.Z),
            new DyncameloPoint(min.X, max.Y, max.Z),
        };
    }

    /// <summary>
    /// Grows a bounding box by an amount on every side (the size increases by twice the amount along each
    /// axis); a negative amount shrinks it. Shrinking by more than half the smallest size is an error.
    /// </summary>
    /// <param name="box">The bounding box.</param>
    /// <param name="amount">Distance to move every face outwards (negative moves them inwards).</param>
    /// <returns>The expanded bounding box.</returns>
    [NodeName("BoundingBox.Expand")]
    [return: NodeName("boundingBox")]
    [NodeDescription("Grows a bounding box by an amount on every side (negative shrinks it; shrinking past zero size is an error) — a clearance or tolerance zone around an element.")]
    [NodeSearchTags("grow", "inflate", "pad", "offset", "margin", "clearance", "buffer", "shrink", "tolerance")]
    public static DyncameloBoundingBox Expand(DyncameloBoundingBox box, double amount)
    {
        Require(box, "BoundingBox.Expand", nameof(box));
        if (double.IsNaN(amount) || double.IsInfinity(amount))
        {
            throw new ArgumentException("BoundingBox.Expand needs a finite amount (got " + Format(amount) + ").", nameof(amount));
        }

        var min = new DyncameloPoint(box.Min.X - amount, box.Min.Y - amount, box.Min.Z - amount);
        var max = new DyncameloPoint(box.Max.X + amount, box.Max.Y + amount, box.Max.Z + amount);

        // Shrinking moves every face inwards; once opposite faces cross, the box has passed zero size.
        var collapsed = new List<string>();
        AddIfCollapsed(collapsed, "X", min.X, max.X, SizeX(box));
        AddIfCollapsed(collapsed, "Y", min.Y, max.Y, SizeY(box));
        AddIfCollapsed(collapsed, "Z", min.Z, max.Z, SizeZ(box));
        if (collapsed.Count > 0)
        {
            throw new ArgumentException(
                "BoundingBox.Expand cannot shrink the box by " + Format(-amount) + " on every side: it would pass zero size along " +
                string.Join(", ", collapsed) + ". Use a smaller negative amount.",
                nameof(amount));
        }

        return new DyncameloBoundingBox(min, max);
    }

    /// <summary>
    /// The box where two bounding boxes overlap. Boxes that only touch give a flat (or line, or point)
    /// box, consistent with BoundingBox.Intersects. Boxes that do not meet at all are an error; test them
    /// with BoundingBox.Intersects first.
    /// </summary>
    /// <param name="a">The first bounding box.</param>
    /// <param name="b">The second bounding box.</param>
    /// <returns>The intersection box.</returns>
    [NodeName("BoundingBox.Overlap")]
    [return: NodeName("boundingBox")]
    [NodeDescription("The bounding box shared by two overlapping boxes; an error when they do not meet, so test with BoundingBox.Intersects first.")]
    [NodeSearchTags("intersection", "intersect", "common", "shared", "clash", "interference", "volume")]
    public static DyncameloBoundingBox Overlap(DyncameloBoundingBox a, DyncameloBoundingBox b)
    {
        Require(a, "BoundingBox.Overlap", nameof(a));
        Require(b, "BoundingBox.Overlap", nameof(b));

        var minX = Math.Max(a.Min.X, b.Min.X);
        var minY = Math.Max(a.Min.Y, b.Min.Y);
        var minZ = Math.Max(a.Min.Z, b.Min.Z);
        var maxX = Math.Min(a.Max.X, b.Max.X);
        var maxY = Math.Min(a.Max.Y, b.Max.Y);
        var maxZ = Math.Min(a.Max.Z, b.Max.Z);

        if (minX > maxX || minY > maxY || minZ > maxZ)
        {
            throw new InvalidOperationException(
                "BoundingBox.Overlap: the two boxes do not overlap, so there is no shared box. " +
                "Use BoundingBox.Intersects to test for an overlap first.");
        }

        return new DyncameloBoundingBox(new DyncameloPoint(minX, minY, minZ), new DyncameloPoint(maxX, maxY, maxZ));
    }

    /// <summary>Tests whether a point lies inside a bounding box; points on a face, edge or corner count as inside.</summary>
    /// <param name="box">The bounding box.</param>
    /// <param name="point">The point to test.</param>
    /// <returns>True when the point is inside or on the box.</returns>
    [NodeName("BoundingBox.ContainsPoint")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("contains")]
    [NodeDescription("True when a point lies inside a bounding box (the boundary counts as inside).")]
    [NodeSearchTags("inside", "within", "point in box", "zone", "test", "containment")]
    public static bool ContainsPoint(DyncameloBoundingBox box, DyncameloPoint point)
    {
        Require(box, "BoundingBox.ContainsPoint", nameof(box));
        if (point == null)
        {
            throw new ArgumentNullException(nameof(point), "BoundingBox.ContainsPoint requires a point to test. Wire a point into the 'point' input.");
        }

        return box.Min.X <= point.X && point.X <= box.Max.X &&
               box.Min.Y <= point.Y && point.Y <= box.Max.Y &&
               box.Min.Z <= point.Z && point.Z <= box.Max.Z;
    }

    /// <summary>Tests whether the inner box lies entirely inside the outer box; touching faces count as inside.</summary>
    /// <param name="outer">The box that should hold the other.</param>
    /// <param name="inner">The box that should fit inside.</param>
    /// <returns>True when every part of the inner box is inside or on the outer box.</returns>
    [NodeName("BoundingBox.ContainsBox")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("contains")]
    [NodeDescription("True when the inner box fits entirely inside the outer box (touching faces count as inside).")]
    [NodeSearchTags("inside", "within", "fits", "enclosed", "enclose", "zone", "test", "containment")]
    public static bool ContainsBox(DyncameloBoundingBox outer, DyncameloBoundingBox inner)
    {
        if (outer == null)
        {
            throw new ArgumentNullException(nameof(outer), "BoundingBox.ContainsBox requires the outer box. Wire a bounding box into the 'outer' input.");
        }

        if (inner == null)
        {
            throw new ArgumentNullException(nameof(inner), "BoundingBox.ContainsBox requires the inner box. Wire a bounding box into the 'inner' input.");
        }

        return outer.Min.X <= inner.Min.X && inner.Max.X <= outer.Max.X &&
               outer.Min.Y <= inner.Min.Y && inner.Max.Y <= outer.Max.Y &&
               outer.Min.Z <= inner.Min.Z && inner.Max.Z <= outer.Max.Z;
    }

    /// <summary>
    /// The smallest bounding box around a set of points. Several wires can feed the one input; a single
    /// point gives a zero-size box.
    /// </summary>
    /// <param name="points">The points to enclose (at least one; every item must be a point).</param>
    /// <returns>The bounding box that just contains all the points.</returns>
    [NodeName("BoundingBox.FromPoints")]
    [return: NodeName("boundingBox")]
    [NodeDescription("The smallest bounding box around a set of points (several wires can feed one input; a single point gives a zero-size box).")]
    [NodeSearchTags("fit", "enclose", "extents", "around", "points", "aabb", "bounds", "from points")]
    public static DyncameloBoundingBox FromPoints([MultiInput] IList<object?> points)
    {
        var list = PointExtraNodes.ReadPoints(points, "BoundingBox.FromPoints", nameof(points));

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        foreach (var point in list)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            minZ = Math.Min(minZ, point.Z);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
            maxZ = Math.Max(maxZ, point.Z);
        }

        return new DyncameloBoundingBox(new DyncameloPoint(minX, minY, minZ), new DyncameloPoint(maxX, maxY, maxZ));
    }

    /// <summary>Moves a bounding box by an offset vector without changing its size.</summary>
    /// <param name="box">The bounding box.</param>
    /// <param name="offset">The displacement (same units as the box).</param>
    /// <returns>The moved bounding box.</returns>
    [NodeName("BoundingBox.Translate")]
    [return: NodeName("boundingBox")]
    [NodeDescription("Moves a bounding box by an offset vector, keeping its size.")]
    [NodeSearchTags("move", "shift", "offset", "displace", "vector")]
    public static DyncameloBoundingBox Translate(DyncameloBoundingBox box, DyncameloVector offset)
    {
        Require(box, "BoundingBox.Translate", nameof(box));
        if (offset == null)
        {
            throw new ArgumentNullException(nameof(offset), "BoundingBox.Translate requires an offset vector. Wire a vector into the 'offset' input.");
        }

        return new DyncameloBoundingBox(
            new DyncameloPoint(box.Min.X + offset.X, box.Min.Y + offset.Y, box.Min.Z + offset.Z),
            new DyncameloPoint(box.Max.X + offset.X, box.Max.Y + offset.Y, box.Max.Z + offset.Z));
    }

    // ------------------------------------------------------------------ helpers

    private static void Require(DyncameloBoundingBox? box, string node, string parameter)
    {
        if (box == null)
        {
            throw new ArgumentNullException(parameter, node + " requires a bounding box. Wire a bounding box into the '" + parameter + "' input.");
        }
    }

    private static double SizeX(DyncameloBoundingBox box) => box.Max.X - box.Min.X;

    private static double SizeY(DyncameloBoundingBox box) => box.Max.Y - box.Min.Y;

    private static double SizeZ(DyncameloBoundingBox box) => box.Max.Z - box.Min.Z;

    private static void AddIfCollapsed(List<string> collapsed, string axis, double newMin, double newMax, double size)
    {
        if (newMin > newMax)
        {
            collapsed.Add(axis + " (size " + Format(size) + ")");
        }
    }

    private static string Format(double value)
    {
        return TypeCoercion.FormatValue(value);
    }
}
