using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;
using Dyncamelo.Nodes.Portable;

namespace Dyncamelo.Navisworks;

/// <summary>
/// More selection nodes: invert or trim the current selection, find items by GUID in one pass, and read or
/// duplicate a saved selection or search set.
/// </summary>
[NodeCategory("Navisworks.Selection")]
public static class SelectionExtraNodes
{
    /// <summary>The items that are not in the current selection.</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>Everything except the current selection, as branch-level items (like Navisworks' own Invert Selection).</returns>
    [NodeName("Selection.Invert")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("items")]
    [NodeDescription("The items that are NOT in the current selection (Navisworks' own invert, so whole untouched branches come back as one item each); it does not change the selection — wire it into Selection.SetCurrent to select them.")]
    [NodeSearchTags("selection", "invert", "inverse", "opposite", "except", "everything else", "not selected", "complement")]
    public static List<ModelItem> Invert(Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);

        // SelectedItems is a live view — copy it, then invert the copy in place (the same call Appearance.Isolate
        // uses): the copy then holds everything EXCEPT the selection, and the selection itself is not touched.
        var inverted = new ModelItemCollection(doc.CurrentSelection.SelectedItems);
        inverted.Invert(doc);
        return NavisValues.ToItemList(inverted);
    }

    /// <summary>Takes items out of the current selection.</summary>
    /// <param name="items">The model items to deselect.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>A snapshot of the selection that remains.</returns>
    [NodeName("Selection.Remove")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("items")]
    [NodeDescription("Takes the given items out of the current Navisworks selection and returns what is still selected; items that are not selected themselves (for example children of a selected parent) are ignored.")]
    [NodeSearchTags("selection", "remove", "deselect", "subtract", "exclude", "minus", "unselect")]
    public static List<ModelItem> Remove([MultiInput] IEnumerable<ModelItem> items, Document? document = null)
    {
        var list = NavisValues.NonNullItems(items);
        var doc = NavisworksContext.ResolveDocument(document);

        // Filter in plain .NET and set the selection once (one change event), and only when something was removed.
        // ModelItemCollection.Remove searches the collection on every call, so taking 20 000 items out of a selection of 100 000
        // was 20 000 searches; the requests are hashed by the same identity the collection uses for an item (ModelItemSet) and
        // the selection is walked once. Each request still takes out the first matching entry, like Remove did.
        var selected = NavisValues.ToItemList(doc.CurrentSelection.SelectedItems);
        var remaining = ListRemoval.RemoveFirstOfEach(selected, list, ModelItemIdentityComparer.Instance, out var removed);
        if (removed > 0)
        {
            doc.CurrentSelection.CopyFrom(NavisValues.ToItemCollection(remaining));
        }

        // SelectedItems is a live view — snapshot it before handing it downstream.
        return NavisValues.ToItemList(new ModelItemCollection(doc.CurrentSelection.SelectedItems));
    }

    /// <summary>Finds items by their instance GUIDs in one pass over the model.</summary>
    /// <param name="guids">The GUIDs: text such as "3f81e10a-25b0-49ff-9520-63f2a763150a" (or a 22-character IFC GlobalId) or GUID values.</param>
    /// <param name="document">The document to search (defaults to the active document).</param>
    /// <returns>The items found, in the order of the GUIDs, and the GUIDs that matched no item.</returns>
    [NodeName("Search.ByGuid")]
    [NodeCategory("Navisworks.Search")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [NodeDescription("Finds the items whose instance GUID equals any of the given GUIDs (text, a 22-character IFC GlobalId, or GUID values) in one pass over the model — not one search per GUID — and lists the GUIDs that matched nothing; walks every item of the document once (O(items)).")]
    [NodeSearchTags("search", "find", "guid", "uuid", "id", "instance", "lookup", "ifc", "globalid", "missing")]
    [MultiReturn("items", "missing")]
    [PortKinds("item*", "text*")]
    public static Dictionary<string, object?> ByGuid(IList<object?> guids, Document? document = null)
    {
        var requests = GuidLookup.ParseRequests(guids, "Search.ByGuid");
        var doc = NavisworksContext.ResolveDocument(document);

        var wanted = new HashSet<Guid>();
        foreach (var request in requests)
        {
            wanted.Add(request.Guid);
        }

        var found = new Dictionary<Guid, List<ModelItem>>();
        if (wanted.Count > 0)
        {
            foreach (var item in ModelDataReader.AllItems(doc))
            {
                var guid = item.InstanceGuid;
                if (guid == Guid.Empty || !wanted.Contains(guid))
                {
                    continue;
                }

                if (!found.TryGetValue(guid, out var matches))
                {
                    matches = new List<ModelItem>();
                    found[guid] = matches;
                }

                matches.Add(item);
            }
        }

        var items = GuidLookup.Collect(requests, found, out var missing);
        return new Dictionary<string, object?>
        {
            ["items"] = items,
            ["missing"] = missing,
        };
    }

    /// <summary>The name, kind, size and folder of a saved selection or search set.</summary>
    /// <param name="selectionSet">The selection or search set.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The set's name, its kind ("selection" or "search"), how many items it selects and its folder path.</returns>
    [NodeName("SelectionSet.Info")]
    [NodeCategory("Navisworks.SelectionSets")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [NodeDescription("What a saved set is: its name, whether it is a fixed \"selection\" or a live \"search\" set, how many items it selects right now (a search set is evaluated once to count) and its folder path (\"\" at the top level).")]
    [NodeSearchTags("selection", "set", "info", "kind", "count", "size", "folder", "search set", "audit")]
    [MultiReturn("name", "kind", "itemCount", "folder")]
    [PortKinds("text", "text", "integer", "text")]
    public static Dictionary<string, object?> Info(SelectionSet selectionSet, Document? document = null)
    {
        if (selectionSet == null)
        {
            throw new ArgumentNullException(nameof(selectionSet), "No selection set provided.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var root = doc.SelectionSets.RootItem;

        // Prefer the stored instance (it knows its folder); an unsaved set is read as it is, at the top level.
        var stored = SavedItemTreeHelpers.FindStoredEquivalent(root, selectionSet);
        var folderPath = string.Empty;
        if (stored != null)
        {
            SavedItemTreeHelpers.GetFolderInfo(root, stored, out folderPath, out _);
        }

        var set = stored ?? selectionSet;
        return new Dictionary<string, object?>
        {
            ["name"] = set.DisplayName ?? string.Empty,
            ["kind"] = set.HasSearch ? "search" : "selection",
            ["itemCount"] = set.GetSelectedItems(doc).Count,
            ["folder"] = folderPath,
        };
    }

    /// <summary>Duplicates a saved selection or search set next to the original.</summary>
    /// <param name="selectionSet">The set to copy.</param>
    /// <param name="newName">Name for the copy (empty uses "&lt;name&gt; copy").</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The new stored set.</returns>
    [NodeName("SelectionSet.Duplicate")]
    [NodeCategory("Navisworks.SelectionSets")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("selectionSet")]
    [NodeDescription("Duplicates a saved selection or search set in its folder (a search set stays a live search); the copy is named \"<name> copy\" unless newName is given. Running it again adds another copy.")]
    [NodeSearchTags("selection", "set", "duplicate", "copy", "clone", "backup")]
    public static SelectionSet Duplicate(SelectionSet selectionSet, string newName = "", Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var sets = doc.SelectionSets;
        var stored = SavedItemTreeHelpers.ResolveStored<SelectionSet>(sets.RootItem, selectionSet, "selection set");

        // Top-level items report RootItem as their parent; treat that as "no folder".
        var parentFolder = ReferenceEquals(stored.Parent, sets.RootItem)
            ? null
            : stored.Parent as FolderItem;

        // AddCopy appends a copy at the end of the target collection.
        if (parentFolder != null)
        {
            sets.AddCopy(parentFolder, stored);
        }
        else
        {
            sets.AddCopy(stored);
        }

        var siblings = parentFolder != null ? parentFolder.Children : sets.Value;
        var copy = siblings[siblings.Count - 1] as SelectionSet
            ?? throw new InvalidOperationException(
                "Could not find the copy of the selection set '" + stored.DisplayName + "' after adding it.");
        var name = string.IsNullOrEmpty(newName) ? stored.DisplayName + " copy" : newName;
        sets.EditDisplayName(copy, name);
        return copy;
    }
}
