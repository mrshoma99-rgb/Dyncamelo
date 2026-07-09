using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>Nodes for reading model item properties.</summary>
[NodeCategory("Navisworks.Properties")]
public static class PropertyNodes
{
    /// <summary>Reads a property value from a model item.</summary>
    /// <param name="item">The model item.</param>
    /// <param name="categoryName">Category display name (e.g. "Element", "Item"); internal names also match.</param>
    /// <param name="propertyName">Property display name (e.g. "Category", "Name"); internal names also match.</param>
    /// <returns>The property value as a plain .NET value, or null when the item lacks the property.</returns>
    [NodeName("Properties.Value")]
    [NodeDescription("Reads a property value from a model item, converted to a plain value. Returns null when the property is absent.")]
    [NodeSearchTags("property", "value", "parameter", "attribute", "data")]
    [return: NodeName("value")]
    public static object? Value(ModelItem item, string categoryName, string propertyName)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item), "No model item provided.");
        }

        if (string.IsNullOrEmpty(categoryName))
        {
            throw new ArgumentException("No property category name provided.", nameof(categoryName));
        }

        if (string.IsNullOrEmpty(propertyName))
        {
            throw new ArgumentException("No property name provided.", nameof(propertyName));
        }

        // Prefer the localized display names users see in the Properties window,
        // then fall back to internal names for script-stable lookups.
        var property = item.PropertyCategories.FindPropertyByDisplayName(categoryName, propertyName)
            ?? item.PropertyCategories.FindPropertyByName(categoryName, propertyName);

        return property == null ? null : NavisValues.ToClrObject(property.Value);
    }

    /// <summary>Lists the property categories available on a model item.</summary>
    /// <param name="item">The model item.</param>
    /// <returns>The category display names.</returns>
    [NodeName("Properties.Categories")]
    [NodeDescription("The property category names available on a model item.")]
    [NodeSearchTags("property", "categories", "tabs", "groups")]
    [return: NodeName("categories")]
    public static List<string> Categories(ModelItem item)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item), "No model item provided.");
        }

        var names = new List<string>();
        foreach (var category in item.PropertyCategories)
        {
            names.Add(category.DisplayName);
        }

        return names;
    }
}
