using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using CamelGraph.Nodes.Portable;

namespace CamelGraph.Navisworks.Internal;

/// <summary>
/// Navisworks-side readers shared by the model-data nodes (Properties.Discover / ToTable, Model.Snapshot /
/// Statistics, Search.ByGuid, ModelItem.IfcGuid). The decisions (what a property spec means, how values are counted,
/// how keys are made) live in the pure helpers of CamelGraph.Nodes.Portable; this class only translates Navisworks
/// objects into plain values for them. Internal — never surfaced as nodes.
/// </summary>
internal static class ModelDataReader
{
    /// <summary>The category the Navisworks IFC import puts the GlobalId in (the BCF nodes read the same one).</summary>
    private const string IfcCategory = "IFC";

    /// <summary>The IFC GlobalId property name.</summary>
    private const string IfcGlobalIdProperty = "GlobalId";

    /// <summary>
    /// Finds a property by category and name — display names first, then internal names, as Properties.Value does.
    /// With no category every category is searched and the first match wins.
    /// </summary>
    internal static DataProperty? FindProperty(ModelItem item, string? category, string property)
    {
        var categories = item.PropertyCategories;
        if (!string.IsNullOrEmpty(category))
        {
            return categories.FindPropertyByDisplayName(category, property)
                ?? categories.FindPropertyByName(category, property);
        }

        foreach (var candidate in categories)
        {
            var found = candidate.Properties.FindPropertyByDisplayName(property)
                ?? candidate.Properties.FindPropertyByName(property);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The value a <see cref="PropertySpec"/> reads from an item (null when the item lacks it).</summary>
    internal static object? ReadSpec(ModelItem item, PropertySpec spec)
    {
        switch (spec.Kind)
        {
            case PropertySpecKind.Name:
                return DisplayNameOf(item);
            case PropertySpecKind.Path:
                return NavisValues.ItemPath(item);
            case PropertySpecKind.Guid:
                return item.InstanceGuid == Guid.Empty ? null : item.InstanceGuid.ToString("D");
            default:
                var property = FindProperty(item, spec.Category, spec.Property);
                return property == null ? null : NavisValues.ToClrObject(property.Value);
        }
    }

    /// <summary>The name the tree shows for an item (its class name when it has none), as ModelItem.DisplayName gives.</summary>
    internal static string DisplayNameOf(ModelItem item)
    {
        var name = item.DisplayName;
        return string.IsNullOrEmpty(name) ? item.ClassDisplayName ?? string.Empty : name;
    }

    /// <summary>Every item of every model in the document, in one pass (the models' root items and all their descendants).</summary>
    internal static IEnumerable<ModelItem> AllItems(Document doc)
    {
        return doc.Models.RootItemDescendantsAndSelf;
    }

    /// <summary>The name of the model (file) an item belongs to: its root ancestor's display name, as ModelItem.ModelName gives.</summary>
    internal static string? ModelNameOf(ModelItem item)
    {
        var current = item;
        while (current.Parent != null)
        {
            current = current.Parent;
        }

        return ModelLabel(current);
    }

    /// <summary>The label of a model by its root item: the root's display name, else the model's file name (null when neither exists).</summary>
    internal static string? ModelLabel(ModelItem root)
    {
        var name = root.DisplayName;
        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }

        return root.HasModel && !string.IsNullOrEmpty(root.Model.FileName)
            ? System.IO.Path.GetFileName(root.Model.FileName)
            : null;
    }

    /// <summary>The value of the item's Item &gt; Layer property as text (null when it has none).</summary>
    internal static string? LayerOf(ModelItem item)
    {
        var property = item.PropertyCategories.FindPropertyByName(PropertyCategoryNames.Item, DataPropertyNames.ItemLayer);
        if (property == null)
        {
            return null;
        }

        var text = PropertyCatalog.ValueText(NavisValues.ToClrObject(property.Value)).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// The IFC GlobalId stored in an item's property data, or null when there is none. The IFC &gt; GlobalId property
    /// (what the Navisworks IFC import creates) is tried first; otherwise every property is scanned once for a name
    /// like GlobalId, IfcGUID / IFC GUID or Guid, the best-named one whose value is a 22-character GlobalId or a
    /// standard GUID (which is encoded) winning.
    /// </summary>
    internal static string? FindIfcGlobalId(ModelItem item)
    {
        var categories = item.PropertyCategories;
        var direct = categories.FindPropertyByDisplayName(IfcCategory, IfcGlobalIdProperty)
            ?? categories.FindPropertyByName(IfcCategory, IfcGlobalIdProperty);
        if (direct != null &&
            IfcGuidLookup.TryNormalize(NavisValues.ToClrObject(direct.Value) as string, out var directId))
        {
            return directId;
        }

        string? best = null;
        var bestRank = int.MaxValue;
        foreach (var category in categories)
        {
            foreach (var property in category.Properties)
            {
                var rank = IfcGuidLookup.Rank(property.DisplayName);
                if (rank == 0)
                {
                    rank = IfcGuidLookup.Rank(property.Name);
                }

                if (rank == 0 || rank >= bestRank)
                {
                    continue;
                }

                if (IfcGuidLookup.TryNormalize(NavisValues.ToClrObject(property.Value) as string, out var id))
                {
                    if (rank == 1)
                    {
                        return id;
                    }

                    best = id;
                    bestRank = rank;
                }
            }
        }

        return best;
    }
}
