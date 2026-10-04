using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;
using CamelGraph.Nodes;
using CamelGraph.Nodes.Portable;

namespace CamelGraph.Navisworks;

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
    /// <param name="values">Property values (string, number, boolean or date; index-aligned with <paramref name="names"/>). Every value must be a single value: a list inside the values is refused.</param>
    /// <param name="tabName">User-visible tab name; a stable internal name is derived from it so search sets can target the tab.</param>
    /// <param name="merge">True keeps existing properties of a same-named tab (new values win on name collisions); false replaces the tab's content entirely.</param>
    /// <returns>The items (pass-through for chaining).</returns>
    [NodeName("Properties.SetCustom")]
    [NodeDescription("Writes ONE set of names and values as a user-defined property tab onto every item you give it — values are searchable, schedulable, and travel with the NWF/NWD (source files are never modified). Each value must be a single text, number, true/false or date: a list in the values is refused with a message, because a property holds one value. For a different row of values per item (a table from Excel) use Properties.SetCustomFromTable. Merge keeps existing same-tab properties; new values win on name collisions. Inside a node group every instance writes the same tab name, so wire the name in from the group's input when instances must not share a tab.")]
    [NodeSearchTags("property", "custom", "set", "write", "user", "tab", "parameter", "smartproperties", "stamp")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [return: NodeName("modelItems")]
    public static List<ModelItem> SetCustom(
        [MultiInput] IEnumerable<ModelItem> modelItems,
        IEnumerable<string> names,
        IEnumerable<object?> values,
        [NodeTabChoice("modelItems", UserDefinedOnly = true)] string tabName = "CamelGraph Data",
        [NodePanel("Advanced")] bool merge = true)
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

    /// <summary>Writes each row of a table as a user-defined property tab on its own item.</summary>
    /// <param name="table">The table: one row per item, one column per property (the header is the property name).</param>
    /// <param name="modelItems">The items. With no keyColumn row 1 goes to item 1, row 2 to item 2 and so on, so there must be as many rows as items. With a keyColumn these are the items the GUIDs are looked for among; leave it unwired to look in the whole model.</param>
    /// <param name="columns">The headers of the columns to write; leave empty for every column except the key column and the @ columns that Properties.ToTable adds.</param>
    /// <param name="tabName">User-visible tab name; a stable internal name is derived from it so search sets can target the tab.</param>
    /// <param name="keyColumn">The header of the column that holds each row's item GUID (instance GUID text, or a 22-character IFC id); leave empty to pair rows and items by position.</param>
    /// <param name="merge">True keeps existing properties of a same-named tab (new values win on name collisions); false replaces the tab's content entirely.</param>
    /// <param name="document">The document (defaults to the active document); only used to look items up by GUID.</param>
    /// <returns>The items that were written, how many, and the keys that matched no item.</returns>
    [NodeName("Properties.SetCustomFromTable")]
    [NodeDescription("Writes a table onto model items as a user-defined property tab, a DIFFERENT row for every item — the spreadsheet-to-model step in one node (Table.FromExcelFile, Table.Join with Properties.ToTable, then this). Each column becomes a property named after its header. Either row 1 goes to item 1, row 2 to item 2 and so on (the counts must match, or nothing is written), or give keyColumn the column that holds each row's GUID and every row finds its item by GUID in one pass (rows that match no item are skipped and listed in missing). Every value must be text, a number, true/false or a date (an empty cell is written as empty text), and all values are checked before the first item is touched. Values are searchable, schedulable, and travel with the NWF/NWD (source files are never modified); merge keeps existing same-tab properties.")]
    [NodeSearchTags("property", "custom", "table", "excel", "spreadsheet", "import", "write", "user", "tab", "row", "guid", "stamp", "cobie", "classification", "parameter")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [MultiReturn("modelItems", "written", "missing")]
    [PortKinds("item*", "integer", "text*")]
    public static Dictionary<string, object?> SetCustomFromTable(
        CamelGraphTable table,
        [MultiInput] IEnumerable<ModelItem>? modelItems = null,
        [MultiInput] IList<object?>? columns = null,
        [NodeTabChoice("modelItems", UserDefinedOnly = true)] string tabName = "CamelGraph Data",
        string? keyColumn = null,
        [NodePanel("Advanced")] bool merge = true,
        Document? document = null)
    {
        if (string.IsNullOrWhiteSpace(tabName))
        {
            throw new ArgumentException("No tab name provided.", nameof(tabName));
        }

        var plan = TablePropertyPlan.Create(table, columns, keyColumn, "Properties.SetCustomFromTable");

        // Every value is checked before the first item is written to.
        var rows = new List<List<KeyValuePair<string, object?>>>(plan.RowCount);
        for (var row = 0; row < plan.RowCount; row++)
        {
            rows.Add(plan.PairsOf(row));
        }

        var written = new List<ModelItem>();
        var missing = new List<string>();
        if (!plan.IsKeyed)
        {
            var items = NavisValues.ToItemList(modelItems);
            if (items.Count == 0)
            {
                throw new ArgumentException("No model items provided. Wire the items the rows belong to, or give keyColumn the column that holds each row's GUID.", nameof(modelItems));
            }

            plan.RequireItemCount(items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                ComBridge.SetUserDefinedTab(items[i], tabName, null, rows[i], merge);
                written.Add(items[i]);
            }
        }
        else
        {
            var wanted = new Dictionary<Guid, int>();
            for (var row = 0; row < plan.RowCount; row++)
            {
                wanted[plan.Keys![row].Guid] = row;
            }

            // One pass over the wired items, or over the whole model when none were wired.
            var source = modelItems != null
                ? NavisValues.ToItemList(modelItems)
                : ModelDataReader.AllItems(NavisworksContext.ResolveDocument(document));
            var matches = new Dictionary<int, List<ModelItem>>();
            foreach (var item in source)
            {
                var guid = item.InstanceGuid;
                if (guid == Guid.Empty || !wanted.TryGetValue(guid, out var row))
                {
                    continue;
                }

                if (!matches.TryGetValue(row, out var found))
                {
                    found = new List<ModelItem>();
                    matches[row] = found;
                }

                found.Add(item);
            }

            for (var row = 0; row < plan.RowCount; row++)
            {
                if (!matches.TryGetValue(row, out var found))
                {
                    missing.Add(plan.Keys![row].Text);
                    continue;
                }

                foreach (var item in found)
                {
                    ComBridge.SetUserDefinedTab(item, tabName, null, rows[row], merge);
                    written.Add(item);
                }
            }

            if (missing.Count > 0)
            {
                NodeWarnings.Add(missing.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " of " +
                                 plan.RowCount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                 " row(s) matched no item and were skipped (see the 'missing' output).");
            }
        }

        return new Dictionary<string, object?>
        {
            ["modelItems"] = written,
            ["written"] = written.Count,
            ["missing"] = missing,
        };
    }

    /// <summary>Removes a user-defined tab from items.</summary>
    /// <param name="modelItems">The model items to clean.</param>
    /// <param name="tabName">The tab's user-visible name.</param>
    /// <returns>The items (pass-through) and how many items actually had the tab.</returns>
    [NodeName("Properties.RemoveCustomTab")]
    [NodeDescription("Removes a user-defined property tab from items. Items without the tab are skipped (see removedCount) — safe for clean re-runs of SetCustom graphs.")]
    [NodeSearchTags("property", "custom", "remove", "delete", "tab", "clean", "user")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [MultiReturn("modelItems", "removedCount")]
    [PortKinds("item*", "integer")]
    public static Dictionary<string, object?> RemoveCustomTab(
        [MultiInput] IEnumerable<ModelItem> modelItems,
        [NodeTabChoice("modelItems", UserDefinedOnly = true)] string tabName)
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
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [return: NodeName("modelItems")]
    public static List<ModelItem> RenameCustomTab(
        [MultiInput] IEnumerable<ModelItem> modelItems,
        [NodeTabChoice("modelItems", UserDefinedOnly = true)] string tabName,
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

            // A property holds one value. A list here (a table row that lost its @L2 badge) used to be written as the text of its
            // .NET type into every item; refuse it before any item is touched.
            CustomPropertyValues.Require("Properties.SetCustom", nameList[i], valueList[i]);
            pairs.Add(new KeyValuePair<string, object?>(nameList[i], valueList[i]));
        }

        return pairs;
    }
}
