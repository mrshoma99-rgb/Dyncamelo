using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Navisworks.Api;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;

namespace CamelGraph.Navisworks;

/// <summary>Nodes that read and drive the viewport camera.</summary>
[NodeCategory("Navisworks.Camera")]
public static class CameraNodes
{
    /// <summary>The current camera position and lens parameters.</summary>
    /// <param name="after">Anything at all, only to run this node after the node it comes from: the camera is read when this node runs, and the engine does not order nodes that share no wire.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>Camera position, focal distance (null when unset) and vertical field height.</returns>
    [NodeName("Camera.Current")]
    [LiveState]
    [NodeAliases("CamelGraph.Navisworks.CameraNodes.Current@Autodesk.Navisworks.Api.Document")]
    [NodeDescription("The current camera position, focal distance and vertical field height, read again on every run. To keep the whole camera and put it back later, use Camera.Save and Camera.Restore.")]
    [NodeSearchTags("camera", "current", "position", "view", "eye")]
    [MultiReturn("position", "focalDistance", "heightField")]
    [PortKinds("geometry", "number", "number")]
    public static Dictionary<string, object?> Current(object? after = null, Document? document = null)
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
    /// <param name="after">Anything at all, only to run this node after the node it comes from: the camera moves when this node runs, and the engine does not order nodes that share no wire.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>True when the camera was moved.</returns>
    [NodeName("Camera.LookAt")]
    [NodeAliases("CamelGraph.Navisworks.CameraNodes.LookAt@object,object,Autodesk.Navisworks.Api.Document")]
    [NodeDescription("Moves the camera to 'eye' looking at 'target' (up stays +Z). Wire the output of the node that must run before this one into 'after'.")]
    [NodeSearchTags("camera", "lookat", "aim", "point", "view")]
    [return: NodeName("done")]
    public static bool LookAt(object eye, object target, object? after = null, Document? document = null)
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
    /// <param name="after">Anything at all, only to run this node after the node it comes from: the camera moves when this node runs, and the engine does not order nodes that share no wire.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>True when the camera was moved.</returns>
    [NodeName("Camera.ZoomToItems")]
    [NodeAliases("CamelGraph.Navisworks.CameraNodes.ZoomToItems@System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>,double,Autodesk.Navisworks.Api.Document")]
    [NodeDescription("Frames the given items in the current view (per-item close-ups, screenshot staging). Wire the output of the node that must run before this one into 'after'.")]
    [NodeSearchTags("camera", "zoom", "frame", "fit", "items", "focus")]
    [return: NodeName("done")]
    public static bool ZoomToItems([MultiInput] IEnumerable<ModelItem> items, [NodeRange(1, 10, SoftMin = 1, SoftMax = 3, Step = 0.1)] double paddingFactor = 1.5, object? after = null, Document? document = null)
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
        if (TryFrameItems(doc, items, paddingFactor))
        {
            return true;
        }

        throw new InvalidOperationException(
            "The items carry no geometry to zoom to — they are container/grouping nodes. " +
            "Wire geometry-bearing items (Selection.Resolve with level Geometry resolves containers to theirs).");
    }

    /// <summary>Switches the camera between perspective and orthographic projection.</summary>
    /// <param name="perspective">True for perspective, false for orthographic.</param>
    /// <param name="after">Anything at all, only to run this node after the node it comes from: the camera changes when this node runs, and the engine does not order nodes that share no wire.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>True when the projection was set.</returns>
    [NodeName("Camera.SetProjection")]
    [NodeAliases("CamelGraph.Navisworks.CameraNodes.SetProjection@bool,Autodesk.Navisworks.Api.Document")]
    [NodeDescription("Switches the camera between perspective (true) and orthographic (false) projection. Wire the output of the node that must run before this one into 'after'.")]
    [NodeSearchTags("camera", "projection", "perspective", "orthographic", "ortho", "parallel")]
    [return: NodeName("done")]
    public static bool SetProjection(bool perspective, object? after = null, Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoint = doc.CurrentViewpoint.CreateCopy();
        viewpoint.Projection = perspective ? ViewpointProjection.Perspective : ViewpointProjection.Orthographic;
        doc.CurrentViewpoint.CopyFrom(viewpoint);
        return true;
    }

    /// <summary>Sets the camera's vertical field of view (perspective).</summary>
    /// <param name="degrees">Vertical field of view in degrees (typical 20–90). Applies to the perspective camera.</param>
    /// <param name="after">Anything at all, only to run this node after the node it comes from: the camera changes when this node runs, and the engine does not order nodes that share no wire.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>True when the field of view was set.</returns>
    [NodeName("Camera.SetFieldOfView")]
    [NodeAliases("CamelGraph.Navisworks.CameraNodes.SetFieldOfView@double,Autodesk.Navisworks.Api.Document")]
    [NodeDescription("Sets the camera's vertical field of view in degrees (perspective camera) — smaller = more zoomed/telephoto, larger = wider. Wire the output of the node that must run before this one into 'after'.")]
    [NodeSearchTags("camera", "fov", "field of view", "lens", "zoom", "angle", "wide")]
    [return: NodeName("done")]
    public static bool SetFieldOfView([NodeRange(1, 179, SoftMin = 10, SoftMax = 120, Unit = "°")] double degrees, object? after = null, Document? document = null)
    {
        if (degrees <= 0.0 || degrees >= 180.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(degrees), "The field of view must be between 0 and 180 degrees.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoint = doc.CurrentViewpoint.CreateCopy();
        viewpoint.HeightField = degrees * Math.PI / 180.0; // Navisworks stores the vertical FOV in radians.
        doc.CurrentViewpoint.CopyFrom(viewpoint);
        return true;
    }

    /// <summary>Takes a copy of the whole current camera, to put back later with Camera.Restore.</summary>
    /// <param name="after">Anything at all, only to run this node after the node it comes from: the camera is read when this node runs, and the engine does not order nodes that share no wire.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>A copy of the current camera (position, direction, projection and field of view).</returns>
    [NodeName("Camera.Save")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [LiveState]
    [NodeDescription(
        "Keeps a copy of the current camera inside the graph (nothing is added to the Saved Viewpoints window; for that use " +
        "Viewpoint.Save). Put it before the nodes that move the camera (Camera.ZoomToItems, SavedViewpoint.Apply, " +
        "Camera.SetStandardView ...) and wire its viewpoint into Camera.Restore at the end, so the graph leaves the view as it " +
        "found it. The copy is taken when this node runs: wire what must happen before it into 'after'.")]
    [NodeSearchTags("camera", "save", "remember", "keep", "current", "view", "restore", "undo", "viewpoint")]
    [return: NodeName("viewpoint")]
    public static Viewpoint Save(object? after = null, Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        return doc.CurrentViewpoint.CreateCopy();
    }

    /// <summary>Puts a camera taken with Camera.Save (or any viewpoint) back on the current view.</summary>
    /// <param name="viewpoint">The camera to restore: the viewpoint from Camera.Save, or a saved viewpoint (its camera is used).</param>
    /// <param name="after">Anything at all, only to run this node after the node it comes from: the camera moves when this node runs, and the engine does not order nodes that share no wire.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>True when the camera was restored.</returns>
    [NodeName("Camera.Restore")]
    [NodeDescription(
        "Puts a camera back on the current view: the viewpoint kept by Camera.Save, or a saved viewpoint. Only the camera " +
        "moves; it does not undo hidden items or colour overrides (use Appearance.ShowAll and Appearance.ResetTemporary for those). " +
        "Wire the last node that moves the camera into 'after' so this one runs at the end.")]
    [NodeSearchTags("camera", "restore", "back", "reset", "undo", "view", "viewpoint", "apply")]
    [return: NodeName("done")]
    public static bool Restore([ScalarInput] object viewpoint, object? after = null, Document? document = null)
    {
        if (viewpoint == null)
        {
            throw new ArgumentNullException(nameof(viewpoint), "No camera provided. Wire the viewpoint from Camera.Save.");
        }

        Viewpoint camera;
        if (viewpoint is Viewpoint view)
        {
            camera = view;
        }
        else if (viewpoint is SavedViewpoint saved)
        {
            camera = saved.Viewpoint;
        }
        else
        {
            throw new ArgumentException(
                "Cannot restore a camera from a " + viewpoint.GetType().Name +
                ". Wire the viewpoint from Camera.Save or a saved viewpoint.", nameof(viewpoint));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        doc.CurrentViewpoint.CopyFrom(camera);
        return true;
    }

    /// <summary>
    /// Frames the items in the current view, answering false instead of
    /// throwing when they enclose no geometry. A ModelItem's bounding box
    /// already covers its descendants, so a container frames correctly without
    /// any tree walking; the hidden-inclusive box is the fallback because "zoom
    /// to these items" is an instruction, and Navisworks' own Zoom to Selection
    /// frames hidden items too.
    /// </summary>
    internal static bool TryFrameItems(Document doc, IEnumerable<ModelItem> items, double paddingFactor)
    {
        var collection = NavisValues.ToItemCollection(items);
        var box = collection.BoundingBox(true);
        if (box == null || box.IsEmpty)
        {
            box = collection.BoundingBox(false);
        }

        if (box == null || box.IsEmpty)
        {
            return false;
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
