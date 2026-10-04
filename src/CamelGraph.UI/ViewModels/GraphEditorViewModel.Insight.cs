using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.UI.Mvvm;

namespace CamelGraph.UI.ViewModels;

/// <summary>Which list the side panel shows.</summary>
public enum InfoPanel
{
    /// <summary>Closed.</summary>
    None,

    /// <summary>Nodes that failed or warned in the last run.</summary>
    Problems,

    /// <summary>The undo history, to jump back or forward to any step.</summary>
    History,

    /// <summary>Saved views of the canvas.</summary>
    Bookmarks,
}

/// <summary>The part of the canvas the user should be shown.</summary>
public sealed class RevealEventArgs : EventArgs
{
    /// <summary>Creates the request.</summary>
    /// <param name="bounds">Canvas-space rectangle to bring into view.</param>
    /// <param name="zoom">Zoom to set, or 0 to keep the current one.</param>
    /// <param name="alwaysCenter">True to centre on the rectangle even when it is already visible.</param>
    public RevealEventArgs(Rect bounds, double zoom = 0d, bool alwaysCenter = false)
    {
        Bounds = bounds;
        Zoom = zoom;
        AlwaysCenter = alwaysCenter;
    }

    /// <summary>Canvas-space rectangle to bring into view.</summary>
    public Rect Bounds { get; }

    /// <summary>Zoom to set, or 0 to keep the current one.</summary>
    public double Zoom { get; }

    /// <summary>Centre even when the rectangle is already visible.</summary>
    public bool AlwaysCenter { get; }
}

/// <summary>Where the canvas view is now: the centre (canvas space) and the zoom.</summary>
public readonly struct ViewSnapshot
{
    /// <summary>Creates a snapshot.</summary>
    public ViewSnapshot(Point center, double zoom)
    {
        Center = center;
        Zoom = zoom;
    }

    /// <summary>Centre of the view in canvas space.</summary>
    public Point Center { get; }

    /// <summary>Zoom factor.</summary>
    public double Zoom { get; }
}

/// <summary>
/// Finding your way: the problems list and F8, the undo history, bookmarks, "why didn't this run?", running up to a node,
/// framing the selection, arrow-key navigation, the hint line, and finding a node by name.
/// </summary>
public partial class GraphEditorViewModel
{
    // Nodes have no measured size in the view model; this is roughly a normal node, used to centre on one.
    private const double NodeGuessWidth = 240d;
    private const double NodeGuessHeight = 110d;

    private InfoPanel _infoPanel;
    private string _hintText = string.Empty;
    private ICommand? _toggleProblemsCommand;
    private ICommand? _toggleHistoryCommand;
    private ICommand? _toggleBookmarksCommand;
    private ICommand? _closeInfoPanelCommand;
    private ICommand? _addBookmarkCommand;
    private ICommand? _nextProblemCommand;
    private ICommand? _previousProblemCommand;
    private ICommand? _explainCommand;
    private ICommand? _runToHereCommand;
    private ICommand? _frameSelectedCommand;
    private ICommand? _findNodeCommand;
    private ICommand? _navLeftCommand;
    private ICommand? _navRightCommand;
    private ICommand? _navUpCommand;
    private ICommand? _navDownCommand;
    private IReadOnlyList<NodeProblem> _problems = new List<NodeProblem>();

    /// <summary>Asks the view to bring part of the canvas into view.</summary>
    public event EventHandler<RevealEventArgs>? RevealRequested;

    /// <summary>Reads where the canvas view is (set by the view); used to make a bookmark.</summary>
    public Func<ViewSnapshot>? ViewProvider { get; set; }

    // ----- the side panel ---------------------------------------------------------------------

    /// <summary>The list the side panel shows, or <see cref="InfoPanel.None"/>.</summary>
    public InfoPanel ActiveInfoPanel
    {
        get => _infoPanel;
        private set
        {
            if (SetProperty(ref _infoPanel, value))
            {
                OnPropertyChanged(nameof(IsInfoPanelOpen));
                OnPropertyChanged(nameof(InfoPanelTitle));
                OnPropertyChanged(nameof(InfoPanelEmptyText));
            }
        }
    }

    /// <summary>True while the side panel is shown.</summary>
    public bool IsInfoPanelOpen => _infoPanel != InfoPanel.None;

    /// <summary>The panel's heading.</summary>
    public string InfoPanelTitle
    {
        get
        {
            switch (_infoPanel)
            {
                case InfoPanel.Problems: return "Problems";
                case InfoPanel.History: return "Undo history";
                case InfoPanel.Bookmarks: return "Bookmarks";
                default: return string.Empty;
            }
        }
    }

    /// <summary>What the panel says when its list is empty.</summary>
    public string InfoPanelEmptyText
    {
        get
        {
            switch (_infoPanel)
            {
                case InfoPanel.Problems: return "No problems. Nodes that fail or warn when the graph runs are listed here.";
                case InfoPanel.History: return "Nothing to undo yet.";
                case InfoPanel.Bookmarks: return "No bookmarks. Bookmark This View saves where you are on the canvas.";
                default: return string.Empty;
            }
        }
    }

    /// <summary>The rows of the panel.</summary>
    public ObservableCollection<PanelItemViewModel> InfoPanelItems { get; } = new ObservableCollection<PanelItemViewModel>();

    /// <summary>True when the panel has no rows (shows <see cref="InfoPanelEmptyText"/>).</summary>
    public bool IsInfoPanelEmpty => InfoPanelItems.Count == 0;

    /// <summary>Tooltip of the error and warning counts in the status bar.</summary>
    public string ProblemsTooltip =>
        "Show the problems list" + ShortcutSuffix("view.problems") + " — " + (ShortcutOf("graph.nextproblem") ?? "the commands") + " walks through them";

    private string? ShortcutOf(string id) => _keymap.ShortcutOf(id);

    /// <summary>Shows the problems list, or closes it when it is showing.</summary>
    public ICommand ToggleProblemsCommand => _toggleProblemsCommand ??= new RelayCommand(() => ToggleInfoPanel(InfoPanel.Problems));

    /// <summary>Shows the undo history, or closes it when it is showing.</summary>
    public ICommand ToggleHistoryCommand => _toggleHistoryCommand ??= new RelayCommand(() => ToggleInfoPanel(InfoPanel.History));

    /// <summary>Shows the bookmarks, or closes them when they are showing.</summary>
    public ICommand ToggleBookmarksCommand => _toggleBookmarksCommand ??= new RelayCommand(() => ToggleInfoPanel(InfoPanel.Bookmarks));

    /// <summary>Closes the side panel.</summary>
    public ICommand CloseInfoPanelCommand => _closeInfoPanelCommand ??= new RelayCommand(() => ActiveInfoPanel = InfoPanel.None);

    /// <summary>Opens a list in the side panel, or closes the panel when that list is already showing.</summary>
    /// <param name="panel">The list to show.</param>
    public void ToggleInfoPanel(InfoPanel panel)
    {
        if (_infoPanel == panel)
        {
            ActiveInfoPanel = InfoPanel.None;
            return;
        }

        ActiveInfoPanel = panel;
        RefreshInfoPanel();
    }

    private void RefreshInfoPanel()
    {
        InfoPanelItems.Clear();
        switch (_infoPanel)
        {
            case InfoPanel.Problems:
                foreach (var problem in _problems)
                {
                    var captured = problem;
                    InfoPanelItems.Add(new PanelItemViewModel(
                        problem.Severity == ProblemSeverity.Error ? PanelItemKind.Error : PanelItemKind.Warning,
                        problem.Node.Name,
                        problem.Text,
                        () => GoToNode(captured.Node)));
                }

                break;
            case InfoPanel.History:
                FillHistory();
                break;
            case InfoPanel.Bookmarks:
                foreach (var bookmark in _graph.Bookmarks)
                {
                    var captured = bookmark;
                    InfoPanelItems.Add(new PanelItemViewModel(
                        PanelItemKind.Bookmark,
                        bookmark.Name,
                        string.Empty,
                        () => GoToBookmark(captured),
                        () => RemoveBookmark(captured)));
                }

                break;
        }

        OnPropertyChanged(nameof(IsInfoPanelEmpty));
    }

    // ----- problems ------------------------------------------------------------------------------

    /// <summary>The nodes in error or with a warning after the last run, errors first.</summary>
    public IReadOnlyList<NodeProblem> CurrentProblems => _problems;

    private void RefreshProblems()
    {
        _problems = Problems.Collect(_graph);
        OnPropertyChanged(nameof(CurrentProblems));
        if (_infoPanel == InfoPanel.Problems)
        {
            RefreshInfoPanel();
        }
    }

    /// <summary>Selects the next node with a problem and brings it into view (F8).</summary>
    public ICommand NextProblemCommand => _nextProblemCommand ??= new RelayCommand(() => StepProblem(forward: true));

    /// <summary>Selects the previous node with a problem and brings it into view (Shift+F8).</summary>
    public ICommand PreviousProblemCommand => _previousProblemCommand ??= new RelayCommand(() => StepProblem(forward: false));

    /// <summary>Goes to the next (or previous) problem after the selected node.</summary>
    /// <param name="forward">True for the next, false for the previous.</param>
    public void StepProblem(bool forward)
    {
        if (_problems.Count == 0)
        {
            StatusMessage = "No problems in the last run.";
            return;
        }

        var current = SelectedItems.OfType<NodeViewModel>().LastOrDefault()?.Model;
        var problem = Problems.Step(_problems, current, forward);
        if (problem == null)
        {
            return;
        }

        GoToNode(problem.Node);
        var index = _problems.ToList().IndexOf(problem) + 1;
        StatusMessage = (problem.Severity == ProblemSeverity.Error ? "Error" : "Warning") + " " +
                        index.ToString(CultureInfo.InvariantCulture) + " of " + _problems.Count.ToString(CultureInfo.InvariantCulture) +
                        " — " + problem.Node.Name + ": " + problem.Text;
    }

    /// <summary>Selects a node and brings it into view.</summary>
    /// <param name="node">The node to go to.</param>
    /// <returns>False when the node is not on the canvas (it lives in another node group).</returns>
    public bool GoToNode(NodeModel node)
    {
        var viewModel = node == null ? null : FindNodeViewModel(node);
        if (viewModel == null)
        {
            return false;
        }

        SelectedItems.Clear();
        SelectedItems.Add(viewModel);
        RevealRequested?.Invoke(this, new RevealEventArgs(NodeBounds(viewModel)));
        return true;
    }

    private static Rect NodeBounds(CanvasItemViewModel item) =>
        new Rect(item.Location.X, item.Location.Y, NodeGuessWidth, NodeGuessHeight);

    /// <summary>Says, in the status bar, why the selected node did or did not run (I).</summary>
    public ICommand ExplainSelectedCommand => _explainCommand ??= new RelayCommand(ExplainSelected);

    private void ExplainSelected()
    {
        var node = SelectedItems.OfType<NodeViewModel>().LastOrDefault()?.Model;
        if (node == null)
        {
            StatusMessage = "Select a node to see why it did or did not run.";
            return;
        }

        StatusMessage = RunExplanation.Explain(_graph, node);
    }

    // ----- running up to a node ------------------------------------------------------------------

    /// <summary>Runs only what the selected nodes depend on, then stops (Shift+F5).</summary>
    public ICommand RunToHereCommand => _runToHereCommand ??= new RelayCommand(RunToSelected);

    /// <summary>Brings the selected nodes (and what feeds them) up to date, leaving everything after them as it was.</summary>
    public void RunToSelected()
    {
        var targets = SelectedItems.OfType<NodeViewModel>().Select(n => n.Model).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Select the node to run up to.";
            return;
        }

        // Inside a node group the graph that runs is the document, so "up to here" means up to the instance that was opened.
        if (IsInsideGroup)
        {
            targets = new List<NodeModel> { RootInstance() };
        }

        RunGraph(interactive: true, upTo: targets);
    }

    private NodeModel RootInstance() => _levels[0].Instance;

    // ----- undo history --------------------------------------------------------------------------

    private void FillHistory()
    {
        var undoLabels = _undo.UndoLabels;
        var redoLabels = _undo.RedoLabels;
        InfoPanelItems.Add(new PanelItemViewModel(PanelItemKind.Start, "Original state", "before the first edit", () => JumpInHistory(0), isCurrent: undoLabels.Count == 0));
        for (var i = 0; i < undoLabels.Count; i++)
        {
            var target = i + 1;
            InfoPanelItems.Add(new PanelItemViewModel(PanelItemKind.Step, undoLabels[i], string.Empty, () => JumpInHistory(target), isCurrent: target == undoLabels.Count));
        }

        for (var i = 0; i < redoLabels.Count; i++)
        {
            var target = undoLabels.Count + 1 + i;
            InfoPanelItems.Add(new PanelItemViewModel(PanelItemKind.Future, redoLabels[i], "undone", () => JumpInHistory(target)));
        }
    }

    /// <summary>Undoes or redoes until exactly <paramref name="undoCount"/> steps are applied.</summary>
    /// <param name="undoCount">0 is the original state.</param>
    public void JumpInHistory(int undoCount)
    {
        FinishOpenGesture();
        var moved = _undo.JumpTo(undoCount);
        if (moved != 0)
        {
            StatusMessage = (moved < 0 ? "Went back " : "Went forward ") + Math.Abs(moved).ToString(CultureInfo.InvariantCulture) +
                            " step(s)" + (_hasRunThisGraph && moved < 0 ? " (graph only — Navisworks changes from earlier runs are not reverted)." : ".");
        }
    }

    // ----- bookmarks -----------------------------------------------------------------------------

    /// <summary>Saves the current view of the canvas under a name (Ctrl+K).</summary>
    public ICommand AddBookmarkCommand => _addBookmarkCommand ??= new RelayCommand(AddBookmark);

    /// <summary>Asks for a name and saves the current view as a bookmark.</summary>
    public void AddBookmark()
    {
        if (ViewProvider == null)
        {
            return;
        }

        var suggestion = "View " + (_graph.Bookmarks.Count + 1).ToString(CultureInfo.InvariantCulture);
        var name = Dialogs.Prompt("Name for this view:", "Bookmark This View", suggestion);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var view = ViewProvider();
        _graph.Bookmarks.Add(new BookmarkModel { Name = name!.Trim(), X = view.Center.X, Y = view.Center.Y, Zoom = view.Zoom });
        StatusMessage = "Bookmarked '" + name.Trim() + "'.";
    }

    /// <summary>Brings a bookmarked view back.</summary>
    /// <param name="bookmark">The bookmark to go to.</param>
    public void GoToBookmark(BookmarkModel bookmark)
    {
        if (bookmark == null)
        {
            return;
        }

        RevealRequested?.Invoke(this, new RevealEventArgs(new Rect(bookmark.X, bookmark.Y, 0d, 0d), bookmark.Zoom, alwaysCenter: true));
        StatusMessage = "Went to '" + bookmark.Name + "'.";
    }

    /// <summary>Deletes a bookmark.</summary>
    /// <param name="bookmark">The bookmark to remove.</param>
    public void RemoveBookmark(BookmarkModel bookmark)
    {
        if (bookmark != null && _graph.Bookmarks.Remove(bookmark))
        {
            StatusMessage = "Removed bookmark '" + bookmark.Name + "'.";
        }
    }

    private void OnBookmarksChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_infoPanel == InfoPanel.Bookmarks)
        {
            RefreshInfoPanel();
        }
    }

    // ----- framing and keyboard navigation -------------------------------------------------------

    /// <summary>Zooms to the selection (Shift+F).</summary>
    public ICommand FrameSelectedCommand => _frameSelectedCommand ??= new RelayCommand(FrameSelected);

    /// <summary>Brings the selected items into view, filling the view when they are few.</summary>
    public void FrameSelected()
    {
        var items = SelectedItems.ToList();
        if (items.Count == 0)
        {
            StatusMessage = "Select something to frame.";
            return;
        }

        var bounds = NodeBounds(items[0]);
        foreach (var item in items.Skip(1))
        {
            bounds.Union(NodeBounds(item));
        }

        RevealRequested?.Invoke(this, new RevealEventArgs(bounds, zoom: -1d, alwaysCenter: true));
    }

    /// <summary>Selects the nearest node to the left (Left arrow).</summary>
    public ICommand NavigateLeftCommand => _navLeftCommand ??= new RelayCommand(() => Navigate(NavDirection.Left));

    /// <summary>Selects the nearest node to the right (Right arrow).</summary>
    public ICommand NavigateRightCommand => _navRightCommand ??= new RelayCommand(() => Navigate(NavDirection.Right));

    /// <summary>Selects the nearest node above (Up arrow).</summary>
    public ICommand NavigateUpCommand => _navUpCommand ??= new RelayCommand(() => Navigate(NavDirection.Up));

    /// <summary>Selects the nearest node below (Down arrow).</summary>
    public ICommand NavigateDownCommand => _navDownCommand ??= new RelayCommand(() => Navigate(NavDirection.Down));

    /// <summary>Moves the selection to the nearest node in a direction.</summary>
    /// <param name="direction">Where to go.</param>
    public void Navigate(NavDirection direction)
    {
        var current = SelectedItems.OfType<NodeViewModel>().LastOrDefault()?.Model;
        var target = GraphOps.Nearest(_graph.Nodes, current, direction);
        if (target != null)
        {
            GoToNode(target);
        }
    }

    // ----- finding a node by name ----------------------------------------------------------------

    /// <summary>Opens the palette on node names (Ctrl+F): type part of a node's name, Enter goes to it.</summary>
    public ICommand FindNodeCommand => _findNodeCommand ??= new RelayCommand(() => OpenPalette(NodeSearchPrefix));

    /// <summary>The character that turns a palette search into a search for nodes on the canvas.</summary>
    public const string NodeSearchPrefix = "@";

    // Nodes of the open canvas whose name, category or type matches the words typed (after an optional "@").
    private IEnumerable<PaletteEntry> NodeEntries(string query)
    {
        var text = query.StartsWith(NodeSearchPrefix, StringComparison.Ordinal) ? query.Substring(1) : query;
        var words = text.ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var listAll = query.StartsWith(NodeSearchPrefix, StringComparison.Ordinal);
        if (words.Length == 0 && !listAll)
        {
            yield break;
        }

        var matches = _graph.Nodes
            .Where(n => !(n is CamelGraph.Core.Groups.GroupInputNode) && !(n is CamelGraph.Core.Groups.GroupOutputNode))
            .Where(n =>
            {
                var hay = (n.Name + " " + n.Category).ToLowerInvariant();
                return words.All(hay.Contains);
            })
            .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n.X)
            .Take(30);
        foreach (var node in matches)
        {
            yield return new PaletteEntry("node:" + node.Id.ToString("N"), node.Name, "Node · " + (node.Category.Length > 0 ? node.Category : "canvas"), string.Empty, false, node);
        }
    }

    /// <summary>True when dragging a number past the screen edge carries on from the other side (Settings ▸ Editing).</summary>
    public bool ScrubWrapsPointer
    {
        get => _settings.GetBool(SettingKeys.ScrubWrap, true);
        set
        {
            SetPreference(SettingKeys.ScrubWrap, value, true, nameof(ScrubWrapsPointer));
            Views.ScrubNumberBox.WrapPointerAtScreenEdge = value;
        }
    }

    /// <summary>Shows a message in the status bar (for view models and views that act on the editor's behalf).</summary>
    /// <param name="message">The text.</param>
    public void ReportStatus(string message) => StatusMessage = message;

    // ----- wire focus ----------------------------------------------------------------------------

    private bool _wireFocusQueued;

    /// <summary>True when the wires of the selected nodes are drawn heavier and the others fainter (Settings ▸ Canvas).</summary>
    public bool FocusSelectedWires
    {
        get => _settings.GetBool(SettingKeys.WireFocus, true);
        set
        {
            SetPreference(SettingKeys.WireFocus, value, true, nameof(FocusSelectedWires));
            RefreshWireFocus();
        }
    }

    // A marquee or Select All changes the selection hundreds of times in a row; update the wires once afterwards.
    private void QueueWireFocus()
    {
        if (_wireFocusQueued)
        {
            return;
        }

        _wireFocusQueued = true;
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            new Action(() =>
            {
                _wireFocusQueued = false;
                RefreshWireFocus();
            }),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>Emphasises the wires of the selected nodes and dims the rest, or restores them all when nothing is selected.</summary>
    public void RefreshWireFocus()
    {
        var selected = new HashSet<NodeModel>(SelectedItems.OfType<NodeViewModel>().Select(n => n.Model));
        var active = FocusSelectedWires && selected.Count > 0;
        foreach (var wire in Connections)
        {
            var touches = active && (selected.Contains(wire.Model.SourceNode) || selected.Contains(wire.Model.TargetNode));
            wire.SetFocus(touches, active && !touches);
        }
    }

    // ----- hints ---------------------------------------------------------------------------------

    /// <summary>The one-line hint for what the editor is doing now.</summary>
    public string HintText
    {
        get => _hintText;
        private set => SetProperty(ref _hintText, value);
    }

    /// <summary>True when the status bar shows the hint line (Settings ▸ Appearance).</summary>
    public bool ShowStatusHints
    {
        get => _settings.GetBool(SettingKeys.StatusHints, true);
        set
        {
            SetPreference(SettingKeys.StatusHints, value, true, nameof(ShowStatusHints));
        }
    }

    /// <summary>True when an empty canvas shows hints for getting started (Settings ▸ Appearance).</summary>
    public bool ShowEmptyCanvasHints
    {
        get => _settings.GetBool(SettingKeys.EmptyHints, true);
        set => SetPreference(SettingKeys.EmptyHints, value, true, nameof(ShowEmptyCanvasHints), nameof(IsEmptyCanvasHintVisible), nameof(IsStartScreenVisible), nameof(IsEmptyHintOnlyVisible));
    }

    /// <summary>True while the getting-started hints are on screen: an empty canvas, hints switched on.</summary>
    public bool IsEmptyCanvasHintVisible => ShowEmptyCanvasHints && NodeCount == 0 && !IsInsideGroup && Items.Count == 0;

    /// <summary>The lines of the getting-started hint.</summary>
    public IReadOnlyList<string> EmptyCanvasHintLines => HintLine.ForEmptyCanvas(_keymap, DoubleClickAction);

    /// <summary>The window's scale in percent ("90" … "150"; 100 by default).</summary>
    public string UiScale
    {
        get => _settings.GetString(SettingKeys.UiScale, "100");
        set => SetPreference(SettingKeys.UiScale, value, "100", nameof(UiScale), nameof(UiScaleFactor));
    }

    /// <summary>The window's scale as a factor (1.0 = 100%).</summary>
    public double UiScaleFactor =>
        double.TryParse(UiScale, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) && percent >= 50d && percent <= 200d
            ? percent / 100d
            : 1d;

    private void RefreshHint()
    {
        var nodes = SelectedItems.OfType<NodeViewModel>().ToList();
        HintText = HintLine.ForStatusBar(
            new HintContext
            {
                IsRunning = IsRunning,
                DraggingWire = PendingConnection.IsVisible,
                SelectedNodes = nodes.Count,
                SelectedGroupInstance = nodes.Any(n => n.IsGroupInstance),
                InsideGroup = IsInsideGroup,
                HasNodes = _graph.Nodes.Count > 0,
                HasErrors = ErrorCount > 0,
            },
            _keymap);
        OnPropertyChanged(nameof(IsEmptyCanvasHintVisible));
        OnPropertyChanged(nameof(EmptyCanvasHintLines));
        NotifyStartScreen();
    }
}
