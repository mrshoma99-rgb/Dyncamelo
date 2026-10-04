using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;

namespace CamelGraph.Navisworks;

/// <summary>Nodes that find model items by property, like the Find Items window.</summary>
[NodeCategory("Navisworks.Search")]
public static class SearchNodes
{
    /// <summary>Finds every model item by one property: equal to a value, containing text, matching a wildcard or compared with a number.</summary>
    /// <param name="categoryName">Category display name (e.g. "Element"). Internal names do not match.</param>
    /// <param name="propertyName">Property display name (e.g. "Category"). Internal names do not match.</param>
    /// <param name="value">What to look for: any value for "equals"; text for "contains" and "wildcard" (* matches any text, ? one character); a number for &gt;, &gt;=, &lt; and &lt;=.</param>
    /// <param name="mode">How the property is matched: equals, contains, wildcard, &gt;, &gt;=, &lt; or &lt;=.</param>
    /// <param name="resolveTo">Optional selection resolution applied to the matches, like Options &gt; Interface &gt; Selection &gt; Resolution in Navisworks: Self (default, keep matches as found), File, Layer, FirstObject, LastObject, LastUnique or Geometry.</param>
    /// <param name="document">The document to search (defaults to the active document).</param>
    /// <returns>All matching model items.</returns>
    [NodeName("Search.ByProperty")]
    [NodeDescription("Finds every model item by one property, like Find Items with its condition drop-down: equals a value, contains text, matches a wildcard pattern (* and ?), or is >, >=, < or <= a number (e.g. pipes with Diameter > 100).")]
    [NodeSearchTags("search", "find", "filter", "property", "equals", "contains", "wildcard", "pattern", "compare", "greater", "less", "numeric", "text", "query")]
    [return: NodeName("items")]
    public static List<ModelItem> ByProperty(
        [NodeTabChoice(NodeDataSource.Selection)] string categoryName,
        [NodePropertyChoice(NodeDataSource.Selection, "categoryName")] string propertyName,
        object value,
        [NodeChoices("equals", "contains", "wildcard", ">", ">=", "<", "<=")]
        string mode = "equals",
        [NodeChoices("Self", "File", "Layer", "FirstObject", "LastObject", "LastUnique", "Geometry")]
        string resolveTo = "Self",
        Document? document = null)
    {
        var chosen = (mode ?? string.Empty).Trim();
        switch (chosen.ToLowerInvariant())
        {
            case "":
            case "equals":
                return ByPropertyValue(categoryName, propertyName, value, resolveTo, document);
            case "contains":
                return ByPropertyContains(categoryName, propertyName, ValueAsText(value, "contains"), resolveTo, document);
            case "wildcard":
                return ByPropertyWildcard(categoryName, propertyName, ValueAsText(value, "wildcard"), resolveTo, document);
            case ">":
            case ">=":
            case "<":
            case "<=":
                return ByPropertyCompare(categoryName, propertyName, chosen, ValueAsNumber(value, chosen), resolveTo, document);
            default:
                throw new ArgumentException(
                    "Unknown mode '" + mode + "'. Use equals, contains, wildcard, >, >=, < or <=.", nameof(mode));
        }
    }

    /// <summary>Finds every model item whose property equals a value.</summary>
    /// <param name="categoryName">Category display name (e.g. "Element"). Internal names do not match.</param>
    /// <param name="propertyName">Property display name (e.g. "Category"). Internal names do not match.</param>
    /// <param name="value">The value to match (string, number, boolean or date). Numbers match plain, length, area, volume and angle properties; strings match display and identifier strings.</param>
    /// <param name="resolveTo">Optional selection resolution applied to the matches, like Options &gt; Interface &gt; Selection &gt; Resolution in Navisworks: Self (default, keep matches as found), File, Layer, FirstObject, LastObject, LastUnique or Geometry.</param>
    /// <param name="document">The document to search (defaults to the active document).</param>
    /// <returns>All matching model items.</returns>
    [NodeName("Search.ByPropertyValue")]
    [NodeDeprecated("Search.ByProperty")]
    [NodeDescription("Finds every model item whose property exactly equals the given value.")]
    [NodeSearchTags("search", "find", "filter", "property", "equals", "query")]
    // Pre-0.4 id (before the optional resolveTo parameter was appended).
    [NodeAliases("CamelGraph.Navisworks.SearchNodes.ByPropertyValue@string,string,object,Autodesk.Navisworks.Api.Document")]
    [return: NodeName("items")]
    public static List<ModelItem> ByPropertyValue(
        string categoryName,
        string propertyName,
        object value,
        [NodeChoices("Self", "File", "Layer", "FirstObject", "LastObject", "LastUnique", "Geometry")]
        string resolveTo = "Self",
        Document? document = null)
    {
        var level = SelectionLevels.Parse(resolveTo);
        var doc = NavisworksContext.ResolveDocument(document);
        return ResolveFound(RunEqualitySearch(doc, null, categoryName, propertyName, value), level);
    }

    /// <summary>Finds every model item whose property display string contains a substring.</summary>
    /// <param name="categoryName">Category display name (e.g. "Item"). Internal names do not match.</param>
    /// <param name="propertyName">Property display name (e.g. "Name"). Internal names do not match.</param>
    /// <param name="value">The substring to look for (case sensitive, like Find Items).</param>
    /// <param name="resolveTo">Optional selection resolution applied to the matches, like Options &gt; Interface &gt; Selection &gt; Resolution in Navisworks: Self (default, keep matches as found), File, Layer, FirstObject, LastObject, LastUnique or Geometry.</param>
    /// <param name="document">The document to search (defaults to the active document).</param>
    /// <returns>All matching model items.</returns>
    [NodeName("Search.ByPropertyContains")]
    [NodeDeprecated("Search.ByProperty")]
    [NodeDescription("Finds every model item whose property text contains the given substring.")]
    [NodeSearchTags("search", "find", "filter", "property", "contains", "text")]
    // Pre-0.4 id (before the optional resolveTo parameter was appended).
    [NodeAliases("CamelGraph.Navisworks.SearchNodes.ByPropertyContains@string,string,string,Autodesk.Navisworks.Api.Document")]
    [return: NodeName("items")]
    public static List<ModelItem> ByPropertyContains(
        string categoryName,
        string propertyName,
        string value,
        [NodeChoices("Self", "File", "Layer", "FirstObject", "LastObject", "LastUnique", "Geometry")]
        string resolveTo = "Self",
        Document? document = null)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value), "No search text provided.");
        }

        var level = SelectionLevels.Parse(resolveTo);
        var doc = NavisworksContext.ResolveDocument(document);
        var condition = BuildPropertyCondition(categoryName, propertyName)
            .DisplayStringContains(value);
        return ResolveFound(RunSearch(doc, condition), level);
    }

    /// <summary>Finds every model item whose property display string matches a wildcard pattern.</summary>
    /// <param name="categoryName">Category display name (e.g. "Item"). Internal names do not match.</param>
    /// <param name="propertyName">Property display name (e.g. "Name"). Internal names do not match.</param>
    /// <param name="pattern">Wildcard pattern: * matches any text, ? matches one character (e.g. "*-L1-*").</param>
    /// <param name="resolveTo">Optional selection resolution applied to the matches, like Options &gt; Interface &gt; Selection &gt; Resolution in Navisworks: Self (default, keep matches as found), File, Layer, FirstObject, LastObject, LastUnique or Geometry.</param>
    /// <param name="document">The document to search (defaults to the active document).</param>
    /// <returns>All matching model items.</returns>
    [NodeName("Search.ByPropertyWildcard")]
    [NodeDeprecated("Search.ByProperty")]
    [NodeDescription("Finds every model item whose property text matches a wildcard pattern (* and ?).")]
    [NodeSearchTags("search", "find", "filter", "property", "wildcard", "pattern")]
    // Pre-0.4 id (before the optional resolveTo parameter was appended).
    [NodeAliases("CamelGraph.Navisworks.SearchNodes.ByPropertyWildcard@string,string,string,Autodesk.Navisworks.Api.Document")]
    [return: NodeName("items")]
    public static List<ModelItem> ByPropertyWildcard(
        string categoryName,
        string propertyName,
        string pattern,
        [NodeChoices("Self", "File", "Layer", "FirstObject", "LastObject", "LastUnique", "Geometry")]
        string resolveTo = "Self",
        Document? document = null)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            throw new ArgumentException("No wildcard pattern provided (use * and ?).", nameof(pattern));
        }

        var level = SelectionLevels.Parse(resolveTo);
        var doc = NavisworksContext.ResolveDocument(document);
        var condition = BuildPropertyCondition(categoryName, propertyName)
            .DisplayStringWildcard(pattern);
        return ResolveFound(RunSearch(doc, condition), level);
    }

    /// <summary>Finds every model item whose numeric property compares against a value.</summary>
    /// <param name="categoryName">Category display name (e.g. "Element"). Internal names do not match.</param>
    /// <param name="propertyName">Property display name (e.g. "Diameter"). Internal names do not match.</param>
    /// <param name="comparison">One of: &gt;, &gt;=, &lt;, &lt;= (or GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual).</param>
    /// <param name="value">The number to compare against, in document units.</param>
    /// <param name="resolveTo">Optional selection resolution applied to the matches, like Options &gt; Interface &gt; Selection &gt; Resolution in Navisworks: Self (default, keep matches as found), File, Layer, FirstObject, LastObject, LastUnique or Geometry.</param>
    /// <param name="document">The document to search (defaults to the active document).</param>
    /// <returns>All matching model items.</returns>
    [NodeName("Search.ByPropertyCompare")]
    [NodeDeprecated("Search.ByProperty")]
    [NodeDescription("Finds every model item whose numeric property is >, >=, < or <= a value (e.g. pipes with Diameter > 100).")]
    [NodeSearchTags("search", "find", "filter", "property", "compare", "greater", "less", "numeric")]
    // Pre-0.4 id (before the optional resolveTo parameter was appended).
    [NodeAliases("CamelGraph.Navisworks.SearchNodes.ByPropertyCompare@string,string,string,double,Autodesk.Navisworks.Api.Document")]
    [return: NodeName("items")]
    public static List<ModelItem> ByPropertyCompare(
        string categoryName,
        string propertyName,
        [NodeChoices(">", ">=", "<", "<=")]
        string comparison,
        double value,
        [NodeChoices("Self", "File", "Layer", "FirstObject", "LastObject", "LastUnique", "Geometry")]
        string resolveTo = "Self",
        Document? document = null)
    {
        var level = SelectionLevels.Parse(resolveTo);
        var comparisonKind = ParseComparison(comparison);
        var doc = NavisworksContext.ResolveDocument(document);

        // The variant's data type must equal the stored property's data type, so the comparison is tried against every plausible
        // numeric storage type (as alternatives of ONE search, see RunAlternativesSearch).
        var variants = new List<VariantData>();
        AddNumericVariants(variants, value);
        if (value >= int.MinValue && value <= int.MaxValue && value == Math.Floor(value))
        {
            variants.Add(VariantData.FromInt32((int)value));
        }

        var found = RunAlternativesSearch(doc, null, categoryName, propertyName, variants, (condition, variant) => condition.CompareWith(comparisonKind, variant));
        return ResolveFound(found, level);
    }

    /// <summary>Finds every model item that carries a property at all.</summary>
    /// <param name="categoryName">Category display name (e.g. "Element"). Internal names do not match.</param>
    /// <param name="propertyName">Property display name (e.g. "Level"). Internal names do not match.</param>
    /// <param name="resolveTo">Optional selection resolution applied to the matches, like Options &gt; Interface &gt; Selection &gt; Resolution in Navisworks: Self (default, keep matches as found), File, Layer, FirstObject, LastObject, LastUnique or Geometry.</param>
    /// <param name="document">The document to search (defaults to the active document).</param>
    /// <returns>All items carrying the property.</returns>
    [NodeName("Search.HasProperty")]
    [NodeDescription("Finds every model item that carries the property at all, regardless of value.")]
    [NodeSearchTags("search", "find", "has", "property", "exists", "audit")]
    // Pre-0.4 id (before the optional resolveTo parameter was appended).
    [NodeAliases("CamelGraph.Navisworks.SearchNodes.HasProperty@string,string,Autodesk.Navisworks.Api.Document")]
    [return: NodeName("items")]
    public static List<ModelItem> HasProperty(
        [NodeTabChoice(NodeDataSource.Selection)] string categoryName,
        [NodePropertyChoice(NodeDataSource.Selection, "categoryName")] string propertyName,
        [NodeChoices("Self", "File", "Layer", "FirstObject", "LastObject", "LastUnique", "Geometry")]
        string resolveTo = "Self",
        Document? document = null)
    {
        var level = SelectionLevels.Parse(resolveTo);
        var doc = NavisworksContext.ResolveDocument(document);
        return ResolveFound(RunSearch(doc, BuildPropertyCondition(categoryName, propertyName)), level);
    }

    /// <summary>Finds every model item that carries a property category (tab).</summary>
    /// <param name="categoryName">Category display name (e.g. "TimeLiner"). Internal names do not match.</param>
    /// <param name="resolveTo">Optional selection resolution applied to the matches, like Options &gt; Interface &gt; Selection &gt; Resolution in Navisworks: Self (default, keep matches as found), File, Layer, FirstObject, LastObject, LastUnique or Geometry.</param>
    /// <param name="document">The document to search (defaults to the active document).</param>
    /// <returns>All items carrying the category.</returns>
    [NodeName("Search.HasCategory")]
    [NodeDescription("Finds every model item that carries a property tab (e.g. every item with \"TimeLiner\" data).")]
    [NodeSearchTags("search", "find", "has", "category", "tab", "audit")]
    // Pre-0.4 id (before the optional resolveTo parameter was appended).
    [NodeAliases("CamelGraph.Navisworks.SearchNodes.HasCategory@string,Autodesk.Navisworks.Api.Document")]
    [return: NodeName("items")]
    public static List<ModelItem> HasCategory(
        [NodeTabChoice(NodeDataSource.Selection)] string categoryName,
        [NodeChoices("Self", "File", "Layer", "FirstObject", "LastObject", "LastUnique", "Geometry")]
        string resolveTo = "Self",
        Document? document = null)
    {
        if (string.IsNullOrEmpty(categoryName))
        {
            throw new ArgumentException("No property category name provided.", nameof(categoryName));
        }

        var level = SelectionLevels.Parse(resolveTo);
        var doc = NavisworksContext.ResolveDocument(document);
        return ResolveFound(RunSearch(doc, SearchCondition.HasCategoryByDisplayName(categoryName)), level);
    }

    /// <summary>Runs the property-equals test only inside the given items.</summary>
    /// <param name="items">The items (and their descendants) to search within.</param>
    /// <param name="categoryName">Category display name. Internal names do not match.</param>
    /// <param name="propertyName">Property display name. Internal names do not match.</param>
    /// <param name="value">The value to match (string, number, boolean or date).</param>
    /// <param name="resolveTo">Optional selection resolution applied to the matches, like Options &gt; Interface &gt; Selection &gt; Resolution in Navisworks: Self (default, keep matches as found), File, Layer, FirstObject, LastObject, LastUnique or Geometry.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The matching subset.</returns>
    [NodeName("Search.InItems")]
    [NodeDescription("Scoped search: finds items whose property equals the value, looking only inside the given items (chained refinement).")]
    [NodeSearchTags("search", "find", "scoped", "within", "refine", "subset")]
    // Pre-0.4 id (before the optional resolveTo parameter was appended).
    [NodeAliases("CamelGraph.Navisworks.SearchNodes.InItems@System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>,string,string,object,Autodesk.Navisworks.Api.Document")]
    [return: NodeName("items")]
    public static List<ModelItem> InItems(
        [MultiInput] IEnumerable<ModelItem> items,
        [NodeTabChoice("items")] string categoryName,
        [NodePropertyChoice("items", "categoryName")] string propertyName,
        object value,
        [NodeChoices("Self", "File", "Layer", "FirstObject", "LastObject", "LastUnique", "Geometry")]
        string resolveTo = "Self",
        Document? document = null)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items), "No model items provided.");
        }

        var level = SelectionLevels.Parse(resolveTo);
        var doc = NavisworksContext.ResolveDocument(document);
        return ResolveFound(RunEqualitySearch(doc, NavisValues.ToItemCollection(items), categoryName, propertyName, value), level);
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
        AddAlternatives(search, categoryName, propertyName, BuildEqualityVariants(value), (condition, variant) => condition.EqualValue(variant));
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

    private static string ValueAsText(object? value, string mode)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value), "No search text provided for mode '" + mode + "'.");
        }

        return value as string ?? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static double ValueAsNumber(object? value, string mode)
    {
        switch (value)
        {
            case null:
                throw new ArgumentNullException(nameof(value), "No number provided for mode '" + mode + "'.");
            case double d:
                return d;
            case string text when double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed):
                return parsed;
            case IConvertible convertible when !(value is string):
                return convertible.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
            default:
                throw new ArgumentException("Mode '" + mode + "' compares with a number; '" + value + "' is not one.", nameof(value));
        }
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

    /// <summary>
    /// Adds the property rule once per alternative to the search: the first as it is, every other one opening a new OR-group (conditions
    /// are ANDed inside a group, the groups are ORed). An item that matches any alternative is found, once.
    /// </summary>
    internal static void AddAlternatives(
        Search search,
        string categoryName,
        string propertyName,
        IReadOnlyList<VariantData> variants,
        Func<SearchCondition, VariantData, SearchCondition> rule)
    {
        for (var i = 0; i < variants.Count; i++)
        {
            var condition = rule(BuildPropertyCondition(categoryName, propertyName), variants[i]);
            search.SearchConditions.Add(i == 0 ? condition : condition.StartGroup());
        }
    }

    /// <summary>
    /// ONE search for a property matching any of several data-type variants of a value. Navisworks only matches a value whose type equals the
    /// stored one, so a plain number has to be tried as double, length, area, volume, angle (and integer) and a text as display and identifier
    /// string; running a search per variant walked the whole model that many times. As alternatives of a single search the model is walked
    /// once and each item is tried against them.
    /// </summary>
    private static List<ModelItem> RunAlternativesSearch(
        Document doc,
        ModelItemCollection? scope,
        string categoryName,
        string propertyName,
        IReadOnlyList<VariantData> variants,
        Func<SearchCondition, VariantData, SearchCondition> rule)
    {
        var search = new Search();
        if (scope == null)
        {
            search.Selection.SelectAll();
        }
        else
        {
            search.Selection.CopyFrom(scope);
        }

        search.Locations = SearchLocations.DescendantsAndSelf;
        AddAlternatives(search, categoryName, propertyName, variants, rule);

        // reportProgress must stay false: progress pumping can re-enter the host.
        return NavisValues.ToItemList(search.FindAll(doc, false));
    }

    private static List<ModelItem> RunEqualitySearch(Document doc, ModelItemCollection? scope, string categoryName, string propertyName, object? value) =>
        RunAlternativesSearch(doc, scope, categoryName, propertyName, BuildEqualityVariants(value), (condition, variant) => condition.EqualValue(variant));

    /// <summary>
    /// The found items at the requested selection level. A search result is already a fresh list without nulls, so with no resolution
    /// (Self, the default) it is handed on as it is instead of being copied again.
    /// </summary>
    private static List<ModelItem> ResolveFound(List<ModelItem> found, SelectionLevel level) =>
        level == SelectionLevel.Self ? found : SelectionLevels.Resolve(found, level);
}
