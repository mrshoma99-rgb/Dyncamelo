using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Zone assignment (iConstruct Zone Tool style): tag target items with the name
/// of the zone volume that contains them. Containment is the axis-aligned
/// bounding-box test "zone box contains the target's box center" — fast and
/// robust, but a coarse approximation for non-box-shaped zone geometry. The tag
/// is written as a user-defined property tab via the COM bridge, so it is
/// searchable, schedulable, and travels with the NWF/NWD (source files are
/// never modified). Works only inside a live Navisworks session.
/// </summary>
[NodeCategory("Navisworks.Takeoff")]
public static class ZoneNodes
{
    /// <summary>Tags each target item with the name of the zone volume containing its bounding-box center.</summary>
    /// <param name="zoneItems">The zone volume items (e.g. rooms, provisional spaces or mass elements).</param>
    /// <param name="zoneNames">Zone names, index-aligned with <paramref name="zoneItems"/> (null/empty falls back to each zone item's display name).</param>
    /// <param name="targetItems">The items to tag.</param>
    /// <param name="tabName">User-visible property tab name the zone value is written to.</param>
    /// <param name="propertyName">Property name inside the tab that carries the zone name.</param>
    /// <returns>The target items (pass-through) and how many of them landed inside a zone.</returns>
    [NodeName("Zone.AssignByVolumes")]
    [NodeDescription("Tags each target with the name of the zone volume containing its bounding-box center — the iConstruct Zone Tool as one node. The first zone (in list order) that contains an item wins; items inside no zone are left untouched. Written as a searchable user property tab (persists in NWF/NWD only).")]
    [NodeSearchTags("zone", "assign", "volume", "room", "area", "tag", "spatial", "contains")]
    [MultiReturn("items", "assignedCount")]
    public static Dictionary<string, object?> AssignByVolumes(
        IEnumerable<ModelItem> zoneItems,
        IEnumerable<string>? zoneNames,
        IEnumerable<ModelItem> targetItems,
        string tabName = "Zones",
        string propertyName = "Zone")
    {
        var zones = NavisValues.ToItemList(zoneItems);
        if (zones.Count == 0)
        {
            throw new ArgumentException("No zone items provided.", nameof(zoneItems));
        }

        var targets = NavisValues.ToItemList(targetItems);
        if (targets.Count == 0)
        {
            throw new ArgumentException("No target items provided.", nameof(targetItems));
        }

        if (string.IsNullOrWhiteSpace(tabName))
        {
            throw new ArgumentException("No tab name provided.", nameof(tabName));
        }

        if (string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException("No property name provided.", nameof(propertyName));
        }

        var names = ResolveZoneNames(zones, zoneNames);

        // Precompute zone boxes; zones without geometry cannot contain anything.
        var zoneBoxes = new List<BoundingBox3D?>(zones.Count);
        int usableZones = 0;
        for (int i = 0; i < zones.Count; i++)
        {
            var box = zones[i].BoundingBox();
            if (box == null || box.IsEmpty)
            {
                zoneBoxes.Add(null);
            }
            else
            {
                zoneBoxes.Add(box);
                usableZones++;
            }
        }

        if (usableZones == 0)
        {
            throw new ArgumentException(
                "None of the zone items has a bounding box — they carry no geometry, so no zone can contain anything.",
                nameof(zoneItems));
        }

        int assigned = 0;
        foreach (var target in targets)
        {
            var targetBox = target.BoundingBox();
            if (targetBox == null || targetBox.IsEmpty)
            {
                continue; // no geometry, no center to test
            }

            var center = targetBox.Center;
            for (int i = 0; i < zones.Count; i++)
            {
                var zoneBox = zoneBoxes[i];
                if (zoneBox == null || !zoneBox.Contains(center))
                {
                    continue;
                }

                ComBridge.SetUserDefinedTab(
                    target,
                    tabName,
                    null,
                    new[] { new KeyValuePair<string, object?>(propertyName, names[i]) },
                    merge: true);
                assigned++;
                break; // first matching zone in list order wins
            }
        }

        return new Dictionary<string, object?>
        {
            ["items"] = targets,
            ["assignedCount"] = assigned,
        };
    }

    /// <summary>
    /// Aligns zone names with zone items: explicit names must match the item
    /// count one-to-one; a missing list falls back to each zone item's display
    /// name (which must then be non-empty).
    /// </summary>
    private static List<string> ResolveZoneNames(List<ModelItem> zones, IEnumerable<string>? zoneNames)
    {
        var names = zoneNames == null ? new List<string>() : new List<string>(zoneNames);
        if (names.Count == 0)
        {
            foreach (var zone in zones)
            {
                var fallback = zone.DisplayName;
                if (string.IsNullOrEmpty(fallback))
                {
                    fallback = zone.ClassDisplayName;
                }

                if (string.IsNullOrEmpty(fallback))
                {
                    throw new ArgumentException(
                        "No zone names provided and a zone item has no display name to fall back on — wire zoneNames explicitly.",
                        nameof(zoneNames));
                }

                names.Add(fallback);
            }

            return names;
        }

        if (names.Count != zones.Count)
        {
            throw new ArgumentException(
                "zoneItems and zoneNames must be the same length (got " + zones.Count +
                " zones and " + names.Count + " names).", nameof(zoneNames));
        }

        for (int i = 0; i < names.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(names[i]))
            {
                throw new ArgumentException(
                    "Zone name at index " + i + " is empty — every zone needs a name.", nameof(zoneNames));
            }
        }

        return names;
    }
}
