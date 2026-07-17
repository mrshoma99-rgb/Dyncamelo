using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows.Input;
using System.Windows.Threading;
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
    private readonly string _lowerName;
    private readonly string _lowerCategoryAndTags;
    private readonly string _searchHaystack;
    private bool _isFavorite;
    private bool _isSelected;
    private bool _isDescriptionVisible;

    /// <summary>Creates an entry and precomputes its lower-case search index.</summary>
    /// <param name="id">Definition id (zero-touch) or node type tag (interactive nodes).</param>
    /// <param name="name">Display name.</param>
    /// <param name="category">Dot-separated category path.</param>
    /// <param name="description">Description shown under the name and in the tooltip.</param>
    /// <param name="searchTags">Extra search keywords.</param>
    /// <param name="signature">Input/output signature line for the tooltip.</param>
    /// <param name="function">Functional role (Create / Modify / Info) for leaf grouping and the tint dot.</param>
    public LibraryEntryViewModel(string id, string name, string category, string description, IReadOnlyList<string> searchTags, string signature = "", Dyncamelo.Core.Graph.NodeFunction function = Dyncamelo.Core.Graph.NodeFunction.Modify)
    {
        Id = id;
        Name = name;
        Category = category;
        Description = description;
        SearchTags = searchTags;
        Signature = signature;
        Function = function;

        // The search index is built exactly once so typing in the search box
        // never lowercases or concatenates entry metadata again. Index and
        // query go through the same LibrarySearchText fold, so matching stays
        // immune to case, exotic whitespace and invisible layout characters.
        _lowerName = LibrarySearchText.Normalize(name);
        _lowerCategoryAndTags = searchTags.Count == 0
            ? LibrarySearchText.Normalize(category)
            : LibrarySearchText.Normalize(category) + "\n" + LibrarySearchText.Normalize(string.Join("\n", searchTags));
        _searchHaystack = _lowerName + "\n" + _lowerCategoryAndTags + "\n" + LibrarySearchText.Normalize(description);
    }

    /// <summary>Creation id understood by the editor's AddNode.</summary>
    public string Id { get; }

    /// <summary>Display name.</summary>
    public string Name { get; }

    /// <summary>Dot-separated category path.</summary>
    public string Category { get; }

    /// <summary>Functional role (Create / Modify / Info) — groups the entry at its leaf and tints its dot.</summary>
    public Dyncamelo.Core.Graph.NodeFunction Function { get; }

    /// <summary>Description shown under the name (when enabled) and in the tooltip.</summary>
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

    /// <summary>
    /// Selection highlight in the library browser (tree and search results bind
    /// this two-way, so the highlight can be cleared when focus moves to the canvas).
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// True when the in-row description line renders: the entry has a
    /// description and the library's descriptions toggle is on.
    /// </summary>
    public bool IsDescriptionVisible
    {
        get => _isDescriptionVisible;
        set => SetProperty(ref _isDescriptionVisible, value);
    }

    /// <summary>True when the entry matches the (lower-case) search text.</summary>
    /// <param name="lowerSearch">Search text, already lower-cased. Empty matches everything.</param>
    public bool Matches(string lowerSearch)
    {
        return lowerSearch.Length == 0 || _searchHaystack.Contains(lowerSearch);
    }

    /// <summary>
    /// Relevance of the entry for a tokenized search: 0 name-starts-with,
    /// 1 name-contains, 2 category/tag match, 3 description-only match,
    /// -1 when any token misses. Every token must occur somewhere in the
    /// prebuilt index; the first token decides the rank.
    /// </summary>
    /// <param name="lowerTokens">Search tokens, already lower-cased and non-empty.</param>
    public int MatchRank(IReadOnlyList<string> lowerTokens)
    {
        for (int i = 0; i < lowerTokens.Count; i++)
        {
            if (!_searchHaystack.Contains(lowerTokens[i]))
            {
                return -1;
            }
        }

        var first = lowerTokens[0];
        if (_lowerName.StartsWith(first, StringComparison.Ordinal))
        {
            return 0;
        }

        if (_lowerName.Contains(first))
        {
            return 1;
        }

        return _lowerCategoryAndTags.Contains(first) ? 2 : 3;
    }
}

/// <summary>
/// One category folder in the library tree. Children are sub-categories followed by entries.
/// </summary>
public class LibraryCategoryViewModel : ObservableObject
{
    private bool _isExpanded;
    private bool _isSelected;

    /// <summary>Creates a category folder.</summary>
    /// <param name="name">Category segment name.</param>
    /// <param name="iconKey">Icon key for the tree glyph (top-level roots only; null for sub-categories).</param>
    public LibraryCategoryViewModel(string name, string? iconKey = null)
    {
        Name = name;
        IconKey = iconKey;
        Children = new ObservableCollection<object>();
    }

    /// <summary>
    /// Creates a Create/Modify/Info grouping header shown at a leaf category, with
    /// its function glyph. These divide a leaf's nodes by what they do; the tree
    /// structure above is unchanged.
    /// </summary>
    /// <param name="function">The functional role this header groups.</param>
    public static LibraryCategoryViewModel CreateFunctionGroup(Dyncamelo.Core.Graph.NodeFunction function)
    {
        var name = function switch
        {
            Dyncamelo.Core.Graph.NodeFunction.Create => "Create",
            Dyncamelo.Core.Graph.NodeFunction.Info => "Info",
            _ => "Modify",
        };

        return new LibraryCategoryViewModel(name, "Fn" + name)
        {
            IsFunctionGroup = true,
            IsExpanded = true,
        };
    }

    /// <summary>Category segment name.</summary>
    public string Name { get; }

    /// <summary>True when this is a Create/Modify/Info function-grouping header (not a real category).</summary>
    public bool IsFunctionGroup { get; private set; }

    /// <summary>
    /// Icon key that selects the category glyph in the tree; set only for
    /// top-level roots (and the favourites section) so sub-categories stay
    /// text-only. Null means "no glyph".
    /// </summary>
    public string? IconKey { get; }

    /// <summary>Sub-categories (<see cref="LibraryCategoryViewModel"/>) then entries (<see cref="LibraryEntryViewModel"/>).</summary>
    public ObservableCollection<object> Children { get; }

    /// <summary>Tree expansion state.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>Selection highlight in the library tree (two-way bound, clearable).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>
/// Payload of <see cref="LibraryViewModel.EntryRevealRequested"/>: the tree path
/// (root category, sub-categories, then the entry) the view should materialize
/// and scroll into view.
/// </summary>
public class LibraryRevealEventArgs : EventArgs
{
    /// <summary>Creates the payload.</summary>
    /// <param name="path">Tree path from root category down to the entry.</param>
    public LibraryRevealEventArgs(IReadOnlyList<object> path)
    {
        Path = path;
    }

    /// <summary>Tree path from root category down to the entry.</summary>
    public IReadOnlyList<object> Path { get; }
}

/// <summary>
/// The node library browser: a category tree built from a <see cref="NodeRegistry"/>
/// (zero-touch definitions plus hand-written interactive node types) with a
/// debounced search that renders a flat, capped result list (the tree keeps its
/// expansion state and comes back untouched when the search is cleared), a
/// pinned Favourites section, per-row descriptions and expand/collapse-all commands.
/// </summary>
public class LibraryViewModel : ObservableObject
{
    /// <summary>Display name of the pinned favourites section.</summary>
    public const string FavoritesCategoryName = "★ Favourites";

    /// <summary>Maximum number of search hits shown before asking to refine the search.</summary>
    public const int MaxSearchResults = 200;

    private readonly NodeRegistry _registry;
    private readonly UiSettingsService? _settings;
    private readonly List<LibraryEntryViewModel> _allEntries = new List<LibraryEntryViewModel>();
    private readonly HashSet<string> _expandedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _searchTimer;
    private string _searchText = string.Empty;
    private bool _isSearching;
    private string _searchStatusText = string.Empty;
    private bool _showDescriptions;

    /// <summary>Creates the browser and builds the catalog from the registry.</summary>
    /// <param name="registry">Registry to browse.</param>
    /// <param name="settings">Optional persisted UI settings (favourites, descriptions toggle); session-only when null.</param>
    public LibraryViewModel(NodeRegistry registry, UiSettingsService? settings = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _settings = settings;
        _showDescriptions = settings == null || settings.ShowLibraryDescriptions;
        RootItems = new ObservableCollection<object>();
        SearchResults = new ObservableCollection<LibraryEntryViewModel>();
        ExpandAllCommand = new RelayCommand(() => SetAllExpanded(true));
        CollapseAllCommand = new RelayCommand(() => SetAllExpanded(false));
        ToggleFavoriteCommand = new RelayCommand<LibraryEntryViewModel>(ToggleFavorite);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);

        // Typing must never rebuild UI per keystroke: the search itself is
        // debounced and only fills the flat results list.
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _searchTimer.Tick += OnSearchTimerTick;

        Refresh();
    }

    /// <summary>
    /// Raised when <see cref="RevealEntry"/> selected an entry; the view scrolls
    /// the given tree path into view.
    /// </summary>
    public event EventHandler<LibraryRevealEventArgs>? EntryRevealRequested;

    /// <summary>Top level of the category tree (always unfiltered; hidden while searching).</summary>
    public ObservableCollection<object> RootItems { get; }

    /// <summary>Flat search hits (relevance-ordered, capped at <see cref="MaxSearchResults"/>).</summary>
    public ObservableCollection<LibraryEntryViewModel> SearchResults { get; }

    /// <summary>Expands every category folder in the tree.</summary>
    public ICommand ExpandAllCommand { get; }

    /// <summary>Collapses every category folder in the tree.</summary>
    public ICommand CollapseAllCommand { get; }

    /// <summary>Stars/un-stars an entry (parameter: the <see cref="LibraryEntryViewModel"/>).</summary>
    public ICommand ToggleFavoriteCommand { get; }

    /// <summary>Clears the search box (restoring the tree). Bound to the search box's ✕ button.</summary>
    public ICommand ClearSearchCommand { get; }

    /// <summary>True while a non-empty search is active (the view swaps tree ⇄ result list).</summary>
    public bool IsSearching
    {
        get => _isSearching;
        private set => SetProperty(ref _isSearching, value);
    }

    /// <summary>Result-list footer ("no matches" / "refine your search"); empty when not needed.</summary>
    public string SearchStatusText
    {
        get => _searchStatusText;
        private set
        {
            if (SetProperty(ref _searchStatusText, value))
            {
                OnPropertyChanged(nameof(HasSearchStatus));
            }
        }
    }

    /// <summary>True when <see cref="SearchStatusText"/> has content.</summary>
    public bool HasSearchStatus => _searchStatusText.Length > 0;

    /// <summary>
    /// Library-header toggle: render the description line under each node name.
    /// Persisted in ui-settings.json; defaults to on.
    /// </summary>
    public bool ShowDescriptions
    {
        get => _showDescriptions;
        set
        {
            if (SetProperty(ref _showDescriptions, value))
            {
                _settings?.SetShowLibraryDescriptions(value);
                UpdateDescriptionVisibility();
            }
        }
    }

    /// <summary>
    /// Search box text. Non-empty values apply after a short debounce; clearing
    /// applies immediately and restores the tree with its previous expansion.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                OnPropertyChanged(nameof(HasSearchText));
                _searchTimer.Stop();
                if (_searchText.Trim().Length == 0)
                {
                    ApplySearch();
                }
                else
                {
                    _searchTimer.Start();
                }
            }
        }
    }

    /// <summary>True when the search box has any text (drives the ✕ clear button's visibility).</summary>
    public bool HasSearchText => _searchText.Length > 0;

    /// <summary>
    /// Re-reads the registry (call after the host registers additional node
    /// assemblies), rebuilds the tree and re-applies any active search.
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
                FormatSignature(definition.Inputs, definition.Outputs),
                definition.Function));
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
                FormatSignature(sample.InPorts, sample.OutPorts),
                sample.Function));
        }

        foreach (var entry in _allEntries)
        {
            if (_settings != null)
            {
                entry.IsFavorite = _settings.IsFavorite(entry.Id);
            }

            entry.IsDescriptionVisible = _showDescriptions && entry.HasDescription;
        }

        RebuildTree();
        if (IsSearching || _searchText.Trim().Length > 0)
        {
            _searchTimer.Stop();
            ApplySearch();
        }
    }

    /// <summary>Clears the selection highlight everywhere in the library (tree and results).</summary>
    public void ClearSelection()
    {
        foreach (var item in RootItems)
        {
            if (item is LibraryCategoryViewModel category)
            {
                ClearSelectionRecursive(category);
            }
        }

        foreach (var entry in SearchResults)
        {
            entry.IsSelected = false;
        }

        foreach (var entry in _allEntries)
        {
            entry.IsSelected = false;
        }
    }

    /// <summary>
    /// Finds the entry with the given library id, clears any active search,
    /// expands its category chain, selects it and asks the view (via
    /// <see cref="EntryRevealRequested"/>) to scroll it into view.
    /// </summary>
    /// <param name="libraryId">Zero-touch definition id or interactive node type tag.</param>
    /// <returns>The revealed entry, or null when the id is not in the library.</returns>
    public LibraryEntryViewModel? RevealEntry(string libraryId)
    {
        if (string.IsNullOrEmpty(libraryId))
        {
            return null;
        }

        LibraryEntryViewModel? entry = null;
        foreach (var candidate in _allEntries)
        {
            if (string.Equals(candidate.Id, libraryId, StringComparison.Ordinal))
            {
                entry = candidate;
                break;
            }
        }

        if (entry == null)
        {
            return null;
        }

        // A live search hides the tree; clear it first (immediate, restores the
        // previous expansion) so the reveal has real tree items to walk.
        if (_searchText.Length > 0)
        {
            SearchText = string.Empty;
        }

        ClearSelection();

        var path = FindTreePath(entry);
        if (path == null)
        {
            return null;
        }

        foreach (var step in path)
        {
            if (step is LibraryCategoryViewModel category)
            {
                category.IsExpanded = true;
            }
        }

        entry.IsSelected = true;
        EntryRevealRequested?.Invoke(this, new LibraryRevealEventArgs(path));
        return entry;
    }

    // ----- searching -----------------------------------------------------------

    /// <summary>
    /// Ranks the library against a search text and returns the best hits,
    /// without touching the panel's own search state. Used by the canvas
    /// quick-search popup (Space bar).
    /// </summary>
    /// <param name="searchText">Raw search text (empty returns no hits).</param>
    /// <param name="maxResults">Cap on the number of hits returned.</param>
    public List<LibraryEntryViewModel> QuickSearch(string searchText, int maxResults)
    {
        var results = new List<LibraryEntryViewModel>();
        var tokens = LibrarySearchText.Tokenize(searchText);
        if (tokens.Length == 0 || maxResults <= 0)
        {
            return results;
        }

        var hits = new List<KeyValuePair<int, LibraryEntryViewModel>>();
        foreach (var entry in _allEntries)
        {
            int rank = entry.MatchRank(tokens);
            if (rank >= 0)
            {
                hits.Add(new KeyValuePair<int, LibraryEntryViewModel>(rank, entry));
            }
        }

        hits.Sort(CompareHits);
        int shown = Math.Min(hits.Count, maxResults);
        for (int i = 0; i < shown; i++)
        {
            results.Add(hits[i].Value);
        }

        return results;
    }

    private void OnSearchTimerTick(object? sender, EventArgs e)
    {
        _searchTimer.Stop();
        ApplySearch();
    }

    private void ApplySearch()
    {
        var tokens = LibrarySearchText.Tokenize(_searchText);
        if (tokens.Length == 0)
        {
            SearchResults.Clear();
            SearchStatusText = string.Empty;
            IsSearching = false;
            return;
        }

        var hits = new List<KeyValuePair<int, LibraryEntryViewModel>>();
        foreach (var entry in _allEntries)
        {
            int rank = entry.MatchRank(tokens);
            if (rank >= 0)
            {
                hits.Add(new KeyValuePair<int, LibraryEntryViewModel>(rank, entry));
            }
        }

        hits.Sort(CompareHits);

        SearchResults.Clear();
        int shown = Math.Min(hits.Count, MaxSearchResults);
        for (int i = 0; i < shown; i++)
        {
            SearchResults.Add(hits[i].Value);
        }

        if (hits.Count == 0)
        {
            SearchStatusText = "No nodes match '" + _searchText.Trim() + "'.";
        }
        else if (hits.Count > MaxSearchResults)
        {
            SearchStatusText = "Showing " + MaxSearchResults.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                               " of " + hits.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                               " matches — refine your search.";
        }
        else
        {
            SearchStatusText = string.Empty;
        }

        IsSearching = true;
    }

    private static int CompareHits(
        KeyValuePair<int, LibraryEntryViewModel> left,
        KeyValuePair<int, LibraryEntryViewModel> right)
    {
        int byRank = left.Key.CompareTo(right.Key);
        if (byRank != 0)
        {
            return byRank;
        }

        int byName = string.Compare(left.Value.Name, right.Value.Name, StringComparison.OrdinalIgnoreCase);
        return byName != 0
            ? byName
            : string.Compare(left.Value.Category, right.Value.Category, StringComparison.OrdinalIgnoreCase);
    }

    // ----- favourites / descriptions -------------------------------------------

    private void ToggleFavorite(LibraryEntryViewModel? entry)
    {
        if (entry == null)
        {
            return;
        }

        entry.IsFavorite = !entry.IsFavorite;
        _settings?.SetFavorite(entry.Id, entry.IsFavorite);

        // Only the tree owns a favourites section; the flat search results
        // update in place through the IsFavorite binding.
        RebuildTree();
    }

    private void UpdateDescriptionVisibility()
    {
        foreach (var entry in _allEntries)
        {
            entry.IsDescriptionVisible = _showDescriptions && entry.HasDescription;
        }
    }

    // ----- tree ----------------------------------------------------------------

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

    private static void ClearSelectionRecursive(LibraryCategoryViewModel category)
    {
        category.IsSelected = false;
        foreach (var child in category.Children)
        {
            if (child is LibraryCategoryViewModel sub)
            {
                ClearSelectionRecursive(sub);
            }
            else if (child is LibraryEntryViewModel entry)
            {
                entry.IsSelected = false;
            }
        }
    }

    private void RebuildTree()
    {
        // Snapshot the user's expansion state so rebuilds (favourite toggles,
        // registry refreshes) keep the folders they opened. The tree always
        // shows the full catalog — searching renders a separate flat list.
        _expandedPaths.Clear();
        foreach (var item in RootItems)
        {
            if (item is LibraryCategoryViewModel category &&
                !string.Equals(category.Name, FavoritesCategoryName, StringComparison.Ordinal))
            {
                CollectExpandedPaths(category, category.Name);
            }
        }

        RootItems.Clear();

        var roots = new Dictionary<string, LibraryCategoryViewModel>(StringComparer.OrdinalIgnoreCase);
        var ordered = _allEntries
            .OrderBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var entry in ordered)
        {
            var category = ResolveCategory(roots, entry.Category);
            category.Children.Add(entry);
        }

        // At each leaf, split the nodes into Create / Modify / Info headers so it
        // is obvious what each does. The category tree above is untouched.
        foreach (var root in roots.Values)
        {
            GroupLeafEntriesByFunction(root);
        }

        // The pinned favourites section always sits at the top, expanded.
        var favorites = ordered
            .Where(e => e.IsFavorite)
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (favorites.Count > 0)
        {
            var section = new LibraryCategoryViewModel(FavoritesCategoryName, "Favorites") { IsExpanded = true };
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

    // The fixed order the function headers appear in at a leaf.
    private static readonly Dyncamelo.Core.Graph.NodeFunction[] FunctionOrder =
    {
        Dyncamelo.Core.Graph.NodeFunction.Create,
        Dyncamelo.Core.Graph.NodeFunction.Modify,
        Dyncamelo.Core.Graph.NodeFunction.Info,
    };

    /// <summary>
    /// Recursively replaces a category's direct node entries with Create / Modify /
    /// Info sub-headers — but only where a leaf actually mixes ≥2 functions, so a
    /// single-purpose folder stays a flat list. Sub-categories are grouped too,
    /// then kept ahead of the function headers.
    /// </summary>
    private static void GroupLeafEntriesByFunction(LibraryCategoryViewModel category)
    {
        var subCategories = new List<LibraryCategoryViewModel>();
        var entries = new List<LibraryEntryViewModel>();
        foreach (var child in category.Children)
        {
            if (child is LibraryCategoryViewModel sub)
            {
                subCategories.Add(sub);
            }
            else if (child is LibraryEntryViewModel entry)
            {
                entries.Add(entry);
            }
        }

        foreach (var sub in subCategories)
        {
            GroupLeafEntriesByFunction(sub);
        }

        if (entries.Count == 0)
        {
            return;
        }

        // Every leaf's nodes sit under a Create / Modify / Info header, even when
        // they are all one function, so the classification is always visible.
        category.Children.Clear();
        foreach (var sub in subCategories)
        {
            category.Children.Add(sub);
        }

        foreach (var function in FunctionOrder)
        {
            LibraryCategoryViewModel? group = null;
            foreach (var entry in entries)
            {
                if (entry.Function != function)
                {
                    continue;
                }

                group ??= LibraryCategoryViewModel.CreateFunctionGroup(function);
                group.Children.Add(entry);
            }

            if (group != null)
            {
                category.Children.Add(group);
            }
        }
    }

    private void CollectExpandedPaths(LibraryCategoryViewModel category, string path)
    {
        if (category.IsExpanded)
        {
            _expandedPaths.Add(path);
        }

        foreach (var child in category.Children)
        {
            if (child is LibraryCategoryViewModel sub)
            {
                CollectExpandedPaths(sub, path + "." + sub.Name);
            }
        }
    }

    private LibraryCategoryViewModel ResolveCategory(
        Dictionary<string, LibraryCategoryViewModel> roots,
        string categoryPath)
    {
        var segments = SplitCategory(categoryPath);

        var path = segments[0];
        if (!roots.TryGetValue(segments[0], out var current))
        {
            // The top-level segment is also its icon key (sub-categories get none).
            current = new LibraryCategoryViewModel(segments[0], segments[0])
            {
                IsExpanded = _expandedPaths.Contains(path)
            };
            roots[segments[0]] = current;
        }

        for (int i = 1; i < segments.Length; i++)
        {
            path = path + "." + segments[i];
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
                child = new LibraryCategoryViewModel(segments[i])
                {
                    IsExpanded = _expandedPaths.Contains(path)
                };
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

    private static string[] SplitCategory(string categoryPath)
    {
        return string.IsNullOrEmpty(categoryPath)
            ? new[] { "Other" }
            : categoryPath.Split('.');
    }

    /// <summary>
    /// Walks the visible tree for the entry's real category location (the
    /// favourites section is skipped) and returns root-to-entry path items.
    /// </summary>
    private IReadOnlyList<object>? FindTreePath(LibraryEntryViewModel entry)
    {
        var segments = SplitCategory(entry.Category);
        var path = new List<object>(segments.Length + 1);

        LibraryCategoryViewModel? current = null;
        foreach (var item in RootItems)
        {
            if (item is LibraryCategoryViewModel category &&
                !string.Equals(category.Name, FavoritesCategoryName, StringComparison.Ordinal) &&
                string.Equals(category.Name, segments[0], StringComparison.OrdinalIgnoreCase))
            {
                current = category;
                break;
            }
        }

        if (current == null)
        {
            return null;
        }

        path.Add(current);
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
                return null;
            }

            current = child;
            path.Add(current);
        }

        if (current.Children.Contains(entry))
        {
            path.Add(entry);
            return path;
        }

        // At a mixed leaf the entry sits under a Create/Modify/Info header — add it.
        foreach (var child in current.Children)
        {
            if (child is LibraryCategoryViewModel group && group.IsFunctionGroup &&
                group.Children.Contains(entry))
            {
                path.Add(group);
                path.Add(entry);
                return path;
            }
        }

        return null;
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
