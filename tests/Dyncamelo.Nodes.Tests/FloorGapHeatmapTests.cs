using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Dyncamelo.Nodes.Imaging;
using Dyncamelo.Nodes.Spatial;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

public class FloorGapHeatmapTests
{
    /// <summary>Two triangles covering an axis-aligned rectangle.</summary>
    private static IEnumerable<Tri2> Rect(double x0, double y0, double x1, double y1)
    {
        yield return new Tri2(x0, y0, x1, y0, x1, y1);
        yield return new Tri2(x0, y0, x1, y1, x0, y1);
    }

    /// <summary>A 10×10 slab with a 2×2 hole in the middle, as a frame of four strips.</summary>
    private static List<Tri2> DonutSlab()
    {
        var tris = new List<Tri2>();
        tris.AddRange(Rect(0, 0, 10, 4));   // bottom
        tris.AddRange(Rect(0, 6, 10, 10));  // top
        tris.AddRange(Rect(0, 4, 4, 6));    // left
        tris.AddRange(Rect(6, 4, 10, 6));   // right
        return tris;                        // hole = [4,6] × [4,6]
    }

    // ----------------------------------------------------------- Hole detection

    [Fact]
    public void Analyze_FindsTheEnclosedHole()
    {
        var result = FloorGapHeatmap.Analyze(
            0, 0, 10, 10, 0.5, DonutSlab(), Array.Empty<Tri2>(), minGap: 0.0);

        Assert.Single(result.Openings);
        var hole = result.Openings[0];
        Assert.Equal(5.0, hole.CenterX, 1);
        Assert.Equal(5.0, hole.CenterY, 1);
        Assert.Equal(4.0, hole.Area, 1);            // 2×2
        Assert.InRange(hole.WidestGap, 1.5, 2.5);   // ≈ 2 (inscribed diameter)
    }

    [Fact]
    public void Analyze_DoesNotFlagTheExterior()
    {
        // A solid slab with padding all around — the empty margin is exterior, not a hole.
        var result = FloorGapHeatmap.Analyze(
            -2, -2, 12, 12, 0.5, Rect(0, 0, 10, 10).ToList(), Array.Empty<Tri2>(), minGap: 0.0);

        Assert.Empty(result.Openings);
    }

    [Fact]
    public void Analyze_ObstructionPlugsTheHole()
    {
        // Equipment fills the hole → no fall hazard.
        var result = FloorGapHeatmap.Analyze(
            0, 0, 10, 10, 0.5, DonutSlab(), Rect(4, 4, 6, 6).ToList(), minGap: 0.0);

        Assert.Empty(result.Openings);
    }

    [Fact]
    public void Analyze_PartialObstructionLeavesAGap()
    {
        // Equipment sits in the corner of the hole, leaving open floor on the far side.
        var result = FloorGapHeatmap.Analyze(
            0, 0, 10, 10, 0.5, DonutSlab(), Rect(4, 4, 5, 5).ToList(), minGap: 0.0);

        Assert.Single(result.Openings);
        Assert.True(result.Openings[0].Area < 4.0); // smaller than the whole hole
    }

    [Theory]
    [InlineData(0.5, true)]   // deepest point ≈ 0.875 from an edge ≥ 0.5 → flagged
    [InlineData(1.5, false)]  // 0.875 < 1.5 → within reach of an edge → not flagged
    public void Analyze_FlagsByThreshold(double minGap, bool expectFlagged)
    {
        var result = FloorGapHeatmap.Analyze(
            0, 0, 10, 10, 0.5, DonutSlab(), Array.Empty<Tri2>(), minGap);

        Assert.Single(result.Openings);
        Assert.Equal(expectFlagged, result.Openings[0].Flagged);
    }

    [Fact]
    public void Analyze_TwoSeparateHoles_AreCountedSeparately()
    {
        // A 20×10 slab with two holes: [4,6]×[4,6] and [14,16]×[4,6].
        var tris = new List<Tri2>();
        tris.AddRange(Rect(0, 0, 20, 4));
        tris.AddRange(Rect(0, 6, 20, 10));
        tris.AddRange(Rect(0, 4, 4, 6));
        tris.AddRange(Rect(6, 4, 14, 6));   // bridge between the two holes
        tris.AddRange(Rect(16, 4, 20, 6));
        var result = FloorGapHeatmap.Analyze(0, 0, 20, 10, 0.5, tris, Array.Empty<Tri2>(), minGap: 0.0);

        Assert.Equal(2, result.Openings.Count);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    public void Analyze_ClearanceIsCellSizeIndependent(double cell)
    {
        // A 6×6 slab with a 4×4 hole: the hole's deepest point is ~2 m from any
        // edge, whatever the resolution. (Regression: the distance transform used
        // to collapse this to ~1 cell, so the clearance — and therefore the heat-map
        // colours — changed with cell size.)
        var slab = new List<Tri2>();
        slab.AddRange(Rect(0, 0, 6, 1));   // bottom
        slab.AddRange(Rect(0, 5, 6, 6));   // top
        slab.AddRange(Rect(0, 1, 1, 5));   // left
        slab.AddRange(Rect(5, 1, 6, 5));   // right  → hole [1,5]×[1,5]

        var result = FloorGapHeatmap.Analyze(0, 0, 6, 6, cell, slab, Array.Empty<Tri2>(), minGap: 0.2);

        Assert.Single(result.Openings);
        Assert.InRange(result.MaxClearance, 1.8, 2.2); // ≈ 2 m at every cell size
        Assert.InRange(result.Openings[0].WidestGap, 3.6, 4.4);
    }

    // ------------------------------------------------------- Edge / handrail

    [Fact]
    public void AnalyzeEdges_WideVoidNoHandrail_AllEdgesDangerous()
    {
        var result = FloorGapHeatmap.Analyze(0, 0, 10, 10, 0.5, DonutSlab(), Array.Empty<Tri2>(), minGap: 0.2);
        var edges = FloorGapHeatmap.AnalyzeEdges(result, Array.Empty<Tri2>(), limit: 0.5, tolerance: 0.3);

        Assert.True(edges.DangerousLength > 0);         // 2 m gap ≫ 0.5 all round
        Assert.Equal(0.0, edges.ProtectedLength);
        Assert.Equal(0.0, edges.SafeLength);
    }

    [Fact]
    public void AnalyzeEdges_LimitAboveGap_AllEdgesSafe()
    {
        var result = FloorGapHeatmap.Analyze(0, 0, 10, 10, 0.5, DonutSlab(), Array.Empty<Tri2>(), minGap: 0.2);
        var edges = FloorGapHeatmap.AnalyzeEdges(result, Array.Empty<Tri2>(), limit: 3.0, tolerance: 0.3);

        Assert.True(edges.SafeLength > 0);              // 2 m hole < 3 m limit
        Assert.Equal(0.0, edges.DangerousLength);
        Assert.Equal(0.0, edges.ProtectedLength);
    }

    [Fact]
    public void AnalyzeEdges_HandrailProtectsOnlyItsSide()
    {
        var result = FloorGapHeatmap.Analyze(0, 0, 10, 10, 0.5, DonutSlab(), Array.Empty<Tri2>(), minGap: 0.2);
        // A rail projected along the hole's bottom edge (row centred at y=3.75), spanning its width.
        var rail = Rect(4, 3.7, 6, 4.0).ToList();
        var edges = FloorGapHeatmap.AnalyzeEdges(result, rail, limit: 0.5, tolerance: 0.4);

        Assert.True(edges.ProtectedLength > 0);         // the railed edge
        Assert.True(edges.DangerousLength > 0);         // the other three sides
    }

    [Theory]
    [InlineData(0.5, false)]  // 0.2 m slot < 0.5 limit → every edge safe, ends included
    [InlineData(0.15, true)]  // 0.2 m slot ≥ 0.15 limit → dangerous
    public void AnalyzeEdges_NarrowSlot_EndsJudgedByWidthNotLength(double limit, bool expectDanger)
    {
        // A 0.2 m × 3 m slot in a 10×10 slab. The old axis-march measured the
        // slot's LENGTH from its end edges (3 m ≥ any limit → dangerous); the gap
        // that matters is its WIDTH — you cannot fall through 0.2 m however long
        // it is. Regression for: a 0.184 m gap read as above a 0.5 limit.
        var slab = new List<Tri2>();
        slab.AddRange(Rect(0, 0, 3, 10));      // left of the slot
        slab.AddRange(Rect(3.2, 0, 10, 10));   // right of the slot
        slab.AddRange(Rect(3, 0, 3.2, 3));     // below it
        slab.AddRange(Rect(3, 6, 3.2, 10));    // above it → slot [3,3.2]×[3,6]

        var result = FloorGapHeatmap.Analyze(0, 0, 10, 10, 0.05, slab, Array.Empty<Tri2>(), minGap: limit);
        var edges = FloorGapHeatmap.AnalyzeEdges(result, Array.Empty<Tri2>(), limit, tolerance: 0.3);

        if (expectDanger)
        {
            Assert.True(edges.DangerousLength > 0);
        }
        else
        {
            Assert.Equal(0.0, edges.DangerousLength);
            Assert.True(edges.SafeLength > 0);
        }
    }

    [Theory]
    [InlineData(0.15, "DDDD")]  // all four strips ≥ 0.15
    [InlineData(0.20, "SDDD")]  // right 0.184 < 0.2 → safe; others dangerous
    [InlineData(0.30, "SSDD")]  // top 0.275 < 0.3 → safe
    [InlineData(0.45, "SSDD")]  // left 0.516 ≥ 0.45 → still dangerous
    [InlineData(0.70, "SSSD")]  // left 0.516 < 0.7 → safe; bottom 1.025 dangerous
    [InlineData(1.10, "SSSS")]  // bottom 1.025 < 1.1 → all safe
    public void AnalyzeEdges_StripsFlipExactlyAtTheirGapWidth(double limit, string expected)
    {
        // The user's real model, distilled: an opening with equipment through it
        // leaving four strips — right 0.184, top 0.275, left 0.516, bottom 1.025.
        // Each strip's edge must flip from dangerous to safe exactly when the
        // limit passes its width (±1 cell). Regression for: a 0.184 m gap read
        // as dangerous at a 0.2 m (and even 0.3 m) limit.
        var slab = new List<Tri2>();
        slab.AddRange(Rect(0, 0, 10, 2));       // hole [2,6.4]×[2,6.5]
        slab.AddRange(Rect(0, 6.5, 10, 10));
        slab.AddRange(Rect(0, 2, 2, 6.5));
        slab.AddRange(Rect(6.4, 2, 10, 6.5));
        var box = Rect(2.516, 3.025, 6.216, 6.225).ToList(); // the equipment

        const double cell = 0.05;
        var result = FloorGapHeatmap.Analyze(0, 0, 10, 10, cell, slab, box, limit);
        var edges = FloorGapHeatmap.AnalyzeEdges(result, Array.Empty<Tri2>(), limit, tolerance: 0.3);

        string Probe(double x, double y)
        {
            var c = (int)((x - result.OriginX) / cell);
            var r = (int)((y - result.OriginY) / cell);
            // walk outward from the probe until a classified edge cell is found
            for (int ring = 0; ring <= 4; ring++)
            {
                for (int dr = -ring; dr <= ring; dr++)
                {
                    for (int dc = -ring; dc <= ring; dc++)
                    {
                        var rr = r + dr;
                        var cc = c + dc;
                        if (rr < 0 || rr >= result.Rows || cc < 0 || cc >= result.Cols) continue;
                        var cls = edges.Edges[rr * result.Cols + cc];
                        if (cls == EdgeClass.Dangerous) return "D";
                        if (cls == EdgeClass.SafeGap) return "S";
                    }
                }
            }

            return "?";
        }

        // Mid-strip probes on the floor just outside each opening edge.
        var actual =
            Probe(6.4 + cell, 4.5) +   // right strip (0.184)
            Probe(4.5, 6.5 + cell) +   // top strip (0.275)
            Probe(2 - cell, 4.5) +     // left strip (0.516)
            Probe(4.5, 2 - cell);      // bottom strip (1.025)
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void AnalyzeEdges_CornersOfADangerousOpening_AreDangerous()
    {
        // Regression: the core of a big opening sits diagonally at √2·(limit/2)
        // from a corner, which slipped past the straight-wall reach — every
        // corner of a square opening read safe while its sides read dangerous.
        var slab = new List<Tri2>();
        slab.AddRange(Rect(0, 0, 10, 2));   // opening [2,8]×[2,8] — 6×6, clearly dangerous
        slab.AddRange(Rect(0, 8, 10, 10));
        slab.AddRange(Rect(0, 2, 2, 8));
        slab.AddRange(Rect(8, 2, 10, 8));

        const double cell = 0.05;
        var result = FloorGapHeatmap.Analyze(0, 0, 10, 10, cell, slab, Array.Empty<Tri2>(), minGap: 0.5);
        var edges = FloorGapHeatmap.AnalyzeEdges(result, Array.Empty<Tri2>(), limit: 0.5, tolerance: 0.3);

        // The floor cell diagonally outside the corner and the side cells beside
        // it must all be dangerous — nothing green at the corner of a 6 m hole.
        foreach (var (x, y) in new[] { (2 - cell, 2 - cell), (2 + cell, 2 - cell), (2 - cell, 2 + cell) })
        {
            var c = (int)((x - result.OriginX) / cell);
            var r = (int)((y - result.OriginY) / cell);
            var cls = edges.Edges[r * result.Cols + c];
            Assert.True(cls == EdgeClass.Dangerous, $"corner cell at ({x},{y}) was {cls}");
        }

        Assert.Equal(0.0, edges.SafeLength);
    }

    [Theory]
    [InlineData(0.5, true)]   // 0.4 m break < 0.5 min passage → nobody fits through → safe
    [InlineData(0.1, false)]  // 0.4 m break ≥ 0.1 min passage → stays dangerous
    public void AnalyzeEdges_ShortBreakBetweenHandrails_IsSafeUnderMinPassage(double minPassage, bool expectSafe)
    {
        // Opening [2,8]×[2,6]; rails cover the whole rim except a 0.6 m break in
        // the top side. With tolerance 0.1, the unprotected run is ~0.4 m.
        var slab = new List<Tri2>();
        slab.AddRange(Rect(0, 0, 10, 2));
        slab.AddRange(Rect(0, 6, 10, 10));
        slab.AddRange(Rect(0, 2, 2, 6));
        slab.AddRange(Rect(8, 2, 10, 6));

        var rails = new List<Tri2>();
        rails.AddRange(Rect(1.9, 1.9, 8.1, 2.0));    // south rim
        rails.AddRange(Rect(1.9, 6.0, 4.7, 6.1));    // north rim, west part
        rails.AddRange(Rect(5.3, 6.0, 8.1, 6.1));    // north rim, east part → 0.6 break
        rails.AddRange(Rect(1.9, 1.9, 2.0, 6.1));    // west rim
        rails.AddRange(Rect(8.0, 1.9, 8.1, 6.1));    // east rim

        const double cell = 0.05;
        var result = FloorGapHeatmap.Analyze(0, 0, 10, 10, cell, slab, Array.Empty<Tri2>(), minGap: 0.5);
        var edges = FloorGapHeatmap.AnalyzeEdges(result, rails, limit: 0.5, tolerance: 0.1, minPassage: minPassage);

        if (expectSafe)
        {
            Assert.Equal(0.0, edges.DangerousLength);
            Assert.True(edges.SafeLength > 0);
        }
        else
        {
            Assert.InRange(edges.DangerousLength, 0.2, 0.7); // the ~0.4 m break
        }

        Assert.True(edges.ProtectedLength > 10); // most of the ~20 m rim is railed
    }

    [Fact]
    public void AnalyzeEdges_LabelsDangerousRunsWithGapOverLimit()
    {
        // 6×6 opening, limit 0.5 → one dangerous rim run labelled ≈ +5.5.
        var slab = new List<Tri2>();
        slab.AddRange(Rect(0, 0, 10, 2));
        slab.AddRange(Rect(0, 8, 10, 10));
        slab.AddRange(Rect(0, 2, 2, 8));
        slab.AddRange(Rect(8, 2, 10, 8));

        var result = FloorGapHeatmap.Analyze(0, 0, 10, 10, 0.05, slab, Array.Empty<Tri2>(), minGap: 0.5);
        var edges = FloorGapHeatmap.AnalyzeEdges(result, Array.Empty<Tri2>(), limit: 0.5, tolerance: 0.3);

        var label = Assert.Single(edges.Labels);
        Assert.InRange(label.Overage, 5.1, 5.9);        // widest gap ~6 − limit 0.5
        Assert.InRange(label.X, 4, 6);                  // centroid of the rim ≈ opening centre
        Assert.InRange(label.Y, 4, 6);
    }

    [Fact]
    public void RenderEdgePng_CustomPaletteAndOverageLabels()
    {
        var result = FloorGapHeatmap.Analyze(0, 0, 10, 10, 0.5, DonutSlab(), Array.Empty<Tri2>(), minGap: 0.2);
        var edges = FloorGapHeatmap.AnalyzeEdges(result, Array.Empty<Tri2>(), limit: 0.5, tolerance: 0.3);

        var palette = new EdgePalette { Dangerous = 0xFF00FF }; // magenta instead of red
        var png = FloorGapHeatmap.RenderEdgePng(edges, 4, palette, showOverage: true);

        Assert.True(CountExact(png, 255, 0, 255) > 0, "expected magenta dangerous edges");
        Assert.Equal(0, CountExact(png, 220, 40, 40));  // default red fully replaced
        Assert.True(CountExact(png, 255, 255, 255) > 0, "expected white label text");
    }

    [Fact]
    public void RenderEdgePng_ProducesAValidPng()
    {
        var result = FloorGapHeatmap.Analyze(0, 0, 10, 10, 0.5, DonutSlab(), Array.Empty<Tri2>(), minGap: 0.2);
        var edges = FloorGapHeatmap.AnalyzeEdges(result, Array.Empty<Tri2>(), 0.5, 0.3);
        var png = FloorGapHeatmap.RenderEdgePng(edges, 3);

        var (w, h) = ReadPngSize(png);
        Assert.Equal(result.Cols * 3, w);
        Assert.Equal(result.Rows * 3, h);
    }

    [Fact]
    public void Analyze_RejectsTooFineAGrid()
    {
        Assert.Throws<InvalidOperationException>(() =>
            FloorGapHeatmap.Analyze(0, 0, 10000, 10000, 0.001, DonutSlab(), Array.Empty<Tri2>(), 0.0));
    }

    // -------------------------------------------------------------------- Render

    [Fact]
    public void RenderPng_ProducesAValidPngOfTheRightSize()
    {
        var result = FloorGapHeatmap.Analyze(
            0, 0, 10, 10, 0.5, DonutSlab(), Array.Empty<Tri2>(), minGap: 0.0);
        var png = FloorGapHeatmap.RenderPng(result, pixelsPerCell: 3, minGap: 0.5);

        var (w, h) = ReadPngSize(png);
        Assert.Equal(result.Cols * 3, w);
        Assert.Equal(result.Rows * 3, h);
    }

    [Fact]
    public void RenderPng_BelowLimitVoidIsNotRed_AboveLimitIsRed()
    {
        // Donut hole is 2×2, so its deepest point is ~1 unit from any edge.
        var result = FloorGapHeatmap.Analyze(
            0, 0, 10, 10, 0.5, DonutSlab(), Array.Empty<Tri2>(), minGap: 0.0);

        // Limit far above the hole's clearance → nothing reaches it → no red.
        var below = CountRed(FloorGapHeatmap.RenderPng(result, 3, minGap: 5.0));
        Assert.Equal(0, below);

        // Limit well below the clearance → the middle exceeds it → red present.
        var above = CountRed(FloorGapHeatmap.RenderPng(result, 3, minGap: 0.3));
        Assert.True(above > 0, "Expected red hazard pixels above the limit.");
    }

    // ---------------------------------------------------------------- PngWriter

    [Fact]
    public void PngWriter_EncodesPixelsThatInflateBackToTheInput()
    {
        // 2×2 checker: red, green / blue, white.
        var rgba = new byte[]
        {
            255, 0, 0, 255,    0, 255, 0, 255,
            0, 0, 255, 255,    255, 255, 255, 255,
        };
        var png = PngWriter.Encode(2, 2, rgba);

        Assert.True(png.Length > 8);
        // PNG signature.
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png.Take(8).ToArray());

        var (w, h) = ReadPngSize(png);
        Assert.Equal(2, w);
        Assert.Equal(2, h);

        // Inflate IDAT and confirm the scanlines (filter byte + row) match the input.
        var raw = InflateIdat(png);
        var expected = new byte[]
        {
            0, 255, 0, 0, 255, 0, 255, 0, 255,     // row 0: filter None + 2 px
            0, 0, 0, 255, 255, 255, 255, 255, 255, // row 1: filter None + 2 px
        };
        Assert.Equal(expected, raw);
    }

    [Fact]
    public void PngWriter_RejectsAMismatchedBuffer()
    {
        Assert.Throws<ArgumentException>(() => PngWriter.Encode(2, 2, new byte[3]));
    }

    // ------------------------------------------------------------------ Helpers

    private static (int width, int height) ReadPngSize(byte[] png)
    {
        // IHDR width/height are the first two big-endian ints after the 8-byte
        // signature + 4-byte length + 4-byte "IHDR".
        int Read(int offset) =>
            (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];
        return (Read(16), Read(20));
    }

    /// <summary>Counts pixels exactly matching an RGB value in a rendered PNG.</summary>
    private static int CountExact(byte[] png, byte r, byte g, byte b)
    {
        var raw = InflateIdat(png);
        var (w, h) = ReadPngSize(png);
        var stride = w * 4;
        var count = 0;
        for (int y = 0; y < h; y++)
        {
            var rowStart = y * (stride + 1) + 1;
            for (int x = 0; x < w; x++)
            {
                var o = rowStart + x * 4;
                if (raw[o] == r && raw[o + 1] == g && raw[o + 2] == b)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Counts clearly-red pixels (hot end of the ramp) in a rendered PNG.</summary>
    private static int CountRed(byte[] png)
    {
        var raw = InflateIdat(png);
        var (w, h) = ReadPngSize(png);
        var stride = w * 4;
        var count = 0;
        for (int y = 0; y < h; y++)
        {
            var rowStart = y * (stride + 1) + 1; // skip the per-row filter byte
            for (int x = 0; x < w; x++)
            {
                var o = rowStart + x * 4;
                if (raw[o] >= 180 && raw[o + 1] <= 90 && raw[o + 2] <= 90)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static byte[] InflateIdat(byte[] png)
    {
        // Walk chunks to find IDAT, strip the 2-byte zlib header, inflate the rest.
        var offset = 8;
        using var output = new MemoryStream();
        while (offset < png.Length)
        {
            var length = (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];
            var type = new string(new[] { (char)png[offset + 4], (char)png[offset + 5], (char)png[offset + 6], (char)png[offset + 7] });
            var dataStart = offset + 8;
            if (type == "IDAT")
            {
                using var raw = new MemoryStream(png, dataStart + 2, length - 2); // skip zlib header
                using var inflate = new DeflateStream(raw, CompressionMode.Decompress);
                inflate.CopyTo(output);
            }

            offset = dataStart + length + 4; // + CRC
        }

        return output.ToArray();
    }
}
