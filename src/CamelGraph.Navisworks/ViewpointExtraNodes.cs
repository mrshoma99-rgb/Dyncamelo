using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;
using CamelGraph.Nodes;
using CamelGraph.Nodes.Spatial;

namespace CamelGraph.Navisworks;

/// <summary>
/// Saved-viewpoint read-back and refresh (SavedViewpoint.Info, SavedViewpoint.Update) and the
/// standard camera views (Camera.SetStandardView). Reads use only members the other viewpoint
/// nodes already rely on; the camera maths lives in <see cref="CameraMath"/> (pure, unit-tested).
/// </summary>
[NodeCategory("Navisworks.Viewpoints")]
public static class ViewpointExtraNodes
{
    /// <summary>Summary information about a saved viewpoint.</summary>
    /// <param name="viewpoint">The saved viewpoint.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>Name, folder path, section / overrides flags, comment count, camera position and look-at point.</returns>
    [NodeName("SavedViewpoint.Info")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [LiveState]
    [NodeDescription(
        "Reads a saved viewpoint: its name, folder path (\"A/B\", \"\" at the top level), whether it carries a section box, " +
        "whether it has baked appearance or visibility overrides, its comment count and its camera — the position and the " +
        "look-at point (one focal distance ahead of the camera, or one unit when none is stored). " +
        "hasSection reads nothing on Navisworks 2025 and later (it comes back empty).")]
    [NodeSearchTags("viewpoint", "view", "info", "camera", "folder", "section", "overrides", "comments", "position", "lookat")]
    [MultiReturn("name", "folder", "hasSection", "hasOverrides", "commentCount", "position", "lookAt")]
    [PortKinds("text", "text", "boolean", "boolean", "integer", "geometry", "geometry")]
    public static Dictionary<string, object?> Info(SavedViewpoint viewpoint, Document? document = null)
    {
        if (viewpoint == null)
        {
            throw new ArgumentNullException(
                nameof(viewpoint),
                "SavedViewpoint.Info requires a saved viewpoint. Wire one from Viewpoints.All or SavedViewpoint.ByName into the 'viewpoint' input.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var root = doc.SavedViewpoints.RootItem;

        // Prefer the stored instance so the folder is right even for a copy that lost its parent.
        var stored = SavedItemTreeHelpers.FindStoredEquivalent(root, viewpoint) ?? viewpoint;
        SavedItemTreeHelpers.GetFolderInfo(root, stored, out var folderPath, out _);

        var camera = stored.Viewpoint;
        var position = camera.Position;
        var rotation = camera.Rotation;
        var distance = CameraMath.LookAtDistance(camera.HasFocalDistance, camera.HasFocalDistance ? camera.FocalDistance : 0.0);
        var lookAt = CameraMath.LookAtPoint(
            position.X, position.Y, position.Z, rotation.A, rotation.B, rotation.C, rotation.D, distance);

        return new Dictionary<string, object?>
        {
            ["name"] = stored.DisplayName ?? string.Empty,
            ["folder"] = folderPath,
            ["hasSection"] = HasSection(camera),
            ["hasOverrides"] = stored.ContainsAppearanceOverrides || stored.ContainsVisibilityOverrides,
            ["commentCount"] = stored.Comments?.Count ?? 0,
            ["position"] = new CamelGraphPoint(position.X, position.Y, position.Z),
            ["lookAt"] = new CamelGraphPoint(lookAt.X, lookAt.Y, lookAt.Z),
        };
    }

    /// <summary>Replaces a saved viewpoint's camera with the current view.</summary>
    /// <param name="viewpoint">The saved viewpoint to refresh (or wire it by name through SavedViewpoint.ByName).</param>
    /// <param name="after">Anything at all, only to run this node after the node it comes from: the view is read when this node runs, and the engine does not order nodes that share no wire.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The updated stored viewpoint, still in its folder and position with its name.</returns>
    [NodeName("SavedViewpoint.Update")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeAliases("CamelGraph.Navisworks.ViewpointExtraNodes.Update@Autodesk.Navisworks.Api.SavedViewpoint,Autodesk.Navisworks.Api.Document")]
    [NodeDescription(
        "Re-captures the current view into an existing saved viewpoint — the UI's \"Update\" — keeping its name, folder and " +
        "place in the list. Set the camera first (Camera.LookAt, Camera.ZoomToItems, Camera.SetStandardView) and wire the " +
        "output of the last of those nodes into 'after', because the view is read when this node runs. Compare Viewpoint.Save, " +
        "which replaces a view by name with a new one.")]
    [NodeSearchTags("viewpoint", "view", "update", "refresh", "recapture", "replace", "camera", "current")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint Update(SavedViewpoint viewpoint, object? after = null, Document? document = null)
    {
        if (viewpoint == null)
        {
            throw new ArgumentNullException(
                nameof(viewpoint),
                "SavedViewpoint.Update requires a saved viewpoint. Wire one from Viewpoints.All or SavedViewpoint.ByName into the 'viewpoint' input.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoints = doc.SavedViewpoints;
        var root = viewpoints.RootItem;
        var stored = SavedItemTreeHelpers.ResolveStored<SavedViewpoint>(root, viewpoint, "saved viewpoint");

        // Remember where the viewpoint sits: the edit may hand back a new wrapper.
        var name = stored.DisplayName ?? string.Empty;
        var parentFolder = ReferenceEquals(stored.Parent, root) ? null : stored.Parent as FolderItem;
        var siblings = parentFolder != null ? parentFolder.Children : viewpoints.Value;
        var index = SavedItemTreeHelpers.IndexByIdentity(siblings, stored);

        // Camera (and visibility) from the current view; the same call Viewpoint.SaveWithOverrides
        // uses to give a stored viewpoint the live camera while keeping its other contents.
        viewpoints.ReplaceFromCurrentView(stored);

        var folderAfter = parentFolder == null
            ? null
            : SavedItemTreeHelpers.FindStoredEquivalent(root, parentFolder) ?? parentFolder;
        var siblingsAfter = folderAfter != null ? folderAfter.Children : viewpoints.Value;
        if (index >= 0 && index < siblingsAfter.Count &&
            siblingsAfter[index] is SavedViewpoint updated &&
            string.Equals(updated.DisplayName, name, StringComparison.Ordinal))
        {
            return updated;
        }

        return SavedItemTreeHelpers.FindByName<SavedViewpoint>(root.Children, name)
            ?? throw new InvalidOperationException(
                "The saved viewpoint '" + name + "' could not be found after it was updated — it may have been renamed or deleted while the graph ran.");
    }

    /// <summary>Points the camera along a standard direction and frames the model or some items.</summary>
    /// <param name="view">One of top, bottom, front, back, left, right, iso.</param>
    /// <param name="items">The items to frame (leave unwired to frame the whole model).</param>
    /// <param name="paddingFactor">Space to leave around the framed box (1 = tight fit).</param>
    /// <param name="after">Anything at all, only to run this node after the node it comes from: the camera moves when this node runs, and the engine does not order nodes that share no wire.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>A copy of the new current viewpoint (feed it to nodes that take a viewpoint, or save it with Viewpoint.Save).</returns>
    [NodeName("Camera.SetStandardView")]
    [NodeCategory("Navisworks.Camera")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeAliases("CamelGraph.Navisworks.ViewpointExtraNodes.SetStandardView@string,System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>,double,Autodesk.Navisworks.Api.Document")]
    [NodeDescription(
        "Sets the camera to a standard view (top, bottom, front, back, left, right, iso) and frames the given items — or the " +
        "whole model when none are wired. Convention: Z is up and +Y is north, so top is a plan with north at the top of the " +
        "screen, front looks north from the south side, left looks east from the west side, and iso looks from the " +
        "south-east above. Assumes a Z-up model; for others use Camera.LookAt. Wire the output of the node that must run " +
        "before this one into 'after'.")]
    [NodeSearchTags("camera", "standard", "view", "top", "plan", "front", "back", "left", "right", "iso", "isometric", "elevation")]
    [return: NodeName("viewpoint")]
    public static Viewpoint SetStandardView(
        [NodeChoices("top", "bottom", "front", "back", "left", "right", "iso")]
        string view = "iso",
        [MultiInput] IEnumerable<ModelItem>? items = null,
        [NodeRange(1, 10, SoftMin = 1, SoftMax = 3, Step = 0.1)] double paddingFactor = 1.2,
        object? after = null,
        Document? document = null)
    {
        var viewName = CameraMath.NormalizeViewName(view);
        if (double.IsNaN(paddingFactor) || paddingFactor <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(paddingFactor), "The padding factor must be positive.");
        }

        var doc = NavisworksContext.ResolveDocument(document);

        ModelItemCollection target;
        if (items != null)
        {
            var list = NavisValues.ToItemList(items);
            if (list.Count == 0)
            {
                throw new ArgumentException(
                    "Camera.SetStandardView got an empty list for 'items'. Leave 'items' unwired to frame the whole model, " +
                    "or wire at least one model item.", nameof(items));
            }

            target = NavisValues.ToItemCollection(list);
        }
        else
        {
            target = NavisValues.ToItemCollection(doc.Models.RootItems);
        }

        // Same box rule as Camera.ZoomToItems: visible geometry first, hidden items as the fallback.
        var box = target.BoundingBox(true);
        if (box == null || box.IsEmpty)
        {
            box = target.BoundingBox(false);
        }

        if (box == null || box.IsEmpty)
        {
            throw new InvalidOperationException(
                items != null
                    ? "The items carry no geometry to frame — they are container/grouping nodes. Wire geometry-bearing items (Selection.Resolve with level Geometry resolves containers to theirs)."
                    : "The model has no geometry to frame.");
        }

        var centre = box.Center;
        var size = box.Size;
        var eye = CameraMath.EyePosition(
            viewName, centre.X, centre.Y, centre.Z, CameraMath.SuggestedDistance(size.X, size.Y, size.Z));
        var up = CameraMath.UpDirection(viewName);

        // Orientation first (the Camera.LookAt recipe: eye, point at the centre, align up), then
        // let the zoom-to-box pull the camera in along that direction.
        var camera = doc.CurrentViewpoint.CreateCopy();
        camera.Position = new Point3D(eye.X, eye.Y, eye.Z);
        camera.PointAt(centre);
        camera.AlignUp(new Vector3D(up.X, up.Y, up.Z));
        doc.CurrentViewpoint.CopyFrom(camera);

        CameraNodes.TryFrameItems(doc, target, paddingFactor);
        return doc.CurrentViewpoint.Value.CreateCopy();
    }

    /// <summary>Whether a saved camera carries an enabled section (null where the API is not wired for it).</summary>
    private static bool? HasSection(Viewpoint camera)
    {
#if NAV2024
        var clip = camera.InternalClipPlanes;
        return clip != null && clip.IsEnabled();
#else
        // The internal clip-plane API changed in Navisworks 2025+ (see Viewpoint.SetSectionBox).
        return null;
#endif
    }
}
