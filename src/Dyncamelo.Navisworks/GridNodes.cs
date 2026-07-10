using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Read-only access to the model's grid systems (Quick Find grids/levels — the
/// data source models like Revit/IFC publish). Elevations and positions are in
/// document units — chain Units.Convert for meters/feet.
///
/// The 2024 API exposes no "all intersections" collection — only
/// <c>ClosestIntersection(Point3D)</c> queries — so Grids.Intersections
/// discovers intersections by sampling the model's bounding box per level and
/// then completing the line-pair lattice from what the samples found.
/// </summary>
[NodeCategory("Navisworks.Grids")]
public static class GridNodes
{
    /// <summary>Hard cap on intersections discovered per level (runaway-guard).</summary>
    private const int MaxIntersectionsPerLevel = 10000;

    /// <summary>The model's own levels (names + elevations), read from the active grid system.</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>Level names, their elevations (document units) and the GridLevel objects, in grid-system order.</returns>
    [NodeName("Grids.Levels")]
    [NodeDescription("The model's own levels from the active grid system — names and elevations (document units) without hand-typed elevation lists. Grid data comes from source models (e.g. Revit/IFC) and is read-only.")]
    [NodeSearchTags("grid", "level", "levels", "elevation", "storey", "story", "floor")]
    [MultiReturn("names", "elevations", "levels")]
    public static Dictionary<string, object?> Levels(Document? document = null)
    {
        var system = ResolveActiveSystem(document);

        var names = new List<string>();
        var elevations = new List<double>();
        var levels = new List<GridLevel>();
        foreach (var level in system.Levels)
        {
            if (level == null)
            {
                continue;
            }

            names.Add(level.DisplayName ?? string.Empty);
            elevations.Add(level.Elevation);
            levels.Add(level);
        }

        return new Dictionary<string, object?>
        {
            ["names"] = names,
            ["elevations"] = elevations,
            ["levels"] = levels,
        };
    }

    /// <summary>All grid intersections of the active grid system, discovered per level.</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <param name="samples">Sampling density per axis used to seed the discovery (higher finds denser grids; 2-100).</param>
    /// <returns>Intersection names (e.g. "A-1"), their positions (document units) and the level each lies on.</returns>
    [NodeName("Grids.Intersections")]
    [NodeDescription("All grid intersections of the active grid system, per level — names like \"A-1\" with positions in document units. The API only answers closest-intersection queries, so intersections are discovered by sampling the model's bounding box and completing the line lattice; raise samples if a very dense grid comes back incomplete.")]
    [NodeSearchTags("grid", "intersection", "intersections", "gridline", "axis", "lattice")]
    [MultiReturn("names", "points", "levelNames")]
    public static Dictionary<string, object?> Intersections(Document? document = null, int samples = 20)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var system = ResolveActiveSystem(doc);
        var density = Math.Max(2, Math.Min(100, samples));

        var bounds = doc.GetBoundingBox(false);
        if (bounds == null || bounds.IsEmpty)
        {
            throw new InvalidOperationException(
                "The model has no bounding box to sample for grid intersections — the document contains no geometry.");
        }

        var names = new List<string>();
        var points = new List<Point3D>();
        var levelNames = new List<string>();
        foreach (var level in system.Levels)
        {
            if (level == null)
            {
                continue;
            }

            var found = DiscoverLevelIntersections(level, bounds, density);
            foreach (var entry in found)
            {
                names.Add(entry.Name);
                points.Add(new Point3D(entry.X, entry.Y, entry.Z));
                levelNames.Add(level.DisplayName ?? string.Empty);
            }
        }

        return new Dictionary<string, object?>
        {
            ["names"] = names,
            ["points"] = points,
            ["levelNames"] = levelNames,
        };
    }

    /// <summary>The grid intersection and level nearest to a point.</summary>
    /// <param name="point">The query point (document units) — e.g. a ClashResult center or a bounding-box center.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The intersection name (e.g. "B-3"), its position, the level name, and a combined "B-3 : Level 2" label.</returns>
    [NodeName("Grids.ClosestIntersection")]
    [NodeDescription("The grid intersection and level nearest to any point — ready-made \"B-3 : Level 2\" location labels for clash naming, reports and zone tagging. Document units.")]
    [NodeSearchTags("grid", "intersection", "closest", "nearest", "location", "label", "level")]
    [MultiReturn("name", "position", "levelName", "label")]
    public static Dictionary<string, object?> ClosestIntersection(object point, Document? document = null)
    {
        var queryPoint = NavisValues.ToPoint3D(point);
        var system = ResolveActiveSystem(document);

        var intersection = system.ClosestIntersection(queryPoint);
        if (intersection == null)
        {
            throw new InvalidOperationException(
                "The active grid system has no grid intersections (it needs at least one level and two crossing grid lines).");
        }

        var name = intersection.DisplayName ?? string.Empty;
        var position = intersection.Position;
        var levelName = intersection.Level?.DisplayName ?? string.Empty;

        string label;
        try
        {
            // Navisworks' own combined formatting ("B-3 : Level 2"); factor 1.0 = document units.
            label = intersection.FormatCombinedDisplayString(queryPoint, 1.0);
        }
        catch (Exception)
        {
            label = null!;
        }

        if (string.IsNullOrEmpty(label))
        {
            label = string.IsNullOrEmpty(levelName) ? name : name + " : " + levelName;
        }

        return new Dictionary<string, object?>
        {
            ["name"] = name,
            ["position"] = position == null ? null : new Point3D(position.X, position.Y, position.Z),
            ["levelName"] = levelName,
            ["label"] = label,
        };
    }

    // ------------------------------------------------------------- helpers

    /// <summary>One discovered intersection on a level (values copied out of the native handles).</summary>
    private sealed class IntersectionEntry
    {
        public string Name = string.Empty;
        public double X;
        public double Y;
        public double Z;
        public string Line1 = string.Empty;
        public string Line2 = string.Empty;
        public double Dir1X;
        public double Dir1Y;
        public double Dir2X;
        public double Dir2Y;
    }

    /// <summary>
    /// Resolves the document's active grid system or fails with a node-friendly
    /// message (grid-less models are common — grids come from source models).
    /// </summary>
    private static GridSystem ResolveActiveSystem(Document? document)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var system = doc.Grids?.ActiveSystem;
        if (system == null)
        {
            throw new InvalidOperationException(
                "The document has no active grid system. Grids and levels come from source models " +
                "(e.g. Revit or IFC files that publish grids) — this model carries none.");
        }

        return system;
    }

    /// <summary>
    /// Discovers a level's intersections in two phases: (1) seed by sampling a
    /// lattice over the model bounds and snapping each sample to its closest
    /// intersection; (2) complete the grid by intersecting every known line
    /// pair in plan and snapping those candidate points too, until no new
    /// intersection appears. Correctness note: snapping can only ever return
    /// real intersections, so wrong candidates cost a query but never invent
    /// data; lines whose every intersection is far outside the model bounds can
    /// still be missed (raise samples).
    /// </summary>
    private static List<IntersectionEntry> DiscoverLevelIntersections(
        GridLevel level, BoundingBox3D bounds, int density)
    {
        var byName = new Dictionary<string, IntersectionEntry>(StringComparer.Ordinal);
        var order = new List<IntersectionEntry>();

        // Phase 1: lattice sampling over the model's plan extent at this level.
        double minX = bounds.Min.X, maxX = bounds.Max.X;
        double minY = bounds.Min.Y, maxY = bounds.Max.Y;
        double stepX = (maxX - minX) / (density - 1);
        double stepY = (maxY - minY) / (density - 1);
        for (int i = 0; i < density && order.Count < MaxIntersectionsPerLevel; i++)
        {
            for (int j = 0; j < density && order.Count < MaxIntersectionsPerLevel; j++)
            {
                var sample = new Point3D(minX + i * stepX, minY + j * stepY, level.Elevation);
                AddIntersection(level.ClosestIntersection(sample), byName, order);
            }
        }

        // Phase 2: line-pair completion. Every intersection names its two lines
        // and their plan directions; intersecting each known line-1 with each
        // known line-2 yields candidate points for pairs the sampling missed.
        var knownPairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in order)
        {
            knownPairs.Add(entry.Line1 + "\u001f" + entry.Line2);
        }

        var scale = Math.Max(Math.Max(maxX - minX, maxY - minY), 1e-9);
        for (int pass = 0; pass < 10; pass++)
        {
            var line1Reps = CollectLineReps(order, first: true);
            var line2Reps = CollectLineReps(order, first: false);

            bool foundNew = false;
            foreach (var l1 in line1Reps)
            {
                foreach (var l2 in line2Reps)
                {
                    if (order.Count >= MaxIntersectionsPerLevel)
                    {
                        return order;
                    }

                    if (!knownPairs.Add(l1.Key + "\u001f" + l2.Key))
                    {
                        continue;
                    }

                    if (!TryIntersectInPlan(l1.Value, l2.Value, scale, out var x, out var y))
                    {
                        continue; // parallel or degenerate in plan
                    }

                    var candidate = new Point3D(x, y, level.Elevation);
                    if (AddIntersection(level.ClosestIntersection(candidate), byName, order))
                    {
                        foundNew = true;
                    }
                }
            }

            if (!foundNew)
            {
                break;
            }
        }

        return order;
    }

    /// <summary>
    /// Records an intersection when it is new (keyed by display name within the
    /// level). Values are copied out immediately so nothing downstream depends
    /// on the native handle's lifetime. Returns true when a new entry was added.
    /// </summary>
    private static bool AddIntersection(
        GridIntersection? intersection,
        Dictionary<string, IntersectionEntry> byName,
        List<IntersectionEntry> order)
    {
        if (intersection == null)
        {
            return false;
        }

        var name = intersection.DisplayName ?? string.Empty;
        if (name.Length == 0 || byName.ContainsKey(name))
        {
            return false;
        }

        var position = intersection.Position;
        if (position == null)
        {
            return false;
        }

        var entry = new IntersectionEntry
        {
            Name = name,
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            Line1 = intersection.Line1?.DisplayName ?? string.Empty,
            Line2 = intersection.Line2?.DisplayName ?? string.Empty,
        };

        var dir1 = intersection.Line1Direction;
        var dir2 = intersection.Line2Direction;
        entry.Dir1X = dir1?.X ?? 0.0;
        entry.Dir1Y = dir1?.Y ?? 0.0;
        entry.Dir2X = dir2?.X ?? 0.0;
        entry.Dir2Y = dir2?.Y ?? 0.0;

        byName.Add(name, entry);
        order.Add(entry);
        return true;
    }

    /// <summary>
    /// One representative (anchor point + plan direction) per distinct grid line
    /// seen so far. <paramref name="first"/> selects the intersection's Line1 or
    /// Line2 family.
    /// </summary>
    private static Dictionary<string, IntersectionEntry> CollectLineReps(
        List<IntersectionEntry> entries, bool first)
    {
        var reps = new Dictionary<string, IntersectionEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var lineName = first ? entry.Line1 : entry.Line2;
            if (lineName.Length > 0 && !reps.ContainsKey(lineName))
            {
                reps.Add(lineName, entry);
            }
        }

        return reps;
    }

    /// <summary>
    /// Plan (XY) intersection of line 1 through <paramref name="a"/>'s position
    /// along its Dir1 with line 2 through <paramref name="b"/>'s position along
    /// its Dir2. False for (near-)parallel or direction-less lines. Straight
    /// lines assumed — for arc grids the candidate merely lands near the true
    /// intersection, and the closest-intersection snap resolves it.
    /// </summary>
    private static bool TryIntersectInPlan(
        IntersectionEntry a, IntersectionEntry b, double scale, out double x, out double y)
    {
        double d1x = a.Dir1X, d1y = a.Dir1Y;
        double d2x = b.Dir2X, d2y = b.Dir2Y;
        x = 0;
        y = 0;

        var denominator = d1x * d2y - d1y * d2x;
        if (Math.Abs(denominator) < 1e-9)
        {
            return false;
        }

        var t = ((b.X - a.X) * d2y - (b.Y - a.Y) * d2x) / denominator;
        x = a.X + t * d1x;
        y = a.Y + t * d1y;

        // Reject candidates absurdly far outside the model (broken directions).
        if (double.IsNaN(x) || double.IsNaN(y) ||
            Math.Abs(x - a.X) > scale * 100 || Math.Abs(y - a.Y) > scale * 100)
        {
            return false;
        }

        return true;
    }
}
