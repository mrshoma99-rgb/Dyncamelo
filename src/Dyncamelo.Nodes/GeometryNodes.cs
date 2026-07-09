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
}
