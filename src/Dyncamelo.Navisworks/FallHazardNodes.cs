using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;
using Dyncamelo.Nodes.Spatial;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Whole-floor fall-hazard analysis. Slices the model at a chosen level, rasterises
/// the floor and (optional) equipment/pipe geometry onto a plan grid, finds the
/// openings enclosed by floor, grades each by how far its centre sits from the
/// nearest edge, and exports a top-down heat-map image plus a saved viewpoint per
/// opening that needs a handrail.
/// </summary>
[NodeCategory("Navisworks.Analysis")]
public static class FallHazardNodes
{
    /// <summary>Builds a fall-hazard heat map of the floor openings at a level.</summary>
    /// <param name="floors">The floor/slab items to treat as safe ground (e.g. a search for floor elements).</param>
    /// <param name="level">The elevation (world Z) of the plan to analyse.</param>
    /// <param name="obstructions">Equipment, ducts and pipes that plug openings — where these sit, there is no fall hazard. Optional.</param>
    /// <param name="band">Vertical tolerance for deciding an element crosses the level: an element counts when its bounding box reaches within this of the level. Its full silhouette is then read, so this need not match the slab thickness.</param>
    /// <param name="cellSize">Grid resolution in document units (smaller = finer and slower).</param>
    /// <param name="minGap">The handrail limit as a distance from the nearest floor edge or obstacle (e.g. 0.2 = 20 cm): a void point farther than this from any solid is a hazard. It is the heat-map pivot — cells below it read cool, at it yellow, above it red — and openings that contain such a point are flagged.</param>
    /// <param name="imagePath">Where to write the PNG heat map. Empty = a file in the temp folder (the path is returned).</param>
    /// <param name="saveViewpoints">True to add one top-down saved viewpoint per flagged opening.</param>
    /// <param name="pixelsPerCell">How many image pixels each grid cell spans (bigger = larger image).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The image path, the flagged-opening count, their widest gaps and centre points, any saved viewpoints, and a diagnostic report string (triangles read, openings found, grid size).</returns>
    [NodeName("FallHazard.FloorOpeningMap")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [NodeDescription("Whole-floor fall-hazard heat map. At 'level', reads the filled silhouette of the floor and (optional) equipment elements that cross that plane, finds the voids enclosed by floor, subtracts the equipment that plugs them, and writes a top-down PNG heat map coloured by how far each void point is from the nearest floor edge or obstacle: cool below the 'minGap' limit (within reach of a solid), yellow at it, red beyond it (a genuine fall hazard). Openings that reach past the limit are flagged and get a saved viewpoint. Reads real mesh geometry so it sees true holes inside a slab and equipment passing through them. Keep the floor out of 'obstructions'. Needs a live Navisworks session.")]
    [NodeSearchTags("fall", "hazard", "opening", "hole", "floor", "handrail", "heatmap", "heat map", "gap", "safety", "plan", "grid", "slab")]
    [MultiReturn("imagePath", "openingCount", "widestGaps", "centers", "viewpoints", "report")]
    public static Dictionary<string, object?> FloorOpeningMap(
        IEnumerable<ModelItem> floors,
        double level,
        IEnumerable<ModelItem>? obstructions = null,
        double band = 1.0,
        double cellSize = 0.25,
        double minGap = 0.2,
        string? imagePath = null,
        bool saveViewpoints = true,
        int pixelsPerCell = 6,
        Document? document = null)
    {
        var floorList = NavisValues.ToItemList(floors);
        if (floorList.Count == 0)
        {
            throw new ArgumentException("No floor items provided.", nameof(floors));
        }

        if (band <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(band), "The band must be positive.");
        }

        if (cellSize <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), "The cell size must be positive.");
        }

        if (pixelsPerCell < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelsPerCell), "There must be at least one pixel per cell.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        // Select whole elements whose bounding box crosses the level (band = vertical
        // tolerance), then read ALL of their triangles — the filled silhouette. A slab
        // contributes its holed top/bottom faces; equipment passing through contributes
        // its full footprint, including the horizontal cap faces a thin Z-band around the
        // level would miss. (Missing those caps was why equipment read as open floor, not
        // as a plug: its side walls alone project to outlines, not a filled area.)
        var floorLeaves = SelectSpanningLeaves(NormalizeToObjects(floorList), level, band, out var floorMinZ, out var floorMaxZ, out var floorHasBox);
        var floorTriangles = GeometryReader
            .ReadTrianglesInBand(floorLeaves, double.NegativeInfinity, double.PositiveInfinity).Triangles;
        if (floorTriangles.Count == 0)
        {
            throw new InvalidOperationException(
                BuildEmptyFloorMessage(floorLeaves.Count, floorHasBox, floorMinZ, floorMaxZ, level, band));
        }

        var obstructionList = NavisValues.ToItemList(obstructions);
        var plugTriangles = new List<Tri2>();
        var plugLeafCount = 0;
        if (obstructionList.Count > 0)
        {
            var plugLeaves = SelectSpanningLeaves(NormalizeToObjects(obstructionList), level, band, out _, out _, out _);
            plugLeafCount = plugLeaves.Count;
            plugTriangles = GeometryReader
                .ReadTrianglesInBand(plugLeaves, double.NegativeInfinity, double.PositiveInfinity).Triangles;
        }

        Bounds(floorTriangles, out var minX, out var minY, out var maxX, out var maxY);
        var pad = cellSize * 2.0;
        var result = FloorGapHeatmap.Analyze(
            minX - pad, minY - pad, maxX + pad, maxY + pad, cellSize, floorTriangles, plugTriangles, minGap);

        var path = ResolveImagePath(imagePath, doc);
        var png = FloorGapHeatmap.RenderPng(result, pixelsPerCell, minGap);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllBytes(path, png);

        var widestGaps = new List<double>();
        var centers = new List<Point3D>();
        var flagged = new List<FloorOpening>();
        foreach (var opening in result.Openings)
        {
            if (!opening.Flagged)
            {
                continue;
            }

            flagged.Add(opening);
            widestGaps.Add(opening.WidestGap);
            centers.Add(new Point3D(opening.CenterX, opening.CenterY, level));
        }

        var viewpoints = saveViewpoints && flagged.Count > 0
            ? CreateViewpoints(doc, flagged, level, band, cellSize)
            : new List<SavedViewpoint>();

        var hazardCells = 0;
        foreach (var isHazard in result.Hazard)
        {
            if (isHazard)
            {
                hazardCells++;
            }
        }

        var report = BuildReport(
            level, band, cellSize, result,
            floorTriangles.Count, floorLeaves.Count, floorMinZ, floorMaxZ,
            plugTriangles.Count, plugLeafCount, obstructionList.Count,
            flagged.Count, minGap, hazardCells);

        return new Dictionary<string, object?>
        {
            ["imagePath"] = path,
            ["openingCount"] = flagged.Count,
            ["widestGaps"] = widestGaps,
            ["centers"] = centers,
            ["viewpoints"] = viewpoints,
            ["report"] = report,
        };
    }

    /// <summary>A one-line summary of what the analysis actually saw, for diagnosis.</summary>
    private static string BuildReport(
        double level, double band, double cellSize, FloorGapResult result,
        int floorTriangles, int floorLeaves, double floorMinZ, double floorMaxZ,
        int plugTriangles, int plugLeaves, int obstructionCount,
        int flaggedCount, double minGap, int hazardCells)
    {
        string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        var openArea = hazardCells * cellSize * cellSize;
        var largest = result.Openings.Count > 0 ? result.Openings[0].WidestGap : 0.0;

        return
            "Level " + F(level) + " (±" + F(band) + "). " +
            "Floor: " + floorTriangles + " triangles from " + floorLeaves + " geometry item(s), world Z " +
            F(floorMinZ) + " to " + F(floorMaxZ) + ". " +
            "Equipment: " + plugTriangles + " triangles from " + plugLeaves + " of " + obstructionCount + " item(s). " +
            "Grid " + result.Cols + "×" + result.Rows + " @ " + F(cellSize) + ". " +
            "Openings found: " + result.Openings.Count + " (" + flaggedCount + " ≥ minGap " + F(minGap) + "), " +
            "open area " + F(openArea) + ", largest gap " + F(largest) + ".";
    }

    /// <summary>Classifies each floor edge along a void as safe, handrail-protected, or dangerous.</summary>
    /// <param name="floors">The floor/slab items to treat as safe ground.</param>
    /// <param name="level">The elevation (world Z) of the plan to analyse.</param>
    /// <param name="handrails">The handrail items, picked at any selection-tree level (a leaf, the railing object or a whole group all resolve to the same railings). Only railings standing on this level (bbox underside within ±band) count; their geometry is projected flat onto the plan (posts and rails alike), so a handrail protects only the length it actually runs along an edge.</param>
    /// <param name="obstructions">Equipment/ducts/pipes that plug openings; a floor edge facing one at less than the limit is safe. Optional.</param>
    /// <param name="band">Vertical tolerance for deciding a floor/equipment element crosses the level.</param>
    /// <param name="cellSize">Grid resolution in document units (smaller = finer, use a few times below the limit).</param>
    /// <param name="limit">A floor edge needs a handrail when the void it faces is locally at least this wide (e.g. 0.2 = 20 cm, document units) — a narrower gap cannot be fallen through however long it runs.</param>
    /// <param name="handrailTolerance">A handrail within this distance of a dangerous edge protects it.</param>
    /// <param name="imagePath">Where to write the PNG (empty = a temp file; the path is returned).</param>
    /// <param name="pixelsPerCell">Image pixels per grid cell.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The image path, the dangerous/protected/safe edge lengths, and a diagnostic report.</returns>
    [NodeName("FallHazard.EdgeHandrailCheck")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [NodeDescription("Marks the floor edges around voids: green where the gap across the void is under the limit (safe), red where it is over the limit and there is no handrail (needs one), and blue where a handrail covers it. Handrail geometry is projected flat onto the plan — posts and rails, corners and all — and matched per length, so a 1 m handrail on a 5 m edge protects only its 1 m. Writes a top-down PNG and reports the dangerous / protected / safe edge lengths. Needs a live Navisworks session.")]
    [NodeSearchTags("fall", "hazard", "edge", "handrail", "guardrail", "railing", "floor", "void", "opening", "safety", "protected", "perimeter")]
    [MultiReturn("imagePath", "dangerousLength", "protectedLength", "safeLength", "report")]
    public static Dictionary<string, object?> EdgeHandrailCheck(
        IEnumerable<ModelItem> floors,
        double level,
        IEnumerable<ModelItem> handrails,
        IEnumerable<ModelItem>? obstructions = null,
        double band = 1.0,
        double cellSize = 0.1,
        double limit = 0.2,
        double handrailTolerance = 0.3,
        string? imagePath = null,
        int pixelsPerCell = 6,
        Document? document = null)
    {
        var floorList = NavisValues.ToItemList(floors);
        if (floorList.Count == 0)
        {
            throw new ArgumentException("No floor items provided.", nameof(floors));
        }

        if (band <= 0.0) throw new ArgumentOutOfRangeException(nameof(band), "The band must be positive.");
        if (cellSize <= 0.0) throw new ArgumentOutOfRangeException(nameof(cellSize), "The cell size must be positive.");
        if (limit <= 0.0) throw new ArgumentOutOfRangeException(nameof(limit), "The limit must be positive.");
        if (pixelsPerCell < 1) throw new ArgumentOutOfRangeException(nameof(pixelsPerCell), "There must be at least one pixel per cell.");

        var doc = NavisworksContext.ResolveDocument(document);

        var floorLeaves = SelectSpanningLeaves(NormalizeToObjects(floorList), level, band, out var floorMinZ, out var floorMaxZ, out var floorHasBox);
        var floorTriangles = GeometryReader
            .ReadTrianglesInBand(floorLeaves, double.NegativeInfinity, double.PositiveInfinity).Triangles;
        if (floorTriangles.Count == 0)
        {
            throw new InvalidOperationException(
                BuildEmptyFloorMessage(floorLeaves.Count, floorHasBox, floorMinZ, floorMaxZ, level, band));
        }

        var obstructionList = NavisValues.ToItemList(obstructions);
        var plugTriangles = new List<Tri2>();
        if (obstructionList.Count > 0)
        {
            var plugLeaves = SelectSpanningLeaves(NormalizeToObjects(obstructionList), level, band, out _, out _, out _);
            plugTriangles = GeometryReader
                .ReadTrianglesInBand(plugLeaves, double.NegativeInfinity, double.PositiveInfinity).Triangles;
        }

        // Handrails sit above the floor and don't cross the level, so read ALL of
        // each relevant handrail's geometry (posts and rails alike) and let the
        // projection to the plan (dropping Z) place it. Normalising to whole
        // object-level elements first makes the result independent of where in
        // the selection tree the handrail was picked (a single post leaf, the
        // railing object or a group all resolve to the same railings). A railing
        // is relevant to this level when it stands on it: bbox underside within
        // ±band of the level — so railings of other storeys never protect here.
        var handrailObjects = NormalizeToObjects(handrails);
        var handrailLeaves = new List<ModelItem>();
        var handrailsOnLevel = 0;
        foreach (var railing in handrailObjects)
        {
            var box = railing.BoundingBox();
            if (box == null || box.IsEmpty)
            {
                continue;
            }

            if (box.Min.Z >= level - band && box.Min.Z <= level + band)
            {
                handrailsOnLevel++;
                handrailLeaves.AddRange(ModelItemNodes.GeometryLeaves(new[] { railing }));
            }
        }

        var handrailTriangles = handrailLeaves.Count > 0
            ? GeometryReader.ReadTrianglesInBand(
                handrailLeaves, double.NegativeInfinity, double.PositiveInfinity).Triangles
            : new List<Tri2>();

        Bounds(floorTriangles, out var minX, out var minY, out var maxX, out var maxY);
        var pad = cellSize * 2.0;
        var result = FloorGapHeatmap.Analyze(
            minX - pad, minY - pad, maxX + pad, maxY + pad, cellSize, floorTriangles, plugTriangles, limit);
        var edges = FloorGapHeatmap.AnalyzeEdges(result, handrailTriangles, limit, handrailTolerance);

        var path = ResolveImagePath(imagePath, doc);
        var png = FloorGapHeatmap.RenderEdgePng(edges, pixelsPerCell);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllBytes(path, png);

        string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        var report =
            "Level " + F(level) + " (±" + F(band) + "), limit " + F(limit) + ", handrail tol " + F(handrailTolerance) + ". " +
            "Floor: " + floorTriangles.Count + " triangles, world Z " + F(floorMinZ) + " to " + F(floorMaxZ) + ". " +
            "Handrails: " + handrailObjects.Count + " element(s), " + handrailsOnLevel + " on this level, " +
            handrailTriangles.Count + " triangles. " +
            "Grid " + result.Cols + "×" + result.Rows + " @ " + F(cellSize) + ". " +
            "Edge length — dangerous " + F(edges.DangerousLength) + ", protected " + F(edges.ProtectedLength) +
            ", safe " + F(edges.SafeLength) + ".";

        return new Dictionary<string, object?>
        {
            ["imagePath"] = path,
            ["dangerousLength"] = edges.DangerousLength,
            ["protectedLength"] = edges.ProtectedLength,
            ["safeLength"] = edges.SafeLength,
            ["report"] = report,
        };
    }

    private static string BuildEmptyFloorMessage(
        int spanningLeafCount, bool hasBox, double minZ, double maxZ, double level, double band)
    {
        string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        if (!hasBox)
        {
            return "No floor geometry could be read from the given items. Make sure they carry geometry " +
                "and that this is running inside a live Navisworks session (the geometry read uses the COM " +
                "bridge, which is unavailable when running headless).";
        }

        if (spanningLeafCount == 0)
        {
            return "Floor elements were found, but their geometry sits at world Z " + F(minZ) + " to " + F(maxZ) +
                ", which does not reach the level " + F(level) + " (±" + F(band) + " tolerance). These are world " +
                "coordinates — the model may be offset from your project datum, or in different units. Set 'level' " +
                "between " + F(minZ) + " and " + F(maxZ) + ".";
        }

        return "Floor elements crossing the level were found, but no mesh triangles could be read from them " +
            "(the COM geometry bridge returned nothing). This usually means it is not running inside a live " +
            "Navisworks session.";
    }

    /// <summary>
    /// Normalizes picked items to whole object-level elements, so the result does
    /// not depend on where in the selection tree the user picked: a geometry leaf
    /// (one post of a railing), the object itself, or a group above it all resolve
    /// to the same element set. Each item is expanded to its geometry leaves and
    /// every leaf is lifted to its first composite-object ancestor (the leaf
    /// itself when it has none), deduplicated.
    /// </summary>
    private static List<ModelItem> NormalizeToObjects(IEnumerable<ModelItem>? items)
    {
        var objects = new List<ModelItem>();
        var seen = new ModelItemSet();
        foreach (var item in NavisValues.ToItemList(items))
        {
            foreach (var leaf in ModelItemNodes.GeometryLeaves(new[] { item }))
            {
                var element = leaf.FindFirstObjectAncestor() ?? leaf;
                if (seen.Add(element))
                {
                    objects.Add(element);
                }
            }
        }

        return objects;
    }

    /// <summary>
    /// The geometry leaves of every element whose bounding box reaches within
    /// <paramref name="band"/> of the level — i.e. the elements that cross the
    /// analysis plane. Reports the overall world-Z range seen for diagnostics.
    /// </summary>
    private static List<ModelItem> SelectSpanningLeaves(
        List<ModelItem> items, double level, double band,
        out double minZ, out double maxZ, out bool hasBox)
    {
        minZ = double.PositiveInfinity;
        maxZ = double.NegativeInfinity;
        hasBox = false;
        var lo = level - band;
        var hi = level + band;
        var leaves = new List<ModelItem>();

        foreach (var item in items)
        {
            if (item == null)
            {
                continue;
            }

            var box = item.BoundingBox();
            if (box == null || box.IsEmpty)
            {
                continue;
            }

            hasBox = true;
            if (box.Min.Z < minZ) minZ = box.Min.Z;
            if (box.Max.Z > maxZ) maxZ = box.Max.Z;

            if (box.Min.Z <= hi && box.Max.Z >= lo)
            {
                leaves.AddRange(ModelItemNodes.GeometryLeaves(new[] { item }));
            }
        }

        return leaves;
    }

    private static void Bounds(
        List<Tri2> triangles, out double minX, out double minY, out double maxX, out double maxY)
    {
        minX = double.PositiveInfinity;
        minY = double.PositiveInfinity;
        maxX = double.NegativeInfinity;
        maxY = double.NegativeInfinity;
        foreach (var t in triangles)
        {
            minX = Math.Min(minX, Math.Min(t.Ax, Math.Min(t.Bx, t.Cx)));
            maxX = Math.Max(maxX, Math.Max(t.Ax, Math.Max(t.Bx, t.Cx)));
            minY = Math.Min(minY, Math.Min(t.Ay, Math.Min(t.By, t.Cy)));
            maxY = Math.Max(maxY, Math.Max(t.Ay, Math.Max(t.By, t.Cy)));
        }
    }

    private static string ResolveImagePath(string? imagePath, Document doc)
    {
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            return imagePath!;
        }

        var stem = string.IsNullOrEmpty(doc.Title) ? "model" : Path.GetFileNameWithoutExtension(doc.Title);
        var name = "Dyncamelo_FloorOpenings_" + stem + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png";
        return Path.Combine(Path.GetTempPath(), name);
    }

    private static List<SavedViewpoint> CreateViewpoints(
        Document doc, List<FloorOpening> openings, double level, double band, double cellSize)
    {
        var saved = new List<SavedViewpoint>();
        var viewpoints = doc.SavedViewpoints;

        const string folderName = "Floor Openings";
        var folderIndex = NavisValues.FindTopLevelIndex<FolderItem>(viewpoints.Value, folderName);
        if (folderIndex < 0)
        {
            viewpoints.AddCopy(new FolderItem { DisplayName = folderName });
            folderIndex = NavisValues.FindTopLevelIndex<FolderItem>(viewpoints.Value, folderName);
        }

        var folder = folderIndex >= 0 ? (FolderItem)viewpoints.Value[folderIndex] : null;

        var index = 1;
        foreach (var opening in openings)
        {
            var name = "Opening " + index.ToString(CultureInfo.InvariantCulture) +
                       " — gap " + opening.WidestGap.ToString("0.##", CultureInfo.InvariantCulture);
            index++;

            var viewpoint = BuildTopDownViewpoint(doc, opening, level, band, cellSize);
            var savedViewpoint = new SavedViewpoint(viewpoint) { DisplayName = name };

            if (folder != null)
            {
                viewpoints.AddCopy(folder, savedViewpoint);
            }
            else
            {
                viewpoints.AddCopy(savedViewpoint);
            }

            var children = folder != null ? folder.Children : viewpoints.Value;
            var storedIndex = NavisValues.FindTopLevelIndex<SavedViewpoint>(children, name);
            saved.Add(storedIndex >= 0 ? (SavedViewpoint)children[storedIndex] : savedViewpoint);
        }

        return saved;
    }

    private static Viewpoint BuildTopDownViewpoint(
        Document doc, FloorOpening opening, double level, double band, double cellSize)
    {
        var viewpoint = doc.CurrentViewpoint.CreateCopy();
        viewpoint.Projection = ViewpointProjection.Orthographic;

        var cx = (opening.MinX + opening.MaxX) * 0.5;
        var cy = (opening.MinY + opening.MaxY) * 0.5;
        var target = new Point3D(cx, cy, level);

        var span = Math.Max(opening.MaxX - opening.MinX, opening.MaxY - opening.MinY);
        var eyeHeight = Math.Max(span * 2.0, band * 4.0 + 5.0);
        viewpoint.Position = new Point3D(cx, cy, level + eyeHeight);
        viewpoint.PointAt(target);
        viewpoint.AlignUp(new Vector3D(0, 1, 0)); // north up

        var framePad = span * 0.6 + cellSize * 4.0;
        var box = new BoundingBox3D(
            new Point3D(opening.MinX - framePad, opening.MinY - framePad, level - band),
            new Point3D(opening.MaxX + framePad, opening.MaxY + framePad, level + band));
        viewpoint.ZoomBox(box);
        return viewpoint;
    }
}
