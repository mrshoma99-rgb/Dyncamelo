using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;
using Dyncamelo.Nodes.Spatial;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Tests which model items a viewpoint can see — a bounding-box-versus-camera-
/// frustum check, the geometric core shared with the fall-hazard tooling.
/// </summary>
[NodeCategory("Navisworks.Viewpoints")]
public static class ViewpointVisibilityNodes
{
    /// <summary>
    /// Splits candidate items into those inside a viewpoint's view frustum and
    /// those outside it. The check is geometric — "is the item's bounding box
    /// inside what the camera frames" — so an item hidden BEHIND another object
    /// still counts as visible, and the viewpoint's own hide overrides are not
    /// evaluated.
    /// </summary>
    /// <param name="items">What to test: a list of model items, a single item, a saved selection/search set, or a set name.</param>
    /// <param name="viewpoint">The viewpoint to test against: a saved viewpoint, a viewpoint name, or empty for the CURRENT view.</param>
    /// <param name="fullyInside">True requires an item's whole bounding box inside the view; false (default) counts partly visible items.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>visibleItems / outsideItems split, a boolean mask over the input order, whether anything is visible, and a diagnostic report.</returns>
    [NodeName("Viewpoint.VisibleItems")]
    [NodeDescription(
        "Checks which of the given items a viewpoint can see (bounding box vs the camera frustum). " +
        "Feed it a list of items, a selection/search set, a set name or one item; the viewpoint input " +
        "accepts a saved viewpoint, a viewpoint name, or nothing for the current view. Note: this is a " +
        "framing test — items behind other objects still count, and the viewpoint's hide overrides are ignored.")]
    [NodeSearchTags("viewpoint", "visible", "contains", "frustum", "camera", "view", "sees", "inview", "mask")]
    [MultiReturn("visibleItems", "outsideItems", "mask", "containsAny", "report")]
    public static Dictionary<string, object?> VisibleItems(
        object items,
        object? viewpoint = null,
        bool fullyInside = false,
        Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var candidates = new List<ModelItem>();
        ResolveCandidates(items, doc, candidates);
        if (candidates.Count == 0)
        {
            throw new ArgumentException(
                "No items to test — wire in model items, a selection/search set, or a set name.", nameof(items));
        }

        var view = ResolveViewpoint(viewpoint, doc, out var viewLabel);
        var frustum = BuildFrustum(view);

        var visible = new List<ModelItem>();
        var outside = new List<ModelItem>();
        var mask = new List<bool>(candidates.Count);
        int noGeometry = 0;

        foreach (var item in candidates)
        {
            var box = item.BoundingBox();
            bool inView = false;
            if (box == null || box.IsEmpty)
            {
                noGeometry++;
            }
            else
            {
                inView = fullyInside
                    ? frustum.ContainsBox(box.Min.X, box.Min.Y, box.Min.Z, box.Max.X, box.Max.Y, box.Max.Z)
                    : frustum.IntersectsBox(box.Min.X, box.Min.Y, box.Min.Z, box.Max.X, box.Max.Y, box.Max.Z);
            }

            mask.Add(inView);
            (inView ? visible : outside).Add(item);
        }

        var report =
            "Viewpoint " + viewLabel + ": " + (view.Projection == ViewpointProjection.Orthographic ? "orthographic" : "perspective") +
            ", camera (" + Format(view.Position.X) + ", " + Format(view.Position.Y) + ", " + Format(view.Position.Z) + "). " +
            visible.Count.ToString(CultureInfo.InvariantCulture) + " of " +
            candidates.Count.ToString(CultureInfo.InvariantCulture) + " item(s) in view" +
            (fullyInside ? " (fully inside)" : string.Empty) + "." +
            (noGeometry > 0
                ? " " + noGeometry.ToString(CultureInfo.InvariantCulture) + " item(s) had no geometry box and count as outside."
                : string.Empty) +
            " Framing test only: occlusion and the viewpoint's hide overrides are not evaluated.";

        return new Dictionary<string, object?>
        {
            { "visibleItems", visible },
            { "outsideItems", outside },
            { "mask", mask },
            { "containsAny", visible.Count > 0 },
            { "report", report },
        };
    }

    /// <summary>
    /// Flattens any accepted candidate shape into model items: a single item, a
    /// selection/search set (evaluated), a set name, or any nesting of those in
    /// lists.
    /// </summary>
    private static void ResolveCandidates(object? value, Document doc, List<ModelItem> into)
    {
        switch (value)
        {
            case null:
                return;
            case ModelItem item:
                into.Add(item);
                return;
            case SelectionSet set:
                into.AddRange(NavisValues.ToItemList(set.GetSelectedItems(doc)));
                return;
            case string name when name.Trim().Length > 0:
                var named = NavisValues.FindSavedItemByName<SelectionSet>(doc.SelectionSets.RootItem.Children, name.Trim());
                if (named == null)
                {
                    throw new InvalidOperationException("No selection set named '" + name.Trim() + "' exists in the document.");
                }

                into.AddRange(NavisValues.ToItemList(named.GetSelectedItems(doc)));
                return;
            case IEnumerable sequence when !(value is string):
                foreach (var element in sequence)
                {
                    ResolveCandidates(element, doc, into);
                }

                return;
            default:
                throw new ArgumentException(
                    "Cannot read items from a " + value.GetType().Name +
                    " — wire in model items, a selection/search set, or a set name.");
        }
    }

    /// <summary>Resolves the viewpoint input: SavedViewpoint, Viewpoint, name, or null for the current view.</summary>
    private static Viewpoint ResolveViewpoint(object? value, Document doc, out string label)
    {
        switch (value)
        {
            case null:
                label = "(current view)";
                return doc.CurrentViewpoint.Value;
            case SavedViewpoint saved:
                label = "'" + saved.DisplayName + "'";
                return saved.Viewpoint;
            case Viewpoint view:
                label = "(viewpoint)";
                return view;
            case string name when name.Trim().Length > 0:
                var found = NavisValues.FindSavedItemByName<SavedViewpoint>(
                    doc.SavedViewpoints.RootItem.Children, name.Trim());
                if (found == null)
                {
                    throw new InvalidOperationException("No saved viewpoint named '" + name.Trim() + "' exists in the document.");
                }

                label = "'" + found.DisplayName + "'";
                return found.Viewpoint;
            case string _:
                label = "(current view)";
                return doc.CurrentViewpoint.Value;
            default:
                throw new ArgumentException(
                    "Cannot read a viewpoint from a " + value.GetType().Name +
                    " — wire in a saved viewpoint, a viewpoint name, or leave empty for the current view.");
        }
    }

    /// <summary>
    /// Builds the pure-math frustum from a viewpoint. The camera rotation is a
    /// quaternion (A,B,C = vector, D = scalar; identity is 0,0,0,1) converted to
    /// axis + angle; the view extents at the focal distance work uniformly for
    /// perspective and orthographic projections.
    /// </summary>
    private static ViewFrustum BuildFrustum(Viewpoint view)
    {
        var rotation = view.Rotation;
        double a = rotation.A, b = rotation.B, c = rotation.C, d = rotation.D;
        var norm = Math.Sqrt(a * a + b * b + c * c + d * d);
        if (norm > 1e-12)
        {
            a /= norm;
            b /= norm;
            c /= norm;
            d /= norm;
        }

        var clampedScalar = Math.Max(-1.0, Math.Min(1.0, d));
        var angle = 2.0 * Math.Acos(clampedScalar);
        var sin = Math.Sqrt(Math.Max(0.0, 1.0 - clampedScalar * clampedScalar));
        double axisX = 0, axisY = 0, axisZ = 1;
        if (sin > 1e-9)
        {
            axisX = a / sin;
            axisY = b / sin;
            axisZ = c / sin;
        }
        else
        {
            angle = 0;
        }

        // HeightField semantics (mirrors Camera.SetFieldOfView): perspective =
        // vertical FOV in radians, orthographic = view height in model units.
        // AspectRatio = width / height.
        bool orthographic = view.Projection == ViewpointProjection.Orthographic;
        var aspect = view.AspectRatio > 1e-9 ? view.AspectRatio : 1.0;
        double halfHeight = orthographic
            ? Math.Max(1e-9, view.HeightField / 2.0)
            : Math.Tan(Math.Max(1e-6, Math.Min(Math.PI - 1e-6, view.HeightField)) / 2.0);
        double halfWidth = halfHeight * aspect;

        return ViewFrustum.Create(
            view.Position.X, view.Position.Y, view.Position.Z,
            axisX, axisY, axisZ, angle,
            orthographic,
            halfWidth, halfHeight, focalDistance: 1.0);
    }

    private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
