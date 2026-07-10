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

    /// <summary>Finds every model item whose property display string matches a wildcard pattern.</summary>
    /// <param name="categoryName">Category display name (e.g. "Item"). Internal names do not match.</param>
    /// <param name="propertyName">Property display name (e.g. "Name"). Internal names do not match.</param>
    /// <param name="pattern">Wildcard pattern: * matches any text, ? matches one character (e.g. "*-L1-*").</param>
    /// <param name="document">The document to search (defaults to the active document).</param>
    /// <returns>All matching model items.</returns>
    [NodeName("Search.ByPropertyWildcard")]
    [NodeDescription("Finds every model item whose property text matches a wildcard pattern (* and ?).")]
    [NodeSearchTags("search", "find", "filter", "property", "wildcard", "pattern")]
    [return: NodeName("items")]
    public static List<ModelItem> ByPropertyWildcard(
        string categoryName,
        string propertyName,
        string pattern,
        Document? document = null)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            throw new ArgumentException("No wildcard pattern provided (use * and ?).", nameof(pattern));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var condition = BuildPropertyCondition(categoryName, propertyName)
            .DisplayStringWildcard(pattern);
        return RunSearch(doc, condition);
    }

    /// <summary>Finds every model item whose numeric property compares against a value.</summary>
    /// <param name="categoryName">Category display name (e.g. "Element"). Internal names do not match.</param>
    /// <param name="propertyName">Property display name (e.g. "Diameter"). Internal names do not match.</param>
    /// <param name="comparison">One of: &gt;, &gt;=, &lt;, &lt;= (or GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual).</param>
    /// <param name="value">The number to compare against, in document units.</param>
    /// <param name="document">The document to search (defaults to the active document).</param>
    /// <returns>All matching model items.</returns>
    [NodeName("Search.ByPropertyCompare")]
    [NodeDescription("Finds every model item whose numeric property is >, >=, < or <= a value (e.g. pipes with Diameter > 100).")]
    [NodeSearchTags("search", "find", "filter", "property", "compare", "greater", "less", "numeric")]
    [return: NodeName("items")]
    public static List<ModelItem> ByPropertyCompare(
        string categoryName,
        string propertyName,
        string comparison,
        double value,
        Document? document = null)
    {
        var comparisonKind = ParseComparison(comparison);
        var doc = NavisworksContext.ResolveDocument(document);
        var results = new List<ModelItem>();
        var seen = new HashSet<ModelItem>();

        // The variant's data type must equal the stored property's data type, so
        // the comparison runs once per plausible numeric storage type and the
        // results are unioned (same technique as Search.ByPropertyValue).
        var variants = new List<VariantData>();
        AddNumericVariants(variants, value);
        if (value >= int.MinValue && value <= int.MaxValue && value == Math.Floor(value))
        {
            variants.Add(VariantData.FromInt32((int)value));
        }

        foreach (var variant in variants)
        {
            var condition = BuildPropertyCondition(categoryName, propertyName)
                .CompareWith(comparisonKind, variant);
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

    /// <summary>Finds every model item that carries a property at all.</summary>
    /// <param name="categoryName">Category display name (e.g. "Element"). Internal names do not match.</param>
    /// <param name="propertyName">Property display name (e.g. "Level"). Internal names do not match.</param>
    /// <param name="document">The document to search (defaults to the active document).</param>
    /// <returns>All items carrying the property.</returns>
    [NodeName("Search.HasProperty")]
    [NodeDescription("Finds every model item that carries the property at all, regardless of value.")]
    [NodeSearchTags("search", "find", "has", "property", "exists", "audit")]
    [return: NodeName("items")]
    public static List<ModelItem> HasProperty(
        string categoryName,
        string propertyName,
        Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        return RunSearch(doc, BuildPropertyCondition(categoryName, propertyName));
    }

    /// <summary>Finds every model item that carries a property category (tab).</summary>
    /// <param name="categoryName">Category display name (e.g. "TimeLiner"). Internal names do not match.</param>
    /// <param name="document">The document to search (defaults to the active document).</param>
    /// <returns>All items carrying the category.</returns>
    [NodeName("Search.HasCategory")]
    [NodeDescription("Finds every model item that carries a property tab (e.g. every item with \"TimeLiner\" data).")]
    [NodeSearchTags("search", "find", "has", "category", "tab", "audit")]
    [return: NodeName("items")]
    public static List<ModelItem> HasCategory(string categoryName, Document? document = null)
    {
        if (string.IsNullOrEmpty(categoryName))
        {
            throw new ArgumentException("No property category name provided.", nameof(categoryName));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        return RunSearch(doc, SearchCondition.HasCategoryByDisplayName(categoryName));
    }

    /// <summary>Runs the property-equals test only inside the given items.</summary>
    /// <param name="items">The items (and their descendants) to search within.</param>
    /// <param name="categoryName">Category display name. Internal names do not match.</param>
    /// <param name="propertyName">Property display name. Internal names do not match.</param>
    /// <param name="value">The value to match (string, number, boolean or date).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The matching subset.</returns>
    [NodeName("Search.InItems")]
    [NodeDescription("Scoped search: finds items whose property equals the value, looking only inside the given items (chained refinement).")]
    [NodeSearchTags("search", "find", "scoped", "within", "refine", "subset")]
    [return: NodeName("items")]
    public static List<ModelItem> InItems(
        IEnumerable<ModelItem> items,
        string categoryName,
        string propertyName,
        object value,
        Document? document = null)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items), "No model items provided.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var scope = NavisValues.ToItemCollection(items);
        var results = new List<ModelItem>();
        var seen = new HashSet<ModelItem>();
        foreach (var variant in BuildEqualityVariants(value))
        {
            var search = new Search();
            search.Selection.CopyFrom(scope);
            search.Locations = SearchLocations.DescendantsAndSelf;
            search.SearchConditions.Add(BuildPropertyCondition(categoryName, propertyName).EqualValue(variant));

            // reportProgress must stay false: progress pumping can re-enter the host.
            foreach (var item in NavisValues.ToItemList(search.FindAll(doc, false)))
            {
                if (seen.Add(item))
                {
                    results.Add(item);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Builds a whole-model search whose conditions match the property equalling
    /// the value in any plausible storage type (the variants are OR-grouped).
    /// Used for live search sets, which must persist a single Search object.
    /// </summary>
    internal static Search CreateEqualitySearch(string categoryName, string propertyName, object value)
    {
        var search = new Search();
        search.Selection.SelectAll();
        search.Locations = SearchLocations.DescendantsAndSelf;

        var first = true;
        foreach (var variant in BuildEqualityVariants(value))
        {
            var condition = BuildPropertyCondition(categoryName, propertyName).EqualValue(variant);
            // Conditions are ANDed by default; StartGroup opens a new OR-group,
            // so each variant after the first becomes an OR alternative.
            search.SearchConditions.Add(first ? condition : condition.StartGroup());
            first = false;
        }

        return search;
    }

    /// <summary>
    /// Builds a whole-model search for the property equalling one exact variant
    /// (the variant already carries the property's true storage type).
    /// </summary>
    internal static Search CreateVariantEqualitySearch(string categoryName, string propertyName, VariantData variant)
    {
        var search = new Search();
        search.Selection.SelectAll();
        search.Locations = SearchLocations.DescendantsAndSelf;
        search.SearchConditions.Add(BuildPropertyCondition(categoryName, propertyName).EqualValue(variant));
        return search;
    }

    private static SearchConditionComparison ParseComparison(string? comparison)
    {
        switch ((comparison ?? string.Empty).Trim().ToLowerInvariant())
        {
            case ">":
            case "greaterthan":
                return SearchConditionComparison.NumericGreaterThan;
            case ">=":
            case "greaterthanorequal":
                return SearchConditionComparison.NumericGreaterThanOrEqual;
            case "<":
            case "lessthan":
                return SearchConditionComparison.NumericLessThan;
            case "<=":
            case "lessthanorequal":
                return SearchConditionComparison.NumericLessThanOrEqual;
            default:
                throw new ArgumentException(
                    "'" + comparison + "' is not a comparison. Use >, >=, < or <= " +
                    "(or GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual).", nameof(comparison));
        }
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
