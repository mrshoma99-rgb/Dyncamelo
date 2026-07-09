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

    private static ModelItem RequireItem(ModelItem? item)
    {
        return item ?? throw new ArgumentNullException(nameof(item), "No model item provided.");
    }
}
