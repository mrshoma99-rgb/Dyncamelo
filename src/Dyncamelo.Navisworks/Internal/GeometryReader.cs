using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Navisworks.Api;
using Dyncamelo.Nodes.Spatial;
using ComApi = Autodesk.Navisworks.Api.Interop.ComApi;

namespace Dyncamelo.Navisworks.Internal;

/// <summary>The outcome of a banded geometry read: the in-band triangles plus context for diagnostics.</summary>
internal sealed class BandRead
{
    /// <summary>Triangles (XY projected) whose world-Z range overlaps the requested band.</summary>
    public List<Tri2> Triangles { get; } = new List<Tri2>();

    /// <summary>Lowest world Z of any triangle read (before band filtering); +∞ when none.</summary>
    public double MinZ { get; set; } = double.PositiveInfinity;

    /// <summary>Highest world Z of any triangle read (before band filtering); −∞ when none.</summary>
    public double MaxZ { get; set; } = double.NegativeInfinity;

    /// <summary>Total triangles read across all items, regardless of the band.</summary>
    public int TotalTriangles { get; set; }

    /// <summary>True when any geometry at all was read.</summary>
    public bool HasAnyGeometry => TotalTriangles > 0;
}

/// <summary>
/// Reads world-space geometry from model items through the COM bridge
/// (<c>InwOaFragment3.GenerateSimplePrimitives</c>) and projects the triangles
/// onto the horizontal analysis plane. Live-session only — the COM bridge is
/// unavailable headlessly. Read-only: touches no viewport or document state, so
/// it is safe to run on large selections without the freeze that per-item
/// override operations cause.
/// </summary>
internal static class GeometryReader
{
    /// <summary>
    /// Collects the XY projection of every triangle in the items whose world-Z
    /// range intersects the band [<paramref name="zMin"/>, <paramref name="zMax"/>],
    /// while also reporting the full world-Z range of all geometry read (so a
    /// caller can tell the user where their floor actually sits when the band
    /// catches nothing).
    /// </summary>
    /// <param name="items">The model items to read (flatten to geometry leaves first).</param>
    /// <param name="zMin">Bottom of the analysis band (world Z).</param>
    /// <param name="zMax">Top of the analysis band (world Z).</param>
    /// <returns>The triangles in the band plus the overall Z range and triangle count observed.</returns>
    internal static BandRead ReadTrianglesInBand(IEnumerable<ModelItem> items, double zMin, double zMax)
    {
        var read = new BandRead();
        if (items == null)
        {
            return read;
        }

        var callback = new TriangleCollector(zMin, zMax, read);
        foreach (var item in items)
        {
            if (item == null || !item.HasGeometry)
            {
                continue;
            }

            ReadItem(item, callback);
        }

        return read;
    }

    private static void ReadItem(ModelItem item, TriangleCollector callback)
    {
        ComApi.InwOaPath? path = null;
        ComApi.InwNodeFragsColl? fragments = null;
        try
        {
            path = ComBridge.ToPath(item);
            fragments = path.Fragments();
            foreach (var fragmentObject in fragments)
            {
                var fragment = fragmentObject as ComApi.InwOaFragment3;
                if (fragment == null)
                {
                    ComBridge.Release(fragmentObject);
                    continue;
                }

                object? matrix = null;
                try
                {
                    matrix = fragment.GetLocalToWorldMatrix();
                    callback.SetMatrix(ToMatrix(matrix as ComApi.InwLTransform3f2));
                    fragment.GenerateSimplePrimitives(ComApi.nwEVertexProperty.eNORMAL, callback);
                }
                finally
                {
                    ComBridge.Release(matrix, fragment);
                }
            }
        }
        catch (Exception)
        {
            // Best-effort per item: a fragment we cannot read simply contributes
            // no triangles rather than failing the whole analysis.
        }
        finally
        {
            ComBridge.Release(fragments, path);
        }
    }

    /// <summary>The 16-element local-to-world matrix as a 0-based double array (null when unavailable).</summary>
    private static double[]? ToMatrix(ComApi.InwLTransform3f2? matrix)
    {
        var values = matrix?.Matrix as Array;
        if (values == null || values.Length < 16)
        {
            return null;
        }

        var m = new double[16];
        var i = 0;
        foreach (var value in values)
        {
            if (i >= 16)
            {
                break;
            }

            m[i++] = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        return m;
    }

    /// <summary>
    /// The <c>InwSimplePrimitivesCB</c> sink: keeps the XY footprint of triangles
    /// whose world-Z band overlaps the requested slice, and tracks the full Z
    /// range and count of all triangles seen. Lines/points are ignored.
    /// </summary>
    private sealed class TriangleCollector : ComApi.InwSimplePrimitivesCB
    {
        private readonly double _zMin;
        private readonly double _zMax;
        private readonly BandRead _read;
        private double[]? _matrix;

        internal TriangleCollector(double zMin, double zMax, BandRead read)
        {
            _zMin = zMin;
            _zMax = zMax;
            _read = read;
        }

        internal void SetMatrix(double[]? matrix)
        {
            _matrix = matrix;
        }

        public void Triangle(ComApi.InwSimpleVertex v1, ComApi.InwSimpleVertex v2, ComApi.InwSimpleVertex v3)
        {
            if (!ToWorld(v1, out var ax, out var ay, out var az) ||
                !ToWorld(v2, out var bx, out var by, out var bz) ||
                !ToWorld(v3, out var cx, out var cy, out var cz))
            {
                return;
            }

            var loZ = Math.Min(az, Math.Min(bz, cz));
            var hiZ = Math.Max(az, Math.Max(bz, cz));

            _read.TotalTriangles++;
            if (loZ < _read.MinZ) _read.MinZ = loZ;
            if (hiZ > _read.MaxZ) _read.MaxZ = hiZ;

            if (hiZ < _zMin || loZ > _zMax)
            {
                return;
            }

            _read.Triangles.Add(new Tri2(ax, ay, bx, by, cx, cy));
        }

        public void Line(ComApi.InwSimpleVertex v1, ComApi.InwSimpleVertex v2)
        {
        }

        public void Point(ComApi.InwSimpleVertex v1)
        {
        }

        public void SnapPoint(ComApi.InwSimpleVertex v1)
        {
        }

        private bool ToWorld(ComApi.InwSimpleVertex vertex, out double x, out double y, out double z)
        {
            x = 0;
            y = 0;
            z = 0;
            var coord = vertex?.coord as Array;
            if (coord == null || coord.Length < 3)
            {
                return false;
            }

            var lb = coord.GetLowerBound(0);
            var lx = Convert.ToDouble(coord.GetValue(lb), CultureInfo.InvariantCulture);
            var ly = Convert.ToDouble(coord.GetValue(lb + 1), CultureInfo.InvariantCulture);
            var lz = Convert.ToDouble(coord.GetValue(lb + 2), CultureInfo.InvariantCulture);

            var m = _matrix;
            if (m == null)
            {
                x = lx;
                y = ly;
                z = lz;
                return true;
            }

            // Column-major 4×4 with translation at 12,13,14 (matches the layout the
            // COM bridge exposes elsewhere in this project).
            x = m[0] * lx + m[4] * ly + m[8] * lz + m[12];
            y = m[1] * lx + m[5] * ly + m[9] * lz + m[13];
            z = m[2] * lx + m[6] * ly + m[10] * lz + m[14];
            return true;
        }
    }
}
