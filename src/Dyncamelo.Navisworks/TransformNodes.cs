using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Nodes;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Nodes that move, rotate and transform model items (wishlist #2). The 2024
/// API has no temporary transform: every override applied here is PERMANENT —
/// undoable, saved in the NWF, and removable with ModelItem.ResetTransform.
/// Translate/RotateAboutAxis compose their delta onto each item's existing
/// override, so re-running a graph accumulates movement instead of silently
/// replacing earlier moves; ModelItem.SetTransform is the absolute variant.
/// All lengths are in document units — chain Units.Convert for meters/feet.
/// </summary>
[NodeCategory("Navisworks.Transform")]
public static class TransformNodes
{
    /// <summary>Moves model items by a vector.</summary>
    /// <param name="items">The model items to move.</param>
    /// <param name="vector">The offset, in document units: a Vector (Vector.ByCoordinates) or a list of three numbers.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The moved items (pass-through for chaining).</returns>
    [NodeName("ModelItem.Translate")]
    [NodeDescription("Moves model items by a vector, in document units (chain Units.Convert for meters/feet). A permanent override: undoable, saved in the NWF, removed by ModelItem.ResetTransform. Re-runs accumulate — each run moves the items again.")]
    [NodeSearchTags("item", "translate", "move", "offset", "transform", "shift")]
    [return: NodeName("items")]
    public static List<ModelItem> Translate(IEnumerable<ModelItem> items, object vector, Document? document = null)
    {
        var list = RequireItems(items);
        var delta = TransformHelpers.Translation(ToVector3D(vector));
        var doc = NavisworksContext.ResolveDocument(document);
        ApplyDelta(doc, list, delta);
        return list;
    }

    /// <summary>Rotates model items about an axis through a point.</summary>
    /// <param name="items">The model items to rotate.</param>
    /// <param name="origin">A point on the rotation axis (document units): a Point or a list of three numbers.</param>
    /// <param name="axis">The axis direction (need not be unit length): a Vector or a list of three numbers.</param>
    /// <param name="degrees">The rotation angle in degrees (right-hand rule around the axis).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The rotated items (pass-through for chaining).</returns>
    [NodeName("ModelItem.RotateAboutAxis")]
    [NodeDescription("Rotates model items by an angle (degrees) about an axis through a point. A permanent override: undoable, saved in the NWF, removed by ModelItem.ResetTransform. Re-runs accumulate.")]
    [NodeSearchTags("item", "rotate", "rotation", "axis", "angle", "degrees", "transform")]
    [return: NodeName("items")]
    public static List<ModelItem> RotateAboutAxis(
        IEnumerable<ModelItem> items,
        object origin,
        object axis,
        double degrees,
        Document? document = null)
    {
        var list = RequireItems(items);
        var delta = TransformHelpers.RotationAboutAxis(
            NavisValues.ToPoint3D(origin), ToVector3D(axis), degrees);
        var doc = NavisworksContext.ResolveDocument(document);
        ApplyDelta(doc, list, delta);
        return list;
    }

    /// <summary>Sets an absolute transform override on model items from a 4×4 matrix.</summary>
    /// <param name="items">The model items to transform.</param>
    /// <param name="matrix">16 numbers, row-major (translation at indices 3, 7, 11; bottom row 0 0 0 1), or four rows of four numbers, or a matrix from ModelItem.GetTransform.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The transformed items (pass-through for chaining).</returns>
    [NodeName("ModelItem.SetTransform")]
    [NodeDescription("Sets the permanent transform override of model items to an absolute 4×4 matrix (16 numbers, row-major, translation at indices 3/7/11) — mirror-place or scale-in-place for power users. Unlike Translate/RotateAboutAxis this REPLACES any earlier override; re-runs are idempotent.")]
    [NodeSearchTags("item", "transform", "matrix", "set", "override", "scale", "mirror")]
    [return: NodeName("items")]
    public static List<ModelItem> SetTransform(IEnumerable<ModelItem> items, object matrix, Document? document = null)
    {
        var list = RequireItems(items);
        var transform = TransformHelpers.FromMatrixValue(matrix);
        var doc = NavisworksContext.ResolveDocument(document);
        doc.Models.OverridePermanentTransform(list, transform, false);
        return list;
    }

    /// <summary>Removes transform overrides from model items (all items when the list is empty).</summary>
    /// <param name="items">The model items to reset; leave unwired (or wire an empty list) to reset every override in the document.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The reset items (empty when every override was reset).</returns>
    [NodeName("ModelItem.ResetTransform")]
    [NodeDescription("Removes permanent transform overrides, restoring items to their original position. With no items wired (or an empty list) every transform override in the document is reset.")]
    [NodeSearchTags("item", "transform", "reset", "restore", "original", "undo", "position")]
    [return: NodeName("items")]
    public static List<ModelItem> ResetTransform(IEnumerable<ModelItem>? items = null, Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var list = NavisValues.ToItemList(items);
        if (list.Count == 0)
        {
            doc.Models.ResetAllPermanentTransforms();
        }
        else
        {
            doc.Models.ResetPermanentTransform(list);
        }

        return list;
    }

    /// <summary>Reads a model item's current transform and override state.</summary>
    /// <param name="item">The model item.</param>
    /// <returns>The transform origin (translation — a practical base point), the 16-number row-major matrix, and whether a permanent override is active.</returns>
    [NodeName("ModelItem.GetTransform")]
    [NodeDescription("Reads an item's current (active) transform: origin = its translation (a practical base point), matrix = 16 numbers row-major (feed ModelItem.SetTransform to round-trip), hasOverride = whether a permanent transform override is applied.")]
    [NodeSearchTags("item", "transform", "get", "read", "matrix", "origin", "override", "position")]
    [MultiReturn("origin", "matrix", "hasOverride")]
    public static Dictionary<string, object?> GetTransform(ModelItem item)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item), "No model item provided.");
        }

        var geometry = item.Geometry;
        var active = geometry?.ActiveTransform ?? item.Transform ?? Transform3D.CreateIdentity();
        var permanentOverride = geometry?.PermanentOverrideTransform;
        var translation = active.Translation;

        return new Dictionary<string, object?>
        {
            ["origin"] = new Point3D(translation.X, translation.Y, translation.Z),
            ["matrix"] = TransformHelpers.ToRowMajorMatrix(active),
            ["hasOverride"] = permanentOverride != null && !permanentOverride.IsIdentity(),
        };
    }

    // ------------------------------------------------------------- Helpers

    /// <summary>
    /// Applies a delta transform on top of each item's existing permanent
    /// override (per item — overrides can differ across the selection), so
    /// repeated runs accumulate. RUNTIME-CHECK: assumes
    /// OverridePermanentTransform replaces (rather than composes with) an
    /// existing override — see the v0.3 Windows smoke list.
    /// </summary>
    private static void ApplyDelta(Document doc, List<ModelItem> items, Transform3D delta)
    {
        foreach (var item in items)
        {
            var composed = TransformHelpers.ComposeWithOverride(item, delta);
            doc.Models.OverridePermanentTransform(new[] { item }, composed, false);
        }
    }

    private static List<ModelItem> RequireItems(IEnumerable<ModelItem>? items)
    {
        var list = NavisValues.ToItemList(items);
        if (list.Count == 0)
        {
            throw new ArgumentException("No model items provided.", nameof(items));
        }

        return list;
    }

    /// <summary>
    /// Converts a port value to a Navisworks <see cref="Vector3D"/>. Accepts a
    /// Navisworks Vector3D, a Dyncamelo Vector (Vector.ByCoordinates), or a
    /// list of three numbers.
    /// </summary>
    internal static Vector3D ToVector3D(object? value)
    {
        switch (value)
        {
            case null:
                throw new ArgumentNullException(nameof(value), "No vector provided.");
            case Vector3D vector:
                return vector;
            case DyncameloVector dyncameloVector:
                return new Vector3D(dyncameloVector.X, dyncameloVector.Y, dyncameloVector.Z);
            case IList list when !(value is string):
                if (list.Count < 3)
                {
                    throw new ArgumentException("A vector list needs three numeric components (x, y, z).");
                }

                try
                {
                    return new Vector3D(
                        Convert.ToDouble(list[0], CultureInfo.InvariantCulture),
                        Convert.ToDouble(list[1], CultureInfo.InvariantCulture),
                        Convert.ToDouble(list[2], CultureInfo.InvariantCulture));
                }
                catch (Exception)
                {
                    throw new ArgumentException("Vector components must be numbers (x, y, z).");
                }

            default:
                throw new ArgumentException(
                    "Cannot interpret a value of type '" + value.GetType().Name +
                    "' as a vector. Wire a Vector (e.g. Vector.ByCoordinates) or a list of three numbers.");
        }
    }
}
