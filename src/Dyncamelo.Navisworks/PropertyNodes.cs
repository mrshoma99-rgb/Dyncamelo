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

    /// <summary>All property names and values inside one category of an item.</summary>
    /// <param name="item">The model item.</param>
    /// <param name="categoryName">Category display name (e.g. "Element"); internal names also match.</param>
    /// <returns>Index-aligned property names and plain values (empty lists when the category is absent).</returns>
    [NodeName("Properties.InCategory")]
    [NodeDescription("All property names and values inside one category of an item. Returns empty lists when the item lacks the category.")]
    [NodeSearchTags("property", "category", "all", "names", "values", "tab")]
    [MultiReturn("names", "values")]
    public static Dictionary<string, object?> InCategory(ModelItem item, string categoryName)
    {
        var modelItem = RequireItem(item);
        if (string.IsNullOrEmpty(categoryName))
        {
            throw new ArgumentException("No property category name provided.", nameof(categoryName));
        }

        var names = new List<string>();
        var values = new List<object?>();
        var category = modelItem.PropertyCategories.FindCategoryByDisplayName(categoryName)
            ?? modelItem.PropertyCategories.FindCategoryByName(categoryName);
        if (category != null)
        {
            foreach (var property in category.Properties)
            {
                names.Add(property.DisplayName);
                values.Add(NavisValues.ToClrObject(property.Value));
            }
        }

        return new Dictionary<string, object?>
        {
            ["names"] = names,
            ["values"] = values,
        };
    }

    /// <summary>Reads a property value as display text.</summary>
    /// <param name="item">The model item.</param>
    /// <param name="categoryName">Category display name; internal names also match.</param>
    /// <param name="propertyName">Property display name; internal names also match.</param>
    /// <returns>The value as text ("" when the property is absent).</returns>
    [NodeName("Properties.ValueAsString")]
    [NodeDescription("Reads a property value as text. Returns \"\" when the property is absent.")]
    [NodeSearchTags("property", "value", "string", "text", "display")]
    [return: NodeName("text")]
    public static string ValueAsString(ModelItem item, string categoryName, string propertyName)
    {
        var value = Value(item, categoryName, propertyName);
        if (value == null)
        {
            return string.Empty;
        }

        return value is IFormattable formattable
            ? formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString() ?? string.Empty;
    }

    /// <summary>Whether a model item carries a property.</summary>
    /// <param name="item">The model item.</param>
    /// <param name="categoryName">Category display name; internal names also match.</param>
    /// <param name="propertyName">Property display name; internal names also match.</param>
    /// <returns>True when the property exists on the item — drives model-QA "missing data" masks.</returns>
    [NodeName("Properties.HasProperty")]
    [NodeDescription("True when the item carries the property — drives model-QA missing-data masks.")]
    [NodeSearchTags("property", "has", "exists", "qa", "missing")]
    [return: NodeName("hasProperty")]
    public static bool HasProperty(ModelItem item, string categoryName, string propertyName)
    {
        var modelItem = RequireItem(item);
        if (string.IsNullOrEmpty(categoryName))
        {
            throw new ArgumentException("No property category name provided.", nameof(categoryName));
        }

        if (string.IsNullOrEmpty(propertyName))
        {
            throw new ArgumentException("No property name provided.", nameof(propertyName));
        }

        return modelItem.PropertyCategories.FindPropertyByDisplayName(categoryName, propertyName) != null
            || modelItem.PropertyCategories.FindPropertyByName(categoryName, propertyName) != null;
    }

    /// <summary>Every property of an item flattened into one dictionary.</summary>
    /// <param name="item">The model item.</param>
    /// <returns>A "Category.Property" → value dictionary (full data dump / JSON export).</returns>
    [NodeName("Properties.AsDictionary")]
    [NodeDescription("Every property of an item flattened to a \"Category.Property\" → value dictionary (full data dump).")]
    [NodeSearchTags("property", "dictionary", "all", "dump", "json", "export")]
    [return: NodeName("properties")]
    public static Dictionary<string, object?> AsDictionary(ModelItem item)
    {
        var modelItem = RequireItem(item);
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var category in modelItem.PropertyCategories)
        {
            foreach (var property in category.Properties)
            {
                var key = category.DisplayName + "." + property.DisplayName;
                if (!result.ContainsKey(key))
                {
                    result[key] = NavisValues.ToClrObject(property.Value);
                }
            }
        }

        return result;
    }

    /// <summary>Deconstructs a raw data property.</summary>
    /// <param name="property">The data property (e.g. from Properties.InCategory workflows).</param>
    /// <returns>Internal name, display name, and the plain value.</returns>
    [NodeName("Property.Info")]
    [NodeDescription("The internal name, display name and plain value of a raw data property.")]
    [NodeSearchTags("property", "info", "name", "displayname", "raw")]
    [MultiReturn("name", "displayName", "value")]
    public static Dictionary<string, object?> Info(DataProperty property)
    {
        if (property == null)
        {
            throw new ArgumentNullException(nameof(property), "No data property provided.");
        }

        return new Dictionary<string, object?>
        {
            ["name"] = property.Name,
            ["displayName"] = property.DisplayName,
            ["value"] = NavisValues.ToClrObject(property.Value),
        };
    }

    private static ModelItem RequireItem(ModelItem? item)
    {
        return item ?? throw new ArgumentNullException(nameof(item), "No model item provided.");
    }
}
