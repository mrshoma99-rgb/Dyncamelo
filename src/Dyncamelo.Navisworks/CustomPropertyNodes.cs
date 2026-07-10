using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Nodes that write user-defined property tabs onto model items via the COM
/// bridge (the .NET API has no property write surface). Values become COM
/// VARIANTs (string/double/int/bool/DateTime), are searchable and schedulable,
/// set <c>Document.IsModified</c>, and persist in NWF/NWD only — they are never
/// written back to source files. COM runs on the Navisworks main thread (a host
/// invariant); these nodes only work inside a live Navisworks session.
/// </summary>
[NodeCategory("Navisworks.Properties")]
public static class CustomPropertyNodes
{
    /// <summary>Writes a user-defined property tab onto items.</summary>
    /// <param name="modelItems">The model items to stamp.</param>
    /// <param name="names">Property display names (index-aligned with <paramref name="values"/>).</param>
    /// <param name="values">Property values (string, number, boolean or date; index-aligned with <paramref name="names"/>).</param>
    /// <param name="tabName">User-visible tab name; a stable internal name is derived from it so search sets can target the tab.</param>
    /// <param name="merge">True keeps existing properties of a same-named tab (new values win on name collisions); false replaces the tab's content entirely.</param>
    /// <returns>The items (pass-through for chaining).</returns>
    [NodeName("Properties.SetCustom")]
    [NodeDescription("Writes a user-defined property tab onto items — values are searchable, schedulable, and travel with the NWF/NWD (source files are never modified). Merge keeps existing same-tab properties; new values win on name collisions.")]
    [NodeSearchTags("property", "custom", "set", "write", "user", "tab", "parameter", "smartproperties", "stamp")]
    [return: NodeName("modelItems")]
    public static List<ModelItem> SetCustom(
        IEnumerable<ModelItem> modelItems,
        IEnumerable<string> names,
        IEnumerable<object?> values,
        string tabName = "Dyncamelo Data",
        bool merge = true)
    {
        var items = NavisValues.ToItemList(modelItems);
        if (items.Count == 0)
        {
            throw new ArgumentException("No model items provided.", nameof(modelItems));
        }

        if (string.IsNullOrWhiteSpace(tabName))
        {
            throw new ArgumentException("No tab name provided.", nameof(tabName));
        }

        var pairs = ZipNameValuePairs(names, values);
        foreach (var item in items)
        {
            ComBridge.SetUserDefinedTab(item, tabName, null, pairs, merge);
        }

        return items;
    }

    /// <summary>Removes a user-defined tab from items.</summary>
    /// <param name="modelItems">The model items to clean.</param>
    /// <param name="tabName">The tab's user-visible name.</param>
    /// <returns>The items (pass-through) and how many items actually had the tab.</returns>
    [NodeName("Properties.RemoveCustomTab")]
    [NodeDescription("Removes a user-defined property tab from items. Items without the tab are skipped (see removedCount) — safe for clean re-runs of SetCustom graphs.")]
    [NodeSearchTags("property", "custom", "remove", "delete", "tab", "clean", "user")]
    [MultiReturn("modelItems", "removedCount")]
    public static Dictionary<string, object?> RemoveCustomTab(
        IEnumerable<ModelItem> modelItems,
        string tabName)
    {
        var items = NavisValues.ToItemList(modelItems);
        if (items.Count == 0)
        {
            throw new ArgumentException("No model items provided.", nameof(modelItems));
        }

        if (string.IsNullOrWhiteSpace(tabName))
        {
            throw new ArgumentException("No tab name provided.", nameof(tabName));
        }

        int removed = 0;
        foreach (var item in items)
        {
            if (ComBridge.RemoveUserDefinedTab(item, tabName))
            {
                removed++;
            }
        }

        return new Dictionary<string, object?>
        {
            ["modelItems"] = items,
            ["removedCount"] = removed,
        };
    }

    /// <summary>Renames a user-defined tab on items.</summary>
    /// <param name="modelItems">The model items whose tab to rename.</param>
    /// <param name="tabName">The tab's current user-visible name.</param>
    /// <param name="newTabName">The new user-visible name.</param>
    /// <returns>The items (pass-through for chaining).</returns>
    [NodeName("Properties.RenameCustomTab")]
    [NodeDescription("Renames a user-defined property tab in place (same properties, same internal name — search sets targeting the tab stay valid). Items without the tab are skipped.")]
    [NodeSearchTags("property", "custom", "rename", "tab", "user")]
    [return: NodeName("modelItems")]
    public static List<ModelItem> RenameCustomTab(
        IEnumerable<ModelItem> modelItems,
        string tabName,
        string newTabName)
    {
        var items = NavisValues.ToItemList(modelItems);
        if (items.Count == 0)
        {
            throw new ArgumentException("No model items provided.", nameof(modelItems));
        }

        if (string.IsNullOrWhiteSpace(tabName))
        {
            throw new ArgumentException("No tab name provided.", nameof(tabName));
        }

        if (string.IsNullOrWhiteSpace(newTabName))
        {
            throw new ArgumentException("No new tab name provided.", nameof(newTabName));
        }

        foreach (var item in items)
        {
            ComBridge.RenameUserDefinedTab(item, tabName, newTabName);
        }

        return items;
    }

    /// <summary>Lists the user-defined tabs on an item.</summary>
    /// <param name="modelItem">The model item to inspect.</param>
    /// <returns>The user-defined tab display names, in tab order.</returns>
    [NodeName("Properties.CustomTabs")]
    [NodeDescription("The user-defined property tabs on an item (discovery/QA before SetCustom or RemoveCustomTab). Built-in source-file categories are not listed — use Properties.Categories for those.")]
    [NodeSearchTags("property", "custom", "tabs", "list", "user", "discover")]
    [return: NodeName("tabNames")]
    public static List<string> CustomTabs(ModelItem modelItem)
    {
        if (modelItem == null)
        {
            throw new ArgumentNullException(nameof(modelItem), "No model item provided.");
        }

        return ComBridge.UserTabNames(modelItem);
    }

    /// <summary>Pairs names with values, validating alignment.</summary>
    private static List<KeyValuePair<string, object?>> ZipNameValuePairs(
        IEnumerable<string> names,
        IEnumerable<object?> values)
    {
        if (names == null)
        {
            throw new ArgumentNullException(nameof(names), "No property names provided.");
        }

        if (values == null)
        {
            throw new ArgumentNullException(nameof(values), "No property values provided.");
        }

        var nameList = new List<string>(names);
        var valueList = new List<object?>(values);
        if (nameList.Count == 0)
        {
            throw new ArgumentException("No property names provided.", nameof(names));
        }

        if (nameList.Count != valueList.Count)
        {
            throw new ArgumentException(
                "names and values must be the same length (got " + nameList.Count +
                " names and " + valueList.Count + " values).", nameof(values));
        }

        var pairs = new List<KeyValuePair<string, object?>>(nameList.Count);
        for (int i = 0; i < nameList.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(nameList[i]))
            {
                throw new ArgumentException(
                    "Property name at index " + i + " is empty — every property needs a name.", nameof(names));
            }

            pairs.Add(new KeyValuePair<string, object?>(nameList[i], valueList[i]));
        }

        return pairs;
    }
}
