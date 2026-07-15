using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

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
    [NodeDescription("Shortest distance between two selections, with the closest (witness) point on each side. method \"mesh\" = exact surface-to-surface via the Clash engine (can be slow on very large selections); \"bbox\" = fast bounding-box approximation. Document units — chain Units.Convert.")]
    [NodeSearchTags("distance", "clearance", "closest", "shortest", "measure", "between", "gap")]
    [MultiReturn("distance", "pointA", "pointB")]
    public static Dictionary<string, object?> BetweenItems(
        IEnumerable<ModelItem> itemsA,
        IEnumerable<ModelItem> itemsB,
        [NodeChoices("mesh", "bbox")]
        string method = "mesh",
        Document? document = null)
    {
        var listA = RequireItems(itemsA, "itemsA");
        var listB = RequireItems(itemsB, "itemsB");
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
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [NodeDescription("For each item, the distance to the NEAREST of the targets (document units), so you can flag items with nothing close by — e.g. openings with no handrail within a distance: compare the result with GreaterThan. Returns +∞ for an item when there are no targets at all. method \"bbox\" = fast, \"mesh\" = exact surfaces (slower on big sets).")]
    [NodeSearchTags("proximity", "nearest", "closest", "distance", "neighbour", "near", "far", "within", "handrail")]
    [return: NodeName("distances")]
    public static List<double> NearestDistance(
        IEnumerable<ModelItem> items,
        IEnumerable<ModelItem> targets,
        [NodeChoices("bbox", "mesh")]
        string method = "bbox",
        Document? document = null)
    {
        var itemList = RequireItems(items, "items");
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

        // Pre-box the targets once (bbox mode measures to each target individually,
        // NOT to their combined box, so a spread-out target set stays correct).
        var targetBoxes = new List<BoundingBox3D>(targetList.Count);
        if (mode == "bbox")
        {
            foreach (var target in targetList)
            {
                var box = target.BoundingBox();
                if (box != null && !box.IsEmpty)
                {
                    targetBoxes.Add(box);
                }
            }
        }

        var results = new List<double>(itemList.Count);
        foreach (var item in itemList)
        {
            if (targetList.Count == 0)
            {
                results.Add(double.PositiveInfinity);
                continue;
            }

            if (mode == "mesh")
            {
                // The clash minimum-clearance already returns the nearest across the set.
                var clearance = MeshDistance(new List<ModelItem> { item }, targetList, document);
                results.Add(clearance["distance"] is double d ? d : double.PositiveInfinity);
                continue;
            }

            var itemBox = item.BoundingBox();
            if (itemBox == null || itemBox.IsEmpty)
            {
                results.Add(double.PositiveInfinity);
                continue;
            }

            var nearest = double.PositiveInfinity;
            foreach (var targetBox in targetBoxes)
            {
                var distance = BoxDistanceBetween(itemBox, targetBox);
                if (distance < nearest)
                {
                    nearest = distance;
                }
            }

            results.Add(nearest);
        }

        return results;
    }

    // ---------------------------------------------------------- Mesh tier

    private static Dictionary<string, object?> MeshDistance(
        List<ModelItem> listA, List<ModelItem> listB, Document? document)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var clash = doc.GetClash()
            ?? throw new InvalidOperationException(
                "The Clash engine is not available in this Navisworks edition — use method = \"bbox\" instead.");

        var collectionA = NavisValues.ToItemCollection(listA);
        var collectionB = NavisValues.ToItemCollection(listB);

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

        ClosestCoordinates(boxA.Min.X, boxA.Max.X, boxB.Min.X, boxB.Max.X, out var ax, out var bx);
        ClosestCoordinates(boxA.Min.Y, boxA.Max.Y, boxB.Min.Y, boxB.Max.Y, out var ay, out var by);
        ClosestCoordinates(boxA.Min.Z, boxA.Max.Z, boxB.Min.Z, boxB.Max.Z, out var az, out var bz);

        var pointA = new Point3D(ax, ay, az);
        var pointB = new Point3D(bx, by, bz);

        return new Dictionary<string, object?>
        {
            ["distance"] = pointA.DistanceTo(pointB),
            ["pointA"] = pointA,
            ["pointB"] = pointB,
        };
    }

    /// <summary>Shortest distance between two axis-aligned boxes (0 when they touch/overlap).</summary>
    private static double BoxDistanceBetween(BoundingBox3D boxA, BoundingBox3D boxB)
    {
        ClosestCoordinates(boxA.Min.X, boxA.Max.X, boxB.Min.X, boxB.Max.X, out var ax, out var bx);
        ClosestCoordinates(boxA.Min.Y, boxA.Max.Y, boxB.Min.Y, boxB.Max.Y, out var ay, out var by);
        ClosestCoordinates(boxA.Min.Z, boxA.Max.Z, boxB.Min.Z, boxB.Max.Z, out var az, out var bz);
        return new Point3D(ax, ay, az).DistanceTo(new Point3D(bx, by, bz));
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

    /// <summary>
    /// Per-axis closest coordinates of two intervals: when the intervals are
    /// disjoint the nearest faces, when they overlap the midpoint of the
    /// overlap (so touching/intersecting boxes report distance 0 with a
    /// shared witness point).
    /// </summary>
    private static void ClosestCoordinates(
        double minA, double maxA, double minB, double maxB, out double a, out double b)
    {
        if (minB > maxA)
        {
            a = maxA;
            b = minB;
        }
        else if (maxB < minA)
        {
            a = minA;
            b = maxB;
        }
        else
        {
            var mid = (Math.Max(minA, minB) + Math.Min(maxA, maxB)) / 2.0;
            a = mid;
            b = mid;
        }
    }

    private static List<ModelItem> RequireItems(IEnumerable<ModelItem>? items, string parameterName)
    {
        var list = NavisValues.ToItemList(items);
        if (list.Count == 0)
        {
            throw new ArgumentException("No model items provided for '" + parameterName + "'.", parameterName);
        }

        return list;
    }
}
