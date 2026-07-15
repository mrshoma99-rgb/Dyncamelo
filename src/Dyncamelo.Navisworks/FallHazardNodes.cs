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
    /// <param name="band">Half-thickness of the slice around the level; make it span the slab thickness so a full horizontal face is captured.</param>
    /// <param name="cellSize">Grid resolution in document units (smaller = finer and slower).</param>
    /// <param name="minGap">Openings whose widest clear span is at least this need a handrail — these are the flagged ones.</param>
    /// <param name="imagePath">Where to write the PNG heat map. Empty = a file in the temp folder (the path is returned).</param>
    /// <param name="saveViewpoints">True to add one top-down saved viewpoint per flagged opening.</param>
    /// <param name="pixelsPerCell">How many image pixels each grid cell spans (bigger = larger image).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The image path, the flagged-opening count, their widest gaps and centre points, and any saved viewpoints.</returns>
    [NodeName("FallHazard.FloorOpeningMap")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [NodeDescription("Whole-floor fall-hazard heat map. Slices the model at 'level', rasterises the floor and (optional) equipment geometry, finds the openings enclosed by floor, grades each by its widest clear span, and writes a top-down PNG heat map (hot = middle of a big opening). Openings whose widest gap ≥ minGap are flagged and get a saved viewpoint. Reads real mesh geometry so it sees true holes inside a slab. Needs a live Navisworks session; run it once at a level rather than per element.")]
    [NodeSearchTags("fall", "hazard", "opening", "hole", "floor", "handrail", "heatmap", "heat map", "gap", "safety", "plan", "grid", "slab")]
    [MultiReturn("imagePath", "openingCount", "widestGaps", "centers", "viewpoints")]
    public static Dictionary<string, object?> FloorOpeningMap(
        IEnumerable<ModelItem> floors,
        double level,
        IEnumerable<ModelItem>? obstructions = null,
        double band = 1.0,
        double cellSize = 0.25,
        double minGap = 0.5,
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
        var zMin = level - band;
        var zMax = level + band;

        var floorTriangles = GeometryReader.ReadTrianglesInBand(floorList, zMin, zMax);
        if (floorTriangles.Count == 0)
        {
            throw new InvalidOperationException(
                "No floor geometry was found in the band around level " +
                level.ToString(CultureInfo.InvariantCulture) + " (±" + band.ToString(CultureInfo.InvariantCulture) +
                "). Check the level and band match the slab elevation, that the items carry geometry, and that " +
                "this is running inside a live Navisworks session (the geometry read uses the COM bridge).");
        }

        var obstructionList = NavisValues.ToItemList(obstructions);
        var plugTriangles = obstructionList.Count > 0
            ? GeometryReader.ReadTrianglesInBand(obstructionList, zMin, zMax)
            : new List<Tri2>();

        Bounds(floorTriangles, out var minX, out var minY, out var maxX, out var maxY);
        var pad = cellSize * 2.0;
        var result = FloorGapHeatmap.Analyze(
            minX - pad, minY - pad, maxX + pad, maxY + pad, cellSize, floorTriangles, plugTriangles, minGap);

        var path = ResolveImagePath(imagePath, doc);
        var png = FloorGapHeatmap.RenderPng(result, pixelsPerCell);
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

        return new Dictionary<string, object?>
        {
            ["imagePath"] = path,
            ["openingCount"] = flagged.Count,
            ["widestGaps"] = widestGaps,
            ["centers"] = centers,
            ["viewpoints"] = viewpoints,
        };
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
