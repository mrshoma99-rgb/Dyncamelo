using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>Nodes for traversing and inspecting model items.</summary>
[NodeCategory("Navisworks.ModelItem")]
public static class ModelItemNodes
{
    /// <summary>The direct children of a model item.</summary>
    /// <param name="item">The model item.</param>
    /// <returns>The item's direct children.</returns>
    [NodeName("ModelItem.Children")]
    [NodeDescription("The direct children of a model item.")]
    [NodeSearchTags("item", "children", "tree", "hierarchy")]
    [return: NodeName("children")]
    public static List<ModelItem> Children(ModelItem item)
    {
        return NavisValues.ToItemList(RequireItem(item).Children);
    }

    /// <summary>All descendants of a model item (children, grandchildren, ...).</summary>
    /// <param name="item">The model item.</param>
    /// <returns>Every descendant, depth first.</returns>
    [NodeName("ModelItem.Descendants")]
    [NodeDescription("All descendants of a model item (the whole subtree below it).")]
    [NodeSearchTags("item", "descendants", "tree", "subtree", "all")]
    [return: NodeName("descendants")]
    public static List<ModelItem> Descendants(ModelItem item)
    {
        return NavisValues.ToItemList(RequireItem(item).Descendants);
    }

    /// <summary>The display name of a model item.</summary>
    /// <param name="item">The model item.</param>
    /// <returns>The item's display name; falls back to its class display name when unnamed.</returns>
    [NodeName("ModelItem.DisplayName")]
    [NodeDescription("The display name of a model item (falls back to its class name when unnamed).")]
    [NodeSearchTags("item", "name", "displayname", "label")]
    [return: NodeName("name")]
    public static string DisplayName(ModelItem item)
    {
        var modelItem = RequireItem(item);

        // Anonymous geometry nodes frequently have an empty DisplayName.
        var name = modelItem.DisplayName;
        return string.IsNullOrEmpty(name) ? modelItem.ClassDisplayName ?? string.Empty : name;
    }

    /// <summary>Whether a model item carries geometry.</summary>
    /// <param name="item">The model item.</param>
    /// <returns>True when the item has geometry.</returns>
    [NodeName("ModelItem.HasGeometry")]
    [NodeDescription("True when the model item carries geometry.")]
    [NodeSearchTags("item", "geometry", "solid", "mesh")]
    [return: NodeName("hasGeometry")]
    public static bool HasGeometry(ModelItem item)
    {
        return RequireItem(item).HasGeometry;
    }

    /// <summary>The axis-aligned bounding box of a model item.</summary>
    /// <param name="item">The model item.</param>
    /// <param name="ignoreHidden">True to exclude hidden geometry from the box.</param>
    /// <returns>The item's bounding box, in document units.</returns>
    [NodeName("ModelItem.BoundingBox")]
    [NodeDescription("The axis-aligned bounding box of a model item, in document units.")]
    [NodeSearchTags("item", "boundingbox", "bounds", "extents", "bbox")]
    [return: NodeName("boundingBox")]
    public static BoundingBox3D BoundingBox(ModelItem item, bool ignoreHidden = false)
    {
        return RequireItem(item).BoundingBox(ignoreHidden);
    }

    /// <summary>The parent of a model item.</summary>
    /// <param name="item">The model item.</param>
    /// <returns>The parent item, or null for a model root.</returns>
    [NodeName("ModelItem.Parent")]
    [NodeDescription("The parent of a model item (null for a model root).")]
    [NodeSearchTags("item", "parent", "tree", "hierarchy", "up")]
    [return: NodeName("parent")]
    public static ModelItem? Parent(ModelItem item)
    {
        return RequireItem(item).Parent;
    }

    /// <summary>The chain of parents of a model item, up to its model root.</summary>
    /// <param name="item">The model item.</param>
    /// <param name="includeSelf">True to include the item itself.</param>
    /// <returns>The ancestors (nearest first).</returns>
    [NodeName("ModelItem.Ancestors")]
    [NodeDescription("The chain of parents of a model item, up to its model root.")]
    [NodeSearchTags("item", "ancestors", "parents", "tree", "hierarchy")]
    [return: NodeName("ancestors")]
    public static List<ModelItem> Ancestors(ModelItem item, bool includeSelf = false)
    {
        var modelItem = RequireItem(item);
        return NavisValues.ToItemList(includeSelf ? modelItem.AncestorsAndSelf : modelItem.Ancestors);
    }

    /// <summary>The whole object/element a model item belongs to.</summary>
    /// <param name="item">The model item (often a geometry leaf deep in the tree).</param>
    /// <returns>The nearest ancestor flagged as a composite object (the "element" in the tree); the item itself when it is already an object.</returns>
    [NodeName("ModelItem.ObjectAncestor")]
    [NodeDescription("Walks up the selection tree to the whole object/element a geometry item belongs to (Navisworks' first composite-object ancestor) — the item you usually want to name, colour or tag. Returns the item itself when it is already an object.")]
    [NodeSearchTags("item", "object", "element", "ancestor", "parent", "composite", "tree", "up")]
    [return: NodeName("object")]
    public static ModelItem ObjectAncestor(ModelItem item)
    {
        var modelItem = RequireItem(item);
        return modelItem.FindFirstObjectAncestor() ?? modelItem;
    }

    /// <summary>The class names of a model item.</summary>
    /// <param name="item">The model item.</param>
    /// <returns>The internal and localized class names (layer/group/geometry detection).</returns>
    [NodeName("ModelItem.ClassInfo")]
    [NodeDescription("The internal and localized class names of a model item (layer/group/geometry detection).")]
    [NodeSearchTags("item", "class", "classname", "type", "kind")]
    [MultiReturn("className", "classDisplayName")]
    public static Dictionary<string, object?> ClassInfo(ModelItem item)
    {
        var modelItem = RequireItem(item);
        return new Dictionary<string, object?>
        {
            ["className"] = modelItem.ClassName,
            ["classDisplayName"] = modelItem.ClassDisplayName,
        };
    }

    /// <summary>Whether a model item is currently hidden.</summary>
    /// <param name="item">The model item.</param>
    /// <returns>True when the item is hidden.</returns>
    [NodeName("ModelItem.IsHidden")]
    [NodeDescription("True when the model item is currently hidden in the viewport.")]
    [NodeSearchTags("item", "hidden", "visible", "state")]
    [return: NodeName("isHidden")]
    public static bool IsHidden(ModelItem item)
    {
        return RequireItem(item).IsHidden;
    }

    /// <summary>The stable instance GUID of a model item.</summary>
    /// <param name="item">The model item.</param>
    /// <returns>The GUID string, or "" when the item has none — cross-run identity for reports.</returns>
    [NodeName("ModelItem.InstanceGuid")]
    [NodeDescription("The stable instance GUID of a model item (\"\" when absent) — cross-run identity for reports.")]
    [NodeSearchTags("item", "guid", "id", "identity", "instance")]
    [return: NodeName("guid")]
    public static string InstanceGuid(ModelItem item)
    {
        var guid = RequireItem(item).InstanceGuid;
        return guid == Guid.Empty ? string.Empty : guid.ToString();
    }

    /// <summary>Flattens items to their unique geometry-bearing descendants.</summary>
    /// <param name="items">The model items to flatten.</param>
    /// <returns>The unique geometry leaves — the items QTO, coloring and clash selections actually want.</returns>
    [NodeName("ModelItem.GeometryLeaves")]
    [NodeDescription("Flattens items to their unique geometry-bearing descendants (the items QTO and coloring actually want).")]
    [NodeSearchTags("item", "geometry", "leaves", "flatten", "descendants")]
    [return: NodeName("leaves")]
    public static List<ModelItem> GeometryLeaves(IEnumerable<ModelItem> items)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items), "No model items provided.");
        }

        var leaves = new List<ModelItem>();
        var seen = new ModelItemSet();
        foreach (var item in items)
        {
            if (item == null)
            {
                continue;
            }

            foreach (var descendant in item.DescendantsAndSelf)
            {
                if (descendant.HasGeometry && seen.Add(descendant))
                {
                    leaves.Add(descendant);
                }
            }
        }

        return leaves;
    }

    private static ModelItem RequireItem(ModelItem? item)
    {
        return item ?? throw new ArgumentNullException(nameof(item), "No model item provided.");
    }
}
