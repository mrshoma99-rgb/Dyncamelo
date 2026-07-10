using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows.Input;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.UI.Mvvm;
using Dyncamelo.UI.Services;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// One creatable node in the library browser. <see cref="Id"/> is either a
/// zero-touch definition id or a hand-written node type tag; pass it to
/// <see cref="GraphEditorViewModel.AddNode(string, System.Windows.Point)"/>.
/// </summary>
public class LibraryEntryViewModel : ObservableObject
{
    private bool _isFavorite;

    /// <summary>Creates an entry.</summary>
    /// <param name="id">Definition id (zero-touch) or node type tag (interactive nodes).</param>
    /// <param name="name">Display name.</param>
    /// <param name="category">Dot-separated category path.</param>
    /// <param name="description">Tooltip text.</param>
    /// <param name="searchTags">Extra search keywords.</param>
    /// <param name="signature">Input/output signature line for the tooltip.</param>
    public LibraryEntryViewModel(string id, string name, string category, string description, IReadOnlyList<string> searchTags, string signature = "")
    {
        Id = id;
        Name = name;
        Category = category;
        Description = description;
        SearchTags = searchTags;
        Signature = signature;
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

    /// <summary>Input/output signature, e.g. <c>a: double, b: double → result: double</c>.</summary>
    public string Signature { get; }

    /// <summary>True when the description is non-empty (tooltip layout helper).</summary>
    public bool HasDescription => !string.IsNullOrEmpty(Description);

    /// <summary>True when the signature is non-empty (tooltip layout helper).</summary>
    public bool HasSignature => !string.IsNullOrEmpty(Signature);

    /// <summary>True when the entry is starred (pinned to the Favourites section).</summary>
    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

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
/// (zero-touch definitions plus hand-written interactive node types) with live
/// search, a pinned Favourites section, and expand/collapse-all commands.
/// </summary>
public class LibraryViewModel : ObservableObject
{
    /// <summary>Display name of the pinned favourites section.</summary>
    public const string FavoritesCategoryName = "★ Favourites";

    private readonly NodeRegistry _registry;
    private readonly UiSettingsService? _settings;
    private readonly List<LibraryEntryViewModel> _allEntries = new List<LibraryEntryViewModel>();
    private string _searchText = string.Empty;

    /// <summary>Creates the browser and builds the catalog from the registry.</summary>
    /// <param name="registry">Registry to browse.</param>
    /// <param name="settings">Optional persisted UI settings (favourites); favourites are session-only when null.</param>
    public LibraryViewModel(NodeRegistry registry, UiSettingsService? settings = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _settings = settings;
        RootItems = new ObservableCollection<object>();
        ExpandAllCommand = new RelayCommand(() => SetAllExpanded(true));
        CollapseAllCommand = new RelayCommand(() => SetAllExpanded(false));
        ToggleFavoriteCommand = new RelayCommand<LibraryEntryViewModel>(ToggleFavorite);
        Refresh();
    }

    /// <summary>Top level of the (filtered) category tree.</summary>
    public ObservableCollection<object> RootItems { get; }

    /// <summary>Expands every category folder in the tree.</summary>
    public ICommand ExpandAllCommand { get; }

    /// <summary>Collapses every category folder in the tree.</summary>
    public ICommand CollapseAllCommand { get; }

    /// <summary>Stars/un-stars an entry (parameter: the <see cref="LibraryEntryViewModel"/>).</summary>
    public ICommand ToggleFavoriteCommand { get; }

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
                definition.SearchTags,
                FormatSignature(definition.Inputs, definition.Outputs)));
        }

        // Hand-written node types carry their metadata on instances; create a
        // throwaway instance per type to read name/category/description/ports.
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
                Array.Empty<string>(),
                FormatSignature(sample.InPorts, sample.OutPorts)));
        }

        if (_settings != null)
        {
            foreach (var entry in _allEntries)
            {
                entry.IsFavorite = _settings.IsFavorite(entry.Id);
            }
        }

        RebuildTree();
    }

    private void ToggleFavorite(LibraryEntryViewModel? entry)
    {
        if (entry == null)
        {
            return;
        }

        entry.IsFavorite = !entry.IsFavorite;
        _settings?.SetFavorite(entry.Id, entry.IsFavorite);
        RebuildTree();
    }

    private void SetAllExpanded(bool expanded)
    {
        foreach (var item in RootItems)
        {
            if (item is LibraryCategoryViewModel category)
            {
                SetExpandedRecursive(category, expanded);
            }
        }
    }

    private static void SetExpandedRecursive(LibraryCategoryViewModel category, bool expanded)
    {
        category.IsExpanded = expanded;
        foreach (var child in category.Children)
        {
            if (child is LibraryCategoryViewModel sub)
            {
                SetExpandedRecursive(sub, expanded);
            }
        }
    }

    private void RebuildTree()
    {
        RootItems.Clear();

        var lowerSearch = _searchText.Trim().ToLowerInvariant();
        bool searching = lowerSearch.Length > 0;
        var roots = new Dictionary<string, LibraryCategoryViewModel>(StringComparer.OrdinalIgnoreCase);
        var matching = _allEntries
            .Where(e => e.Matches(lowerSearch))
            .OrderBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var entry in matching)
        {
            var category = ResolveCategory(roots, entry.Category, searching);
            category.Children.Add(entry);
        }

        // The pinned favourites section always sits at the top, expanded.
        var favorites = matching
            .Where(e => e.IsFavorite)
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (favorites.Count > 0)
        {
            var section = new LibraryCategoryViewModel(FavoritesCategoryName) { IsExpanded = true };
            foreach (var entry in favorites)
            {
                section.Children.Add(entry);
            }

            RootItems.Add(section);
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

    // ----- signatures ----------------------------------------------------------

    private static string FormatSignature(IReadOnlyList<PortDescriptor> inputs, IReadOnlyList<PortDescriptor> outputs)
    {
        var inputParts = inputs.Select(p => p.Name + ": " + FormatTypeName(p.Type));
        var outputParts = outputs.Select(p => p.Name + ": " + FormatTypeName(p.Type));
        return JoinSignature(inputParts, outputParts);
    }

    private static string FormatSignature(IReadOnlyList<PortModel> inputs, IReadOnlyList<PortModel> outputs)
    {
        var inputParts = inputs.Select(p => p.Name + ": " + FormatTypeName(p.DeclaredType));
        var outputParts = outputs.Select(p => p.Name + ": " + FormatTypeName(p.DeclaredType));
        return JoinSignature(inputParts, outputParts);
    }

    private static string JoinSignature(IEnumerable<string> inputParts, IEnumerable<string> outputParts)
    {
        var inputs = string.Join(", ", inputParts);
        var outputs = string.Join(", ", outputParts);
        if (inputs.Length == 0 && outputs.Length == 0)
        {
            return string.Empty;
        }

        return (inputs.Length > 0 ? inputs : "()") + " → " + (outputs.Length > 0 ? outputs : "()");
    }

    private static string FormatTypeName(Type type)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable != null)
        {
            return FormatTypeName(nullable) + "?";
        }

        if (type == typeof(object))
        {
            return "var";
        }

        if (type == typeof(double))
        {
            return "double";
        }

        if (type == typeof(float))
        {
            return "float";
        }

        if (type == typeof(int))
        {
            return "int";
        }

        if (type == typeof(long))
        {
            return "long";
        }

        if (type == typeof(bool))
        {
            return "bool";
        }

        if (type == typeof(string))
        {
            return "string";
        }

        if (type.IsArray)
        {
            var element = type.GetElementType();
            return (element != null ? FormatTypeName(element) : "var") + "[]";
        }

        if (type.IsGenericType)
        {
            var name = type.Name;
            int tick = name.IndexOf('`');
            if (tick > 0)
            {
                name = name.Substring(0, tick);
            }

            var builder = new StringBuilder(name);
            builder.Append('<');
            var arguments = type.GetGenericArguments();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(FormatTypeName(arguments[i]));
            }

            builder.Append('>');
            return builder.ToString();
        }

        return type.Name;
    }
}
