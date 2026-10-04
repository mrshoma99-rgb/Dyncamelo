using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;

namespace CamelGraph.Navisworks;

/// <summary>Nodes that read and drive the interactive selection.</summary>
[NodeCategory("Navisworks.Selection")]
public static class SelectionNodes
{
    /// <summary>The model items currently selected in Navisworks.</summary>
    /// <param name="resolveTo">Optional selection resolution applied to the picked items, like Options &gt; Interface &gt; Selection &gt; Resolution in Navisworks: Self (default, keep items as picked), File, Layer, FirstObject, LastObject, LastUnique or Geometry.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>A snapshot of the currently selected items.</returns>
    [NodeName("Selection.Current")]
    [NodeDescription("The model items currently selected in Navisworks, read again on every run (press Run after selecting something else and the new selection comes through). The nodes after it run again only when the selection is different from the previous run.")]
    [NodeSearchTags("selection", "selected", "current", "picked", "resolution")]
    [LiveState]
    // Pre-0.4 id (before the optional resolveTo parameter was prepended).
    [NodeAliases("CamelGraph.Navisworks.SelectionNodes.Current@Autodesk.Navisworks.Api.Document")]
    [return: NodeName("items")]
    public static List<ModelItem> Current([NodePanel("Advanced")][NodeChoices("Self", "File", "Layer", "FirstObject", "LastObject", "LastUnique", "Geometry")] string resolveTo = "Self", Document? document = null)
    {
        var level = SelectionLevels.Parse(resolveTo);
        var doc = NavisworksContext.ResolveDocument(document);

        // SelectedItems is a live view — snapshot it before handing it downstream.
        return SelectionLevels.Resolve(new ModelItemCollection(doc.CurrentSelection.SelectedItems), level);
    }

    /// <summary>Replaces the interactive selection with the given items.</summary>
    /// <param name="items">The model items to select.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The items that were selected (pass-through).</returns>
    [NodeName("Selection.SetCurrent")]
    [NodeDescription("Replaces the interactive Navisworks selection with the given items.")]
    [NodeSearchTags("selection", "select", "set", "highlight")]
    [return: NodeName("items")]
    public static List<ModelItem> SetCurrent([MultiInput] IEnumerable<ModelItem> items, Document? document = null)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items), "No model items provided.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var list = NavisValues.ToItemList(items);
        doc.CurrentSelection.CopyFrom(list);
        return list;
    }

    /// <summary>Selects every model item in the document.</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>A snapshot of the resulting selection.</returns>
    [NodeName("Selection.SelectAll")]
    [NodeDescription("Selects everything in the Navisworks UI and returns the selected items.")]
    [NodeSearchTags("selection", "select", "all", "everything")]
    [return: NodeName("items")]
    public static List<ModelItem> SelectAll(Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        doc.CurrentSelection.SelectAll();

        // SelectedItems is a live view — snapshot it before handing it downstream.
        return NavisValues.ToItemList(new ModelItemCollection(doc.CurrentSelection.SelectedItems));
    }

    /// <summary>Adds items to the interactive selection.</summary>
    /// <param name="items">The model items to add.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>A snapshot of the resulting (unioned) selection.</returns>
    [NodeName("Selection.AddToCurrent")]
    [NodeDescription("Adds items to the existing Navisworks selection (union) and returns the result.")]
    [NodeSearchTags("selection", "add", "union", "append", "extend")]
    [return: NodeName("items")]
    public static List<ModelItem> AddToCurrent([MultiInput] IEnumerable<ModelItem> items, Document? document = null)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items), "No model items provided.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        doc.CurrentSelection.AddRange(NavisValues.ToItemList(items));

        // SelectedItems is a live view — snapshot it before handing it downstream.
        return NavisValues.ToItemList(new ModelItemCollection(doc.CurrentSelection.SelectedItems));
    }

    /// <summary>Clears the interactive selection.</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>True when the selection was cleared.</returns>
    [NodeName("Selection.Clear")]
    [NodeDescription("Clears the interactive Navisworks selection.")]
    [NodeSearchTags("selection", "clear", "deselect", "none")]
    [return: NodeName("cleared")]
    public static bool Clear(Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        doc.CurrentSelection.Clear();
        return true;
    }
}
