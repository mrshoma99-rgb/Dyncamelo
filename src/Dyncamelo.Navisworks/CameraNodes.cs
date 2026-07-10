using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>Nodes that read and drive the viewport camera.</summary>
[NodeCategory("Navisworks.Camera")]
public static class CameraNodes
{
    /// <summary>The current camera position and lens parameters.</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>Camera position, focal distance (null when unset) and vertical field height.</returns>
    [NodeName("Camera.Current")]
    [NodeDescription("The current camera position, focal distance and vertical field height.")]
    [NodeSearchTags("camera", "current", "position", "view", "eye")]
    [MultiReturn("position", "focalDistance", "heightField")]
    public static Dictionary<string, object?> Current(Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoint = doc.CurrentViewpoint.Value;
        return new Dictionary<string, object?>
        {
            ["position"] = viewpoint.Position,
            ["focalDistance"] = viewpoint.HasFocalDistance ? viewpoint.FocalDistance : (double?)null,
            ["heightField"] = viewpoint.HeightField,
        };
    }

    /// <summary>Moves the camera to an eye point looking at a target point.</summary>
    /// <param name="eye">Camera position: a Point, or a list of three numbers.</param>
    /// <param name="target">Point to look at: a Point, or a list of three numbers.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>True when the camera was moved.</returns>
    [NodeName("Camera.LookAt")]
    [NodeDescription("Moves the camera to 'eye' looking at 'target' (up stays +Z).")]
    [NodeSearchTags("camera", "lookat", "aim", "point", "view")]
    [return: NodeName("done")]
    public static bool LookAt(object eye, object target, Document? document = null)
    {
        var eyePoint = NavisValues.ToPoint3D(eye);
        var targetPoint = NavisValues.ToPoint3D(target);

        double dx = targetPoint.X - eyePoint.X;
        double dy = targetPoint.Y - eyePoint.Y;
        double dz = targetPoint.Z - eyePoint.Z;
        if (dx * dx + dy * dy + dz * dz < 1e-18)
        {
            throw new ArgumentException(
                "Camera.LookAt requires 'eye' and 'target' to be different points; both are (" +
                eyePoint.X.ToString(CultureInfo.InvariantCulture) + ", " +
                eyePoint.Y.ToString(CultureInfo.InvariantCulture) + ", " +
                eyePoint.Z.ToString(CultureInfo.InvariantCulture) +
                ") so the view direction is undefined.", nameof(target));
        }

        var doc = NavisworksContext.ResolveDocument(document);

        var viewpoint = doc.CurrentViewpoint.CreateCopy();
        viewpoint.Position = eyePoint;
        viewpoint.PointAt(targetPoint);
        viewpoint.AlignUp(new Vector3D(0, 0, 1));
        doc.CurrentViewpoint.CopyFrom(viewpoint);
        return true;
    }

    /// <summary>Frames the given items in the current view.</summary>
    /// <param name="items">The model items to frame.</param>
    /// <param name="paddingFactor">How much space to leave around the items (1 = tight fit).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>True when the camera was moved.</returns>
    [NodeName("Camera.ZoomToItems")]
    [NodeDescription("Frames the given items in the current view (per-item close-ups, screenshot staging).")]
    [NodeSearchTags("camera", "zoom", "frame", "fit", "items", "focus")]
    [return: NodeName("done")]
    public static bool ZoomToItems(IEnumerable<ModelItem> items, double paddingFactor = 1.5, Document? document = null)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items), "No model items provided.");
        }

        if (paddingFactor <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(paddingFactor), "The padding factor must be positive.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var collection = NavisValues.ToItemCollection(items);
        var box = collection.BoundingBox(true);
        if (box == null || box.IsEmpty)
        {
            throw new InvalidOperationException(
                "The items have no visible geometry to zoom to. Wire geometry-bearing items (see ModelItem.GeometryLeaves).");
        }

        var padded = PadBox(box, paddingFactor);
        var viewpoint = doc.CurrentViewpoint.CreateCopy();
        viewpoint.ZoomBox(padded);
        doc.CurrentViewpoint.CopyFrom(viewpoint);
        return true;
    }

    private static BoundingBox3D PadBox(BoundingBox3D box, double paddingFactor)
    {
        if (Math.Abs(paddingFactor - 1.0) < 1e-9)
        {
            return box;
        }

        var center = box.Center;
        var halfX = box.Size.X * 0.5 * paddingFactor;
        var halfY = box.Size.Y * 0.5 * paddingFactor;
        var halfZ = box.Size.Z * 0.5 * paddingFactor;
        return new BoundingBox3D(
            new Point3D(center.X - halfX, center.Y - halfY, center.Z - halfZ),
            new Point3D(center.X + halfX, center.Y + halfY, center.Z + halfZ));
    }
}
