using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;
using Dyncamelo.Nodes.Spatial;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Spatial clustering: turns loose geometry into logical elements by grouping
/// items that touch (or nearly touch) into connected clusters.
/// </summary>
[NodeCategory("Navisworks.Analysis")]
public static class ClusterNodes
{
    /// <summary>
    /// Groups items into clusters of touching geometry: two items join the
    /// same cluster when the gap between their bounding boxes is at most the
    /// tolerance — directly, or through a chain of other items (so every shape
    /// of a ladder clusters with every other even when only neighbours touch).
    /// Optionally stamps each item with its cluster number as a custom
    /// property, ready for search sets and schedules.
    /// </summary>
    /// <param name="items">The geometry items to group (e.g. all ladder shapes).</param>
    /// <param name="tolerance">Maximum gap that still counts as touching; 0 requires contact. Must be smaller than the space between separate elements.</param>
    /// <param name="units">Unit of the tolerance: "document" uses the file's internal unit (often feet!), or name the unit your number is in.</param>
    /// <param name="propertyName">When set (e.g. "Ladder Number"), each item gets its cluster number written as this custom property.</param>
    /// <param name="tabName">Custom property tab that receives the number.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>Item groups (one list per cluster), each input item's 1-based cluster number (0 = no geometry), cluster count and sizes, and a diagnostic report.</returns>
    [NodeName("Proximity.Cluster")]
    [NodeDescription(
        "Groups items into clusters of touching geometry (bounding-box gap <= tolerance, chained) — turns loose " +
        "shapes into logical elements, e.g. numbering each ladder. Set propertyName to also stamp every item with " +
        "its cluster number as a searchable custom property.")]
    [NodeSearchTags("cluster", "group", "touching", "connected", "proximity", "ladder", "assembly", "clump", "component")]
    [MultiReturn("groups", "clusterNumbers", "clusterCount", "sizes", "report")]
    public static Dictionary<string, object?> Cluster(
        IEnumerable<ModelItem> items,
        double tolerance = 0.01,
        [NodeChoices("document", "Meters", "Millimeters", "Centimeters", "Feet", "Inches")]
        string units = "document",
        string propertyName = "",
        string tabName = "Dyncamelo Data",
        Document? document = null)
    {
        var list = NavisValues.ToItemList(items);
        if (list.Count == 0)
        {
            throw new ArgumentException("No items provided to cluster.", nameof(items));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var scale = ResolveUnitsScale(doc, units, out var unitsLabel);
        var worldTolerance = tolerance * scale;

        var boxes = new List<double[]?>(list.Count);
        int noGeometry = 0;
        foreach (var item in list)
        {
            var box = item.BoundingBox();
            if (box == null || box.IsEmpty)
            {
                boxes.Add(null);
                noGeometry++;
            }
            else
            {
                boxes.Add(new[] { box.Min.X, box.Min.Y, box.Min.Z, box.Max.X, box.Max.Y, box.Max.Z });
            }
        }

        var ids = BoxClusterer.Cluster(boxes, worldTolerance);

        int clusterCount = 0;
        foreach (var id in ids)
        {
            if (id >= clusterCount)
            {
                clusterCount = id + 1;
            }
        }

        var groups = new List<List<ModelItem>>(clusterCount);
        for (int c = 0; c < clusterCount; c++)
        {
            groups.Add(new List<ModelItem>());
        }

        var numbers = new List<int>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            numbers.Add(ids[i] + 1); // 1-based for humans; 0 = no geometry
            if (ids[i] >= 0)
            {
                groups[ids[i]].Add(list[i]);
            }
        }

        var sizes = new List<int>(clusterCount);
        int smallest = int.MaxValue;
        int largest = 0;
        foreach (var group in groups)
        {
            sizes.Add(group.Count);
            smallest = Math.Min(smallest, group.Count);
            largest = Math.Max(largest, group.Count);
        }

        if (propertyName.Trim().Length > 0)
        {
            for (int c = 0; c < groups.Count; c++)
            {
                CustomPropertyNodes.SetCustom(
                    groups[c],
                    new[] { propertyName.Trim() },
                    new object?[] { c + 1 },
                    tabName,
                    merge: true);
            }
        }

        var report =
            list.Count.ToString(CultureInfo.InvariantCulture) + " item(s) -> " +
            clusterCount.ToString(CultureInfo.InvariantCulture) + " cluster(s)" +
            (clusterCount > 0
                ? " (sizes " + smallest.ToString(CultureInfo.InvariantCulture) + "–" + largest.ToString(CultureInfo.InvariantCulture) + ")"
                : string.Empty) +
            ", tolerance " + tolerance.ToString("0.###", CultureInfo.InvariantCulture) + " " + unitsLabel +
            (Math.Abs(scale - 1.0) > 1e-9
                ? " (= " + worldTolerance.ToString("0.###", CultureInfo.InvariantCulture) + " document units)"
                : string.Empty) + "." +
            (noGeometry > 0
                ? " " + noGeometry.ToString(CultureInfo.InvariantCulture) + " item(s) had no geometry box (cluster number 0)."
                : string.Empty) +
            (propertyName.Trim().Length > 0
                ? " Wrote '" + propertyName.Trim() + "' onto every clustered item (tab '" + tabName + "')."
                : string.Empty);

        return new Dictionary<string, object?>
        {
            { "groups", groups },
            { "clusterNumbers", numbers },
            { "clusterCount", clusterCount },
            { "sizes", sizes },
            { "report", report },
        };
    }

    /// <summary>
    /// Converts a user-facing unit choice into a scale onto document units
    /// (same contract as the FallHazard nodes: documents often store feet even
    /// when the measure tool displays metres).
    /// </summary>
    private static double ResolveUnitsScale(Document doc, string? units, out string unitsLabel)
    {
        var trimmed = (units ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Equals("document", StringComparison.OrdinalIgnoreCase))
        {
            unitsLabel = doc.Units.ToString();
            return 1.0;
        }

        if (!Enum.TryParse<Units>(trimmed, true, out var parsed))
        {
            throw new ArgumentException(
                "Unknown units '" + units + "'. Use \"document\" or a Navisworks unit name " +
                "(e.g. \"Meters\", \"Millimeters\", \"Feet\").", nameof(units));
        }

        unitsLabel = parsed.ToString();
        return UnitConversion.ScaleFactor(parsed, doc.Units);
    }
}
