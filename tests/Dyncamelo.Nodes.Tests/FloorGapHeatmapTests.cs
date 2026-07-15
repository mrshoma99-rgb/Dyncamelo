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
    [InlineData(1.0, true)]   // gap ≈ 2 ≥ 1 → flagged
    [InlineData(3.0, false)]  // gap ≈ 2 < 3 → not flagged
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
        var png = FloorGapHeatmap.RenderPng(result, pixelsPerCell: 3);

        var (w, h) = ReadPngSize(png);
        Assert.Equal(result.Cols * 3, w);
        Assert.Equal(result.Rows * 3, h);
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
