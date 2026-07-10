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

    /// <summary>Creates (or replaces) a live search set from a property rule.</summary>
    /// <param name="name">Display name for the new search set.</param>
    /// <param name="categoryName">Category display name (e.g. "Element"). Internal names do not match.</param>
    /// <param name="propertyName">Property display name (e.g. "Level"). Internal names do not match.</param>
    /// <param name="value">The value to match (string, number, boolean or date).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored search set (it re-evaluates as the model changes).</returns>
    [NodeName("SelectionSet.CreateFromSearch")]
    [NodeDescription("Creates a live SEARCH set from a property-equals rule — it re-evaluates as the model changes. An existing top-level set with the same name is replaced.")]
    [NodeSearchTags("selection", "search", "set", "create", "live", "rule")]
    [return: NodeName("selectionSet")]
    public static SelectionSet CreateFromSearch(
        string name,
        string categoryName,
        string propertyName,
        object value,
        Document? document = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("No selection set name provided.", nameof(name));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var search = SearchNodes.CreateEqualitySearch(categoryName, propertyName, value);
        var set = new SelectionSet(search) { DisplayName = name };
        return StoreTopLevel(doc, set, name);
    }

    // NOTE (v0.3): the SelectionSets.CreateFolder node moved to
    // SelectionSetTreeNodes (SavedItemTreeNodes.cs) where it gained a
    // parentFolder input for nested folders. The FindOrCreateTopLevelFolder
    // helper below stays because BulkByPropertyValues uses it.

    /// <summary>Deletes a saved set by display name.</summary>
    /// <param name="name">The set's display name (folders are searched too).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>True when a set was deleted; false when no set has that name.</returns>
    [NodeName("SelectionSet.Delete")]
    [NodeDescription("Deletes a saved selection or search set by name (searches folders too). Returns false when absent — safe for clean re-runs.")]
    [NodeSearchTags("selection", "set", "delete", "remove", "clean")]
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
    /// <param name="folderName">Folder to file the sets under (null/empty stores them at the top level).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored sets and the distinct values, index-aligned.</returns>
    [NodeName("SelectionSets.BulkByPropertyValues")]
    [NodeDescription("One search set per distinct value of a property (e.g. one set per Level) — bulk set generation without the Find Items dialog. Existing same-named sets in the target location are replaced.")]
    [NodeSearchTags("selection", "sets", "bulk", "generate", "values", "level", "system")]
    [MultiReturn("selectionSets", "values")]
    public static Dictionary<string, object?> BulkByPropertyValues(
        string categoryName,
        string propertyName,
        string? folderName = null,
        Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);

        // Gather the distinct values, keeping each value's TRUE storage variant so
        // the generated search matches the property's data type exactly.
        var carriers = SearchNodes.HasProperty(categoryName, propertyName, doc);
        var values = new List<string>();
        var variantByValue = new Dictionary<string, VariantData>(StringComparer.Ordinal);
        foreach (var item in carriers)
        {
            var property = item.PropertyCategories.FindPropertyByDisplayName(categoryName, propertyName)
                ?? item.PropertyCategories.FindPropertyByName(categoryName, propertyName);
            if (property == null)
            {
                continue;
            }

            var key = FormatValue(NavisValues.ToClrObject(property.Value));
            if (!variantByValue.ContainsKey(key))
            {
                variantByValue[key] = property.Value;
                values.Add(key);
            }
        }

        values.Sort(StringComparer.OrdinalIgnoreCase);

        var parentFolder = string.IsNullOrEmpty(folderName)
            ? null
            : FindOrCreateTopLevelFolder(doc, folderName!);

        var storedSets = new List<SelectionSet>();
        foreach (var value in values)
        {
            var search = SearchNodes.CreateVariantEqualitySearch(categoryName, propertyName, variantByValue[value]);
            var set = new SelectionSet(search) { DisplayName = value };
            storedSets.Add(parentFolder == null
                ? StoreTopLevel(doc, set, value)
                : StoreInFolder(doc, parentFolder, set, value));
        }

        return new Dictionary<string, object?>
        {
            ["selectionSets"] = storedSets,
            ["values"] = values,
        };
    }

    private static string FormatValue(object? value)
    {
        if (value == null)
        {
            return "(none)";
        }

        var text = value is IFormattable formattable
            ? formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString();
        return string.IsNullOrEmpty(text) ? "(empty)" : text!;
    }

    /// <summary>Adds or replaces a top-level set and returns the STORED instance.</summary>
    private static SelectionSet StoreTopLevel(Document doc, SelectionSet set, string name)
    {
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

        var storedIndex = NavisValues.FindTopLevelIndex<SelectionSet>(sets.Value, name);
        return storedIndex >= 0 ? (SelectionSet)sets.Value[storedIndex] : set;
    }

    /// <summary>Adds or replaces a set inside a stored folder and returns the STORED instance.</summary>
    private static SelectionSet StoreInFolder(Document doc, FolderItem folder, SelectionSet set, string name)
    {
        var sets = doc.SelectionSets;
        var existingIndex = NavisValues.FindTopLevelIndex<SelectionSet>(folder.Children, name);
        if (existingIndex >= 0)
        {
            sets.ReplaceWithCopy(folder, existingIndex, set);
        }
        else
        {
            sets.AddCopy(folder, set);
        }

        var storedIndex = NavisValues.FindTopLevelIndex<SelectionSet>(folder.Children, name);
        return storedIndex >= 0 ? (SelectionSet)folder.Children[storedIndex] : set;
    }

    private static FolderItem FindOrCreateTopLevelFolder(Document doc, string name)
    {
        var sets = doc.SelectionSets;
        var index = NavisValues.FindTopLevelIndex<FolderItem>(sets.Value, name);
        if (index < 0)
        {
            sets.AddCopy(new FolderItem { DisplayName = name });
            index = NavisValues.FindTopLevelIndex<FolderItem>(sets.Value, name);
        }

        if (index < 0)
        {
            throw new InvalidOperationException("Could not create the sets folder '" + name + "'.");
        }

        return (FolderItem)sets.Value[index];
    }
}
