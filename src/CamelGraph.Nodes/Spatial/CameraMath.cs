using System;
using System.Collections.Generic;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Spatial;

/// <summary>
/// Pure camera geometry for the Camera.SetStandardView and SavedViewpoint.Info
/// nodes, free of Navisworks types so it is fully unit-testable.
///
/// World convention (the Navisworks default for architectural models): +Z is up,
/// +Y is north, +X is east. The camera convention is Navisworks': identity rotation
/// looks down -Z with +Y up, and rotations are quaternions (A,B,C = vector part,
/// D = scalar part; identity is 0,0,0,1) — the same convention as
/// <see cref="ViewFrustum"/>.
///
/// Standard views (direction the camera LOOKS along, and the direction that points
/// to the top of the screen):
/// top = down -Z with north up; bottom = up +Z with south up (X still to the right);
/// front = looks north (+Y) from the south side, Z up; back = looks south from the
/// north side; left = looks east (+X) from the west side; right = looks west from the
/// east side; iso = south-east isometric (looks toward north-west and down, i.e. along
/// (-1, +1, -1)), Z up.
/// </summary>
[IsVisibleInLibrary(false)]
public static class CameraMath
{
    /// <summary>The standard view names, in the order the node's dropdown offers them.</summary>
    public static readonly IReadOnlyList<string> StandardViewNames =
        new[] { "top", "bottom", "front", "back", "left", "right", "iso" };

    /// <summary>
    /// Normalises a standard-view name: case-insensitive, surrounding spaces ignored,
    /// and the common synonyms accepted ("plan" = top, "isometric"/"3d" = iso, and so on).
    /// </summary>
    /// <param name="view">The name typed or picked by the user.</param>
    /// <returns>One of <see cref="StandardViewNames"/>.</returns>
    /// <exception cref="ArgumentException">The name is empty or not a standard view.</exception>
    public static string NormalizeViewName(string? view)
    {
        var key = (view ?? string.Empty).Trim().ToLowerInvariant();
        switch (key)
        {
            case "top":
            case "plan":
            case "up":
                return "top";
            case "bottom":
            case "underside":
            case "down":
                return "bottom";
            case "front":
            case "south":
                return "front";
            case "back":
            case "rear":
            case "north":
                return "back";
            case "left":
            case "west":
                return "left";
            case "right":
            case "east":
                return "right";
            case "iso":
            case "isometric":
            case "3d":
                return "iso";
            default:
                throw new ArgumentException(
                    "'" + view + "' is not a standard view. Use one of: " + string.Join(", ", StandardViewNames) + ".",
                    nameof(view));
        }
    }

    /// <summary>The unit direction the camera looks along (from the eye toward the target) for a standard view.</summary>
    /// <param name="view">A standard view name (see <see cref="NormalizeViewName"/>).</param>
    /// <returns>The unit forward vector.</returns>
    public static (double X, double Y, double Z) ViewDirection(string view)
    {
        switch (NormalizeViewName(view))
        {
            case "top": return (0.0, 0.0, -1.0);
            case "bottom": return (0.0, 0.0, 1.0);
            case "front": return (0.0, 1.0, 0.0);
            case "back": return (0.0, -1.0, 0.0);
            case "left": return (1.0, 0.0, 0.0);
            case "right": return (-1.0, 0.0, 0.0);
            default:
                var k = 1.0 / Math.Sqrt(3.0);
                return (-k, k, -k);
        }
    }

    /// <summary>The direction that points to the top of the screen for a standard view (a unit vector, perpendicular to the view direction).</summary>
    /// <param name="view">A standard view name (see <see cref="NormalizeViewName"/>).</param>
    /// <returns>The unit up vector.</returns>
    public static (double X, double Y, double Z) UpDirection(string view)
    {
        switch (NormalizeViewName(view))
        {
            case "top": return (0.0, 1.0, 0.0);      // north at the top of a plan
            case "bottom": return (0.0, -1.0, 0.0);  // seen from below with X still to the right
            default: return (0.0, 0.0, 1.0);
        }
    }

    /// <summary>
    /// How far from the target centre the eye should start, before the camera is
    /// refined by a zoom-to-box: three radii of the bounding sphere, or one unit
    /// when the box has no size.
    /// </summary>
    /// <param name="sizeX">Box extent along X.</param>
    /// <param name="sizeY">Box extent along Y.</param>
    /// <param name="sizeZ">Box extent along Z.</param>
    /// <returns>A positive distance.</returns>
    public static double SuggestedDistance(double sizeX, double sizeY, double sizeZ)
    {
        var radius = 0.5 * Math.Sqrt((sizeX * sizeX) + (sizeY * sizeY) + (sizeZ * sizeZ));
        return radius > 1e-9 && !double.IsInfinity(radius) ? 3.0 * radius : 1.0;
    }

    /// <summary>The eye position for a standard view: the target centre pushed back along the view direction.</summary>
    /// <param name="view">A standard view name (see <see cref="NormalizeViewName"/>).</param>
    /// <param name="centreX">Target centre X.</param>
    /// <param name="centreY">Target centre Y.</param>
    /// <param name="centreZ">Target centre Z.</param>
    /// <param name="distance">Distance from the centre to the eye (positive).</param>
    /// <returns>The eye point.</returns>
    public static (double X, double Y, double Z) EyePosition(
        string view, double centreX, double centreY, double centreZ, double distance)
    {
        if (!(distance > 0.0) || double.IsInfinity(distance))
        {
            throw new ArgumentOutOfRangeException(nameof(distance), "The eye distance must be a positive, finite number.");
        }

        var d = ViewDirection(view);
        return (centreX - (d.X * distance), centreY - (d.Y * distance), centreZ - (d.Z * distance));
    }

    /// <summary>
    /// The unit direction a Navisworks camera looks along, from its rotation quaternion:
    /// the rotation applied to the camera's rest direction (0, 0, -1). The quaternion need
    /// not be normalised; a zero quaternion is treated as the identity.
    /// </summary>
    /// <param name="a">Quaternion X (vector part).</param>
    /// <param name="b">Quaternion Y (vector part).</param>
    /// <param name="c">Quaternion Z (vector part).</param>
    /// <param name="d">Quaternion W (scalar part).</param>
    /// <returns>The unit forward vector.</returns>
    public static (double X, double Y, double Z) ForwardFromRotation(double a, double b, double c, double d)
    {
        var norm = Math.Sqrt((a * a) + (b * b) + (c * c) + (d * d));
        if (norm < 1e-12 || double.IsNaN(norm) || double.IsInfinity(norm))
        {
            return (0.0, 0.0, -1.0);
        }

        a /= norm;
        b /= norm;
        c /= norm;
        d /= norm;

        // v' = v + 2w (q x v) + 2 q x (q x v) with v = (0, 0, -1).
        var tx = (b * -1.0) - (c * 0.0);   // (q x v).x
        var ty = (c * 0.0) - (a * -1.0);   // (q x v).y
        var tz = (a * 0.0) - (b * 0.0);    // (q x v).z
        var x = 0.0 + (2.0 * d * tx) + (2.0 * ((b * tz) - (c * ty)));
        var y = 0.0 + (2.0 * d * ty) + (2.0 * ((c * tx) - (a * tz)));
        var z = -1.0 + (2.0 * d * tz) + (2.0 * ((a * ty) - (b * tx)));
        var length = Math.Sqrt((x * x) + (y * y) + (z * z));
        return length < 1e-12 ? (0.0, 0.0, -1.0) : (x / length, y / length, z / length);
    }

    /// <summary>
    /// The point a camera is looking at: its position moved along its view direction.
    /// </summary>
    /// <param name="positionX">Camera position X.</param>
    /// <param name="positionY">Camera position Y.</param>
    /// <param name="positionZ">Camera position Z.</param>
    /// <param name="a">Rotation quaternion X.</param>
    /// <param name="b">Rotation quaternion Y.</param>
    /// <param name="c">Rotation quaternion Z.</param>
    /// <param name="d">Rotation quaternion W.</param>
    /// <param name="distance">How far ahead of the camera: its focal distance, or any positive length.</param>
    /// <returns>The look-at point.</returns>
    public static (double X, double Y, double Z) LookAtPoint(
        double positionX, double positionY, double positionZ,
        double a, double b, double c, double d,
        double distance)
    {
        var f = ForwardFromRotation(a, b, c, d);
        return (positionX + (f.X * distance), positionY + (f.Y * distance), positionZ + (f.Z * distance));
    }

    /// <summary>
    /// The distance to use for a look-at point: the camera's focal distance when it has a
    /// usable one, otherwise one document unit.
    /// </summary>
    /// <param name="hasFocalDistance">Whether the viewpoint stores a focal distance.</param>
    /// <param name="focalDistance">The stored focal distance (ignored unless usable).</param>
    /// <returns>A positive distance.</returns>
    public static double LookAtDistance(bool hasFocalDistance, double focalDistance)
    {
        return hasFocalDistance && focalDistance > 1e-9 && !double.IsInfinity(focalDistance) && !double.IsNaN(focalDistance)
            ? focalDistance
            : 1.0;
    }
}
