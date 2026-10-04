using System;
using System.Collections.Generic;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes;

/// <summary>
/// Basic geometry nodes over the library's lightweight
/// <see cref="CamelGraphPoint"/> / <see cref="CamelGraphBoundingBox"/> types.
/// </summary>
[NodeCategory("Geometry")]
public static class GeometryNodes
{
    /// <summary>Creates a point from X, Y and Z coordinates.</summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <param name="z">Z coordinate.</param>
    /// <returns>The point.</returns>
    [NodeName("Point.ByCoordinates")]
    [return: NodeName("point")]
    [NodeDescription("Creates a 3D point from X, Y and Z coordinates.")]
    [NodeSearchTags("xyz", "coordinate", "position")]
    public static CamelGraphPoint PointByCoordinates(double x = 0d, double y = 0d, double z = 0d)
    {
        GeometryWarnings.NotFinite("Point.ByCoordinates", "the point", ("x", x), ("y", y), ("z", z));
        return new CamelGraphPoint(x, y, z);
    }

    /// <summary>Decomposes a point into its X, Y and Z coordinates.</summary>
    /// <param name="point">The point to decompose.</param>
    /// <returns>Dictionary with "x", "y" and "z" values.</returns>
    [NodeName("Point.Components")]
    [MultiReturn("x", "y", "z")]
    [PortKinds("number", "number", "number")]
    [NodeDescription("Splits a point into its X, Y and Z coordinates.")]
    [NodeSearchTags("deconstruct", "xyz", "coordinates")]
    public static Dictionary<string, object> PointComponents(CamelGraphPoint point)
    {
        if (point == null)
        {
            throw new ArgumentNullException(nameof(point), "Point.Components requires a point.");
        }

        return new Dictionary<string, object>
        {
            ["x"] = point.X,
            ["y"] = point.Y,
            ["z"] = point.Z,
        };
    }

    /// <summary>Creates an axis-aligned bounding box from two opposite corners (any order).</summary>
    /// <param name="cornerA">One corner of the box.</param>
    /// <param name="cornerB">The opposite corner of the box.</param>
    /// <returns>The bounding box.</returns>
    [NodeName("BoundingBox.ByCorners")]
    [PortAlias("min", "cornerA")]
    [PortAlias("max", "cornerB")]
    [return: NodeName("boundingBox")]
    [NodeDescription("Creates an axis-aligned bounding box spanning two opposite corner points, given in any order (the smaller coordinates become the box's min corner, the larger its max corner).")]
    [NodeSearchTags("box", "extent", "aabb")]
    public static CamelGraphBoundingBox BoundingBoxByCorners(CamelGraphPoint cornerA, CamelGraphPoint cornerB)
    {
        if (cornerA == null)
        {
            throw new ArgumentNullException(nameof(cornerA), "BoundingBox.ByCorners requires two corner points.");
        }

        if (cornerB == null)
        {
            throw new ArgumentNullException(nameof(cornerB), "BoundingBox.ByCorners requires two corner points.");
        }

        return new CamelGraphBoundingBox(cornerA, cornerB);
    }

    /// <summary>
    /// One bounding box that fits everything you give it: boxes, points,
    /// [x, y, z] number triples, or any nesting of those in lists — the
    /// geometric union. Navisworks boxes/points are accepted through the
    /// registered converters.
    /// </summary>
    /// <param name="geometry">Boxes and/or points to enclose (several wires can feed this one input; lists nest freely; at least one required). Wire a single [x, y, z] triple on its own, or inside a list: next to other wires a bare list is spread into its numbers.</param>
    /// <returns>The bounding box fitting all inputs.</returns>
    [NodeName("BoundingBox.Union")]
    [return: NodeName("boundingBox")]
    [NodeDescription("ONE bounding box fitting every box and/or point wired in — the geometric union, e.g. one frame around scattered elements' boxes or around a set of points (several wires can feed the one input; [x,y,z] triples work too; lists nest freely, a list of lists included).")]
    [NodeSearchTags("union", "combine", "fit", "merge", "enclose", "multiple", "all", "extents", "aabb", "around", "points", "bounds", "from points")]
    public static CamelGraphBoundingBox BoundingBoxUnion([MultiInput] IList<object?> geometry)
    {
        if (geometry == null || geometry.Count == 0)
        {
            throw new ArgumentException("BoundingBox.Union needs at least one box or point.", nameof(geometry));
        }

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        bool any = false;

        void ExtendPoint(double x, double y, double z)
        {
            any = true;
            minX = Math.Min(minX, x); minY = Math.Min(minY, y); minZ = Math.Min(minZ, z);
            maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); maxZ = Math.Max(maxZ, z);
        }

        void Add(object? value)
        {
            switch (value)
            {
                case null:
                    return;
                case CamelGraphBoundingBox box:
                    ExtendPoint(box.Min.X, box.Min.Y, box.Min.Z);
                    ExtendPoint(box.Max.X, box.Max.Y, box.Max.Z);
                    return;
                case CamelGraphPoint point:
                    ExtendPoint(point.X, point.Y, point.Z);
                    return;
                case System.Collections.IList list when !(value is string):
                    // Three plain numbers form a point; anything else nests.
                    if (list.Count == 3 &&
                        TryNumber(list[0], out var x) && TryNumber(list[1], out var y) && TryNumber(list[2], out var z))
                    {
                        ExtendPoint(x, y, z);
                        return;
                    }

                    foreach (var element in list)
                    {
                        Add(element);
                    }

                    return;
                default:
                    // Foreign box/point types (Navisworks) come through the
                    // registered type converters.
                    if (Core.Types.TypeCoercion.TryCoerce(value, typeof(CamelGraphBoundingBox), out var asBox) &&
                        asBox is CamelGraphBoundingBox coercedBox)
                    {
                        Add(coercedBox);
                        return;
                    }

                    if (Core.Types.TypeCoercion.TryCoerce(value, typeof(CamelGraphPoint), out var asPoint) &&
                        asPoint is CamelGraphPoint coercedPoint)
                    {
                        Add(coercedPoint);
                        return;
                    }

                    throw new ArgumentException(
                        "BoundingBox.Union cannot read a " + value.GetType().Name +
                        " — wire bounding boxes, points, or [x, y, z] number lists.");
            }
        }

        foreach (var entry in geometry)
        {
            Add(entry);
        }

        if (!any)
        {
            throw new ArgumentException("BoundingBox.Union found no usable boxes or points in the input.", nameof(geometry));
        }

        return new CamelGraphBoundingBox(
            new CamelGraphPoint(minX, minY, minZ),
            new CamelGraphPoint(maxX, maxY, maxZ));
    }

    private static bool TryNumber(object? value, out double number)
    {
        switch (value)
        {
            case double d:
                number = d;
                return true;
            case IConvertible convertible when !(value is string):
                number = convertible.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
                return true;
            case string text:
                return double.TryParse(
                    text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out number);
            default:
                number = 0;
                return false;
        }
    }

    /// <summary>Geometric center of a bounding box.</summary>
    /// <param name="boundingBox">The bounding box.</param>
    /// <returns>The center point.</returns>
    [NodeName("BoundingBox.Center")]
    [return: NodeName("point")]
    [NodeDescription("Returns the center point of a bounding box.")]
    [NodeSearchTags("middle", "centroid", "midpoint")]
    public static CamelGraphPoint BoundingBoxCenter(CamelGraphBoundingBox boundingBox)
    {
        if (boundingBox == null)
        {
            throw new ArgumentNullException(nameof(boundingBox), "BoundingBox.Center requires a bounding box.");
        }

        return boundingBox.Center;
    }

    /// <summary>Scales a bounding box about its center by a factor.</summary>
    /// <param name="boundingBox">The bounding box.</param>
    /// <param name="factor">Scale factor above 0 (2 = double size, 0.5 = half, 1 = unchanged). Applied about the center.</param>
    /// <returns>The scaled bounding box.</returns>
    [NodeName("BoundingBox.Scale")]
    [return: NodeName("boundingBox")]
    [NodeDescription("Scales a bounding box about its center by a factor (2 = double, 0.5 = half) — e.g. to pad a box before a section or zoom.")]
    [NodeSearchTags("scale", "grow", "shrink", "expand", "pad", "resize", "inflate")]
    public static CamelGraphBoundingBox BoundingBoxScale(
        CamelGraphBoundingBox boundingBox,
        [NodeRange(0.001, 1000000, SoftMin = 0.1, SoftMax = 5, Step = 0.1)] double factor)
    {
        if (boundingBox == null)
        {
            throw new ArgumentNullException(nameof(boundingBox), "BoundingBox.Scale requires a bounding box.");
        }

        if (double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(factor),
                "BoundingBox.Scale needs a scale factor above 0 and finite (got " + Core.Types.TypeCoercion.FormatValue(factor) + "). Check the number wired into 'factor'.");
        }

        var center = boundingBox.Center;
        var min = boundingBox.Min;
        var max = boundingBox.Max;
        return new CamelGraphBoundingBox(
            new CamelGraphPoint(
                center.X - (center.X - min.X) * factor,
                center.Y - (center.Y - min.Y) * factor,
                center.Z - (center.Z - min.Z) * factor),
            new CamelGraphPoint(
                center.X + (max.X - center.X) * factor,
                center.Y + (max.Y - center.Y) * factor,
                center.Z + (max.Z - center.Z) * factor));
    }

    /// <summary>Euclidean distance between two points (in model units).</summary>
    /// <param name="a">The first point.</param>
    /// <param name="b">The second point.</param>
    /// <returns>The distance.</returns>
    [NodeName("Point.DistanceTo")]
    [PortAlias("point", "a")]
    [PortAlias("other", "b")]
    [return: NodeName("distance")]
    [NodeDescription("Returns the straight-line distance between two points.")]
    [NodeSearchTags("length", "measure", "euclidean", "between")]
    public static double PointDistanceTo(CamelGraphPoint a, CamelGraphPoint b)
    {
        if (a == null)
        {
            throw new ArgumentNullException(nameof(a), "Point.DistanceTo requires two points.");
        }

        if (b == null)
        {
            throw new ArgumentNullException(nameof(b), "Point.DistanceTo requires two points.");
        }

        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var dz = b.Z - a.Z;
        var distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        GeometryWarnings.ResultNotFinite("Point.DistanceTo", "The distance", distance, "A point has a coordinate that is not a finite number; check the points wired in.");
        return distance;
    }

    /// <summary>Creates a direction vector from X, Y and Z components.</summary>
    /// <param name="x">X component.</param>
    /// <param name="y">Y component.</param>
    /// <param name="z">Z component.</param>
    /// <returns>The vector.</returns>
    [NodeName("Vector.ByCoordinates")]
    [return: NodeName("vector")]
    [NodeDescription("Creates a 3D direction vector from X, Y and Z components.")]
    [NodeSearchTags("xyz", "direction", "axis")]
    public static CamelGraphVector VectorByCoordinates(double x = 0d, double y = 0d, double z = 0d)
    {
        GeometryWarnings.NotFinite("Vector.ByCoordinates", "the vector", ("x", x), ("y", y), ("z", z));
        return new CamelGraphVector(x, y, z);
    }

    /// <summary>
    /// Extents of a bounding box: the size along each axis plus the min and
    /// max corner points.
    /// </summary>
    /// <param name="boundingBox">The bounding box.</param>
    /// <returns>Dictionary with "sizeX", "sizeY", "sizeZ", "min" and "max".</returns>
    [NodeName("BoundingBox.Size")]
    [MultiReturn("sizeX", "sizeY", "sizeZ", "min", "max")]
    [PortKinds("number", "number", "number", "geometry", "geometry")]
    [NodeDescription("Returns a bounding box's size along each axis and its min/max corner points.")]
    [NodeSearchTags("extent", "dimensions", "width", "height", "depth")]
    public static Dictionary<string, object> BoundingBoxSize(CamelGraphBoundingBox boundingBox)
    {
        if (boundingBox == null)
        {
            throw new ArgumentNullException(nameof(boundingBox), "BoundingBox.Size requires a bounding box.");
        }

        return new Dictionary<string, object>
        {
            ["sizeX"] = boundingBox.Max.X - boundingBox.Min.X,
            ["sizeY"] = boundingBox.Max.Y - boundingBox.Min.Y,
            ["sizeZ"] = boundingBox.Max.Z - boundingBox.Min.Z,
            ["min"] = boundingBox.Min,
            ["max"] = boundingBox.Max,
        };
    }

    /// <summary>
    /// Axis-aligned overlap test between two bounding boxes. Boxes that merely
    /// touch (share a face, edge or corner) count as intersecting.
    /// </summary>
    /// <param name="boundingBox">The first box.</param>
    /// <param name="other">The second box.</param>
    /// <returns>True when the boxes overlap or touch.</returns>
    [NodeName("BoundingBox.Intersects")]
    [return: NodeName("intersects")]
    [NodeDescription("Tests whether two bounding boxes overlap (touching counts as intersecting).")]
    [NodeSearchTags("overlap", "collision", "touch", "clash")]
    public static bool BoundingBoxIntersects(CamelGraphBoundingBox boundingBox, CamelGraphBoundingBox other)
    {
        if (boundingBox == null)
        {
            throw new ArgumentNullException(nameof(boundingBox), "BoundingBox.Intersects requires two bounding boxes.");
        }

        if (other == null)
        {
            throw new ArgumentNullException(nameof(other), "BoundingBox.Intersects requires two bounding boxes.");
        }

        return boundingBox.Min.X <= other.Max.X && other.Min.X <= boundingBox.Max.X &&
               boundingBox.Min.Y <= other.Max.Y && other.Min.Y <= boundingBox.Max.Y &&
               boundingBox.Min.Z <= other.Max.Z && other.Min.Z <= boundingBox.Max.Z;
    }

    /// <summary>
    /// The largest horizontal (plan) gap between an inner box and the outer box
    /// around it — the widest strip of open floor beside equipment that sits in
    /// an opening. Compares the two XY footprints on all four sides and returns
    /// the biggest single-sided clearance; 0 when the inner box reaches or passes
    /// every edge. A bounding-rectangle approximation of the true perimeter-to-edge
    /// gap (exact for rectangular openings, a slight over-estimate for round ones).
    /// </summary>
    /// <param name="outer">The opening's bounding box.</param>
    /// <param name="inner">The equipment's bounding box.</param>
    /// <returns>The largest horizontal clear gap, in the same units as the boxes.</returns>
    [NodeName("BoundingBox.PlanGap")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("gap")]
    [NodeDescription("The widest strip of open floor between an inner box (equipment) and the outer box (opening) in plan — the 'space between the equipment and the opening edge'. Returns the largest of the four horizontal side gaps; 0 when the equipment reaches every edge. Threshold it to flag openings that need a handrail.")]
    [NodeSearchTags("gap", "clearance", "opening", "edge", "plan", "space", "perimeter", "handrail")]
    public static double BoundingBoxPlanGap(CamelGraphBoundingBox outer, CamelGraphBoundingBox inner)
    {
        if (outer == null)
        {
            throw new ArgumentNullException(nameof(outer), "BoundingBox.PlanGap requires the opening box.");
        }

        if (inner == null)
        {
            throw new ArgumentNullException(nameof(inner), "BoundingBox.PlanGap requires the equipment box.");
        }

        var gapMinX = inner.Min.X - outer.Min.X;
        var gapMaxX = outer.Max.X - inner.Max.X;
        var gapMinY = inner.Min.Y - outer.Min.Y;
        var gapMaxY = outer.Max.Y - inner.Max.Y;

        var maxGap = Math.Max(Math.Max(gapMinX, gapMaxX), Math.Max(gapMinY, gapMaxY));
        return maxGap < 0.0 ? 0.0 : maxGap;
    }
}

/// <summary>The one warning the Geometry nodes give when a number that reaches them is not finite (NaN or Infinity).</summary>
internal static class GeometryWarnings
{
    /// <summary>
    /// Adds one warning, naming the node, when any of the given inputs is NaN or Infinity, and does nothing otherwise. The node
    /// still returns its result, so the nodes after it run; the warning says what is wrong with it.
    /// </summary>
    /// <param name="node">The node's name as the user sees it.</param>
    /// <param name="result">What the node makes, in the user's words ("the vector").</param>
    /// <param name="inputs">The inputs to check, each with its port name.</param>
    internal static void NotFinite(string node, string result, params (string Name, double Value)[] inputs)
    {
        var bad = new List<string>();
        foreach (var (name, value) in inputs)
        {
            if (double.IsNaN(value))
            {
                bad.Add("'" + name + "' is not a number (NaN)");
            }
            else if (double.IsInfinity(value))
            {
                bad.Add("'" + name + "' is infinite");
            }
        }

        if (bad.Count > 0)
        {
            NodeWarnings.Add(node + ": " + string.Join(" and ", bad) + ", so " + result + " is not usable. Check the numbers wired into this node.");
        }
    }

    /// <summary>Adds one warning, naming the node, when a number the node computed is NaN or Infinity.</summary>
    /// <param name="node">The node's name as the user sees it.</param>
    /// <param name="result">What the number is, in the user's words ("The distance").</param>
    /// <param name="value">The computed number.</param>
    /// <param name="hint">What to check, as a plain sentence.</param>
    internal static void ResultNotFinite(string node, string result, double value, string hint)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            NodeWarnings.Add(node + ": " + result + " is not a finite number (" + (double.IsNaN(value) ? "NaN" : "Infinity") + "). " + hint);
        }
    }
}
