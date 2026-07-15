using System;
using System.Collections.Generic;
using Dyncamelo.Nodes.Imaging;

namespace Dyncamelo.Nodes.Spatial;

/// <summary>A triangle projected onto the analysis plane (world XY, Z dropped).</summary>
public readonly struct Tri2
{
    /// <summary>Creates a 2D triangle from its three vertices' X/Y coordinates.</summary>
    public Tri2(double ax, double ay, double bx, double by, double cx, double cy)
    {
        Ax = ax; Ay = ay; Bx = bx; By = by; Cx = cx; Cy = cy;
    }

    /// <summary>Vertex A, X.</summary>
    public double Ax { get; }

    /// <summary>Vertex A, Y.</summary>
    public double Ay { get; }

    /// <summary>Vertex B, X.</summary>
    public double Bx { get; }

    /// <summary>Vertex B, Y.</summary>
    public double By { get; }

    /// <summary>Vertex C, X.</summary>
    public double Cx { get; }

    /// <summary>Vertex C, Y.</summary>
    public double Cy { get; }
}

/// <summary>One enclosed floor opening found by the analysis.</summary>
public sealed class FloorOpening
{
    /// <summary>Creates an opening record from its measured properties.</summary>
    public FloorOpening(
        double centerX, double centerY, double area, double widestGap,
        double minX, double minY, double maxX, double maxY, int cellCount)
    {
        CenterX = centerX;
        CenterY = centerY;
        Area = area;
        WidestGap = widestGap;
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
        CellCount = cellCount;
    }

    /// <summary>World X of the opening's cell centroid.</summary>
    public double CenterX { get; }

    /// <summary>World Y of the opening's cell centroid.</summary>
    public double CenterY { get; }

    /// <summary>Plan area of the opening (world units²).</summary>
    public double Area { get; }

    /// <summary>The widest clear span across the opening (≈ diameter of the largest inscribed circle).</summary>
    public double WidestGap { get; }

    /// <summary>World min X of the opening's bounding rectangle.</summary>
    public double MinX { get; }

    /// <summary>World min Y of the opening's bounding rectangle.</summary>
    public double MinY { get; }

    /// <summary>World max X of the opening's bounding rectangle.</summary>
    public double MaxX { get; }

    /// <summary>World max Y of the opening's bounding rectangle.</summary>
    public double MaxY { get; }

    /// <summary>Number of grid cells in the opening.</summary>
    public int CellCount { get; }

    /// <summary>True when the widest gap meets or exceeds the flag threshold.</summary>
    public bool Flagged { get; internal set; }
}

/// <summary>The full result of a floor-gap analysis over one plan grid.</summary>
public sealed class FloorGapResult
{
    /// <summary>Creates a result from the computed grids and openings.</summary>
    public FloorGapResult(
        int cols, int rows, double originX, double originY, double cellSize,
        bool[] floor, bool[] plug, bool[] hazard, double[] clearance,
        double maxClearance, IReadOnlyList<FloorOpening> openings)
    {
        Cols = cols;
        Rows = rows;
        OriginX = originX;
        OriginY = originY;
        CellSize = cellSize;
        Floor = floor;
        Plug = plug;
        Hazard = hazard;
        Clearance = clearance;
        MaxClearance = maxClearance;
        Openings = openings;
    }

    /// <summary>Grid width in cells.</summary>
    public int Cols { get; }

    /// <summary>Grid height in cells.</summary>
    public int Rows { get; }

    /// <summary>World X of the grid's min corner (cell 0,0).</summary>
    public double OriginX { get; }

    /// <summary>World Y of the grid's min corner (cell 0,0).</summary>
    public double OriginY { get; }

    /// <summary>Grid cell size in world units.</summary>
    public double CellSize { get; }

    /// <summary>Per-cell floor coverage (row-major, index = row*Cols + col).</summary>
    public bool[] Floor { get; }

    /// <summary>Per-cell obstruction (equipment/pipe) coverage.</summary>
    public bool[] Plug { get; }

    /// <summary>Per-cell hazard flag: an enclosed opening with no floor and no obstruction.</summary>
    public bool[] Hazard { get; }

    /// <summary>Per-cell distance (world units) from a hazard cell to the nearest safe cell; 0 elsewhere.</summary>
    public double[] Clearance { get; }

    /// <summary>The largest clearance found (for colour scaling).</summary>
    public double MaxClearance { get; }

    /// <summary>The openings found, largest widest-gap first.</summary>
    public IReadOnlyList<FloorOpening> Openings { get; }

    /// <summary>Per-cell index into <see cref="Openings"/> for hazard cells (-1 elsewhere); may be empty when not computed.</summary>
    public int[] OpeningIndex { get; internal set; } = Array.Empty<int>();
}

/// <summary>How a floor edge cell facing a void is classified.</summary>
public enum EdgeClass : byte
{
    /// <summary>Not a floor edge facing a void.</summary>
    None = 0,

    /// <summary>The gap across the void is under the limit — no handrail needed.</summary>
    SafeGap = 1,

    /// <summary>Over the limit, but a handrail runs along it within tolerance.</summary>
    Protected = 2,

    /// <summary>Over the limit and no handrail — needs one.</summary>
    Dangerous = 3,
}

/// <summary>Colours for the edge render, as 0xRRGGBB values.</summary>
public sealed class EdgePalette
{
    /// <summary>Colour of unprotected dangerous edges (default red).</summary>
    public int Dangerous { get; set; } = 0xDC2828;

    /// <summary>Colour of handrail-protected edges (default blue).</summary>
    public int Protected { get; set; } = 0x3C8CDC;

    /// <summary>Colour of edges safe by gap alone (default green).</summary>
    public int Safe { get; set; } = 0x46B45A;
}

/// <summary>A printable annotation for one connected dangerous run of edge.</summary>
public sealed class EdgeLabel
{
    /// <summary>Creates the label.</summary>
    public EdgeLabel(double x, double y, double overage)
    {
        X = x;
        Y = y;
        Overage = overage;
    }

    /// <summary>World X of the run's centroid.</summary>
    public double X { get; }

    /// <summary>World Y of the run's centroid.</summary>
    public double Y { get; }

    /// <summary>How far the gap the run faces exceeds the limit (world units).</summary>
    public double Overage { get; }
}

/// <summary>The result of classifying the floor edges of a <see cref="FloorGapResult"/>.</summary>
public sealed class FloorEdgeResult
{
    /// <summary>Creates the result.</summary>
    public FloorEdgeResult(
        FloorGapResult baseResult, EdgeClass[] edges,
        double dangerousLength, double protectedLength, double safeLength,
        IReadOnlyList<EdgeLabel>? labels = null)
    {
        Base = baseResult;
        Edges = edges;
        DangerousLength = dangerousLength;
        ProtectedLength = protectedLength;
        SafeLength = safeLength;
        Labels = labels ?? Array.Empty<EdgeLabel>();
    }

    /// <summary>One annotation per connected dangerous run: where it is and by how much it exceeds the limit.</summary>
    public IReadOnlyList<EdgeLabel> Labels { get; }

    /// <summary>The underlying floor/void analysis this edge pass sits on.</summary>
    public FloorGapResult Base { get; }

    /// <summary>Per-cell edge classification (row-major, same grid as <see cref="Base"/>).</summary>
    public EdgeClass[] Edges { get; }

    /// <summary>Approximate length of unprotected dangerous edge (world units).</summary>
    public double DangerousLength { get; }

    /// <summary>Approximate length of dangerous edge covered by a handrail (world units).</summary>
    public double ProtectedLength { get; }

    /// <summary>Approximate length of edge safe by gap alone (world units).</summary>
    public double SafeLength { get; }
}

/// <summary>
/// Pure, headless-testable core of the floor-opening fall-hazard heat map.
/// Rasterises floor and obstruction triangles onto a plan grid, isolates the
/// openings that are fully enclosed by floor (so the building's exterior is not
/// mistaken for a hole), grades each opening by how far its centre sits from the
/// nearest edge, and renders the result to an RGBA heat map / PNG. All geometry
/// arrives already projected to the analysis plane — no Navisworks types here.
/// Called by the FallHazard node; not itself a node.
/// </summary>
[Dyncamelo.Core.Loader.IsVisibleInLibrary(false)]
public static class FloorGapHeatmap
{
    /// <summary>The largest grid the analysis will build, as a guard against a tiny cell size.</summary>
    public const int MaxCells = 6_000_000;

    /// <summary>
    /// Runs the analysis over a world-space rectangle.
    /// </summary>
    /// <param name="minX">Region min X (world).</param>
    /// <param name="minY">Region min Y (world).</param>
    /// <param name="maxX">Region max X (world).</param>
    /// <param name="maxY">Region max Y (world).</param>
    /// <param name="cellSize">Grid cell size in world units (smaller = finer, more cells).</param>
    /// <param name="floorTriangles">Floor/slab triangles crossing the analysis band.</param>
    /// <param name="plugTriangles">Obstruction (equipment/pipe) triangles crossing the band; may be empty.</param>
    /// <param name="minGap">Openings whose widest gap ≥ this are flagged as needing a handrail.</param>
    /// <returns>The analysis result (grids, openings, clearances).</returns>
    public static FloorGapResult Analyze(
        double minX, double minY, double maxX, double maxY,
        double cellSize,
        IReadOnlyList<Tri2> floorTriangles,
        IReadOnlyList<Tri2> plugTriangles,
        double minGap)
    {
        if (cellSize <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), "The cell size must be positive.");
        }

        if (maxX <= minX || maxY <= minY)
        {
            throw new ArgumentException("The analysis region is empty (max must exceed min on both axes).");
        }

        floorTriangles ??= Array.Empty<Tri2>();
        plugTriangles ??= Array.Empty<Tri2>();

        var cols = (int)Math.Ceiling((maxX - minX) / cellSize);
        var rows = (int)Math.Ceiling((maxY - minY) / cellSize);
        cols = Math.Max(cols, 1);
        rows = Math.Max(rows, 1);
        if ((long)cols * rows > MaxCells)
        {
            throw new InvalidOperationException(
                "The grid would be " + cols + "×" + rows + " = " + ((long)cols * rows).ToString("N0") +
                " cells, over the " + MaxCells.ToString("N0") + "-cell limit. Increase the cell size or " +
                "shrink the region.");
        }

        var count = cols * rows;
        var floor = new bool[count];
        var plug = new bool[count];
        Rasterize(floorTriangles, minX, minY, cellSize, cols, rows, floor);
        Rasterize(plugTriangles, minX, minY, cellSize, cols, rows, plug);

        // Open = neither floor nor obstruction. Enclosed opens (not reachable from
        // the grid border through other opens) are the holes inside the slab.
        var hazard = EnclosedOpens(floor, plug, cols, rows);

        var clearance = ChamferClearance(hazard, cols, rows, cellSize, out var maxClearance);
        var openings = LabelOpenings(hazard, clearance, minX, minY, cellSize, cols, rows, minGap, out var openingIndex);

        return new FloorGapResult(
            cols, rows, minX, minY, cellSize, floor, plug, hazard, clearance, maxClearance, openings)
        {
            OpeningIndex = openingIndex,
        };
    }

    // ----------------------------------------------------------- Edge / handrail

    /// <summary>
    /// Classifies each floor edge cell that faces a void. An edge is dangerous
    /// when the void it faces is locally wide enough to fall through — it contains
    /// a point whose distance to the nearest solid on every side reaches half the
    /// <paramref name="limit"/> (local width ≥ limit) within reach of the edge. An
    /// edge whose void is narrower than that everywhere nearby (the nearest object
    /// across it is closer than the limit) is safe. A dangerous edge becomes
    /// protected when a handrail (its geometry projected onto the plane and
    /// rasterised) runs within <paramref name="tolerance"/> of it — per cell, so a
    /// short handrail only covers the length it actually spans.
    /// </summary>
    /// <param name="result">The floor/void analysis to classify the edges of.</param>
    /// <param name="handrailTriangles">Handrail geometry projected to the plane; may be empty.</param>
    /// <param name="limit">A void locally at least this wide makes the facing edge a hazard.</param>
    /// <param name="tolerance">A handrail within this distance of a dangerous edge protects it.</param>
    /// <param name="minPassage">A connected dangerous stretch of edge shorter than this is reclassified safe — a person cannot fit through so small a break in the protection (e.g. between two handrails, or a handrail and an obstacle). 0 disables.</param>
    /// <returns>The per-cell edge classification and the length in each class.</returns>
    public static FloorEdgeResult AnalyzeEdges(
        FloorGapResult result, IReadOnlyList<Tri2> handrailTriangles, double limit, double tolerance,
        double minPassage = 0.0)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        var cols = result.Cols;
        var rows = result.Rows;
        var cellSize = result.CellSize;
        var floor = result.Floor;
        var hazard = result.Hazard;
        var clearance = result.Clearance;
        var count = cols * rows;

        // Handrails, projected and rasterised, then a distance field from them.
        var handrail = new bool[count];
        if (handrailTriangles != null && handrailTriangles.Count > 0)
        {
            Rasterize(handrailTriangles, result.OriginX, result.OriginY, cellSize, cols, rows, handrail);
        }

        var handrailDistance = ChamferFromSeeds(handrail, cols, rows, cellSize);

        // Fall-through core: void cells whose clearance reaches half the limit —
        // the spots where the void is locally wide enough to fall through. An edge
        // is dangerous only when that core comes within reach of it THROUGH the
        // void. Measuring this way (instead of marching one axis until the next
        // solid) keeps the ends of a long narrow slot safe: the void there is long
        // but never wide enough to fall through, so its length is irrelevant.
        //
        // The chamfer clearance is measured cell-centre to cell-centre, which
        // overestimates the true distance to the solid boundary by half a cell;
        // the +0.5·cell on the threshold cancels that, guaranteeing a gap
        // narrower than the limit NEVER reads dangerous (a gap within one cell
        // above the limit may read safe — the resolution the cell size buys).
        var core = new bool[count];
        var coreThreshold = limit / 2.0 + 0.5 * cellSize;
        for (int i = 0; i < count; i++)
        {
            core[i] = hazard[i] && clearance[i] >= coreThreshold;
        }

        var coreDistance = ChamferThroughMask(core, hazard, cols, rows, cellSize);

        // Along a straight wall the first core cell sits ~limit/2 beyond the
        // edge's face; at a CORNER of an opening the core is pulled back from
        // both walls, so the nearest core sits diagonally at √2·(limit/2). Reach
        // must cover that or corner cells read safe while the sides read
        // dangerous; the 1.75-cell tail covers the half-cell to the edge cell's
        // centre plus discretisation.
        var reach = Math.Sqrt(2.0) * (limit / 2.0) + 1.75 * cellSize;

        var edges = new EdgeClass[count];
        var neighbourR = new[] { -1, 1, 0, 0, -1, -1, 1, 1 };
        var neighbourC = new[] { 0, 0, -1, 1, -1, 1, -1, 1 };
        var diagonalStep = cellSize * Math.Sqrt(2.0);

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var idx = r * cols + c;
                if (!floor[idx])
                {
                    continue; // only floor cells can be an edge of the void
                }

                var facesVoid = false;
                var coreDist = double.PositiveInfinity;
                for (int d = 0; d < 8; d++)
                {
                    var nr = r + neighbourR[d];
                    var nc = c + neighbourC[d];
                    if (nr < 0 || nr >= rows || nc < 0 || nc >= cols)
                    {
                        continue;
                    }

                    var nIdx = nr * cols + nc;
                    if (!hazard[nIdx])
                    {
                        continue;
                    }

                    // Diagonal contact counts too: the floor cell at the exact
                    // corner of a rectangular opening touches the void only
                    // diagonally, and skipping it left an unclassified notch at
                    // every corner.
                    facesVoid = true;

                    var reached = coreDistance[nIdx] + (d < 4 ? cellSize : diagonalStep);
                    if (reached < coreDist)
                    {
                        coreDist = reached;
                    }
                }

                if (!facesVoid)
                {
                    continue;
                }

                edges[idx] = coreDist > reach ? EdgeClass.SafeGap
                    : handrailDistance[idx] <= tolerance ? EdgeClass.Protected
                    : EdgeClass.Dangerous;
            }
        }

        // Walk each 8-connected run of dangerous cells. A run shorter than
        // minPassage is reclassified safe — a person cannot fit through so small
        // a break in the protection (between two handrails, or a handrail and an
        // obstacle). Every surviving run gets a label stating how far the gap it
        // faces exceeds the limit, placed at the run's centroid.
        var labels = new List<EdgeLabel>();
        {
            var visited = new bool[count];
            var run = new List<int>();
            var stack = new Stack<int>();
            var openingIndex = result.OpeningIndex;
            var openings = result.Openings;

            for (int start = 0; start < count; start++)
            {
                if (edges[start] != EdgeClass.Dangerous || visited[start])
                {
                    continue;
                }

                run.Clear();
                visited[start] = true;
                stack.Push(start);
                var facedGap = 0.0;
                double sumX = 0, sumY = 0;
                while (stack.Count > 0)
                {
                    var idx = stack.Pop();
                    run.Add(idx);
                    var r = idx / cols;
                    var c = idx % cols;
                    sumX += result.OriginX + (c + 0.5) * cellSize;
                    sumY += result.OriginY + (r + 0.5) * cellSize;
                    for (int d = 0; d < 8; d++)
                    {
                        var nr = r + neighbourR[d];
                        var nc = c + neighbourC[d];
                        if (nr < 0 || nr >= rows || nc < 0 || nc >= cols)
                        {
                            continue;
                        }

                        var nIdx = nr * cols + nc;
                        if (edges[nIdx] == EdgeClass.Dangerous && !visited[nIdx])
                        {
                            visited[nIdx] = true;
                            stack.Push(nIdx);
                        }

                        // The widest gap of the void this run borders.
                        if (hazard[nIdx] && openingIndex.Length == count)
                        {
                            var oi = openingIndex[nIdx];
                            if (oi >= 0 && openings[oi].WidestGap > facedGap)
                            {
                                facedGap = openings[oi].WidestGap;
                            }
                        }
                    }
                }

                var runLength = run.Count * cellSize;
                if (minPassage > 0.0 && runLength < minPassage)
                {
                    foreach (var idx in run)
                    {
                        edges[idx] = EdgeClass.SafeGap;
                    }

                    continue;
                }

                if (facedGap > limit)
                {
                    labels.Add(new EdgeLabel(sumX / run.Count, sumY / run.Count, facedGap - limit));
                }
            }
        }

        // Lengths from the final classification in one pass, so reclassification
        // leaves no floating-point residue in the totals.
        int dangerousCells = 0, protectedCells = 0, safeCells = 0;
        for (int i = 0; i < count; i++)
        {
            switch (edges[i])
            {
                case EdgeClass.Dangerous: dangerousCells++; break;
                case EdgeClass.Protected: protectedCells++; break;
                case EdgeClass.SafeGap: safeCells++; break;
            }
        }

        return new FloorEdgeResult(
            result, edges, dangerousCells * cellSize, protectedCells * cellSize, safeCells * cellSize, labels);
    }

    /// <summary>
    /// Chamfer distance (world units) to the nearest seed, propagating only
    /// through <paramref name="passable"/> cells — distance THROUGH the void, so
    /// danger cannot leak across equipment or floor. The two sweeps are iterated
    /// to convergence because a masked (non-convex) domain may need several
    /// passes; impassable cells stay +∞.
    /// </summary>
    private static double[] ChamferThroughMask(
        bool[] seeds, bool[] passable, int cols, int rows, double cellSize)
    {
        var count = cols * rows;
        var dist = new double[count];
        const double big = 1e18;
        var anySeed = false;
        for (int i = 0; i < count; i++)
        {
            if (seeds[i]) { dist[i] = 0.0; anySeed = true; }
            else { dist[i] = big; }
        }

        if (!anySeed)
        {
            for (int i = 0; i < count; i++) dist[i] = double.PositiveInfinity;
            return dist;
        }

        const double d1 = 1.0;
        var d2 = Math.Sqrt(2.0);

        bool changed;
        var iterations = 0;
        do
        {
            changed = false;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var idx = r * cols + c;
                    if (!passable[idx] || dist[idx] == 0.0) continue;
                    var best = dist[idx];
                    if (c > 0 && passable[idx - 1]) best = Math.Min(best, dist[idx - 1] + d1);
                    if (r > 0 && passable[idx - cols]) best = Math.Min(best, dist[idx - cols] + d1);
                    if (r > 0 && c > 0 && passable[idx - cols - 1]) best = Math.Min(best, dist[idx - cols - 1] + d2);
                    if (r > 0 && c < cols - 1 && passable[idx - cols + 1]) best = Math.Min(best, dist[idx - cols + 1] + d2);
                    if (best < dist[idx]) { dist[idx] = best; changed = true; }
                }
            }

            for (int r = rows - 1; r >= 0; r--)
            {
                for (int c = cols - 1; c >= 0; c--)
                {
                    var idx = r * cols + c;
                    if (!passable[idx] || dist[idx] == 0.0) continue;
                    var best = dist[idx];
                    if (c < cols - 1 && passable[idx + 1]) best = Math.Min(best, dist[idx + 1] + d1);
                    if (r < rows - 1 && passable[idx + cols]) best = Math.Min(best, dist[idx + cols] + d1);
                    if (r < rows - 1 && c < cols - 1 && passable[idx + cols + 1]) best = Math.Min(best, dist[idx + cols + 1] + d2);
                    if (r < rows - 1 && c > 0 && passable[idx + cols - 1]) best = Math.Min(best, dist[idx + cols - 1] + d2);
                    if (best < dist[idx]) { dist[idx] = best; changed = true; }
                }
            }
        }
        while (changed && ++iterations < 16);

        for (int i = 0; i < count; i++)
        {
            dist[i] = dist[i] >= big ? double.PositiveInfinity : dist[i] * cellSize;
        }

        return dist;
    }

    /// <summary>Two-pass chamfer distance (world units) to the nearest seed cell; +∞ when no seeds.</summary>
    private static double[] ChamferFromSeeds(bool[] seeds, int cols, int rows, double cellSize)
    {
        var count = cols * rows;
        var dist = new double[count];
        const double big = 1e18;
        var anySeed = false;
        for (int i = 0; i < count; i++)
        {
            if (seeds[i]) { dist[i] = 0.0; anySeed = true; }
            else { dist[i] = big; }
        }

        if (!anySeed)
        {
            for (int i = 0; i < count; i++) dist[i] = double.PositiveInfinity;
            return dist;
        }

        const double d1 = 1.0;
        var d2 = Math.Sqrt(2.0);

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var idx = r * cols + c;
                if (dist[idx] == 0.0) continue;
                var best = dist[idx];
                if (c > 0) best = Math.Min(best, dist[idx - 1] + d1);
                if (r > 0) best = Math.Min(best, dist[idx - cols] + d1);
                if (r > 0 && c > 0) best = Math.Min(best, dist[idx - cols - 1] + d2);
                if (r > 0 && c < cols - 1) best = Math.Min(best, dist[idx - cols + 1] + d2);
                dist[idx] = best;
            }
        }

        for (int r = rows - 1; r >= 0; r--)
        {
            for (int c = cols - 1; c >= 0; c--)
            {
                var idx = r * cols + c;
                if (dist[idx] == 0.0) continue;
                var best = dist[idx];
                if (c < cols - 1) best = Math.Min(best, dist[idx + 1] + d1);
                if (r < rows - 1) best = Math.Min(best, dist[idx + cols] + d1);
                if (r < rows - 1 && c < cols - 1) best = Math.Min(best, dist[idx + cols + 1] + d2);
                if (r < rows - 1 && c > 0) best = Math.Min(best, dist[idx + cols - 1] + d2);
                dist[idx] = best;
            }
        }

        for (int i = 0; i < count; i++) dist[i] *= cellSize;
        return dist;
    }

    /// <summary>
    /// Renders the edge classification: floor dark, void faint, and edge cells in
    /// the palette's colours (default green safe, blue handrail-protected, red
    /// dangerous). With <paramref name="showOverage"/>, each dangerous run is
    /// annotated with how far its gap exceeds the limit (e.g. "+0.35"), so the
    /// plan can go straight into a report.
    /// </summary>
    /// <param name="edgeResult">The edge analysis to draw.</param>
    /// <param name="pixelsPerCell">Image pixels per grid cell (≥1).</param>
    /// <param name="palette">Colours for the three edge classes; null = defaults.</param>
    /// <param name="showOverage">True to print each dangerous run's gap-over-limit at its centroid.</param>
    /// <param name="labelDivisor">Divides label values before formatting (unit conversion); 1 = world units.</param>
    /// <returns>The PNG bytes.</returns>
    public static byte[] RenderEdgePng(
        FloorEdgeResult edgeResult, int pixelsPerCell = 4,
        EdgePalette? palette = null, bool showOverage = false, double labelDivisor = 1.0)
    {
        if (edgeResult == null)
        {
            throw new ArgumentNullException(nameof(edgeResult));
        }

        if (pixelsPerCell < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelsPerCell), "There must be at least one pixel per cell.");
        }

        palette ??= new EdgePalette();
        var result = edgeResult.Base;
        var cols = result.Cols;
        var rows = result.Rows;
        var width = cols * pixelsPerCell;
        var height = rows * pixelsPerCell;
        var rgba = new byte[width * height * 4];

        for (int r = 0; r < rows; r++)
        {
            var pngRowBase = (rows - 1 - r) * pixelsPerCell;
            for (int c = 0; c < cols; c++)
            {
                var idx = r * cols + c;
                byte cr, cg, cb, ca;
                switch (edgeResult.Edges[idx])
                {
                    case EdgeClass.Dangerous: Unpack(palette.Dangerous, out cr, out cg, out cb); ca = 255; break;
                    case EdgeClass.Protected: Unpack(palette.Protected, out cr, out cg, out cb); ca = 255; break;
                    case EdgeClass.SafeGap: Unpack(palette.Safe, out cr, out cg, out cb); ca = 255; break;
                    default:
                        if (result.Hazard[idx]) { cr = 44; cg = 46; cb = 58; ca = 255; }        // void
                        else if (result.Plug[idx]) { cr = 96; cg = 100; cb = 108; ca = 255; }   // equipment
                        else if (result.Floor[idx]) { cr = 60; cg = 62; cb = 68; ca = 255; }    // floor
                        else { cr = 0; cg = 0; cb = 0; ca = 0; }                                // exterior
                        break;
                }

                for (int py = 0; py < pixelsPerCell; py++)
                {
                    var rowStart = ((pngRowBase + py) * width + c * pixelsPerCell) * 4;
                    for (int px = 0; px < pixelsPerCell; px++)
                    {
                        var o = rowStart + px * 4;
                        rgba[o] = cr; rgba[o + 1] = cg; rgba[o + 2] = cb; rgba[o + 3] = ca;
                    }
                }
            }
        }

        if (showOverage && labelDivisor > 0)
        {
            var glyphScale = Math.Max(2, pixelsPerCell / 2);
            foreach (var label in edgeResult.Labels)
            {
                var text = "+" + (label.Overage / labelDivisor).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                var px = (int)((label.X - result.OriginX) / result.CellSize) * pixelsPerCell;
                var py = (rows - 1 - (int)((label.Y - result.OriginY) / result.CellSize)) * pixelsPerCell;
                DrawText(rgba, width, height, text, px, py, glyphScale);
            }
        }

        return PngWriter.Encode(width, height, rgba);
    }

    private static void Unpack(int rgb, out byte r, out byte g, out byte b)
    {
        r = (byte)((rgb >> 16) & 0xFF);
        g = (byte)((rgb >> 8) & 0xFF);
        b = (byte)(rgb & 0xFF);
    }

    // ------------------------------------------------------------ Label text

    /// <summary>5×7 pixel glyphs for the label alphabet ('#' = lit).</summary>
    private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
    {
        ['0'] = new[] { " ### ", "#   #", "#  ##", "# # #", "##  #", "#   #", " ### " },
        ['1'] = new[] { "  #  ", " ##  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### " },
        ['2'] = new[] { " ### ", "#   #", "    #", "   # ", "  #  ", " #   ", "#####" },
        ['3'] = new[] { " ### ", "#   #", "    #", "  ## ", "    #", "#   #", " ### " },
        ['4'] = new[] { "   # ", "  ## ", " # # ", "#  # ", "#####", "   # ", "   # " },
        ['5'] = new[] { "#####", "#    ", "#### ", "    #", "    #", "#   #", " ### " },
        ['6'] = new[] { " ### ", "#    ", "#    ", "#### ", "#   #", "#   #", " ### " },
        ['7'] = new[] { "#####", "    #", "   # ", "  #  ", "  #  ", "  #  ", "  #  " },
        ['8'] = new[] { " ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### " },
        ['9'] = new[] { " ### ", "#   #", "#   #", " ####", "    #", "    #", " ### " },
        ['.'] = new[] { "     ", "     ", "     ", "     ", "     ", " ##  ", " ##  " },
        ['+'] = new[] { "     ", "  #  ", "  #  ", "#####", "  #  ", "  #  ", "     " },
        ['-'] = new[] { "     ", "     ", "     ", "#####", "     ", "     ", "     " },
    };

    /// <summary>Draws text centred on (cx, cy): white glyphs over a dark backing box.</summary>
    private static void DrawText(byte[] rgba, int width, int height, string text, int cx, int cy, int scale)
    {
        var advance = 6 * scale; // 5-wide glyph + 1 gap
        var textWidth = text.Length * advance - scale;
        var textHeight = 7 * scale;
        var x0 = cx - textWidth / 2;
        var y0 = cy - textHeight / 2;

        // Dark backing box (1-glyph-pixel margin) so the label reads on any colour.
        FillRect(rgba, width, height, x0 - scale, y0 - scale, textWidth + 2 * scale, textHeight + 2 * scale, 20, 20, 24);

        var x = x0;
        foreach (var ch in text)
        {
            if (Glyphs.TryGetValue(ch, out var glyph))
            {
                for (int gy = 0; gy < 7; gy++)
                {
                    for (int gx = 0; gx < 5; gx++)
                    {
                        if (glyph[gy][gx] == '#')
                        {
                            FillRect(rgba, width, height, x + gx * scale, y0 + gy * scale, scale, scale, 255, 255, 255);
                        }
                    }
                }
            }

            x += advance;
        }
    }

    private static void FillRect(
        byte[] rgba, int width, int height, int x, int y, int w, int h, byte r, byte g, byte b)
    {
        var x1 = Math.Min(width, x + w);
        var y1 = Math.Min(height, y + h);
        for (int py = Math.Max(0, y); py < y1; py++)
        {
            var row = py * width;
            for (int px = Math.Max(0, x); px < x1; px++)
            {
                var o = (row + px) * 4;
                rgba[o] = r; rgba[o + 1] = g; rgba[o + 2] = b; rgba[o + 3] = 255;
            }
        }
    }

    // ---------------------------------------------------------------- Rasterize

    /// <summary>Marks every cell whose centre falls inside any triangle.</summary>
    private static void Rasterize(
        IReadOnlyList<Tri2> triangles, double originX, double originY, double cellSize,
        int cols, int rows, bool[] mask)
    {
        foreach (var t in triangles)
        {
            var loX = Math.Min(t.Ax, Math.Min(t.Bx, t.Cx));
            var hiX = Math.Max(t.Ax, Math.Max(t.Bx, t.Cx));
            var loY = Math.Min(t.Ay, Math.Min(t.By, t.Cy));
            var hiY = Math.Max(t.Ay, Math.Max(t.By, t.Cy));

            var c0 = (int)Math.Floor((loX - originX) / cellSize);
            var c1 = (int)Math.Floor((hiX - originX) / cellSize);
            var r0 = (int)Math.Floor((loY - originY) / cellSize);
            var r1 = (int)Math.Floor((hiY - originY) / cellSize);
            if (c0 < 0) c0 = 0;
            if (r0 < 0) r0 = 0;
            if (c1 > cols - 1) c1 = cols - 1;
            if (r1 > rows - 1) r1 = rows - 1;

            for (int r = r0; r <= r1; r++)
            {
                var py = originY + (r + 0.5) * cellSize;
                for (int c = c0; c <= c1; c++)
                {
                    var idx = r * cols + c;
                    if (mask[idx])
                    {
                        continue;
                    }

                    var px = originX + (c + 0.5) * cellSize;
                    if (PointInTriangle(px, py, t))
                    {
                        mask[idx] = true;
                    }
                }
            }
        }
    }

    private static bool PointInTriangle(double px, double py, Tri2 t)
    {
        var d1 = (px - t.Bx) * (t.Ay - t.By) - (t.Ax - t.Bx) * (py - t.By);
        var d2 = (px - t.Cx) * (t.By - t.Cy) - (t.Bx - t.Cx) * (py - t.Cy);
        var d3 = (px - t.Ax) * (t.Cy - t.Ay) - (t.Cx - t.Ax) * (py - t.Ay);
        var hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        var hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    // ---------------------------------------------------------------- Enclosure

    /// <summary>
    /// Hazard cells: open (no floor, no obstruction) AND not connected to the
    /// grid border through other open cells. A flood fill from the border marks
    /// the exterior; everything open that it never reaches is an enclosed hole.
    /// </summary>
    private static bool[] EnclosedOpens(bool[] floor, bool[] plug, int cols, int rows)
    {
        var count = cols * rows;
        var open = new bool[count];
        for (int i = 0; i < count; i++)
        {
            open[i] = !floor[i] && !plug[i];
        }

        var exterior = new bool[count];
        var stack = new Stack<int>();
        void Seed(int idx)
        {
            if (open[idx] && !exterior[idx])
            {
                exterior[idx] = true;
                stack.Push(idx);
            }
        }

        for (int c = 0; c < cols; c++)
        {
            Seed(c);                       // top row
            Seed((rows - 1) * cols + c);   // bottom row
        }

        for (int r = 0; r < rows; r++)
        {
            Seed(r * cols);                // left column
            Seed(r * cols + (cols - 1));   // right column
        }

        while (stack.Count > 0)
        {
            var idx = stack.Pop();
            var r = idx / cols;
            var c = idx % cols;
            if (c > 0) Seed(idx - 1);
            if (c < cols - 1) Seed(idx + 1);
            if (r > 0) Seed(idx - cols);
            if (r < rows - 1) Seed(idx + cols);
        }

        var hazard = new bool[count];
        for (int i = 0; i < count; i++)
        {
            hazard[i] = open[i] && !exterior[i];
        }

        return hazard;
    }

    // ----------------------------------------------------------- Distance field

    /// <summary>
    /// Two-pass chamfer distance transform: each hazard cell gets its distance
    /// (world units) to the nearest safe cell, so the centre of a big opening
    /// scores highest. Non-hazard cells stay 0.
    /// </summary>
    private static double[] ChamferClearance(
        bool[] hazard, int cols, int rows, double cellSize, out double maxClearance)
    {
        var count = cols * rows;
        var dist = new double[count];
        const double big = 1e18;
        for (int i = 0; i < count; i++)
        {
            dist[i] = hazard[i] ? big : 0.0;
        }

        const double d1 = 1.0;               // orthogonal step
        var d2 = Math.Sqrt(2.0);             // diagonal step

        // Forward pass.
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var idx = r * cols + c;
                if (dist[idx] == 0.0)
                {
                    continue;
                }

                var best = dist[idx];
                if (c > 0) best = Math.Min(best, dist[idx - 1] + d1);
                if (r > 0) best = Math.Min(best, dist[idx - cols] + d1);
                if (r > 0 && c > 0) best = Math.Min(best, dist[idx - cols - 1] + d2);
                if (r > 0 && c < cols - 1) best = Math.Min(best, dist[idx - cols + 1] + d2);
                dist[idx] = best;
            }
        }

        // Backward pass (still in cell units — do NOT scale here, or the reads of
        // already-visited right/below neighbours would mix world units with the
        // cell-unit step weights and collapse the distance to ~1 cell).
        for (int r = rows - 1; r >= 0; r--)
        {
            for (int c = cols - 1; c >= 0; c--)
            {
                var idx = r * cols + c;
                if (dist[idx] == 0.0)
                {
                    continue;
                }

                var best = dist[idx];
                if (c < cols - 1) best = Math.Min(best, dist[idx + 1] + d1);
                if (r < rows - 1) best = Math.Min(best, dist[idx + cols] + d1);
                if (r < rows - 1 && c < cols - 1) best = Math.Min(best, dist[idx + cols + 1] + d2);
                if (r < rows - 1 && c > 0) best = Math.Min(best, dist[idx + cols - 1] + d2);
                dist[idx] = best;
            }
        }

        // Convert cell distances to world units in a separate pass.
        maxClearance = 0.0;
        for (int i = 0; i < count; i++)
        {
            dist[i] *= cellSize;
            if (dist[i] > maxClearance)
            {
                maxClearance = dist[i];
            }
        }

        return dist;
    }

    // ------------------------------------------------------- Connected openings

    private static IReadOnlyList<FloorOpening> LabelOpenings(
        bool[] hazard, double[] clearance, double originX, double originY, double cellSize,
        int cols, int rows, double minGap, out int[] openingIndex)
    {
        var count = cols * rows;
        var visited = new bool[count];
        var openings = new List<FloorOpening>();
        var stack = new Stack<int>();
        openingIndex = new int[count];
        for (int i = 0; i < count; i++) openingIndex[i] = -1;

        for (int start = 0; start < count; start++)
        {
            if (!hazard[start] || visited[start])
            {
                continue;
            }

            var componentId = openings.Count;
            visited[start] = true;
            stack.Push(start);

            long cells = 0;
            double sumCx = 0, sumCy = 0, peak = 0;
            int minC = cols, minR = rows, maxC = -1, maxR = -1;

            while (stack.Count > 0)
            {
                var idx = stack.Pop();
                openingIndex[idx] = componentId;
                var r = idx / cols;
                var c = idx % cols;
                cells++;
                sumCx += originX + (c + 0.5) * cellSize;
                sumCy += originY + (r + 0.5) * cellSize;
                if (clearance[idx] > peak) peak = clearance[idx];
                if (c < minC) minC = c;
                if (c > maxC) maxC = c;
                if (r < minR) minR = r;
                if (r > maxR) maxR = r;

                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        if (dr == 0 && dc == 0)
                        {
                            continue;
                        }

                        var nr = r + dr;
                        var nc = c + dc;
                        if (nr < 0 || nr >= rows || nc < 0 || nc >= cols)
                        {
                            continue;
                        }

                        var nIdx = nr * cols + nc;
                        if (hazard[nIdx] && !visited[nIdx])
                        {
                            visited[nIdx] = true;
                            stack.Push(nIdx);
                        }
                    }
                }
            }

            // Peak clearance is how far the opening's deepest point sits from any
            // edge/obstacle; the widest clear span across it is roughly twice that.
            // An opening is flagged when that deepest point exceeds the limit — i.e.
            // it contains at least one cell more than `minGap` from any solid.
            var widestGap = peak * 2.0;
            var opening = new FloorOpening(
                sumCx / cells, sumCy / cells, cells * cellSize * cellSize, widestGap,
                originX + minC * cellSize, originY + minR * cellSize,
                originX + (maxC + 1) * cellSize, originY + (maxR + 1) * cellSize, (int)cells)
            {
                Flagged = peak >= minGap,
            };
            openings.Add(opening);
        }

        // Sort widest-first for callers, remapping the per-cell indices to match.
        var order = new List<int>();
        for (int i = 0; i < openings.Count; i++) order.Add(i);
        order.Sort((a, b) => openings[b].WidestGap.CompareTo(openings[a].WidestGap));
        var remap = new int[openings.Count];
        var sorted = new List<FloorOpening>(openings.Count);
        for (int newIdx = 0; newIdx < order.Count; newIdx++)
        {
            remap[order[newIdx]] = newIdx;
            sorted.Add(openings[order[newIdx]]);
        }

        for (int i = 0; i < count; i++)
        {
            if (openingIndex[i] >= 0)
            {
                openingIndex[i] = remap[openingIndex[i]];
            }
        }

        return sorted;
    }

    // ------------------------------------------------------------------- Render

    /// <summary>
    /// Renders the analysis to a PNG. Open cells are coloured by a gradient that
    /// pivots on the <paramref name="minGap"/> limit: a cell's local gap width
    /// (twice its clearance) maps blue/green when it is well under the limit,
    /// yellow right at the limit, and orange→red the further it exceeds it — so a
    /// gap narrower than the limit never reads as red. Floor is dark grey,
    /// equipment mid grey.
    /// </summary>
    /// <param name="result">The analysis result.</param>
    /// <param name="pixelsPerCell">How many image pixels each grid cell spans (≥1).</param>
    /// <param name="minGap">The handrail limit: the gap width that maps to the ramp's pivot.</param>
    /// <param name="lowColor">Optional 0xRRGGBB for the safe end of a custom two-colour gradient.</param>
    /// <param name="highColor">Optional 0xRRGGBB for the hazard end of a custom two-colour gradient.</param>
    /// <param name="showOverage">True to print each flagged opening's gap-over-limit at its centre.</param>
    /// <param name="labelDivisor">Divides label values before formatting (unit conversion); 1 = world units.</param>
    /// <returns>The PNG bytes.</returns>
    public static byte[] RenderPng(
        FloorGapResult result, int pixelsPerCell = 4, double minGap = 0.0,
        int? lowColor = null, int? highColor = null, bool showOverage = false, double labelDivisor = 1.0)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        if (pixelsPerCell < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelsPerCell), "There must be at least one pixel per cell.");
        }

        var cols = result.Cols;
        var rows = result.Rows;
        var width = cols * pixelsPerCell;
        var height = rows * pixelsPerCell;
        var rgba = new byte[width * height * 4];

        for (int r = 0; r < rows; r++)
        {
            // PNG row 0 is the top; grid row 0 is min-Y (south), so flip vertically.
            var pngRowBase = (rows - 1 - r) * pixelsPerCell;
            for (int c = 0; c < cols; c++)
            {
                var idx = r * cols + c;
                GetCellColor(result, idx, minGap, lowColor, highColor, out var cr, out var cg, out var cb, out var ca);

                for (int py = 0; py < pixelsPerCell; py++)
                {
                    var rowStart = ((pngRowBase + py) * width + c * pixelsPerCell) * 4;
                    for (int px = 0; px < pixelsPerCell; px++)
                    {
                        var o = rowStart + px * 4;
                        rgba[o] = cr;
                        rgba[o + 1] = cg;
                        rgba[o + 2] = cb;
                        rgba[o + 3] = ca;
                    }
                }
            }
        }

        if (showOverage && labelDivisor > 0 && minGap > 0)
        {
            var glyphScale = Math.Max(2, pixelsPerCell / 2);
            foreach (var opening in result.Openings)
            {
                if (!opening.Flagged || opening.WidestGap <= minGap)
                {
                    continue;
                }

                var text = "+" + ((opening.WidestGap - minGap) / labelDivisor)
                    .ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                var px = (int)((opening.CenterX - result.OriginX) / result.CellSize) * pixelsPerCell;
                var py = (rows - 1 - (int)((opening.CenterY - result.OriginY) / result.CellSize)) * pixelsPerCell;
                DrawText(rgba, width, height, text, px, py, glyphScale);
            }
        }

        return PngWriter.Encode(width, height, rgba);
    }

    private static void GetCellColor(
        FloorGapResult result, int idx, double minGap, int? lowColor, int? highColor,
        out byte r, out byte g, out byte b, out byte a)
    {
        if (result.Hazard[idx])
        {
            // Distance from this void cell to the nearest floor edge or obstacle:
            // the limit thresholds this directly (> limit from any solid = hazard).
            var clearance = result.Clearance[idx];
            LimitRamp(clearance, minGap, result.MaxClearance, lowColor, highColor, out r, out g, out b);
            a = 255;
            return;
        }

        if (result.Plug[idx])
        {
            r = 122; g = 126; b = 133; a = 255; // obstruction (equipment/pipe)
            return;
        }

        if (result.Floor[idx])
        {
            r = 54; g = 56; b = 62; a = 255; // safe floor
            return;
        }

        r = 0; g = 0; b = 0; a = 0; // exterior / empty
    }

    /// <summary>
    /// Maps a clearance (distance to the nearest floor edge or obstacle) to the
    /// heat gradient, pivoting on the limit: the limit lands at the yellow
    /// midpoint, below it runs blue→green (safe — within reach of an edge), above
    /// it yellow→red (saturating at twice the limit, or at the largest clearance
    /// when no limit is set).
    /// </summary>
    private static void LimitRamp(
        double clearance, double minGap, double maxClearance, int? lowColor, int? highColor,
        out byte r, out byte g, out byte b)
    {
        double s;
        if (minGap > 1e-9)
        {
            var ratio = clearance / minGap;
            s = ratio <= 1.0
                ? 0.5 * Math.Max(0.0, ratio)                          // below the limit
                : 0.5 + 0.5 * Math.Min(1.0, ratio - 1.0);             // above (2× limit → full red)
        }
        else
        {
            // No limit set: fall back to a plain cool→hot ramp over the range.
            s = maxClearance > 1e-9 ? Math.Min(1.0, clearance / maxClearance) : 0.5;
        }

        if (lowColor.HasValue || highColor.HasValue)
        {
            // Custom report gradient: a straight blend from the low colour (gap → 0,
            // the safe end) to the high colour (≥ 2× the limit, the worst hazard);
            // the limit itself sits at the 50% blend.
            Unpack(lowColor ?? 0x2846BE, out var lr, out var lg, out var lb);
            Unpack(highColor ?? 0xD21E1E, out var hr, out var hg, out var hb);
            r = (byte)Math.Round(lr + (hr - lr) * s);
            g = (byte)Math.Round(lg + (hg - lg) * s);
            b = (byte)Math.Round(lb + (hb - lb) * s);
            return;
        }

        JetColor(s, out r, out g, out b);
    }

    private static readonly double[][] JetStops =
    {
        new[] { 0.00, 40, 70, 190 },   // blue      (gap → 0)
        new[] { 0.25, 40, 180, 200 },  // cyan
        new[] { 0.42, 80, 190, 80 },   // green
        new[] { 0.50, 245, 225, 50 },  // yellow    (at the limit)
        new[] { 0.75, 242, 120, 30 },  // orange
        new[] { 1.00, 210, 30, 30 },   // red       (≥ 2× the limit)
    };

    /// <summary>Blue→green→yellow→orange→red gradient over s ∈ [0, 1].</summary>
    private static void JetColor(double s, out byte r, out byte g, out byte b)
    {
        s = s < 0 ? 0 : (s > 1 ? 1 : s);
        for (int i = 1; i < JetStops.Length; i++)
        {
            if (s <= JetStops[i][0])
            {
                var lo = JetStops[i - 1];
                var hi = JetStops[i];
                var u = (s - lo[0]) / (hi[0] - lo[0]);
                r = (byte)Math.Round(lo[1] + (hi[1] - lo[1]) * u);
                g = (byte)Math.Round(lo[2] + (hi[2] - lo[2]) * u);
                b = (byte)Math.Round(lo[3] + (hi[3] - lo[3]) * u);
                return;
            }
        }

        r = (byte)JetStops[JetStops.Length - 1][1];
        g = (byte)JetStops[JetStops.Length - 1][2];
        b = (byte)JetStops[JetStops.Length - 1][3];
    }
}
