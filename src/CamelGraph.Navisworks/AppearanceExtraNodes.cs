using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;
using CamelGraph.Nodes.Coordination;

namespace CamelGraph.Navisworks;

/// <summary>
/// One-node appearance recipes built from the calls the existing Appearance and Action nodes
/// already use (temporary transparency override, clear temporary overrides, invert a collection).
/// </summary>
[NodeCategory("Navisworks.Appearance")]
public static class AppearanceExtraNodes
{
    /// <summary>Keeps some items as they are and ghosts everything else.</summary>
    /// <param name="items">The items to focus on.</param>
    /// <param name="otherTransparency">How transparent everything else becomes, from 0 (opaque) to 100 (invisible).</param>
    /// <param name="resetFirst">True clears every temporary override before ghosting, so each run starts clean.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The focused items (pass-through).</returns>
    [NodeName("Appearance.Focus")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeDescription(
        "Focus on some items: they stay as they are and everything else in the model fades by otherTransparency percent " +
        "(0 = opaque, 100 = invisible) with a TEMPORARY transparency override — the Action.Ghost look in one node, no " +
        "permanent change. With resetFirst on, every temporary override is cleared first; undo it later with " +
        "Appearance.ResetTemporary. Touches every item in the model, so allow a moment on large models.")]
    [NodeSearchTags("appearance", "focus", "ghost", "fade", "transparency", "context", "highlight", "isolate", "temporary")]
    [return: NodeName("items")]
    public static List<ModelItem> Focus(
        [MultiInput] IEnumerable<ModelItem> items,
        [NodeRange(0, 100, SoftMin = 0, SoftMax = 100, Step = 5, Unit = "%")] double otherTransparency = 85,
        bool resetFirst = true,
        Document? document = null)
    {
        if (items == null)
        {
            throw new ArgumentNullException(
                nameof(items), "Appearance.Focus requires the items to focus on. Wire model items into the 'items' input.");
        }

        var list = NavisValues.ToItemList(items);
        if (list.Count == 0)
        {
            throw new ArgumentException(
                "Appearance.Focus got no items to focus on — everything would be ghosted. Wire at least one model item " +
                "(an empty search result is the usual cause).", nameof(items));
        }

        var transparency = ScheduleRules.PercentToFraction(otherTransparency, nameof(otherTransparency));
        var doc = NavisworksContext.ResolveDocument(document);

        if (resetFirst)
        {
            doc.Models.ResetAllTemporaryMaterials();
        }

        // "Everything except these", exactly as Appearance.Isolate builds it: Invert works in place
        // and yields the model's remaining branches.
        var others = NavisValues.ToItemCollection(list);
        others.Invert(doc);
        doc.Models.OverrideTemporaryTransparency(others, transparency);
        return list;
    }
}
