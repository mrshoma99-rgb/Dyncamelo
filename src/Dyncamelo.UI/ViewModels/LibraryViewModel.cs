using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Dyncamelo.Core.Loader;
using Dyncamelo.UI.Mvvm;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// One creatable node in the library browser. <see cref="Id"/> is either a
/// zero-touch definition id or a hand-written node type tag; pass it to
/// <see cref="GraphEditorViewModel.AddNode(string, System.Windows.Point)"/>.
/// </summary>
public class LibraryEntryViewModel : ObservableObject
{
    /// <summary>Creates an entry.</summary>
    /// <param name="id">Definition id (zero-touch) or node type tag (interactive nodes).</param>
    /// <param name="name">Display name.</param>
    /// <param name="category">Dot-separated category path.</param>
    /// <param name="description">Tooltip text.</param>
    /// <param name="searchTags">Extra search keywords.</param>
    public LibraryEntryViewModel(string id, string name, string category, string description, IReadOnlyList<string> searchTags)
    {
        Id = id;
        Name = name;
        Category = category;
        Description = description;
        SearchTags = searchTags;
    }

    /// <summary>Creation id understood by the editor's AddNode.</summary>
    public string Id { get; }

    /// <summary>Display name.</summary>
    public string Name { get; }

    /// <summary>Dot-separated category path.</summary>
    public string Category { get; }

    /// <summary>Tooltip text.</summary>
    public string Description { get; }

    /// <summary>Extra search keywords.</summary>
    public IReadOnlyList<string> SearchTags { get; }

    /// <summary>True when the entry matches the (lower-case) search text.</summary>
    /// <param name="lowerSearch">Search text, already lower-cased. Empty matches everything.</param>
    public bool Matches(string lowerSearch)
    {
        if (lowerSearch.Length == 0)
        {
            return true;
        }

        if (Name.ToLowerInvariant().Contains(lowerSearch) ||
            Category.ToLowerInvariant().Contains(lowerSearch))
        {
            return true;
        }

        foreach (var tag in SearchTags)
        {
            if (tag.ToLowerInvariant().Contains(lowerSearch))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// One category folder in the library tree. Children are sub-categories followed by entries.
/// </summary>
public class LibraryCategoryViewModel : ObservableObject
{
    private bool _isExpanded;

    /// <summary>Creates a category folder.</summary>
    /// <param name="name">Category segment name.</param>
    public LibraryCategoryViewModel(string name)
    {
        Name = name;
        Children = new ObservableCollection<object>();
    }

    /// <summary>Category segment name.</summary>
    public string Name { get; }

    /// <summary>Sub-categories (<see cref="LibraryCategoryViewModel"/>) then entries (<see cref="LibraryEntryViewModel"/>).</summary>
    public ObservableCollection<object> Children { get; }

    /// <summary>Tree expansion state (expanded automatically while searching).</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}

/// <summary>
/// The node library browser: a category tree built from a <see cref="NodeRegistry"/>
/// (zero-touch definitions plus hand-written interactive node types) with live search.
/// </summary>
public class LibraryViewModel : ObservableObject
{
    private readonly NodeRegistry _registry;
    private readonly List<LibraryEntryViewModel> _allEntries = new List<LibraryEntryViewModel>();
    private string _searchText = string.Empty;

    /// <summary>Creates the browser and builds the catalog from the registry.</summary>
    /// <param name="registry">Registry to browse.</param>
    public LibraryViewModel(NodeRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        RootItems = new ObservableCollection<object>();
        Refresh();
    }

    /// <summary>Top level of the (filtered) category tree.</summary>
    public ObservableCollection<object> RootItems { get; }

    /// <summary>Search box text; changing it rebuilds the filtered tree.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RebuildTree();
            }
        }
    }

    /// <summary>
    /// Re-reads the registry (call after the host registers additional node
    /// assemblies) and rebuilds the tree.
    /// </summary>
    public void Refresh()
    {
        _allEntries.Clear();

        foreach (var definition in _registry.Definitions)
        {
            _allEntries.Add(new LibraryEntryViewModel(
                definition.Id,
                definition.Name,
                definition.Category,
                definition.Description,
                definition.SearchTags));
        }

        // Hand-written node types carry their metadata on instances; create a
        // throwaway instance per type to read name/category/description.
        foreach (var nodeType in _registry.NodeTypes)
        {
            var sample = _registry.CreateNode(nodeType);
            if (sample == null)
            {
                continue;
            }

            _allEntries.Add(new LibraryEntryViewModel(
                nodeType,
                sample.Name,
                sample.Category,
                sample.Description,
                Array.Empty<string>()));
        }

        RebuildTree();
    }

    private void RebuildTree()
    {
        RootItems.Clear();

        var lowerSearch = _searchText.Trim().ToLowerInvariant();
        bool searching = lowerSearch.Length > 0;
        var roots = new Dictionary<string, LibraryCategoryViewModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _allEntries
            .Where(e => e.Matches(lowerSearch))
            .OrderBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            var category = ResolveCategory(roots, entry.Category, searching);
            category.Children.Add(entry);
        }

        foreach (var root in roots.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            RootItems.Add(root);
        }
    }

    private static LibraryCategoryViewModel ResolveCategory(
        Dictionary<string, LibraryCategoryViewModel> roots,
        string categoryPath,
        bool expand)
    {
        var segments = string.IsNullOrEmpty(categoryPath)
            ? new[] { "Other" }
            : categoryPath.Split('.');

        if (!roots.TryGetValue(segments[0], out var current))
        {
            current = new LibraryCategoryViewModel(segments[0]) { IsExpanded = expand };
            roots[segments[0]] = current;
        }

        for (int i = 1; i < segments.Length; i++)
        {
            LibraryCategoryViewModel? child = null;
            foreach (var existing in current.Children)
            {
                if (existing is LibraryCategoryViewModel c &&
                    string.Equals(c.Name, segments[i], StringComparison.OrdinalIgnoreCase))
                {
                    child = c;
                    break;
                }
            }

            if (child == null)
            {
                child = new LibraryCategoryViewModel(segments[i]) { IsExpanded = expand };
                // Keep sub-categories ahead of entries.
                int insertAt = 0;
                while (insertAt < current.Children.Count && current.Children[insertAt] is LibraryCategoryViewModel)
                {
                    insertAt++;
                }

                current.Children.Insert(insertAt, child);
            }

            current = child;
        }

        return current;
    }
}
