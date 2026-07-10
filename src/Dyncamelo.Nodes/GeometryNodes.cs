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
}
