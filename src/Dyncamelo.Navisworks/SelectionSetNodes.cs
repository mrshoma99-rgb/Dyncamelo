using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>Nodes for reading and creating saved selection/search sets.</summary>
[NodeCategory("Navisworks.SelectionSets")]
public static class SelectionSetNodes
{
    /// <summary>All saved selection and search sets in a document.</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>Every set, including those nested in folders.</returns>
    [NodeName("SelectionSets.All")]
    [NodeDescription("All saved selection and search sets in a document, including those inside folders.")]
    [NodeSearchTags("selection", "sets", "saved", "search", "all")]
    [return: NodeName("selectionSets")]
    public static List<SelectionSet> All(Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        return NavisValues.FlattenSavedItems<SelectionSet>(doc.SelectionSets.RootItem.Children);
    }

    /// <summary>Finds a saved set by display name.</summary>
    /// <param name="name">The set's display name.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored selection set.</returns>
    [NodeName("SelectionSet.ByName")]
    [NodeDescription("Finds a saved selection or search set by its display name (searches folders too).")]
    [NodeSearchTags("selection", "set", "byname", "find")]
    [return: NodeName("selectionSet")]
    public static SelectionSet ByName(string name, Document? document = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("No selection set name provided.", nameof(name));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var set = NavisValues.FindSavedItemByName<SelectionSet>(doc.SelectionSets.RootItem.Children, name);
        return set ?? throw new InvalidOperationException(
            "No selection set named '" + name + "' exists in the document.");
    }

    /// <summary>Resolves the model items a set selects.</summary>
    /// <param name="selectionSet">The selection or search set.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The items the set currently selects (search sets are evaluated).</returns>
    [NodeName("SelectionSet.Items")]
    [NodeDescription("The model items a saved set selects. Search sets are re-evaluated against the model.")]
    [NodeSearchTags("selection", "set", "items", "contents", "resolve")]
    [return: NodeName("items")]
    public static List<ModelItem> Items(SelectionSet selectionSet, Document? document = null)
    {
        if (selectionSet == null)
        {
            throw new ArgumentNullException(nameof(selectionSet), "No selection set provided.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        return NavisValues.ToItemList(selectionSet.GetSelectedItems(doc));
    }

    /// <summary>Creates (or replaces) a saved selection set from explicit items.</summary>
    /// <param name="name">Display name for the new set.</param>
    /// <param name="items">The model items to store.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored selection set.</returns>
    [NodeName("SelectionSet.Create")]
    [NodeDescription("Creates a saved selection set from the given items. An existing top-level set with the same name is replaced.")]
    [NodeSearchTags("selection", "set", "create", "save", "new")]
    [return: NodeName("selectionSet")]
    public static SelectionSet Create(string name, IEnumerable<ModelItem> items, Document? document = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("No selection set name provided.", nameof(name));
        }

        if (items == null)
        {
            throw new ArgumentNullException(nameof(items), "No model items provided.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var set = new SelectionSet(NavisValues.ToItemCollection(items)) { DisplayName = name };

        // Re-running a graph should update the set, not pile up duplicates. The
        // lookup is type-aware so a same-named folder (or other saved item) is
        // never silently replaced — only an existing top-level SET is.
        var sets = doc.SelectionSets;
        var existingIndex = NavisValues.FindTopLevelIndex<SelectionSet>(sets.Value, name);
        if (existingIndex >= 0)
        {
            sets.ReplaceWithCopy(existingIndex, set);
        }
        else
        {
            sets.AddCopy(set);
        }

        // AddCopy/ReplaceWithCopy store a copy — hand the stored instance downstream.
        var storedIndex = NavisValues.FindTopLevelIndex<SelectionSet>(sets.Value, name);
        return storedIndex >= 0 ? (SelectionSet)sets.Value[storedIndex] : set;
    }
}
