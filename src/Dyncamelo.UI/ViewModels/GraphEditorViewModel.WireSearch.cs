using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Graph;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// Dropping a dragged wire on empty canvas opens the quick search filtered to the nodes that can
/// take (or supply) that wire; choosing one adds it and connects it in a single undo step.
/// </summary>
public partial class GraphEditorViewModel
{
    private const double DefaultNodeWidth = 220d;

    // Ports of a never-added instance per library id: enough to test compatibility without touching the graph.
    private readonly Dictionary<string, NodeModel?> _searchTemplates = new Dictionary<string, NodeModel?>(StringComparer.Ordinal);

    /// <summary>One line describing what the filtered search offers ("Nodes that accept a Number from Add.result"); empty when unfiltered.</summary>
    public string QuickSearchContext
    {
        get => _quickSearchContext;
        private set
        {
            if (SetProperty(ref _quickSearchContext, value))
            {
                OnPropertyChanged(nameof(HasQuickSearchContext));
            }
        }
    }

    /// <summary>True while the search is filtered by a dragged wire.</summary>
    public bool HasQuickSearchContext => _quickSearchContext.Length > 0;

    private static string DescribeSearchSource(ConnectorViewModel source)
    {
        var kind = KindWords(source.Kind);
        var where = source.Node.Title + "." + source.Port.Name;
        return source.IsInput
            ? "Nodes that produce " + kind + " for " + where
            : "Nodes that accept " + kind + " from " + where;
    }

    private static string KindWords(PortKind kind)
    {
        var family = kind.Family == PortFamily.Any ? "any value" : "a " + kind.Family.ToString().ToLowerInvariant();
        return kind.Depth == PortDepth.List || kind.Depth == PortDepth.Nested ? family + " list" : family;
    }

    private bool IsOverNode(Point location)
    {
        foreach (var node in Items.OfType<NodeViewModel>())
        {
            var size = node.Size;
            if (size.Width > 0 && size.Height > 0 && new Rect(node.Location, size).Contains(location))
            {
                return true;
            }
        }

        return false;
    }

    private NodeModel? SearchTemplate(string libraryId)
    {
        // A node group's sockets can change, so its template is never cached.
        if (libraryId.StartsWith(GroupLibraryPrefix, StringComparison.Ordinal))
        {
            return CreateNodeFromLibrary(libraryId, out _);
        }

        if (!_searchTemplates.TryGetValue(libraryId, out var template))
        {
            try
            {
                template = Registry.CreateZeroTouchNode(libraryId) ?? Registry.CreateNode(libraryId);
            }
            catch (Exception)
            {
                template = null;
            }

            _searchTemplates[libraryId] = template;
        }

        return template;
    }

    /// <summary>How well the best matching port of an entry fits the dragged socket (0 exact … 2 loose, 3 unusable).</summary>
    internal int SearchRank(LibraryEntryViewModel entry, ConnectorViewModel source)
    {
        var template = SearchTemplate(entry.Id);
        if (template == null)
        {
            return 3;
        }

        if (source.IsInput)
        {
            var output = GraphOps.BestOutputFor(source.Port, template);
            return output == null ? 3 : GraphOps.Rank(PortKinds.Compare(output, source.Port));
        }

        var input = GraphOps.BestInputFor(_graph, source.Port, template, freeOnly: false);
        return input == null ? 3 : GraphOps.Rank(PortKinds.Compare(source.Port, input));
    }

    internal IReadOnlyList<LibraryEntryViewModel> FindQuickSearchHits(string text, ConnectorViewModel? source, int max)
    {
        if (source == null)
        {
            return Library.QuickSearch(text, max);
        }

        var typed = LibrarySearchText.Tokenize(text).Length > 0;
        IReadOnlyList<LibraryEntryViewModel> candidates;
        if (typed)
        {
            candidates = Library.QuickSearch(text, 400);
        }
        else
        {
            // Starred and recent nodes first, then everything else in library order.
            var suggested = Library.Suggested(400);
            candidates = suggested.Concat(Library.AllEntries.Where(e => !suggested.Contains(e))).ToList();
        }

        // Exact and convertible fits first, loose ones after; each group keeps its search order.
        var tight = new List<LibraryEntryViewModel>();
        var loose = new List<LibraryEntryViewModel>();
        foreach (var entry in candidates)
        {
            var rank = SearchRank(entry, source);
            if (rank <= 1)
            {
                tight.Add(entry);
            }
            else if (rank == 2)
            {
                loose.Add(entry);
            }

            if (tight.Count >= max)
            {
                break;
            }
        }

        return tight.Concat(loose).Take(max).ToList();
    }

    /// <summary>Adds a node from the library and connects its best matching socket to <paramref name="source"/>.</summary>
    internal NodeViewModel? AddNodeConnectedTo(string libraryId, Point dropLocation, ConnectorViewModel source)
    {
        var location = dropLocation;
        var created = AddNode(libraryId, location);
        if (created == null)
        {
            return null;
        }

        var node = created.Model;
        if (source.IsInput)
        {
            // The new node feeds the socket, so it sits to the left of the drop point.
            var width = node.Ui.Width ?? DefaultNodeWidth;
            node.X = dropLocation.X - width;
            var output = GraphOps.BestOutputFor(source.Port, node);
            if (output != null)
            {
                _graph.Connect(output, source.Port);
            }
        }
        else
        {
            var input = GraphOps.BestInputFor(_graph, source.Port, node, freeOnly: false);
            if (input != null)
            {
                _graph.Connect(source.Port, input);
            }
        }

        return created;
    }
}
