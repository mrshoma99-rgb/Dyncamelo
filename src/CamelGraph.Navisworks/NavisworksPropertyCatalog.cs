using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using CamelGraph.Core.Editing;
using CamelGraph.Navisworks.Internal;

namespace CamelGraph.Navisworks;

/// <summary>
/// Backs the search button next to the tab and property inputs of a node: lists the property tabs, or the properties of one tab, of
/// the element(s) the node was handed — and of nothing else. The model is never searched; at most
/// <see cref="ModelPropertyHost.MaxElementsRead"/> elements are read, and the listing says when there were more.
/// </summary>
public sealed class NavisworksPropertyCatalog : IModelPropertyCatalog
{
    private const int MaxAncestors = 16;

    /// <inheritdoc />
    public ModelDataListing List(object? source, ModelDataChoice choice, string? tab)
    {
        if (choice == null)
        {
            throw new ArgumentNullException(nameof(choice));
        }

        var items = new List<ModelItem>();
        var total = 0;
        Collect(source, items, ref total);
        if (items.Count == 0)
        {
            return ModelDataListing.None(total == 0
                ? source is ModelSelectionSource
                    ? "Nothing is selected in Navisworks. Select an element, then search."
                    : "There is no element to read yet. Pick one, or wire one in and run the graph."
                : "The element could not be found in the open model any more.");
        }

        var perElement = new List<List<string>>();
        foreach (var item in items)
        {
            perElement.Add(NamesOf(item, choice, tab ?? string.Empty));
        }

        return ModelDataListing.Union(perElement, total);
    }

    private static List<string> NamesOf(ModelItem start, ModelDataChoice choice, string tab)
    {
        var names = new List<string>();
        var item = start;
        for (var depth = 0; item != null && depth < MaxAncestors; depth++)
        {
            try
            {
                if (choice.Kind == ModelDataKind.Tab)
                {
                    foreach (var category in item.PropertyCategories)
                    {
                        names.Add(category.DisplayName);
                    }
                }
                else
                {
                    var category = item.PropertyCategories.FindCategoryByDisplayName(tab)
                        ?? item.PropertyCategories.FindCategoryByName(tab);
                    if (category != null)
                    {
                        foreach (var property in category.Properties)
                        {
                            names.Add(property.DisplayName);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // An element whose data cannot be read adds nothing; the others still count.
            }

            if (!choice.IncludeAncestors)
            {
                break;
            }

            item = item.Parent;
        }

        return names;
    }

    // Turns whatever the element input holds into model items: an item, a list or collection of them, or a stored pick.
    private static void Collect(object? value, List<ModelItem> items, ref int total)
    {
        switch (value)
        {
            case null:
                return;
            case ModelSelectionSource _:
                // What is selected in Navisworks right now: counted, and only the first elements are read.
                var selected = NavisworksContext.ResolveDocument(null).CurrentSelection.SelectedItems;
                total += selected.Count;
                items.AddRange(selected.Take(Math.Max(0, ModelPropertyHost.MaxElementsRead - items.Count)));
                return;
            case ModelItem item:
                total++;
                if (items.Count < ModelPropertyHost.MaxElementsRead)
                {
                    items.Add(item);
                }

                return;
            case string text:
                if (text.StartsWith(ModelPickerHost.Prefix, StringComparison.Ordinal))
                {
                    // Only as many paths are turned back into items as the search will read.
                    var paths = NavisworksModelPicker.Decode(text);
                    total += paths.Count;
                    var room = Math.Max(0, ModelPropertyHost.MaxElementsRead - items.Count);
                    if (room > 0 && paths.Count > 0)
                    {
                        items.AddRange(ModelItemPaths.ResolvePaths(NavisworksContext.ResolveDocument(null), paths.Take(room).ToList()));
                    }
                }

                return;
            case IEnumerable list:
                foreach (var entry in list)
                {
                    Collect(entry, items, ref total);
                }

                return;
            default:
                return;
        }
    }
}
