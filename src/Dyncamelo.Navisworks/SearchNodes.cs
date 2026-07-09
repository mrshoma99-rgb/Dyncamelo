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
    /// <param name="categoryName">Category display name (e.g. "Element"); internal names also match.</param>
    /// <param name="propertyName">Property display name (e.g. "Category"); internal names also match.</param>
    /// <param name="value">The value to match (string, number, boolean or date).</param>
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
        var condition = BuildPropertyCondition(categoryName, propertyName)
            .EqualValue(NavisValues.ToVariant(value));
        return RunSearch(doc, condition);
    }

    /// <summary>Finds every model item whose property display string contains a substring.</summary>
    /// <param name="categoryName">Category display name (e.g. "Item"); internal names also match.</param>
    /// <param name="propertyName">Property display name (e.g. "Name"); internal names also match.</param>
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
