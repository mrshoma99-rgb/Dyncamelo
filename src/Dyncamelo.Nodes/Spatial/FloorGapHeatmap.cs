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
}

/// <summary>
/// Pure, headless-testable core of the floor-opening fall-hazard heat map.
/// Rasterises floor and obstruction triangles onto a plan grid, isolates the
/// openings that are fully enclosed by floor (so the building's exterior is not
/// mistaken for a hole), grades each opening by how far its centre sits from the
/// nearest edge, and renders the result to an RGBA heat map / PNG. All geometry
/// arrives already projected to the analysis plane — no Navisworks types here.
/// </summary>
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
        var openings = LabelOpenings(hazard, clearance, minX, minY, cellSize, cols, rows, minGap);

        return new FloorGapResult(
            cols, rows, minX, minY, cellSize, floor, plug, hazard, clearance, maxClearance, openings);
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

        // Backward pass.
        maxClearance = 0.0;
        for (int r = rows - 1; r >= 0; r--)
        {
            for (int c = cols - 1; c >= 0; c--)
            {
                var idx = r * cols + c;
                if (dist[idx] != 0.0)
                {
                    var best = dist[idx];
                    if (c < cols - 1) best = Math.Min(best, dist[idx + 1] + d1);
                    if (r < rows - 1) best = Math.Min(best, dist[idx + cols] + d1);
                    if (r < rows - 1 && c < cols - 1) best = Math.Min(best, dist[idx + cols + 1] + d2);
                    if (r < rows - 1 && c > 0) best = Math.Min(best, dist[idx + cols - 1] + d2);
                    dist[idx] = best;
                }

                dist[idx] *= cellSize;
                if (dist[idx] > maxClearance)
                {
                    maxClearance = dist[idx];
                }
            }
        }

        return dist;
    }

    // ------------------------------------------------------- Connected openings

    private static IReadOnlyList<FloorOpening> LabelOpenings(
        bool[] hazard, double[] clearance, double originX, double originY, double cellSize,
        int cols, int rows, double minGap)
    {
        var count = cols * rows;
        var visited = new bool[count];
        var openings = new List<FloorOpening>();
        var stack = new Stack<int>();

        for (int start = 0; start < count; start++)
        {
            if (!hazard[start] || visited[start])
            {
                continue;
            }

            visited[start] = true;
            stack.Push(start);

            long cells = 0;
            double sumCx = 0, sumCy = 0, peak = 0;
            int minC = cols, minR = rows, maxC = -1, maxR = -1;

            while (stack.Count > 0)
            {
                var idx = stack.Pop();
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

        openings.Sort((a, b) => b.WidestGap.CompareTo(a.WidestGap));
        return openings;
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
    /// <param name="minGap">The handrail limit: the gap width that maps to the yellow pivot.</param>
    /// <returns>The PNG bytes.</returns>
    public static byte[] RenderPng(FloorGapResult result, int pixelsPerCell = 4, double minGap = 0.0)
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
                GetCellColor(result, idx, minGap, out var cr, out var cg, out var cb, out var ca);

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

        return PngWriter.Encode(width, height, rgba);
    }

    private static void GetCellColor(
        FloorGapResult result, int idx, double minGap,
        out byte r, out byte g, out byte b, out byte a)
    {
        if (result.Hazard[idx])
        {
            // Distance from this void cell to the nearest floor edge or obstacle:
            // the limit thresholds this directly (> limit from any solid = hazard).
            var clearance = result.Clearance[idx];
            LimitRamp(clearance, minGap, result.MaxClearance, out r, out g, out b);
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
    private static void LimitRamp(double clearance, double minGap, double maxClearance, out byte r, out byte g, out byte b)
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
