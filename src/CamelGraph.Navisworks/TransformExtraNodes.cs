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
    /// <param name="items">The model items to scale. An item with another listed item above it is left out (scaling a container scales everything below it).</param>
    /// <param name="factor">The scale factor: 2 doubles the size, 0.5 halves it, 1 changes nothing. Must be positive. One factor per run: a list of factors scales the same items once per factor and the factors multiply ([2, 3] scales by 6 in total).</param>
    /// <param name="about">The fixed point of the scaling (a Point or a list of three numbers); leave unwired to scale about the centre of the items' combined bounding box.</param>
    /// <param name="accumulate">True (default) adds this scaling to whatever override the items already have, so every run scales them again. False replaces their override, so the items end up exactly this much larger or smaller than the model has them and re-running does not pile up.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The scaled items (pass-through for chaining).</returns>
    [NodeName("ModelItem.Scale")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeAliases("CamelGraph.Navisworks.TransformExtraNodes.Scale@System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>,double,object,Autodesk.Navisworks.Api.Document")]
    [NodeDescription(
        "Scales model items uniformly about a point — default: the centre of the items' combined bounding box, so the group " +
        "grows or shrinks in place and keeps its internal layout. A permanent override: undoable, saved in the NWF, removed " +
        "by ModelItem.ResetTransform. Re-runs accumulate (each run scales again); in Advanced, switch accumulate off to scale " +
        "from where the model had the items, so re-running does not pile up. One factor per run: a list of factors against the " +
        "whole item list scales the same items once per factor and the factors multiply ([2, 3] is 6 in total; the node warns). " +
        "Items that sit below another listed item are left out, because scaling a container already scales everything under " +
        "it. One override per item, so allow a moment on large selections.")]
    [NodeSearchTags("item", "scale", "resize", "size", "grow", "shrink", "factor", "transform", "units")]
    [return: NodeName("items")]
    public static List<ModelItem> Scale(
        [MultiInput] IEnumerable<ModelItem> items,
        [NodeRange(0.001, 1000000, SoftMin = 0.1, SoftMax = 10, Step = 0.1)] double factor,
        [PortKinds("geometry")] object? about = null,
        [NodePanel("Advanced")] bool accumulate = true,
        Document? document = null)
    {
        var list = TransformHelpers.WithoutListedDescendants(NavisValues.RequireItems(items), "scaled");
        ItemTransformMath.RequireScaleFactor(factor);
        var doc = NavisworksContext.ResolveDocument(document);

        if (ItemTransformMath.IsIdentityScale(factor) && accumulate)
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
        TransformHelpers.WarnIfRunOncePerValue(list.Count, "scales", accumulate);
        TransformNodes.ApplyDelta(doc, list, delta, accumulate);
        return list;
    }

    /// <summary>Moves model items so the centre of their combined bounding box lands on a point.</summary>
    /// <param name="items">The model items to move (they keep their layout relative to each other). An item with another listed item above it is left out (moving a container moves everything below it).</param>
    /// <param name="target">Where the centre of the items' combined bounding box should end up (a Point or a list of three numbers), in document units. One target per run: a list of targets moves the same items to each in turn and the last one stays.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The moved items (pass-through for chaining).</returns>
    [NodeName("ModelItem.MoveTo")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription(
        "Moves model items so the centre of their combined bounding box lands on a target point — place a group by where " +
        "it should be, not by how far to push it. A permanent override: undoable, saved in the NWF, removed by " +
        "ModelItem.ResetTransform. Re-running with the same target is a no-op once the items are there. One target per run: " +
        "a list of targets moves the same items to each in turn and the last one stays (the node warns). Items that sit below " +
        "another listed item are left out, because moving a container already moves everything under it.")]
    [NodeSearchTags("item", "move", "moveto", "place", "position", "target", "centre", "center", "transform")]
    [return: NodeName("items")]
    public static List<ModelItem> MoveTo(
        [MultiInput] IEnumerable<ModelItem> items,
        [PortKinds("geometry")] object target,
        Document? document = null)
    {
        var list = TransformHelpers.WithoutListedDescendants(NavisValues.RequireItems(items), "moved");
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

        TransformHelpers.WarnIfRunOncePerValue(list.Count, "moves", false);
        TransformNodes.ApplyDelta(doc, list, TransformHelpers.Translation(new Vector3D(move.X, move.Y, move.Z)));
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
                "(Selection.Resolve with level Geometry resolves containers to theirs). " + hint);
        }

        var min = box.Min;
        var max = box.Max;
        return ItemTransformMath.BoxCentre(min.X, min.Y, min.Z, max.X, max.Y, max.Z);
    }
}
