using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;
using CamelGraph.Nodes.Spatial;

namespace CamelGraph.Navisworks;

/// <summary>
/// Scale and move-to-a-point for model items, built on the same permanent transform-override
/// mechanism as ModelItem.Translate / RotateAboutAxis (a delta composed onto each item's
/// existing override). The matrix maths is in <see cref="ItemTransformMath"/> (pure, unit-tested);
/// the matrix is turned into a Transform3D by <c>TransformHelpers.FromRowMajorMatrix</c>, the
/// builder ModelItem.SetTransform uses — a uniform scale there is a plain linear part with the
/// scale on the diagonal, so it does not depend on the row/column-vector question noted in
/// TransformHelpers.
/// </summary>
[NodeCategory("Navisworks.Transform")]
public static class TransformExtraNodes
{
    /// <summary>Scales model items uniformly about a point.</summary>
    /// <param name="items">The model items to scale. Wire the items themselves, not a container AND its children.</param>
    /// <param name="factor">The scale factor: 2 doubles the size, 0.5 halves it, 1 changes nothing. Must be positive.</param>
    /// <param name="about">The fixed point of the scaling (a Point or a list of three numbers); leave unwired to scale about the centre of the items' combined bounding box.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The scaled items (pass-through for chaining).</returns>
    [NodeName("ModelItem.Scale")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeDescription(
        "Scales model items uniformly about a point — default: the centre of the items' combined bounding box, so the group " +
        "grows or shrinks in place and keeps its internal layout. A permanent override: undoable, saved in the NWF, removed " +
        "by ModelItem.ResetTransform. Re-runs accumulate (each run scales again). One override per item, so allow a moment on large selections.")]
    [NodeSearchTags("item", "scale", "resize", "size", "grow", "shrink", "factor", "transform", "units")]
    [return: NodeName("items")]
    public static List<ModelItem> Scale(
        [MultiInput] IEnumerable<ModelItem> items,
        [NodeRange(0, 1000000, SoftMin = 0.1, SoftMax = 10, Step = 0.1)] double factor,
        [PortKinds("geometry")] object? about = null,
        Document? document = null)
    {
        var list = NavisValues.RequireItems(items);
        ItemTransformMath.RequireScaleFactor(factor);
        var doc = NavisworksContext.ResolveDocument(document);

        if (ItemTransformMath.IsIdentityScale(factor))
        {
            return list; // a factor of 1 changes nothing: do not create overrides
        }

        double cx, cy, cz;
        if (about != null)
        {
            var point = NavisValues.ToPoint3D(about);
            cx = point.X;
            cy = point.Y;
            cz = point.Z;
            ItemTransformMath.RequireFinitePoint(cx, cy, cz, "scale centre ('about')");
        }
        else
        {
            var centre = CombinedCentre(list, "Wire a point into 'about' to choose the scale centre yourself.");
            cx = centre.X;
            cy = centre.Y;
            cz = centre.Z;
        }

        var delta = TransformHelpers.FromRowMajorMatrix(ItemTransformMath.ScaleAboutPoint(factor, cx, cy, cz));
        ApplyDelta(doc, list, delta);
        return list;
    }

    /// <summary>Moves model items so the centre of their combined bounding box lands on a point.</summary>
    /// <param name="items">The model items to move (they keep their layout relative to each other).</param>
    /// <param name="target">Where the centre of the items' combined bounding box should end up (a Point or a list of three numbers), in document units.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The moved items (pass-through for chaining).</returns>
    [NodeName("ModelItem.MoveTo")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeDescription(
        "Moves model items so the centre of their combined bounding box lands on a target point — place a group by where " +
        "it should be, not by how far to push it. A permanent override: undoable, saved in the NWF, removed by " +
        "ModelItem.ResetTransform. Re-running with the same target is a no-op once the items are there.")]
    [NodeSearchTags("item", "move", "moveto", "place", "position", "target", "centre", "center", "transform")]
    [return: NodeName("items")]
    public static List<ModelItem> MoveTo(
        [MultiInput] IEnumerable<ModelItem> items,
        [PortKinds("geometry")] object target,
        Document? document = null)
    {
        var list = NavisValues.RequireItems(items);
        if (target == null)
        {
            throw new ArgumentNullException(
                nameof(target),
                "ModelItem.MoveTo requires a target point. Wire a Point (e.g. Point.ByCoordinates) or a list of three numbers into the 'target' input.");
        }

        var wanted = NavisValues.ToPoint3D(target);
        ItemTransformMath.RequireFinitePoint(wanted.X, wanted.Y, wanted.Z, "target");
        var doc = NavisworksContext.ResolveDocument(document);

        var centre = CombinedCentre(list, "There is no centre to move.");
        var move = ItemTransformMath.MoveDelta(centre.X, centre.Y, centre.Z, wanted.X, wanted.Y, wanted.Z);
        if (ItemTransformMath.IsNegligibleMove(move.X, move.Y, move.Z))
        {
            return list; // already there
        }

        ApplyDelta(doc, list, TransformHelpers.Translation(new Vector3D(move.X, move.Y, move.Z)));
        return list;
    }

    // ------------------------------------------------------------ privates

    /// <summary>The centre of the combined bounding box of the items, hidden ones included.</summary>
    private static (double X, double Y, double Z) CombinedCentre(List<ModelItem> items, string hint)
    {
        var box = NavisValues.ToItemCollection(items).BoundingBox(false);
        if (box == null || box.IsEmpty)
        {
            throw new InvalidOperationException(
                "The items carry no geometry, so they have no bounding box. Wire geometry-bearing items " +
                "(ModelItem.GeometryLeaves resolves containers to theirs). " + hint);
        }

        var min = box.Min;
        var max = box.Max;
        return ItemTransformMath.BoxCentre(min.X, min.Y, min.Z, max.X, max.Y, max.Z);
    }

    /// <summary>
    /// Applies a delta on top of each item's existing permanent override (per item — overrides can
    /// differ across the selection). Same mechanism as ModelItem.Translate; see the RUNTIME-CHECK
    /// note on TransformNodes.ApplyDelta.
    /// </summary>
    private static void ApplyDelta(Document doc, List<ModelItem> items, Transform3D delta)
    {
        foreach (var item in items)
        {
            var composed = TransformHelpers.ComposeWithOverride(item, delta);
            doc.Models.OverridePermanentTransform(new[] { item }, composed, false);
        }
    }
}
