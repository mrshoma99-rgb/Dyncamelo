using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Navisworks.Api;
using CamelGraph.Core.Loader;
using CamelGraph.Nodes;
using CamelGraph.Nodes.Portable;
using CamelGraph.Navisworks.Internal;

namespace CamelGraph.Navisworks;

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
    /// <param name="items">The model items to move. An item with another listed item above it is left out (moving a container moves everything below it).</param>
    /// <param name="vector">The offset, in document units: a Vector (Vector.ByCoordinates) or a list of three numbers. One vector per run: to move every item by its own vector, set List Levels L1 on both items and vector.</param>
    /// <param name="accumulate">True (default) adds this move to whatever override the items already have, so every run moves them again. False replaces their override, so the items end up exactly this far from where the model put them and re-running does not pile up.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The moved items (pass-through for chaining).</returns>
    [NodeName("ModelItem.Translate")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeAliases("CamelGraph.Navisworks.TransformNodes.Translate@System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>,object,Autodesk.Navisworks.Api.Document")]
    [NodeDescription("Moves model items by a vector, in document units (chain Units.Convert for meters/feet). A permanent override: undoable, saved in the NWF, removed by ModelItem.ResetTransform. Re-runs accumulate — each run moves the items again; in Advanced, switch accumulate off to put the items exactly this far from where the model had them, so re-running (or tuning the vector with Auto-run on) does not pile up. One vector per run: to give every item its own vector set List Levels L1 on items and on vector and wire one vector per item; a list of vectors against the whole item list moves the same items once per vector and the moves add up (the node warns). Items that sit below another listed item are left out, because moving a container already moves everything under it. A container has no override of its own to read, so each run on a container starts from where the model had it; use Selection.Resolve with level Geometry first if re-runs should add up.")]
    [NodeSearchTags("item", "translate", "move", "offset", "transform", "shift")]
    [return: NodeName("items")]
    public static List<ModelItem> Translate(
        [MultiInput] IEnumerable<ModelItem> items,
        object vector,
        [NodePanel("Advanced")] bool accumulate = true,
        Document? document = null)
    {
        var list = TransformHelpers.WithoutListedDescendants(NavisValues.RequireItems(items), "moved");
        var delta = TransformHelpers.Translation(ToVector3D(vector));
        var doc = NavisworksContext.ResolveDocument(document);
        TransformHelpers.WarnIfRunOncePerValue(list.Count, "moves", accumulate);
        ApplyDelta(doc, list, delta, accumulate);
        return list;
    }

    /// <summary>Rotates model items about an axis through a point.</summary>
    /// <param name="items">The model items to rotate. An item with another listed item above it is left out (turning a container turns everything below it).</param>
    /// <param name="origin">A point on the rotation axis (document units): a Point or a list of three numbers.</param>
    /// <param name="axis">The axis direction (need not be unit length): a Vector or a list of three numbers.</param>
    /// <param name="degrees">The rotation angle in degrees (right-hand rule around the axis). One angle per run: to turn every item by its own angle, set List Levels L1 on both items and degrees and wire one angle per item.</param>
    /// <param name="accumulate">True (default) adds this turn to whatever override the items already have, so every run turns them again. False replaces their override, so the items end up turned exactly this much from where the model had them and re-running does not pile up.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The rotated items (pass-through for chaining).</returns>
    [NodeName("ModelItem.RotateAboutAxis")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeAliases("CamelGraph.Navisworks.TransformNodes.RotateAboutAxis@System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>,object,object,double,Autodesk.Navisworks.Api.Document")]
    [NodeDescription("Rotates model items by an angle (degrees) about an axis through a point. A permanent override: undoable, saved in the NWF, removed by ModelItem.ResetTransform. Re-runs accumulate; in Advanced, switch accumulate off to turn the items exactly this much from where the model had them, so re-running does not pile up. One angle per run: a list of angles against the whole item list turns the same items once per angle and the turns add up ([10, 20] turns them 30 degrees in total, and the node warns). To give every item its own angle, set List Levels L1 on items and on degrees and wire one angle per item. Items that sit below another listed item are left out, because turning a container already turns everything under it.")]
    [NodeSearchTags("item", "rotate", "rotation", "axis", "angle", "degrees", "transform")]
    [return: NodeName("items")]
    public static List<ModelItem> RotateAboutAxis(
        [MultiInput] IEnumerable<ModelItem> items,
        object origin,
        object axis,
        [NodeRange(-360, 360, SoftMin = -180, SoftMax = 180, Step = 1, Unit = "°")] double degrees,
        [NodePanel("Advanced")] bool accumulate = true,
        Document? document = null)
    {
        var list = TransformHelpers.WithoutListedDescendants(NavisValues.RequireItems(items), "turned");
        var delta = TransformHelpers.RotationAboutAxis(
            NavisValues.ToPoint3D(origin), ToVector3D(axis), degrees);
        var doc = NavisworksContext.ResolveDocument(document);
        TransformHelpers.WarnIfRunOncePerValue(list.Count, "turns", accumulate);
        ApplyDelta(doc, list, delta, accumulate);
        return list;
    }

    /// <summary>Sets an absolute transform override on model items from a 4×4 matrix.</summary>
    /// <param name="items">The model items to transform.</param>
    /// <param name="matrix">16 numbers, row-major (translation at indices 3, 7, 11; bottom row 0 0 0 1), or four rows of four numbers, or a matrix from ModelItem.GetTransform.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The transformed items (pass-through for chaining).</returns>
    [NodeName("ModelItem.SetTransform")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription("Sets the permanent transform override of model items to an absolute 4×4 matrix (16 numbers, row-major, translation at indices 3/7/11) — mirror-place or scale-in-place for power users. Unlike Translate/RotateAboutAxis this REPLACES any earlier override; re-runs are idempotent. One matrix per run: several matrices against the whole item list would replace each other and only the last stays.")]
    [NodeSearchTags("item", "transform", "matrix", "set", "override", "scale", "mirror")]
    [return: NodeName("items")]
    public static List<ModelItem> SetTransform([MultiInput] IEnumerable<ModelItem> items, object matrix, Document? document = null)
    {
        var list = NavisValues.RequireItems(items);
        var transform = TransformHelpers.FromMatrixValue(matrix);
        var doc = NavisworksContext.ResolveDocument(document);
        doc.Models.OverridePermanentTransform(list, transform, false);
        return list;
    }

    /// <summary>Removes transform overrides from model items (all items only with explicit opt-in).</summary>
    /// <param name="items">The model items to reset.</param>
    /// <param name="resetAll">True to reset EVERY transform override in the document (items may then be left unwired). Explicit opt-in — an unwired or empty items list alone never wipes the whole document.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The reset items (empty when every override was reset via resetAll).</returns>
    [NodeName("ModelItem.ResetTransform")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription("Removes permanent transform overrides, restoring items to their original position. To reset every override in the whole document, set resetAll to true — an unwired or empty items input alone does nothing (and errors) so an empty upstream filter can never wipe all placement work.")]
    [NodeSearchTags("item", "transform", "reset", "restore", "original", "undo", "position")]
    [return: NodeName("items")]
    public static List<ModelItem> ResetTransform(
        [MultiInput] IEnumerable<ModelItem>? items = null,
        bool resetAll = false,
        Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var list = NavisValues.ToItemList(items);
        if (resetAll)
        {
            doc.Models.ResetAllPermanentTransforms();
            return new List<ModelItem>();
        }

        if (list.Count == 0)
        {
            throw new ArgumentException(
                "No model items to reset. Wire the items to reset, or set resetAll to true to " +
                "deliberately remove every transform override in the document.", nameof(items));
        }

        doc.Models.ResetPermanentTransform(list);
        return list;
    }

    /// <summary>Reads a model item's current transform and override state.</summary>
    /// <param name="item">The model item.</param>
    /// <returns>The transform origin (translation — a practical base point), the 16-number row-major matrix, and whether a permanent override is active.</returns>
    [NodeName("ModelItem.GetTransform")]
    [NodeDescription("Reads an item's current (active) transform: origin = its translation (a practical base point), matrix = 16 numbers row-major (feed ModelItem.SetTransform to round-trip), hasOverride = whether a permanent transform override is applied.")]
    [NodeSearchTags("item", "transform", "get", "read", "matrix", "origin", "override", "position")]
    [MultiReturn("origin", "matrix", "hasOverride")]
    [PortKinds("geometry", "number*", "boolean")]
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
    /// Applies a delta transform. With <paramref name="accumulate"/> it goes on top of each item's existing permanent
    /// override (per item — overrides can differ across the selection), so repeated runs accumulate. Without it the delta
    /// replaces the override (one call for all items), so the result does not depend on how often the node ran.
    /// RUNTIME-CHECK: assumes OverridePermanentTransform replaces (rather than composes with) an existing override — see
    /// the v0.3 Windows smoke list.
    /// </summary>
    internal static void ApplyDelta(Document doc, List<ModelItem> items, Transform3D delta, bool accumulate = true)
    {
        if (!accumulate)
        {
            doc.Models.OverridePermanentTransform(items, delta, false);
            return;
        }

        foreach (var item in items)
        {
            var composed = TransformHelpers.ComposeWithOverride(item, delta);
            doc.Models.OverridePermanentTransform(new[] { item }, composed, false);
        }
    }

    /// <summary>
    /// Converts a port value to a Navisworks <see cref="Vector3D"/>. Accepts a
    /// Navisworks Vector3D, a CamelGraph Vector (Vector.ByCoordinates), or a
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
            case CamelGraphVector camelGraphVector:
                return new Vector3D(camelGraphVector.X, camelGraphVector.Y, camelGraphVector.Z);
            case IList list when !(value is string):
                if (SeveralValues.HoldsSeveral(list))
                {
                    throw new ArgumentException(SeveralValues.Describe("vector", list));
                }

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
