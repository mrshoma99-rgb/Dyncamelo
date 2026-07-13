using System;
using System.Collections;
using System.Globalization;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Interop;
using Dyncamelo.Core.Loader;
using Dyncamelo.Nodes;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Sectioning nodes (WS-E spike outcome): the clipping-plane set of the current
/// viewpoint is exposed via <c>Viewpoint.InternalClipPlanes</c>
/// (<c>LcOaClipPlaneSet</c> — box mode, enable flag, SetBox), so a section box
/// needs no COM at all. RUNTIME-CHECK: verify in Navisworks that a box set on a
/// viewpoint copy survives <c>CurrentViewpoint.CopyFrom</c> and renders.
/// </summary>
[NodeCategory("Navisworks.Viewpoints")]
public static class SectionBoxNodes
{
    /// <summary>Applies (or clears) a section box on the current view.</summary>
    /// <param name="boundingBox">
    /// The box region, in document units (e.g. from ModelItem.BoundingBox or
    /// BoundingBox.ByCorners). Ignored when <paramref name="enabled"/> is false.
    /// </param>
    /// <param name="enabled">True to section to the box, false to disable sectioning.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>True when the clip planes were applied to the current view.</returns>
    [NodeName("Viewpoint.SetSectionBox")]
    [NodeDescription("Applies a section box around a region on the current view (Sectioning > Box, scriptable) — chain ModelItem.BoundingBox for the clash-viewpoint close-up look. enabled=false turns sectioning off.")]
    [NodeSearchTags("section", "box", "clip", "sectioning", "viewpoint", "crop", "isolate")]
    [return: NodeName("done")]
    public static bool SetSectionBox(object boundingBox, bool enabled = true, Document? document = null)
    {
#if NAV2024
        var doc = NavisworksContext.ResolveDocument(document);

        // Edit a copy of the current viewpoint and copy it back — stored/current
        // viewpoints are read-only in place.
        var viewpoint = doc.CurrentViewpoint.CreateCopy();
        var clipPlanes = viewpoint.InternalClipPlanes
            ?? throw new InvalidOperationException(
                "The current viewpoint exposes no clip-plane set — sectioning is unavailable in this view.");

        if (enabled)
        {
            var box = ToBoundingBox3D(boundingBox);
            clipPlanes.SetBox(box);
            clipPlanes.SetMode(LcOaClipPlaneSetMode.eMODE_BOX);
            clipPlanes.SetEnabled(true);
        }
        else
        {
            clipPlanes.SetEnabled(false);
        }

        doc.CurrentViewpoint.CopyFrom(viewpoint);
        return true;
#else
        // The internal clip-plane API (InternalClipPlanes / LcOaClipPlaneSetMode) changed
        // in Navisworks 2025+; this node is pending a port verified on those releases.
        throw new System.NotSupportedException(
            "Viewpoint.SetSectionBox is currently supported only on Navisworks 2024. " +
            "The 2025/2026 section-box (clip-plane) API differs and this node is pending an update.");
#endif
    }

    /// <summary>
    /// Converts a port value to a <see cref="BoundingBox3D"/>: a Navisworks box
    /// passes through, a Dyncamelo BoundingBox (BoundingBox.ByCorners) converts,
    /// and a list of six numbers is taken as [minX, minY, minZ, maxX, maxY, maxZ].
    /// </summary>
    private static BoundingBox3D ToBoundingBox3D(object? value)
    {
        switch (value)
        {
            case null:
                throw new ArgumentNullException(nameof(value),
                    "No bounding box provided. Wire ModelItem.BoundingBox or BoundingBox.ByCorners.");
            case BoundingBox3D navisBox:
                RequireNonEmpty(navisBox);
                return navisBox;
            case DyncameloBoundingBox dyncameloBox:
                var converted = new BoundingBox3D(
                    new Point3D(dyncameloBox.Min.X, dyncameloBox.Min.Y, dyncameloBox.Min.Z),
                    new Point3D(dyncameloBox.Max.X, dyncameloBox.Max.Y, dyncameloBox.Max.Z));
                RequireNonEmpty(converted);
                return converted;
            case IList list when !(value is string):
                if (list.Count < 6)
                {
                    throw new ArgumentException(
                        "A bounding box list needs six numbers: minX, minY, minZ, maxX, maxY, maxZ.");
                }

                var numbers = new double[6];
                for (int i = 0; i < 6; i++)
                {
                    numbers[i] = Convert.ToDouble(list[i], CultureInfo.InvariantCulture);
                }

                var fromList = new BoundingBox3D(
                    new Point3D(Math.Min(numbers[0], numbers[3]), Math.Min(numbers[1], numbers[4]), Math.Min(numbers[2], numbers[5])),
                    new Point3D(Math.Max(numbers[0], numbers[3]), Math.Max(numbers[1], numbers[4]), Math.Max(numbers[2], numbers[5])));
                RequireNonEmpty(fromList);
                return fromList;
            default:
                throw new ArgumentException(
                    "Cannot interpret a value of type '" + value.GetType().Name +
                    "' as a bounding box. Wire ModelItem.BoundingBox, BoundingBox.ByCorners, or a list of six numbers.");
        }
    }

    private static void RequireNonEmpty(BoundingBox3D box)
    {
        var min = box.Min;
        var max = box.Max;
        if (min.X >= max.X || min.Y >= max.Y || min.Z >= max.Z)
        {
            throw new ArgumentException(
                "The bounding box is empty or degenerate — its Max must exceed its Min on every axis.");
        }
    }
}
