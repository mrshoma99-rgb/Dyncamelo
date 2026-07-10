using System;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes;

/// <summary>
/// Spatial geometry nodes over the library's lightweight
/// <see cref="DyncameloPoint"/> / <see cref="DyncameloVector"/> /
/// <see cref="DyncameloBoundingBox"/> types: point-in-box containment (zone
/// assignment) and point translation ("location + 1 m up" plumbing).
/// </summary>
[NodeCategory("Geometry")]
public static class SpatialNodes
{
    /// <summary>
    /// Tests whether a point lies inside an axis-aligned bounding box.
    /// Points exactly on a face, edge or corner count as inside (consistent
    /// with BoundingBox.Intersects, where touching counts).
    /// </summary>
    /// <param name="boundingBox">The bounding box (e.g. a zone volume).</param>
    /// <param name="point">The point to test (e.g. an element's bounding-box center).</param>
    /// <returns>True when the point is inside or on the box.</returns>
    [NodeName("BoundingBox.Contains")]
    [return: NodeName("contains")]
    [NodeDescription("Tests whether a point lies inside a bounding box (points on the boundary count as inside).")]
    [NodeSearchTags("inside", "within", "containment", "zone", "point in box")]
    public static bool BoundingBoxContains(DyncameloBoundingBox boundingBox, DyncameloPoint point)
    {
        if (boundingBox == null)
        {
            throw new ArgumentNullException(nameof(boundingBox), "BoundingBox.Contains requires a bounding box.");
        }

        if (point == null)
        {
            throw new ArgumentNullException(nameof(point), "BoundingBox.Contains requires a point to test.");
        }

        return boundingBox.Min.X <= point.X && point.X <= boundingBox.Max.X &&
               boundingBox.Min.Y <= point.Y && point.Y <= boundingBox.Max.Y &&
               boundingBox.Min.Z <= point.Z && point.Z <= boundingBox.Max.Z;
    }

    /// <summary>
    /// Offsets a point by a vector, returning a new point (the input point is
    /// not modified). Example: element location "+ 1 m up" is
    /// Vector.ByCoordinates(0, 0, 1) → Units.Convert → Point.Translate.
    /// </summary>
    /// <param name="point">The point to offset.</param>
    /// <param name="vector">The offset vector (same units as the point).</param>
    /// <returns>The translated point.</returns>
    [NodeName("Point.Translate")]
    [return: NodeName("point")]
    [NodeDescription("Offsets a point by a vector, returning a new point.")]
    [NodeSearchTags("move", "offset", "shift", "add", "vector")]
    public static DyncameloPoint PointTranslate(DyncameloPoint point, DyncameloVector vector)
    {
        if (point == null)
        {
            throw new ArgumentNullException(nameof(point), "Point.Translate requires a point.");
        }

        if (vector == null)
        {
            throw new ArgumentNullException(nameof(vector), "Point.Translate requires an offset vector.");
        }

        return new DyncameloPoint(point.X + vector.X, point.Y + vector.Y, point.Z + vector.Z);
    }
}
