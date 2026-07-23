using System;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes.Spatial;

/// <summary>
/// A camera view frustum for containment tests against axis-aligned bounding
/// boxes — the geometry core of the Viewpoint.VisibleItems node, kept free of
/// Navisworks types so it is fully unit-testable. The camera convention is
/// Navisworks': identity rotation looks down −Z with +Y up; the rotation is
/// supplied as an axis + angle (unambiguous, unlike raw quaternion component
/// order) and view extents as the half-width/half-height of the view at the
/// focal distance, which works uniformly for perspective and orthographic
/// cameras.
/// </summary>
[IsVisibleInLibrary(false)]
public sealed class ViewFrustum
{
    private readonly double _px, _py, _pz;
    private readonly double _fx, _fy, _fz; // forward (unit)
    private readonly double _ux, _uy, _uz; // up (unit)
    private readonly double _rx, _ry, _rz; // right (unit)
    private readonly bool _orthographic;
    private readonly double _limX, _limY;  // ortho: half extents; perspective: tan(half angle)

    private ViewFrustum(
        double px, double py, double pz,
        double fx, double fy, double fz,
        double ux, double uy, double uz,
        bool orthographic, double limX, double limY)
    {
        _px = px; _py = py; _pz = pz;
        _fx = fx; _fy = fy; _fz = fz;
        _ux = ux; _uy = uy; _uz = uz;
        _rx = fy * uz - fz * uy;
        _ry = fz * ux - fx * uz;
        _rz = fx * uy - fy * ux;
        _orthographic = orthographic;
        _limX = limX;
        _limY = limY;
    }

    /// <summary>
    /// Builds a frustum from camera parameters.
    /// </summary>
    /// <param name="posX">Camera position X.</param>
    /// <param name="posY">Camera position Y.</param>
    /// <param name="posZ">Camera position Z.</param>
    /// <param name="axisX">Rotation axis X (zero-length axis means identity).</param>
    /// <param name="axisY">Rotation axis Y.</param>
    /// <param name="axisZ">Rotation axis Z.</param>
    /// <param name="angle">Rotation angle in radians.</param>
    /// <param name="orthographic">True for an orthographic camera.</param>
    /// <param name="halfWidth">Half the view width at the focal distance (world units).</param>
    /// <param name="halfHeight">Half the view height at the focal distance (world units).</param>
    /// <param name="focalDistance">Focal distance (perspective only; must be positive).</param>
    public static ViewFrustum Create(
        double posX, double posY, double posZ,
        double axisX, double axisY, double axisZ, double angle,
        bool orthographic,
        double halfWidth, double halfHeight, double focalDistance)
    {
        RotateByAxisAngle(axisX, axisY, axisZ, angle, 0, 0, -1, out var fx, out var fy, out var fz);
        RotateByAxisAngle(axisX, axisY, axisZ, angle, 0, 1, 0, out var ux, out var uy, out var uz);

        double limX, limY;
        if (orthographic)
        {
            limX = Math.Max(1e-9, halfWidth);
            limY = Math.Max(1e-9, halfHeight);
        }
        else
        {
            var focal = focalDistance > 1e-9 ? focalDistance : 1.0;
            limX = Math.Max(1e-9, halfWidth / focal);
            limY = Math.Max(1e-9, halfHeight / focal);
        }

        return new ViewFrustum(posX, posY, posZ, fx, fy, fz, ux, uy, uz, orthographic, limX, limY);
    }

    /// <summary>
    /// True when the box is (at least partly) inside the frustum. Conservative
    /// plane test: a box whose corners all fall outside one frustum plane is
    /// rejected; everything else counts as visible.
    /// </summary>
    /// <param name="minX">Box minimum X.</param>
    /// <param name="minY">Box minimum Y.</param>
    /// <param name="minZ">Box minimum Z.</param>
    /// <param name="maxX">Box maximum X.</param>
    /// <param name="maxY">Box maximum Y.</param>
    /// <param name="maxZ">Box maximum Z.</param>
    public bool IntersectsBox(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        var cx = new double[8];
        var cy = new double[8];
        var cz = new double[8];
        ToCameraSpace(minX, minY, minZ, maxX, maxY, maxZ, cx, cy, cz);

        for (int plane = 0; plane < 5; plane++)
        {
            bool allOutside = true;
            for (int i = 0; i < 8; i++)
            {
                if (PlaneValue(plane, cx[i], cy[i], cz[i]) >= 0)
                {
                    allOutside = false;
                    break;
                }
            }

            if (allOutside)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when the box is entirely inside the frustum (all corners pass all
    /// plane tests).
    /// </summary>
    /// <param name="minX">Box minimum X.</param>
    /// <param name="minY">Box minimum Y.</param>
    /// <param name="minZ">Box minimum Z.</param>
    /// <param name="maxX">Box maximum X.</param>
    /// <param name="maxY">Box maximum Y.</param>
    /// <param name="maxZ">Box maximum Z.</param>
    public bool ContainsBox(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        var cx = new double[8];
        var cy = new double[8];
        var cz = new double[8];
        ToCameraSpace(minX, minY, minZ, maxX, maxY, maxZ, cx, cy, cz);

        for (int i = 0; i < 8; i++)
        {
            for (int plane = 0; plane < 5; plane++)
            {
                if (PlaneValue(plane, cx[i], cy[i], cz[i]) < 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Signed inside-distance for one frustum plane in camera space
    /// (0 = eye/near, 1..4 = right/left/top/bottom); negative is outside.
    /// </summary>
    private double PlaneValue(int plane, double x, double y, double z)
    {
        if (_orthographic)
        {
            switch (plane)
            {
                case 0: return z;
                case 1: return _limX - x;
                case 2: return _limX + x;
                case 3: return _limY - y;
                default: return _limY + y;
            }
        }

        switch (plane)
        {
            case 0: return z;
            case 1: return z * _limX - x;
            case 2: return z * _limX + x;
            case 3: return z * _limY - y;
            default: return z * _limY + y;
        }
    }

    private void ToCameraSpace(
        double minX, double minY, double minZ, double maxX, double maxY, double maxZ,
        double[] cx, double[] cy, double[] cz)
    {
        int i = 0;
        for (int xi = 0; xi < 2; xi++)
        {
            for (int yi = 0; yi < 2; yi++)
            {
                for (int zi = 0; zi < 2; zi++)
                {
                    var dx = (xi == 0 ? minX : maxX) - _px;
                    var dy = (yi == 0 ? minY : maxY) - _py;
                    var dz = (zi == 0 ? minZ : maxZ) - _pz;
                    cx[i] = dx * _rx + dy * _ry + dz * _rz;
                    cy[i] = dx * _ux + dy * _uy + dz * _uz;
                    cz[i] = dx * _fx + dy * _fy + dz * _fz;
                    i++;
                }
            }
        }
    }

    /// <summary>Rodrigues rotation of a vector around a (not necessarily unit) axis.</summary>
    private static void RotateByAxisAngle(
        double axisX, double axisY, double axisZ, double angle,
        double vx, double vy, double vz,
        out double outX, out double outY, out double outZ)
    {
        var length = Math.Sqrt(axisX * axisX + axisY * axisY + axisZ * axisZ);
        if (length < 1e-12 || Math.Abs(angle) < 1e-12)
        {
            outX = vx;
            outY = vy;
            outZ = vz;
            return;
        }

        var kx = axisX / length;
        var ky = axisY / length;
        var kz = axisZ / length;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var dot = kx * vx + ky * vy + kz * vz;

        // v' = v·cosθ + (k×v)·sinθ + k·(k·v)·(1−cosθ)
        var crossX = ky * vz - kz * vy;
        var crossY = kz * vx - kx * vz;
        var crossZ = kx * vy - ky * vx;

        outX = vx * cos + crossX * sin + kx * dot * (1 - cos);
        outY = vy * cos + crossY * sin + ky * dot * (1 - cos);
        outZ = vz * cos + crossZ * sin + kz * dot * (1 - cos);
    }
}
