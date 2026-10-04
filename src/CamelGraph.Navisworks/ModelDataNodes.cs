using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;
using CamelGraph.Nodes;
using CamelGraph.Nodes.Portable;

namespace CamelGraph.Navisworks;

/// <summary>
/// Model-data nodes: discover what properties a model really has, read many items by many properties into a table,
/// snapshot a model for version comparison and count items by model, class or layer. The counting, parsing and keying
/// rules live in the pure helpers of <c>CamelGraph.Nodes.Portable</c>; these nodes only read the Navisworks objects.
/// </summary>
[NodeCategory("Navisworks.Model")]
public static class ModelDataNodes
{
    /// <summary>The most sample values Properties.Discover will keep per property.</summary>
    private const int MaxSamplesLimit = 50;

    /// <summary>What data does this model actually have? Every property of the items, with counts and sample values.</summary>
    /// <param name="items">The model items to look at (their own properties only, not their children's).</param>
    /// <param name="maxSamples">How many distinct sample values to show per property (0 to 50).</param>
    /// <returns>A table with the columns Category, Property, Items, Distinct and Samples.</returns>
    [NodeName("Properties.Discover")]
    [NodeCategory("Navisworks.Properties")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("table")]
    [NodeDescription("Lists every property the given items carry, one row per category and property with how many items have it (Items), how many different values it holds (Distinct, counted up to 10000) and the first few values (Samples) — the answer to \"what data does this model actually have?\". Reads every property of every item once, so cost grows with items times properties.")]
    [NodeSearchTags("properties", "discover", "explore", "schema", "catalog", "what data", "categories", "values", "profile", "audit", "inventory")]
    public static CamelGraphTable Discover(
        [MultiInput] IEnumerable<ModelItem> items,
        [NodeRange(0, MaxSamplesLimit)] int maxSamples = 3)
    {
        var list = NavisValues.NonNullItems(items);
        if (maxSamples < 0 || maxSamples > MaxSamplesLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxSamples),
                "Properties.Discover: maxSamples must be between 0 and " + MaxSamplesLimit + " (it was " + maxSamples + ").");
        }

        var catalog = new PropertyCatalog(maxSamples);
        for (var index = 0; index < list.Count; index++)
        {
            // One pass over the item's categories and properties; each value is read exactly once.
            foreach (var category in list[index].PropertyCategories)
            {
                var categoryName = category.DisplayName;
                foreach (var property in category.Properties)
                {
                    catalog.Add(index, categoryName, property.DisplayName, NavisValues.ToClrObject(property.Value));
                }
            }
        }

        return catalog.ToTable();
    }

    /// <summary>Reads many properties of many items into a table, one row per item.</summary>
    /// <param name="items">The model items, one table row each.</param>
    /// <param name="properties">The columns: "Category.Property" or "Category|Property" (split at the first "|" if there is one, otherwise at the first "."), a bare property name (searched in every category, first match wins), or @Name, @Path, @Guid.</param>
    /// <returns>A table with one column per entry of <paramref name="properties"/>, headed with the text as written.</returns>
    [NodeName("Properties.ToTable")]
    [NodeCategory("Navisworks.Properties")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("table")]
    [NodeDescription("Reads the named properties of every item into a table with one row per item and one column per name (\"Element.Category\", \"Item|Layer\", a bare property name, or @Name, @Path, @Guid), ready for the Table nodes, Excel or CSV; a property an item lacks is an empty cell. Reads items times names values.")]
    [NodeSearchTags("properties", "table", "export", "rows", "columns", "excel", "csv", "dataframe", "report", "schedule", "quantities")]
    public static CamelGraphTable ToTable([MultiInput] IEnumerable<ModelItem> items, IList<object?> properties)
    {
        var list = NavisValues.NonNullItems(items);
        var specs = PropertySpec.ParseList(properties, "Properties.ToTable");

        var headers = new List<string>(specs.Count);
        foreach (var spec in specs)
        {
            headers.Add(spec.Header);
        }

        var rows = new List<IReadOnlyList<object?>>(list.Count);
        foreach (var item in list)
        {
            var cells = new object?[specs.Count];
            for (var column = 0; column < specs.Count; column++)
            {
                cells[column] = ModelDataReader.ReadSpec(item, specs[column]);
            }

            rows.Add(cells);
        }

        return new CamelGraphTable(headers, rows);
    }

    /// <summary>Captures the given properties of the given items, keyed by item GUID, for Snapshot.Diff.</summary>
    /// <param name="items">The model items to capture.</param>
    /// <param name="properties">The properties to capture, written like Properties.ToTable: "Category.Property", "Category|Property", a bare name, or @Name, @Path, @Guid.</param>
    /// <returns>A dictionary from each item's instance GUID text to a dictionary of property text to value.</returns>
    [NodeName("Model.Snapshot")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("snapshot")]
    [NodeDescription("Captures the named properties of the given items as a dictionary keyed by each item's instance GUID (items without one are keyed \"path:\" plus their tree path) — the input Snapshot.Diff compares, so save one with JSON.WriteToFile now and diff it against a later run to see added, removed and changed items. Nothing in the model is changed.")]
    [NodeSearchTags("snapshot", "version", "compare", "delta", "diff", "baseline", "history", "changes", "guid", "properties")]
    public static Dictionary<string, object?> Snapshot([MultiInput] IEnumerable<ModelItem> items, IList<object?> properties)
    {
        var list = NavisValues.NonNullItems(items);
        var specs = PropertySpec.ParseList(properties, "Model.Snapshot");

        var headers = new List<string>(specs.Count);
        foreach (var spec in specs)
        {
            headers.Add(spec.Header);
        }

        var builder = new SnapshotBuilder();
        foreach (var item in list)
        {
            var values = new object?[specs.Count];
            for (var column = 0; column < specs.Count; column++)
            {
                values[column] = ModelDataReader.ReadSpec(item, specs[column]);
            }

            builder.Add(item.InstanceGuid, () => NavisValues.ItemPath(item), headers, values);
        }

        return builder.Snapshot;
    }

    /// <summary>Counts items by model, class or layer.</summary>
    /// <param name="items">The items to count; leave empty (or unwired) to count every item of the document.</param>
    /// <param name="by">What to group by: "model" (the file the item belongs to), "class" (its class name, such as Layer or Geometry) or "layer" (its Item &gt; Layer property).</param>
    /// <param name="document">The document (defaults to the active document); only used when no items are given.</param>
    /// <returns>A table with the columns Group, Items, WithGeometry and Share (percent of all items, one decimal), the biggest group first.</returns>
    [NodeName("Model.Statistics")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("table")]
    [NodeDescription("Counts items per model file, class or layer: how many items, how many carry geometry and each group's share of all items. With no items wired it walks every item of the document once, so on a large federated model it takes a while (O(items)); \"layer\" also reads one property per item.")]
    [NodeSearchTags("model", "statistics", "count", "health", "inventory", "summary", "items", "geometry", "class", "layer", "file", "size", "breakdown")]
    public static CamelGraphTable Statistics(
        [MultiInput] IEnumerable<ModelItem>? items = null,
        [NodeChoices("model", "class", "layer")] string by = "model",
        Document? document = null)
    {
        var mode = ParseBy(by);
        var stats = new GroupStatistics();
        var list = NavisValues.ToItemList(items);
        if (list.Count > 0)
        {
            foreach (var item in list)
            {
                stats.Add(GroupOf(item, mode), item.HasGeometry);
            }

            return stats.ToTable();
        }

        // No items given: every item of the document, walked once.
        var doc = NavisworksContext.ResolveDocument(document);
        if (mode == GroupMode.Model)
        {
            // A model's items all share its root's name, so read it once per model instead of walking up per item.
            foreach (var model in doc.Models)
            {
                var root = model.RootItem;
                var label = ModelDataReader.ModelLabel(root);
                foreach (var item in root.DescendantsAndSelf)
                {
                    stats.Add(label, item.HasGeometry);
                }
            }
        }
        else
        {
            foreach (var item in ModelDataReader.AllItems(doc))
            {
                stats.Add(GroupOf(item, mode), item.HasGeometry);
            }
        }

        return stats.ToTable();
    }

    private enum GroupMode
    {
        Model,
        Class,
        Layer,
    }

    private static GroupMode ParseBy(string? by)
    {
        switch ((by ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "":
            case "model":
                return GroupMode.Model;
            case "class":
                return GroupMode.Class;
            case "layer":
                return GroupMode.Layer;
            default:
                throw new ArgumentException(
                    "Model.Statistics: unknown 'by' value '" + by + "'. Use \"model\", \"class\" or \"layer\".", nameof(by));
        }
    }

    private static string? GroupOf(ModelItem item, GroupMode mode)
    {
        switch (mode)
        {
            case GroupMode.Class:
                return item.ClassDisplayName;
            case GroupMode.Layer:
                return ModelDataReader.LayerOf(item);
            default:
                return ModelDataReader.ModelNameOf(item);
        }
    }
}
