using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;
using CamelGraph.Nodes.Spatial;

namespace CamelGraph.Navisworks;

/// <summary>
/// Distance measurement between model items (wishlist #7). Two tiers: "mesh"
/// (exact surface-to-surface via the Clash engine's minimum-clearance API) and
/// "bbox" (cheap axis-aligned bounding-box approximation). All distances are
/// in document units — chain Units.Convert for meters/feet.
/// </summary>
[NodeCategory("Navisworks.Analysis")]
public static class DistanceNodes
{
    /// <summary>Shortest distance between two selections of model items, with witness points.</summary>
    /// <param name="itemsA">The first selection.</param>
    /// <param name="itemsB">The second selection.</param>
    /// <param name="method">"mesh" = exact surface-to-surface (Clash engine; slower on huge selections), "bbox" = fast bounding-box approximation.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The distance (document units; 0 when touching/intersecting) and the closest point on each selection.</returns>
    [NodeName("Distance.BetweenItems")]
    [NodeDescription("Shortest distance between two selections, with the closest (witness) point on each side. method \"mesh\" = exact surface-to-surface via the Clash engine (can be slow on very large selections); \"bbox\" = fast bounding-box approximation. Document units — chain Units.Convert. The bbox tier measures between the combined box of each side, a coarser answer than Proximity.NearestDistance, which measures to each target on its own. The default is mesh here and bbox on Proximity.NearestDistance.")]
    [NodeSearchTags("distance", "clearance", "closest", "shortest", "measure", "between", "gap")]
    [MultiReturn("distance", "pointA", "pointB")]
    [PortKinds("number", "geometry", "geometry")]
    public static Dictionary<string, object?> BetweenItems(
        IEnumerable<ModelItem> itemsA,
        IEnumerable<ModelItem> itemsB,
        [NodeChoices("mesh", "bbox")]
        string method = "mesh",
        Document? document = null)
    {
        var listA = NavisValues.RequireItems(itemsA, "itemsA");
        var listB = NavisValues.RequireItems(itemsB, "itemsB");
        var mode = (method ?? string.Empty).Trim().ToLowerInvariant();

        switch (mode)
        {
            case "mesh":
            case "":
                return MeshDistance(listA, listB, document);
            case "bbox":
                return BoxDistance(listA, listB);
            default:
                throw new ArgumentException(
                    "Unknown distance method '" + method + "'. Use \"mesh\" (exact surfaces) or \"bbox\" (fast approximation).",
                    nameof(method));
        }
    }

    /// <summary>For each item, the distance to the nearest of the target items.</summary>
    /// <param name="items">The items to measure from (e.g. floor openings).</param>
    /// <param name="targets">The items to measure to (e.g. handrails); empty means "no neighbour anywhere".</param>
    /// <param name="method">"bbox" = fast bounding-box approximation, "mesh" = exact surface-to-surface (slower).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>One distance per item (document units); +∞ when there are no targets.</returns>
    [NodeName("Proximity.NearestDistance")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [NodeDescription("For each item, the distance to the NEAREST of the targets (document units), so you can flag items with nothing close by — e.g. openings with no handrail within a distance: compare the result with GreaterThan. Returns +∞ for an item when there are no targets at all. method \"bbox\" (the default here; Distance.BetweenItems defaults to mesh) = fast, \"mesh\" = exact surfaces (slower on big sets).")]
    [NodeSearchTags("proximity", "nearest", "closest", "distance", "neighbour", "near", "far", "within", "handrail")]
    [return: NodeName("distances")]
    public static List<double> NearestDistance(
        [MultiInput] IEnumerable<ModelItem> items,
        IEnumerable<ModelItem> targets,
        [NodeChoices("bbox", "mesh")]
        string method = "bbox",
        Document? document = null)
    {
        var itemList = NavisValues.RequireItems(items, "items");
        var targetList = NavisValues.ToItemList(targets); // may be empty → +∞
        var mode = (method ?? string.Empty).Trim().ToLowerInvariant();
        if (mode.Length == 0)
        {
            mode = "bbox";
        }

        if (mode != "bbox" && mode != "mesh")
        {
            throw new ArgumentException(
                "Unknown method '" + method + "'. Use \"bbox\" (fast) or \"mesh\" (exact surfaces).", nameof(method));
        }

        if (mode == "mesh")
        {
            return NearestByMesh(itemList, targetList, document);
        }

        return NearestByBox(itemList, targetList);
    }

    /// <summary>
    /// The bbox tier: every box is read from Navisworks once, as six plain numbers (the old code read a dozen numbers through the
    /// API for every PAIR of boxes), and the nearest target of each item is found with <see cref="NearestBoxIndex"/>, which skips
    /// the targets that cannot be the nearest. It measures to each target individually, NOT to their combined box, so a
    /// spread-out target set stays correct, and gives the nearest target and distance that measuring against every target would.
    /// </summary>
    private static List<double> NearestByBox(List<ModelItem> itemList, List<ModelItem> targetList)
    {
        var results = new List<double>(itemList.Count);
        if (targetList.Count == 0)
        {
            foreach (var item in itemList)
            {
                results.Add(double.PositiveInfinity);
            }

            return results;
        }

        var targetBoxes = new List<AxisBox>(targetList.Count);
        foreach (var target in targetList)
        {
            if (TryReadBox(target, out var box))
            {
                targetBoxes.Add(box);
            }
        }

        var index = new NearestBoxIndex(targetBoxes);
        foreach (var item in itemList)
        {
            if (targetBoxes.Count == 0 || !TryReadBox(item, out var itemBox) || !index.TryFindNearest(itemBox, out var nearest, out _))
            {
                results.Add(double.PositiveInfinity);
                continue;
            }

            // The nearest pair is known; its distance is asked of Navisworks as before (Point3D.DistanceTo of the two closest
            // points), so the number is the one this node has always reported.
            BoxGeometry.ClosestPoints(itemBox, targetBoxes[nearest], out var ax, out var ay, out var az, out var bx, out var by, out var bz);
            results.Add(new Point3D(ax, ay, az).DistanceTo(new Point3D(bx, by, bz)));
        }

        return results;
    }

    /// <summary>
    /// The mesh tier: the clash engine's minimum clearance already returns the nearest across the whole target set, so it is asked
    /// once per item. The engine and the Navisworks collection of the targets are prepared once, not once per item.
    /// </summary>
    private static List<double> NearestByMesh(List<ModelItem> itemList, List<ModelItem> targetList, Document? document)
    {
        var results = new List<double>(itemList.Count);
        if (targetList.Count == 0)
        {
            foreach (var item in itemList)
            {
                results.Add(double.PositiveInfinity);
            }

            return results;
        }

        var clash = RequireMeshEngine(document);
        var targets = NavisValues.ToItemCollection(targetList);
        foreach (var item in itemList)
        {
            var single = new ModelItemCollection();
            single.Add(item);
            var clearance = MeshDistance(clash, single, targets);
            results.Add(clearance["distance"] is double d ? d : double.PositiveInfinity);
        }

        return results;
    }

    /// <summary>A box as plain numbers; false when the item has no geometry (no box, or an empty one).</summary>
    private static bool TryReadBox(ModelItem item, out AxisBox box)
    {
        var navisBox = item.BoundingBox();
        if (navisBox == null || navisBox.IsEmpty)
        {
            box = default;
            return false;
        }

        var min = navisBox.Min;
        var max = navisBox.Max;
        box = new AxisBox(min.X, min.Y, min.Z, max.X, max.Y, max.Z);
        return true;
    }

    // ---------------------------------------------------------- Mesh tier

    private static Dictionary<string, object?> MeshDistance(
        List<ModelItem> listA, List<ModelItem> listB, Document? document)
    {
        var clash = RequireMeshEngine(document);
        return MeshDistance(clash, NavisValues.ToItemCollection(listA), NavisValues.ToItemCollection(listB));
    }

    private static DocumentClash RequireMeshEngine(Document? document)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        return doc.GetClash()
            ?? throw new InvalidOperationException(
                "The Clash engine is not available in this Navisworks edition — use method = \"bbox\" instead.");
    }

    private static Dictionary<string, object?> MeshDistance(
        DocumentClash clash, ModelItemCollection collectionA, ModelItemCollection collectionB)
    {
        MinimumClearanceResult clearance;
        var succeeded = clash.TryCalculateMinimumClearance(collectionA, collectionB, false, out clearance);
        if (!succeeded || clearance == null)
        {
            throw new InvalidOperationException(
                "Navisworks could not compute the mesh clearance between the two selections " +
                "(the items may carry no geometry) — try method = \"bbox\".");
        }

        var pointA = clearance.ClosestPointOnSelection1;
        var pointB = clearance.ClosestPointOnSelection2;
        if (pointA == null || pointB == null)
        {
            throw new InvalidOperationException(
                "The clearance calculation returned no witness points — try method = \"bbox\".");
        }

        return new Dictionary<string, object?>
        {
            ["distance"] = pointA.DistanceTo(pointB),
            ["pointA"] = pointA,
            ["pointB"] = pointB,
        };
    }

    // ---------------------------------------------------------- Bbox tier

    private static Dictionary<string, object?> BoxDistance(List<ModelItem> listA, List<ModelItem> listB)
    {
        var boxA = CombinedBox(listA, "itemsA");
        var boxB = CombinedBox(listB, "itemsB");

        BoxGeometry.ClosestCoordinates(boxA.Min.X, boxA.Max.X, boxB.Min.X, boxB.Max.X, out var ax, out var bx);
        BoxGeometry.ClosestCoordinates(boxA.Min.Y, boxA.Max.Y, boxB.Min.Y, boxB.Max.Y, out var ay, out var by);
        BoxGeometry.ClosestCoordinates(boxA.Min.Z, boxA.Max.Z, boxB.Min.Z, boxB.Max.Z, out var az, out var bz);

        var pointA = new Point3D(ax, ay, az);
        var pointB = new Point3D(bx, by, bz);

        return new Dictionary<string, object?>
        {
            ["distance"] = pointA.DistanceTo(pointB),
            ["pointA"] = pointA,
            ["pointB"] = pointB,
        };
    }

    /// <summary>The union bounding box of all items' boxes (throws when none has one).</summary>
    private static BoundingBox3D CombinedBox(List<ModelItem> items, string parameterName)
    {
        BoundingBox3D? combined = null;
        foreach (var item in items)
        {
            var box = item.BoundingBox();
            if (box == null || box.IsEmpty)
            {
                continue;
            }

            combined = combined == null ? box : combined.Extend(box);
        }

        return combined ?? throw new ArgumentException(
            "None of the '" + parameterName + "' items has a bounding box — they carry no geometry.", parameterName);
    }
}
