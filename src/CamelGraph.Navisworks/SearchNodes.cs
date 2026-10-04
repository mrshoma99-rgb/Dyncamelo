using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;
using CamelGraph.Nodes.Portable;

namespace CamelGraph.Navisworks;

/// <summary>Nodes that find model items by property, like the Find Items window.</summary>
[NodeCategory("Navisworks.Search")]
public static class SearchNodes
{
    /// <summary>Finds every model item by one property: equal to a value, containing text, matching a wildcard, compared with a number, or simply carrying the property.</summary>
    /// <param name="categoryName">Category display name (e.g. "Element"). Internal names do not match.</param>
    /// <param name="propertyName">Property display name (e.g. "Category"). Internal names do not match. With mode "exists", leave it empty to ask only whether the item carries the category (tab).</param>
    /// <param name="value">What to look for: any value for "equals"; text for "contains" and "wildcard" (* matches any text, ? one character); a number for &gt;, &gt;=, &lt; and &lt;=; not used by "exists". A list means "any of these": an item matches when it matches at least one entry, and the model is walked once however long the list is. An empty list matches nothing.</param>
    /// <param name="mode">How the property is matched: equals, contains, wildcard, &gt;, &gt;=, &lt;, &lt;= or exists.</param>
    /// <param name="resolveTo">Optional selection resolution applied to the matches, like Options &gt; Interface &gt; Selection &gt; Resolution in Navisworks: Self (default, keep matches as found), File, Layer, FirstObject, LastObject, LastUnique or Geometry.</param>
    /// <param name="within">Optional: search only inside these items and their descendants (a scoped search that refines an earlier result). Leave it unwired to search the whole model; an empty list searches nothing.</param>
    /// <param name="document">The document to search (defaults to the active document).</param>
    /// <returns>All matching model items.</returns>
    [NodeName("Search.ByProperty")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [NodeDescription("Finds every model item by one property, like Find Items with its condition drop-down: equals a value, contains text, matches a wildcard pattern (* and ?), is >, >=, < or <= a number (e.g. pipes with Diameter > 100), or exists (the item carries the property at all, with any value; leave propertyName empty to ask only for the tab). Wire a LIST into value to match ANY of its entries (\"is one of\") in a single pass over the model; an empty list matches nothing, and List Levels @L1 on value runs one search per entry instead. Wire items into within to look only inside those items and their descendants (a scoped search that refines an earlier result). Replaces Search.HasProperty, Search.HasCategory and Search.InItems.")]
    [NodeSearchTags("search", "find", "filter", "property", "equals", "contains", "wildcard", "pattern", "compare", "greater", "less", "numeric", "text", "query", "has", "exists", "audit", "category", "tab", "scoped", "within", "refine", "subset", "one of", "any of", "in list")]
    // The id before the exists mode, a list of values and within were added: (categoryName, propertyName, value, mode, resolveTo, document).
    [NodeAliases("CamelGraph.Navisworks.SearchNodes.ByProperty@string,string,object,string,string,Autodesk.Navisworks.Api.Document")]
    [return: NodeName("items")]
    public static List<ModelItem> ByProperty(
        [NodeTabChoice(NodeDataSource.Selection)] string categoryName,
        [NodePropertyChoice(NodeDataSource.Selection, "categoryName")] string propertyName,
        object? value = null,
        [NodeChoices("equals", "contains", "wildcard", ">", ">=", "<", "<=", "exists")]
        string mode = "equals",
        [NodePanel("Advanced")]
        [NodeChoices("Self", "File", "Layer", "FirstObject", "LastObject", "LastUnique", "Geometry")]
        string resolveTo = "Self",
        [MultiInput] IEnumerable<ModelItem>? within = null,
        Document? document = null)
    {
        return Find(categoryName, propertyName, value, mode, resolveTo, within, document);
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
        return Find(categoryName, propertyName, value, "equals", resolveTo, null, document);
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

        return Find(categoryName, propertyName, value, "contains", resolveTo, null, document);
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

        return Find(categoryName, propertyName, pattern, "wildcard", resolveTo, null, document);
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
        // The word forms (GreaterThan, ...) and the symbols are both modes of the shared search; anything else is refused here
        // with the message this node always gave.
        ParseComparison(comparison);
        return Find(categoryName, propertyName, value, comparison, resolveTo, null, document);
    }

    /// <summary>Finds every model item that carries a property at all.</summary>
    /// <param name="categoryName">Category display name (e.g. "Element"). Internal names do not match.</param>
    /// <param name="propertyName">Property display name (e.g. "Level"). Internal names do not match.</param>
    /// <param name="resolveTo">Optional selection resolution applied to the matches, like Options &gt; Interface &gt; Selection &gt; Resolution in Navisworks: Self (default, keep matches as found), File, Layer, FirstObject, LastObject, LastUnique or Geometry.</param>
    /// <param name="document">The document to search (defaults to the active document).</param>
    /// <returns>All items carrying the property.</returns>
    [NodeName("Search.HasProperty")]
    [NodeDeprecated("Search.ByProperty")]
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
        return Find(categoryName, propertyName, null, "exists", resolveTo, null, document);
    }

    /// <summary>Finds every model item that carries a property category (tab).</summary>
    /// <param name="categoryName">Category display name (e.g. "TimeLiner"). Internal names do not match.</param>
    /// <param name="resolveTo">Optional selection resolution applied to the matches, like Options &gt; Interface &gt; Selection &gt; Resolution in Navisworks: Self (default, keep matches as found), File, Layer, FirstObject, LastObject, LastUnique or Geometry.</param>
    /// <param name="document">The document to search (defaults to the active document).</param>
    /// <returns>All items carrying the category.</returns>
    [NodeName("Search.HasCategory")]
    [NodeDeprecated("Search.ByProperty")]
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

        return Find(categoryName, string.Empty, null, "exists", resolveTo, null, document);
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
    [NodeDeprecated("Search.ByProperty")]
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

        return Find(categoryName, propertyName, value, "equals", resolveTo, items, document);
    }

    /// <summary>
    /// The one search behind Search.ByProperty, the retired search nodes and the live search sets: the mode and the value (or the list
    /// of alternative values) become the OR-groups of ONE search, so the model is walked once however many values there are.
    /// </summary>
    /// <param name="categoryName">The category (tab) display name.</param>
    /// <param name="propertyName">The property display name (empty with mode exists asks for the category only).</param>
    /// <param name="value">The value, or a list of alternatives.</param>
    /// <param name="mode">The mode text of the node.</param>
    /// <param name="resolveTo">The selection resolution of the matches.</param>
    /// <param name="within">The items to search inside, or null for the whole model.</param>
    /// <param name="document">The document (null for the active one).</param>
    internal static List<ModelItem> Find(
        string categoryName,
        string propertyName,
        object? value,
        string? mode,
        string? resolveTo,
        IEnumerable<ModelItem>? within,
        Document? document)
    {
        var level = SelectionLevels.Parse(resolveTo);
        var plan = SearchPlan.Create(mode, value);
        var doc = NavisworksContext.ResolveDocument(document);

        ModelItemCollection? scope = null;
        if (within != null)
        {
            scope = NavisValues.ToItemCollection(within);
            if (scope.Count == 0)
            {
                NodeWarnings.Add("'within' holds no items, so there is nothing to search in.");
                return new List<ModelItem>();
            }
        }

        if (plan.MatchesNothing)
        {
            NodeWarnings.Add("The list wired into 'value' is empty, so no item can match.");
            return new List<ModelItem>();
        }

        if (plan.Mode == SearchMode.Equal && plan.Values.Count == 1 && plan.Values[0] == null)
        {
            NodeWarnings.Add("No value was given, so this looks for items whose property is empty. Wire a value, or use mode 'exists' to find the items that carry the property at all.");
        }

        var search = BuildSearch(categoryName, propertyName, plan, scope);

        // reportProgress must stay false: progress pumping can re-enter the host.
        return ResolveFound(NavisValues.ToItemList(search.FindAll(doc, false)), level);
    }

    /// <summary>
    /// Builds the search of a plan: over the whole model, or only inside <paramref name="scope"/> and its descendants. Also what a live
    /// search set stores (SelectionSet.CreateFromSearch), which must persist a single Search object.
    /// </summary>
    internal static Search BuildSearch(string categoryName, string propertyName, SearchPlan plan, ModelItemCollection? scope)
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

        switch (plan.Mode)
        {
            case SearchMode.Exists:
                search.SearchConditions.Add(string.IsNullOrEmpty(propertyName)
                    ? HasCategoryCondition(categoryName)
                    : BuildPropertyCondition(categoryName, propertyName));
                break;
            case SearchMode.Equal:
                // Navisworks equality only matches when the variant's data type equals the stored one, so every value is expanded into
                // the types it could be stored as; all of them (of all the values) are alternatives of this one search.
                var equalVariants = new List<VariantData>();
                foreach (var entry in plan.Values)
                {
                    equalVariants.AddRange(BuildEqualityVariants(entry));
                }

                AddAlternatives(search, categoryName, propertyName, equalVariants, (condition, variant) => condition.EqualValue(variant));
                break;
            case SearchMode.Contains:
                var contains = new List<Func<SearchCondition, SearchCondition>>();
                foreach (var text in plan.Texts)
                {
                    var wanted = text;
                    contains.Add(condition => condition.DisplayStringContains(wanted));
                }

                AddRules(search, categoryName, propertyName, contains);
                break;
            case SearchMode.Wildcard:
                var wildcard = new List<Func<SearchCondition, SearchCondition>>();
                foreach (var text in plan.Texts)
                {
                    var pattern = text;
                    wildcard.Add(condition => condition.DisplayStringWildcard(pattern));
                }

                AddRules(search, categoryName, propertyName, wildcard);
                break;
            default:
                // The variant's data type must equal the stored property's data type, so the comparison is tried against every
                // plausible numeric storage type (as alternatives of ONE search).
                var kind = ComparisonOf(plan.Mode);
                var numeric = new List<VariantData>();
                foreach (var number in plan.Numbers)
                {
                    AddNumericVariants(numeric, number);
                    if (number >= int.MinValue && number <= int.MaxValue && number == Math.Floor(number))
                    {
                        numeric.Add(VariantData.FromInt32((int)number));
                    }
                }

                AddAlternatives(search, categoryName, propertyName, numeric, (condition, variant) => condition.CompareWith(kind, variant));
                break;
        }

        if (search.SearchConditions.Count == 0)
        {
            // A search without a condition would select everything: never hand one on.
            throw new InvalidOperationException("The search has no condition, so it would match every item. Give a value to look for.");
        }

        return search;
    }

    /// <summary>
    /// Builds the whole-model search a live search set stores: the property in the given mode against the value (or any of a list of
    /// values).
    /// </summary>
    /// <param name="categoryName">The category (tab) display name.</param>
    /// <param name="propertyName">The property display name.</param>
    /// <param name="value">The value, or a list of alternatives.</param>
    /// <param name="mode">The mode text of the node.</param>
    internal static Search CreateSearch(string categoryName, string propertyName, object? value, string? mode)
    {
        var plan = SearchPlan.Create(mode, value);
        if (plan.MatchesNothing)
        {
            throw new ArgumentException("The list wired into 'value' is empty, so the search set would select nothing. Give it at least one value.", "value");
        }

        if (plan.Mode == SearchMode.Equal && plan.Values.Count == 1 && plan.Values[0] == null)
        {
            NodeWarnings.Add("No value was given, so the search set looks for items whose property is empty. Wire a value, or use mode 'exists' to find the items that carry the property at all.");
        }

        return BuildSearch(categoryName, propertyName, plan, null);
    }

    /// <summary>
    /// Builds a whole-model search for the property equalling any of several exact variants (each variant already carries the property's
    /// true storage type).
    /// </summary>
    internal static Search CreateVariantEqualitySearch(string categoryName, string propertyName, IReadOnlyList<VariantData> variants)
    {
        var search = new Search();
        search.Selection.SelectAll();
        search.Locations = SearchLocations.DescendantsAndSelf;
        AddAlternatives(search, categoryName, propertyName, variants, (condition, variant) => condition.EqualValue(variant));
        return search;
    }

    private static SearchCondition HasCategoryCondition(string categoryName)
    {
        if (string.IsNullOrEmpty(categoryName))
        {
            throw new ArgumentException("No property category name provided.", nameof(categoryName));
        }

        return SearchCondition.HasCategoryByDisplayName(categoryName);
    }

    private static SearchConditionComparison ComparisonOf(SearchMode mode)
    {
        switch (mode)
        {
            case SearchMode.GreaterThan: return SearchConditionComparison.NumericGreaterThan;
            case SearchMode.GreaterOrEqual: return SearchConditionComparison.NumericGreaterThanOrEqual;
            case SearchMode.LessThan: return SearchConditionComparison.NumericLessThan;
            default: return SearchConditionComparison.NumericLessThanOrEqual;
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
    /// Like <see cref="AddAlternatives"/> for rules that are not about a data-type variant (contains, wildcard): one rule per entry of the
    /// list, each in its own OR-group.
    /// </summary>
    private static void AddRules(
        Search search,
        string categoryName,
        string propertyName,
        IReadOnlyList<Func<SearchCondition, SearchCondition>> rules)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            var condition = rules[i](BuildPropertyCondition(categoryName, propertyName));
            search.SearchConditions.Add(i == 0 ? condition : condition.StartGroup());
        }
    }

    /// <summary>
    /// The found items at the requested selection level. A search result is already a fresh list without nulls, so with no resolution
    /// (Self, the default) it is handed on as it is instead of being copied again.
    /// </summary>
    private static List<ModelItem> ResolveFound(List<ModelItem> found, SelectionLevel level) =>
        level == SelectionLevel.Self ? found : SelectionLevels.Resolve(found, level);
}
