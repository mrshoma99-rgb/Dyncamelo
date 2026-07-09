using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>Nodes that find model items by property, like the Find Items window.</summary>
[NodeCategory("Navisworks.Search")]
public static class SearchNodes
{
    /// <summary>Finds every model item whose property equals a value.</summary>
    /// <param name="categoryName">Category display name (e.g. "Element"). Internal names do not match.</param>
    /// <param name="propertyName">Property display name (e.g. "Category"). Internal names do not match.</param>
    /// <param name="value">The value to match (string, number, boolean or date). Numbers match plain, length, area, volume and angle properties; strings match display and identifier strings.</param>
    /// <param name="document">The document to search (defaults to the active document).</param>
    /// <returns>All matching model items.</returns>
    [NodeName("Search.ByPropertyValue")]
    [NodeDescription("Finds every model item whose property exactly equals the given value.")]
    [NodeSearchTags("search", "find", "filter", "property", "equals", "query")]
    [return: NodeName("items")]
    public static List<ModelItem> ByPropertyValue(
        string categoryName,
        string propertyName,
        object value,
        Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var results = new List<ModelItem>();
        var seen = new HashSet<ModelItem>();
        foreach (var variant in BuildEqualityVariants(value))
        {
            var condition = BuildPropertyCondition(categoryName, propertyName).EqualValue(variant);
            foreach (var item in RunSearch(doc, condition))
            {
                if (seen.Add(item))
                {
                    results.Add(item);
                }
            }
        }

        return results;
    }

    /// <summary>Finds every model item whose property display string contains a substring.</summary>
    /// <param name="categoryName">Category display name (e.g. "Item"). Internal names do not match.</param>
    /// <param name="propertyName">Property display name (e.g. "Name"). Internal names do not match.</param>
    /// <param name="value">The substring to look for (case sensitive, like Find Items).</param>
    /// <param name="document">The document to search (defaults to the active document).</param>
    /// <returns>All matching model items.</returns>
    [NodeName("Search.ByPropertyContains")]
    [NodeDescription("Finds every model item whose property text contains the given substring.")]
    [NodeSearchTags("search", "find", "filter", "property", "contains", "text")]
    [return: NodeName("items")]
    public static List<ModelItem> ByPropertyContains(
        string categoryName,
        string propertyName,
        string value,
        Document? document = null)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value), "No search text provided.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var condition = BuildPropertyCondition(categoryName, propertyName)
            .DisplayStringContains(value);
        return RunSearch(doc, condition);
    }

    /// <summary>
    /// Navisworks search equality only matches when the variant's data type equals
    /// the stored property's data type, so one plain value is expanded into every
    /// variant type it could plausibly be stored as: numbers as plain double plus
    /// the measured types (length, area, volume, angle; integers additionally as
    /// Int32), strings as display and identifier strings. The caller unions the
    /// per-variant search results.
    /// </summary>
    private static List<VariantData> BuildEqualityVariants(object? value)
    {
        var variants = new List<VariantData>();
        switch (value)
        {
            case null:
                variants.Add(VariantData.FromNone());
                break;
            case string text:
                variants.Add(VariantData.FromDisplayString(text));
                variants.Add(VariantData.FromIdentifierString(text));
                break;
            case double d:
                AddNumericVariants(variants, d);
                break;
            case float f:
                AddNumericVariants(variants, f);
                break;
            case decimal m:
                AddNumericVariants(variants, (double)m);
                break;
            case int i:
                variants.Add(VariantData.FromInt32(i));
                AddNumericVariants(variants, i);
                break;
            case long l:
                if (l >= int.MinValue && l <= int.MaxValue)
                {
                    variants.Add(VariantData.FromInt32((int)l));
                }

                AddNumericVariants(variants, l);
                break;
            default:
                variants.Add(NavisValues.ToVariant(value));
                break;
        }

        return variants;
    }

    private static void AddNumericVariants(List<VariantData> variants, double value)
    {
        variants.Add(VariantData.FromDouble(value));
        variants.Add(VariantData.FromDoubleLength(value));
        variants.Add(VariantData.FromDoubleArea(value));
        variants.Add(VariantData.FromDoubleVolume(value));
        variants.Add(VariantData.FromDoubleAngle(value));
    }

    private static SearchCondition BuildPropertyCondition(string categoryName, string propertyName)
    {
        if (string.IsNullOrEmpty(categoryName))
        {
            throw new ArgumentException("No property category name provided.", nameof(categoryName));
        }

        if (string.IsNullOrEmpty(propertyName))
        {
            throw new ArgumentException("No property name provided.", nameof(propertyName));
        }

        return SearchCondition.HasPropertyByDisplayName(categoryName, propertyName);
    }

    private static List<ModelItem> RunSearch(Document doc, SearchCondition condition)
    {
        var search = new Search();
        search.Selection.SelectAll();
        search.Locations = SearchLocations.DescendantsAndSelf;
        search.SearchConditions.Add(condition);

        // reportProgress must stay false: progress pumping can re-enter the host.
        return NavisValues.ToItemList(search.FindAll(doc, false));
    }
}
