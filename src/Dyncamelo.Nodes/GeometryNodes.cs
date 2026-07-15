using System;
using System.Collections.Generic;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes;

/// <summary>
/// Basic geometry nodes over the library's lightweight
/// <see cref="DyncameloPoint"/> / <see cref="DyncameloBoundingBox"/> types.
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
    public static DyncameloPoint PointByCoordinates(double x = 0d, double y = 0d, double z = 0d)
    {
        return new DyncameloPoint(x, y, z);
    }

    /// <summary>Decomposes a point into its X, Y and Z coordinates.</summary>
    /// <param name="point">The point to decompose.</param>
    /// <returns>Dictionary with "x", "y" and "z" values.</returns>
    [NodeName("Point.Components")]
    [MultiReturn("x", "y", "z")]
    [NodeDescription("Splits a point into its X, Y and Z coordinates.")]
    [NodeSearchTags("deconstruct", "xyz", "coordinates")]
    public static Dictionary<string, object> PointComponents(DyncameloPoint point)
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
    /// <param name="min">One corner of the box.</param>
    /// <param name="max">The opposite corner of the box.</param>
    /// <returns>The bounding box.</returns>
    [NodeName("BoundingBox.ByCorners")]
    [return: NodeName("boundingBox")]
    [NodeDescription("Creates an axis-aligned bounding box spanning two corner points.")]
    [NodeSearchTags("box", "extent", "aabb")]
    public static DyncameloBoundingBox BoundingBoxByCorners(DyncameloPoint min, DyncameloPoint max)
    {
        if (min == null)
        {
            throw new ArgumentNullException(nameof(min), "BoundingBox.ByCorners requires two corner points.");
        }

        if (max == null)
        {
            throw new ArgumentNullException(nameof(max), "BoundingBox.ByCorners requires two corner points.");
        }

        return new DyncameloBoundingBox(min, max);
    }

    /// <summary>Geometric center of a bounding box.</summary>
    /// <param name="boundingBox">The bounding box.</param>
    /// <returns>The center point.</returns>
    [NodeName("BoundingBox.Center")]
    [return: NodeName("point")]
    [NodeDescription("Returns the center point of a bounding box.")]
    [NodeSearchTags("middle", "centroid", "midpoint")]
    public static DyncameloPoint BoundingBoxCenter(DyncameloBoundingBox boundingBox)
    {
        if (boundingBox == null)
        {
            throw new ArgumentNullException(nameof(boundingBox), "BoundingBox.Center requires a bounding box.");
        }

        return boundingBox.Center;
    }

    /// <summary>Scales a bounding box about its center by a factor.</summary>
    /// <param name="boundingBox">The bounding box.</param>
    /// <param name="factor">Scale factor (2 = double size, 0.5 = half, 1 = unchanged). Applied about the center.</param>
    /// <returns>The scaled bounding box.</returns>
    [NodeName("BoundingBox.Scale")]
    [return: NodeName("boundingBox")]
    [NodeDescription("Scales a bounding box about its center by a factor (2 = double, 0.5 = half) — e.g. to pad a box before a section or zoom.")]
    [NodeSearchTags("scale", "grow", "shrink", "expand", "pad", "resize", "inflate")]
    public static DyncameloBoundingBox BoundingBoxScale(DyncameloBoundingBox boundingBox, double factor)
    {
        if (boundingBox == null)
        {
            throw new ArgumentNullException(nameof(boundingBox), "BoundingBox.Scale requires a bounding box.");
        }

        if (factor <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), "The scale factor must be positive.");
        }

        var center = boundingBox.Center;
        var min = boundingBox.Min;
        var max = boundingBox.Max;
        return new DyncameloBoundingBox(
            new DyncameloPoint(
                center.X - (center.X - min.X) * factor,
                center.Y - (center.Y - min.Y) * factor,
                center.Z - (center.Z - min.Z) * factor),
            new DyncameloPoint(
                center.X + (max.X - center.X) * factor,
                center.Y + (max.Y - center.Y) * factor,
                center.Z + (max.Z - center.Z) * factor));
    }

    /// <summary>Euclidean distance between two points (in model units).</summary>
    /// <param name="point">The first point.</param>
    /// <param name="other">The second point.</param>
    /// <returns>The distance.</returns>
    [NodeName("Point.DistanceTo")]
    [return: NodeName("distance")]
    [NodeDescription("Returns the straight-line distance between two points.")]
    [NodeSearchTags("length", "measure", "euclidean", "between")]
    public static double PointDistanceTo(DyncameloPoint point, DyncameloPoint other)
    {
        if (point == null)
        {
            throw new ArgumentNullException(nameof(point), "Point.DistanceTo requires two points.");
        }

        if (other == null)
        {
            throw new ArgumentNullException(nameof(other), "Point.DistanceTo requires two points.");
        }

        var dx = other.X - point.X;
        var dy = other.Y - point.Y;
        var dz = other.Z - point.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
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
    public static DyncameloVector VectorByCoordinates(double x = 0d, double y = 0d, double z = 0d)
    {
        return new DyncameloVector(x, y, z);
    }

    /// <summary>
    /// Extents of a bounding box: the size along each axis plus the min and
    /// max corner points.
    /// </summary>
    /// <param name="boundingBox">The bounding box.</param>
    /// <returns>Dictionary with "sizeX", "sizeY", "sizeZ", "min" and "max".</returns>
    [NodeName("BoundingBox.Size")]
    [MultiReturn("sizeX", "sizeY", "sizeZ", "min", "max")]
    [NodeDescription("Returns a bounding box's size along each axis and its min/max corner points.")]
    [NodeSearchTags("extent", "dimensions", "width", "height", "depth")]
    public static Dictionary<string, object> BoundingBoxSize(DyncameloBoundingBox boundingBox)
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
    public static bool BoundingBoxIntersects(DyncameloBoundingBox boundingBox, DyncameloBoundingBox other)
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
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("gap")]
    [NodeDescription("The widest strip of open floor between an inner box (equipment) and the outer box (opening) in plan — the 'space between the equipment and the opening edge'. Returns the largest of the four horizontal side gaps; 0 when the equipment reaches every edge. Threshold it to flag openings that need a handrail.")]
    [NodeSearchTags("gap", "clearance", "opening", "edge", "plan", "space", "perimeter", "handrail")]
    public static double BoundingBoxPlanGap(DyncameloBoundingBox outer, DyncameloBoundingBox inner)
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
