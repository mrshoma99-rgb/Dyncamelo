using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Navisworks.Api;
using Dyncamelo.Nodes.Spatial;
using ComApi = Autodesk.Navisworks.Api.Interop.ComApi;

namespace Dyncamelo.Navisworks.Internal;

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
    /// range intersects the band [<paramref name="zMin"/>, <paramref name="zMax"/>].
    /// </summary>
    /// <param name="items">The model items to read.</param>
    /// <param name="zMin">Bottom of the analysis band (world Z).</param>
    /// <param name="zMax">Top of the analysis band (world Z).</param>
    /// <returns>The projected triangles; empty when the items carry no geometry in the band.</returns>
    internal static List<Tri2> ReadTrianglesInBand(IEnumerable<ModelItem> items, double zMin, double zMax)
    {
        var triangles = new List<Tri2>();
        if (items == null)
        {
            return triangles;
        }

        foreach (var item in items)
        {
            if (item == null || !item.HasGeometry)
            {
                continue;
            }

            ReadItem(item, zMin, zMax, triangles);
        }

        return triangles;
    }

    private static void ReadItem(ModelItem item, double zMin, double zMax, List<Tri2> sink)
    {
        ComApi.InwOaPath? path = null;
        ComApi.InwNodeFragsColl? fragments = null;
        try
        {
            path = ComBridge.ToPath(item);
            fragments = path.Fragments();
            var callback = new TriangleCollector(zMin, zMax, sink);
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
    /// whose world-Z band overlaps the requested slice. Lines/points are ignored.
    /// </summary>
    private sealed class TriangleCollector : ComApi.InwSimplePrimitivesCB
    {
        private readonly double _zMin;
        private readonly double _zMax;
        private readonly List<Tri2> _sink;
        private double[]? _matrix;

        internal TriangleCollector(double zMin, double zMax, List<Tri2> sink)
        {
            _zMin = zMin;
            _zMax = zMax;
            _sink = sink;
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
            if (hiZ < _zMin || loZ > _zMax)
            {
                return;
            }

            _sink.Add(new Tri2(ax, ay, bx, by, cx, cy));
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
