using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>Nodes that override how model items look in the viewport.</summary>
[NodeCategory("Navisworks.Appearance")]
public static class AppearanceNodes
{
    /// <summary>Overrides the color of model items.</summary>
    /// <param name="items">The model items to recolor.</param>
    /// <param name="color">A Color, a "#RRGGBB" string, or a list of three numbers.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The recolored items (pass-through).</returns>
    [NodeName("Appearance.OverrideColor")]
    [NodeDescription("Overrides the color of model items (a permanent override: saved with the file and undoable).")]
    [NodeSearchTags("appearance", "color", "override", "paint", "tint")]
    [return: NodeName("items")]
    public static List<ModelItem> OverrideColor(IEnumerable<ModelItem> items, object color, Document? document = null)
    {
        var list = RequireItems(items);
        var doc = NavisworksContext.ResolveDocument(document);
        doc.Models.OverridePermanentColor(list, NavisValues.ToNavisColor(color));
        return list;
    }

    /// <summary>Overrides the transparency of model items.</summary>
    /// <param name="items">The model items to change.</param>
    /// <param name="transparency">0 = fully opaque, 1 = fully transparent.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The changed items (pass-through).</returns>
    [NodeName("Appearance.OverrideTransparency")]
    [NodeDescription("Overrides the transparency of model items (0 = opaque, 1 = invisible).")]
    [NodeSearchTags("appearance", "transparency", "override", "ghost", "opacity")]
    [return: NodeName("items")]
    public static List<ModelItem> OverrideTransparency(
        IEnumerable<ModelItem> items,
        double transparency,
        Document? document = null)
    {
        if (transparency < 0.0 || transparency > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(transparency), "Transparency must be between 0 (opaque) and 1 (invisible).");
        }

        var list = RequireItems(items);
        var doc = NavisworksContext.ResolveDocument(document);
        doc.Models.OverridePermanentTransparency(list, transparency);
        return list;
    }

    /// <summary>Removes color/transparency overrides from model items.</summary>
    /// <param name="items">The model items to reset.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The reset items (pass-through).</returns>
    [NodeName("Appearance.Reset")]
    [NodeDescription("Removes permanent color and transparency overrides from model items, restoring their original materials.")]
    [NodeSearchTags("appearance", "reset", "restore", "original", "materials")]
    [return: NodeName("items")]
    public static List<ModelItem> Reset(IEnumerable<ModelItem> items, Document? document = null)
    {
        var list = RequireItems(items);
        var doc = NavisworksContext.ResolveDocument(document);
        doc.Models.ResetPermanentMaterials(list);
        return list;
    }

    /// <summary>Hides model items in the viewport.</summary>
    /// <param name="items">The model items to hide.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The hidden items (pass-through).</returns>
    [NodeName("Appearance.Hide")]
    [NodeDescription("Hides model items in the viewport.")]
    [NodeSearchTags("appearance", "hide", "hidden", "invisible")]
    [return: NodeName("items")]
    public static List<ModelItem> Hide(IEnumerable<ModelItem> items, Document? document = null)
    {
        var list = RequireItems(items);
        var doc = NavisworksContext.ResolveDocument(document);
        doc.Models.SetHidden(list, true);
        return list;
    }

    /// <summary>Shows (un-hides) model items in the viewport.</summary>
    /// <param name="items">The model items to show.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The shown items (pass-through).</returns>
    [NodeName("Appearance.Show")]
    [NodeDescription("Shows (un-hides) model items in the viewport.")]
    [NodeSearchTags("appearance", "show", "unhide", "visible")]
    [return: NodeName("items")]
    public static List<ModelItem> Show(IEnumerable<ModelItem> items, Document? document = null)
    {
        var list = RequireItems(items);
        var doc = NavisworksContext.ResolveDocument(document);
        doc.Models.SetHidden(list, false);
        return list;
    }

    private static List<ModelItem> RequireItems(IEnumerable<ModelItem>? items)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items), "No model items provided.");
        }

        return NavisValues.ToItemList(items);
    }
}
