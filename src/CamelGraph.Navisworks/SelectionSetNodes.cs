using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;
using CamelGraph.Nodes.Portable;

namespace CamelGraph.Navisworks;

/// <summary>Nodes for reading and creating saved selection/search sets.</summary>
[NodeCategory("Navisworks.SelectionSets")]
public static class SelectionSetNodes
{
    /// <summary>All saved selection and search sets in a document.</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>Every set, including those nested in folders.</returns>
    [NodeName("SelectionSets.All")]
    [NodeDescription("All saved selection and search sets in a document, including those inside folders. Re-reads the Sets window on every run, so a set added or removed since the last run shows up.")]
    [NodeSearchTags("selection", "sets", "saved", "search", "all")]
    [LiveState]
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
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [NodeDescription("Finds a saved selection or search set by its display name (searches folders too). It only looks: nothing is created or changed. Looks again on every run.")]
    [NodeSearchTags("selection", "set", "byname", "find")]
    [LiveState]
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
    [NodeDescription("The model items a saved set selects. Search sets are re-evaluated against the model, and the set is read again on every run, so the result follows the set as it is now.")]
    [NodeSearchTags("selection", "set", "items", "contents", "resolve")]
    [LiveState]
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
    /// <param name="folder">Where to file the set: a folder (e.g. from SelectionSets.CreateFolder), a folder name, or a path such as "Walls/Level 1". A folder that does not exist yet is created. Leave it empty for the top level.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored selection set.</returns>
    [NodeName("SelectionSet.Create")]
    [NodeDescription("Creates a saved selection set from the given items, filed in the folder you name (a folder from SelectionSets.CreateFolder, a folder name, or a path such as \"Walls/Level 1\"; a folder that does not exist yet is created; empty means the top level). An existing set with the same name in the same place is replaced, so re-running a graph updates the set instead of piling up copies. A list of names with a list of item lists makes one set per pair. Inside a node group every instance uses the same name unless you wire it in, so two instances would replace each other's set.")]
    [NodeSearchTags("selection", "set", "create", "save", "new", "folder")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    // Before the folder input was added: (name, items, document).
    [NodeAliases("CamelGraph.Navisworks.SelectionSetNodes.Create@string,System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>,Autodesk.Navisworks.Api.Document")]
    [return: NodeName("selectionSet")]
    public static SelectionSet Create(
        string name,
        [MultiInput] IEnumerable<ModelItem> items,
        [ScalarInput][PortKinds("selection")] object? folder = null,
        Document? document = null)
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

        // Re-running a graph should update the set, not pile up duplicates. The lookup is type-aware so a same-named folder (or other
        // saved item) is never silently replaced, and it looks only in the place the set goes to.
        return Store(doc, set, name, SelectionSetFolders.ResolveOrCreate(doc, folder));
    }

    /// <summary>Creates (or replaces) a live search set from a property rule.</summary>
    /// <param name="name">Display name for the new search set.</param>
    /// <param name="categoryName">Category display name (e.g. "Element"). Internal names do not match.</param>
    /// <param name="propertyName">Property display name (e.g. "Level"). Internal names do not match. With mode "exists", leave it empty to ask only for the category (tab).</param>
    /// <param name="value">What to look for, as in Search.ByProperty: any value for "equals", text for "contains" and "wildcard", a number for the four comparisons, not used by "exists". A list means "any of these".</param>
    /// <param name="mode">How the property is matched: equals, contains, wildcard, &gt;, &gt;=, &lt;, &lt;= or exists.</param>
    /// <param name="folder">Where to file the set: a folder, a folder name, or a path such as "Walls/Level 1". A folder that does not exist yet is created. Leave it empty for the top level.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored search set (it re-evaluates as the model changes).</returns>
    [NodeName("SelectionSet.CreateFromSearch")]
    [NodeDescription("Creates a live SEARCH set from a property rule, with the same modes as Search.ByProperty (equals, contains, wildcard, >, >=, <, <= or exists; a list in value means any of its entries) — it re-evaluates as the model changes, so \"Diameter > 100\" or \"Name contains DEMO\" stays up to date. It is filed in the folder you name (empty means the top level), and an existing set with the same name in the same place is replaced. Inside a node group every instance uses the same name unless you wire it in, so two instances would replace each other's set.")]
    [NodeSearchTags("selection", "search", "set", "create", "live", "rule", "folder", "contains", "wildcard", "compare")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    // Before the mode and folder inputs were added: (name, categoryName, propertyName, value, document).
    [NodeAliases("CamelGraph.Navisworks.SelectionSetNodes.CreateFromSearch@string,string,string,object,Autodesk.Navisworks.Api.Document")]
    [return: NodeName("selectionSet")]
    public static SelectionSet CreateFromSearch(
        string name,
        [NodeTabChoice(NodeDataSource.Selection)] string categoryName,
        [NodePropertyChoice(NodeDataSource.Selection, "categoryName")] string propertyName,
        object? value = null,
        [NodeChoices("equals", "contains", "wildcard", ">", ">=", "<", "<=", "exists")]
        string mode = "equals",
        [ScalarInput][PortKinds("selection")] object? folder = null,
        Document? document = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("No selection set name provided.", nameof(name));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var search = SearchNodes.CreateSearch(categoryName, propertyName, value, mode);
        var set = new SelectionSet(search) { DisplayName = name };
        return Store(doc, set, name, SelectionSetFolders.ResolveOrCreate(doc, folder));
    }

    // NOTE (v0.3): the SelectionSets.CreateFolder node moved to
    // SelectionSetTreeNodes (SavedItemTreeNodes.cs) where it gained a
    // parentFolder input for nested folders. The creators and
    // BulkByPropertyValues find or create their folder with
    // SelectionSetFolders.ResolveOrCreate (Internal).

    /// <summary>Deletes a saved set by display name.</summary>
    /// <param name="name">The set's display name (folders are searched too).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>True when a set was deleted; false when no set has that name.</returns>
    [NodeName("SelectionSet.Delete")]
    [NodeDescription("Deletes a saved selection or search set by name (searches folders too). Returns false when absent — safe for clean re-runs.")]
    [NodeSearchTags("selection", "set", "delete", "remove", "clean")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [return: NodeName("deleted")]
    public static bool Delete(string name, Document? document = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("No selection set name provided.", nameof(name));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var sets = doc.SelectionSets;
        var set = NavisValues.FindSavedItemByName<SelectionSet>(sets.RootItem.Children, name);
        if (set == null)
        {
            return false;
        }

        var parent = set.Parent;
        return parent == null ? sets.Remove(set) : sets.Remove(parent, set);
    }

    /// <summary>The display name of a saved set.</summary>
    /// <param name="selectionSet">The selection or search set.</param>
    /// <returns>The set's display name.</returns>
    [NodeName("SelectionSet.Name")]
    [NodeDescription("The display name of a saved selection or search set.")]
    [NodeSearchTags("selection", "set", "name", "displayname")]
    [return: NodeName("name")]
    public static string Name(SelectionSet selectionSet)
    {
        if (selectionSet == null)
        {
            throw new ArgumentNullException(nameof(selectionSet), "No selection set provided.");
        }

        return selectionSet.DisplayName ?? string.Empty;
    }

    /// <summary>Creates one live search set per distinct value of a property.</summary>
    /// <param name="categoryName">Category display name (e.g. "Element"). Internal names do not match.</param>
    /// <param name="propertyName">Property display name (e.g. "Level"). Internal names do not match.</param>
    /// <param name="folderName">Folder to file the sets under: a name, or a path such as "By Level/Walls" (folders that do not exist yet are created). Null or empty stores them at the top level.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored sets and the distinct values, index-aligned.</returns>
    [NodeName("SelectionSets.BulkByPropertyValues")]
    [NodeDescription("One search set per distinct value of a property (e.g. one set per Level) — bulk set generation without the Find Items dialog. A value that is stored as text in one file and as a number in another gets ONE set that finds both. The sets come in value order (numbers by size, so 2 before 10). Existing same-named sets in the target folder are replaced; the folder can be a path such as \"By Level/Walls\". Reads the property of every item that carries it once.")]
    [NodeSearchTags("selection", "sets", "bulk", "generate", "values", "level", "system")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [MultiReturn("selectionSets", "values")]
    [PortKinds("selection*", "")]
    public static Dictionary<string, object?> BulkByPropertyValues(
        [NodeTabChoice(NodeDataSource.Selection)] string categoryName,
        [NodePropertyChoice(NodeDataSource.Selection, "categoryName")] string propertyName,
        string? folderName = null,
        Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);

        // Gather the distinct values by the text they show, keeping EVERY storage variant seen for a text (the same visible value is
        // often a string in one file and a number in another, and a search only matches its own data type).
        var carriers = SearchNodes.Find(categoryName, propertyName, null, "exists", "Self", null, doc);
        var groups = new DistinctValueGroups<VariantData>();
        foreach (var item in carriers)
        {
            var property = item.PropertyCategories.FindPropertyByDisplayName(categoryName, propertyName)
                ?? item.PropertyCategories.FindPropertyByName(categoryName, propertyName);
            if (property == null)
            {
                continue;
            }

            var variant = property.Value;
            var text = NavisValues.FormatKey(NavisValues.ToClrObject(variant));
            groups.Add(text, variant, variant.DataType + ":" + text);
        }

        var values = groups.SortedKeys();

        var parentFolder = SelectionSetFolders.ResolveOrCreate(doc, folderName);

        // The names already in the target location, read once and kept up to date as sets are added: searching the
        // growing location twice per value made the run slower with every set it made.
        var names = NavisValues.BuildNameIndex<SelectionSet>(
            parentFolder == null ? doc.SelectionSets.Value : parentFolder.Children);

        var storedSets = new List<SelectionSet>();
        foreach (var value in values)
        {
            var search = SearchNodes.CreateVariantEqualitySearch(categoryName, propertyName, groups.VariantsOf(value));
            var set = new SelectionSet(search) { DisplayName = value };
            storedSets.Add(parentFolder == null
                ? StoreTopLevel(doc, set, value, names)
                : StoreInFolder(doc, parentFolder, set, value, names));
        }

        return new Dictionary<string, object?>
        {
            ["selectionSets"] = storedSets,
            ["values"] = values,
        };
    }

    /// <summary>Adds or replaces a set in the top level or in a stored folder, and returns the STORED instance.</summary>
    private static SelectionSet Store(Document doc, SelectionSet set, string name, FolderItem? folder)
    {
        return folder == null
            ? StoreTopLevel(doc, set, name)
            : StoreInFolder(doc, folder, set, name, NavisValues.BuildNameIndex<SelectionSet>(folder.Children));
    }

    /// <summary>
    /// Adds or replaces a top-level set and returns the STORED instance. <paramref name="names"/> is the index of the
    /// top-level sets, kept up to date by the call; a caller that stores many sets passes the same one every time (null
    /// builds one for this call alone).
    /// </summary>
    private static SelectionSet StoreTopLevel(Document doc, SelectionSet set, string name, TopLevelNameIndex? names = null)
    {
        var sets = doc.SelectionSets;
        names ??= NavisValues.BuildNameIndex<SelectionSet>(sets.Value);
        int expectedIndex;
        if (names.TryGetIndex(name, out var existingIndex))
        {
            sets.ReplaceWithCopy(existingIndex, set);
            expectedIndex = existingIndex;
        }
        else
        {
            sets.AddCopy(set);
            expectedIndex = names.Append(name);
        }

        return NavisValues.ConfirmStored<SelectionSet>(sets.Value, () => sets.Value, names, name, expectedIndex) ?? set;
    }

    /// <summary>Adds or replaces a set inside a stored folder and returns the STORED instance (<paramref name="names"/> as for <see cref="StoreTopLevel"/>, for that folder).</summary>
    private static SelectionSet StoreInFolder(
        Document doc, FolderItem folder, SelectionSet set, string name, TopLevelNameIndex names)
    {
        var sets = doc.SelectionSets;
        int expectedIndex;
        if (names.TryGetIndex(name, out var existingIndex))
        {
            sets.ReplaceWithCopy(folder, existingIndex, set);
            expectedIndex = existingIndex;
        }
        else
        {
            sets.AddCopy(folder, set);
            expectedIndex = names.Append(name);
        }

        return NavisValues.ConfirmStored<SelectionSet>(folder.Children, () => folder.Children, names, name, expectedIndex) ?? set;
    }
}
