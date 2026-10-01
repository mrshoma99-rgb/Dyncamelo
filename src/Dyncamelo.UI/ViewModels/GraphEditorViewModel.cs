using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Serialization;
using Dyncamelo.UI.Mvvm;
using Dyncamelo.UI.Services;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// One sample graph offered by the "Sample graphs" menu: display name (file name
/// without extension), full path and the command that opens it.
/// </summary>
public class SampleGraphViewModel
{
    /// <summary>Creates the menu item.</summary>
    /// <param name="name">Display name (file name without extension).</param>
    /// <param name="filePath">Full path of the .dyc file.</param>
    /// <param name="openCommand">Command that opens the sample (parameter: this item).</param>
    public SampleGraphViewModel(string name, string filePath, ICommand openCommand)
    {
        Name = name;
        FilePath = filePath;
        OpenCommand = openCommand;
    }

    /// <summary>Display name (file name without extension).</summary>
    public string Name { get; }

    /// <summary>Full path of the .dyc file (menu tooltip).</summary>
    public string FilePath { get; }

    /// <summary>Opens this sample (parameter: this item).</summary>
    public ICommand OpenCommand { get; }
}

/// <summary>
/// The whole editor: wraps a Core <see cref="GraphModel"/> behind observable
/// collections for the Nodify canvas, drives the <see cref="GraphEngine"/>
/// (manual runs and debounced automatic runs), and handles .dyc open/save.
/// The host supplies an <see cref="EvaluationContextFactory"/> to inject
/// services (e.g. the Navisworks document provider) into each run.
/// </summary>
public partial class GraphEditorViewModel : ObservableObject, IConnectorHost
{
    private const string FileFilter = "Dyncamelo Graph (*.dyc)|*.dyc|All files (*.*)|*.*";

    private readonly GraphEngine _engine = new GraphEngine();
    private readonly DispatcherTimer _autoRunTimer;
    private readonly UiSettingsService _settings;
    private readonly IPreviewService _preview;
    private bool _previewSelection;
    private bool _isRunning;
    private readonly UndoManager _documentUndo = new UndoManager();
    private UndoManager _undo;
    private GraphRecorder? _recorder;
    private UndoTransaction? _dragTransaction;
    private bool _hasRunThisGraph;
    private LodLevel _lodLevel = LodLevel.Full;
    private double _viewportZoom = 1d;

    private GraphModel _graph;
    private string? _currentFilePath;
    private string _statusMessage = "Ready";
    private double _lastRunMilliseconds;
    private int _nodeCount;
    private int _errorCount;
    private int _warningCount;
    private bool _showNodePreviews = true;
    private string? _clipboardFragment;
    private int _pasteGeneration;
    private string _doubleClickAction;
    private string _paletteId;
    private bool _isQuickSearchOpen;
    private string _quickSearchText = string.Empty;
    private LibraryEntryViewModel? _quickSearchSelected;
    private Point _quickSearchLocation;
    private ConnectorViewModel? _quickSearchSource;
    private string _quickSearchContext = string.Empty;

    /// <summary>Creates the editor with an empty untitled graph.</summary>
    /// <param name="registry">Node registry (already populated by the host).</param>
    /// <param name="dialogs">Dialog service; a default WPF implementation is used when null.</param>
    /// <param name="settings">Persisted UI settings (favourites, recent files); the default %APPDATA% store is used when null.</param>
    /// <param name="preview">Host preview service that highlights a selected node's outputs; a no-op is used when null.</param>
    public GraphEditorViewModel(
        NodeRegistry registry,
        IDialogService? dialogs = null,
        UiSettingsService? settings = null,
        IPreviewService? preview = null)
    {
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Dialogs = dialogs ?? new WpfDialogService();
        _settings = settings ?? new UiSettingsService();
        _settings.Changed += OnSettingsChanged;
        _preview = preview ?? new NullPreviewService();
        Library = new LibraryViewModel(registry, _settings);
        RecentFiles = new ObservableCollection<string>(_settings.RecentFiles);
        SampleGraphs = new ObservableCollection<SampleGraphViewModel>();

        Items = new ObservableCollection<CanvasItemViewModel>();
        Connections = new ObservableCollection<ConnectionViewModel>();
        SelectedItems = new ObservableCollection<CanvasItemViewModel>();
        SelectedItems.CollectionChanged += OnSelectedItemsChanged;
        SelectedConnections = new ObservableCollection<ConnectionViewModel>();
        SelectedConnections.CollectionChanged += OnSelectedConnectionsChanged;
        PendingConnection = new PendingConnectionViewModel();

        StartConnectionCommand = new RelayCommand<ConnectorViewModel>(BeginConnectionDrag, connector => connector != null);
        PendingConnection.PropertyChanged += OnPendingConnectionChanged;
        CreateConnectionCommand = new RelayCommand<ConnectorViewModel>(CompletePendingConnection);
        DisconnectConnectorCommand = new RelayCommand<ConnectorViewModel>(DisconnectConnector);
        RemoveConnectionCommand = new RelayCommand<ConnectionViewModel>(RemoveConnection);

        UndoCommand = new RelayCommand(UndoLast, () => _undo.CanUndo);
        RedoCommand = new RelayCommand(RedoLast, () => _undo.CanRedo);
        ItemsDragStartedCommand = new RelayCommand(() =>
        {
            // Every position change during one drag becomes a single undo item.
            _dragTransaction?.Dispose();
            _dragTransaction = _undo.Begin("Move");
        });
        ItemsDragCompletedCommand = new RelayCommand(() =>
        {
            try
            {
                // Dropping a free node on a wire splices it in, as part of the same undo step as the move.
                CommitInsertOnDrop();
            }
            finally
            {
                _dragTransaction?.Dispose();
                _dragTransaction = null;
            }
        });
        _undo = _documentUndo;
        _undo.Changed += OnUndoChanged;

        ToggleCollapseSelectedCommand = new RelayCommand(() => ToggleCollapse(null));
        ToggleMuteSelectedCommand = new RelayCommand(() => ToggleMute(null));
        ToggleFreezeSelectedCommand = new RelayCommand(() => ToggleFreeze(null));
        ToggleHideUnusedSelectedCommand = new RelayCommand(() => ToggleHideUnused(null));
        CollapseAllCommand = new RelayCommand(() => SetAllCollapsed(true));
        ExpandAllCommand = new RelayCommand(() => SetAllCollapsed(false));

        RunCommand = new RelayCommand(RunGraph);
        NewCommand = new RelayCommand(NewGraph);
        OpenCommand = new RelayCommand(OpenGraph);
        SaveCommand = new RelayCommand(SaveGraph);
        SaveAsCommand = new RelayCommand(SaveGraphAs);
        RenameCommand = new RelayCommand(RenameGraph);
        DeleteSelectionCommand = new RelayCommand(DeleteSelection);
        DuplicateSelectionCommand = new RelayCommand(DuplicateSelection);
        CopySelectionCommand = new RelayCommand(CopySelection);
        PasteCommand = new RelayCommand(Paste, () => _clipboardFragment != null);
        GroupSelectionCommand = new RelayCommand(GroupSelection);
        ArrangeSelectionCommand = new RelayCommand(ArrangeSelection);
        OpenRecentFileCommand = new RelayCommand<string>(OpenRecentFile);
        OpenSampleCommand = new RelayCommand<SampleGraphViewModel>(OpenSample);
        AddNodeCommand = new RelayCommand<object>(AddNodeFromParameter);
        AddNoteCommand = new RelayCommand<object>(AddNoteFromParameter);
        CaptureSelectionCommand = new RelayCommand<NodeModel>(CaptureSelection);
        ClearCapturedSelectionCommand = new RelayCommand<NodeModel>(ClearCapturedSelection);
        QuickSearchResults = new ObservableCollection<LibraryEntryViewModel>();
        CommitQuickSearchCommand = new RelayCommand<LibraryEntryViewModel>(entry => CommitQuickSearch(entry));
        RefreshSampleGraphs();

        // Settings-panel state: the double-click action and the colour palette.
        DoubleClickActions = new ObservableCollection<ChoiceOption>(new[]
        {
            new ChoiceOption("string", "Insert a String node"),
            new ChoiceOption("number", "Insert a Number node"),
            new ChoiceOption("note", "Add a note"),
            new ChoiceOption("none", "Do nothing"),
        });
        Palettes = new ObservableCollection<ChoiceOption>(
            PaletteCatalog.All.Select(p => new ChoiceOption(p.Id, p.DisplayName, p.AccentColor)));
        SelectDoubleClickActionCommand = new RelayCommand<ChoiceOption>(
            option => { if (option != null) DoubleClickAction = option.Id; });
        SelectPaletteCommand = new RelayCommand<ChoiceOption>(
            option => { if (option != null) PaletteId = option.Id; });
        _doubleClickAction = _settings.DoubleClickAction;
        _paletteId = _settings.PaletteId;
        _previewSelection = _settings.PreviewSelection;
        UpdateChoiceSelection();
        InitCommandSurface();

        _autoRunTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _autoRunTimer.Tick += OnAutoRunTimerTick;

        _graph = new GraphModel { Name = "Untitled" };
        AttachGraph(_graph);
        InitNodeGroups();
        WatchNodeGroups(_graph.NodeGroups);

        // Building the empty graph counted as edits; a new editor has nothing unsaved.
        _savedChangeCount = _changeCount;
        _autosavedChangeCount = _changeCount;
        Views.ScrubNumberBox.WrapPointerAtScreenEdge = ScrubWrapsPointer;
        RefreshHint();
    }

    /// <summary>The node registry used to create nodes and resolve .dyc files.</summary>
    public NodeRegistry Registry { get; }

    /// <summary>Dialogs used for open/save/browse interactions.</summary>
    public IDialogService Dialogs { get; }

    /// <summary>The library browser shown in the left panel.</summary>
    public LibraryViewModel Library { get; }

    /// <summary>
    /// Last opened/saved .dyc paths, most recent first (max 10, persisted in
    /// ui-settings.json, files that no longer exist pruned).
    /// </summary>
    public ObservableCollection<string> RecentFiles { get; }

    /// <summary>
    /// Sample graphs offered by the "Sample graphs" menu, ordered by name.
    /// Populated from a "Samples" folder next to the plugin assemblies (or the
    /// repository's samples folder during development); refreshed by the view
    /// each time the dropdown opens via <see cref="RefreshSampleGraphs"/>.
    /// </summary>
    public ObservableCollection<SampleGraphViewModel> SampleGraphs { get; }

    /// <summary>Canvas items (nodes and notes); bound to the editor's ItemsSource.</summary>
    public ObservableCollection<CanvasItemViewModel> Items { get; }

    /// <summary>Wires; bound to the editor's Connections.</summary>
    public ObservableCollection<ConnectionViewModel> Connections { get; }

    /// <summary>Current canvas selection; kept in sync by the editor.</summary>
    public ObservableCollection<CanvasItemViewModel> SelectedItems { get; }

    /// <summary>Currently selected wires; kept in sync by the editor.</summary>
    public ObservableCollection<ConnectionViewModel> SelectedConnections { get; }

    /// <summary>State of the wire currently being dragged.</summary>
    public PendingConnectionViewModel PendingConnection { get; }

    /// <summary>The wrapped Core graph.</summary>
    public GraphModel Graph => _graph;

    /// <summary>
    /// Creates the per-run <see cref="EvaluationContext"/>. The hosting layer
    /// registers services (e.g. the Navisworks document provider) here.
    /// A plain empty context is used when null.
    /// </summary>
    public Func<EvaluationContext>? EvaluationContextFactory { get; set; }

    /// <summary>Path of the currently open .dyc file, or null for an unsaved graph.</summary>
    public string? CurrentFilePath
    {
        get => _currentFilePath;
        private set
        {
            if (SetProperty(ref _currentFilePath, value))
            {
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    /// <summary>Window/tab caption: graph name plus file name.</summary>
    public string Title
    {
        get
        {
            var document = DocumentGraph;
            var name = (document.Name.Length > 0 ? document.Name : "Untitled") + (IsModified ? "*" : string.Empty);
            return _currentFilePath == null ? name : name + " — " + System.IO.Path.GetFileName(_currentFilePath);
        }
    }

    /// <summary>Status bar text (last action or run summary).</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>
    /// True while an interactive run is executing. Graph execution is synchronous
    /// on the UI thread (Navisworks nodes must run on the host's main thread), so
    /// the view uses this to show a busy indicator and wait cursor before the
    /// thread blocks — the run would otherwise look like a freeze. Not set for the
    /// fast debounced auto-run, which would only flicker.
    /// </summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                RefreshHint();
            }
        }
    }

    /// <summary>Wall-clock duration of the last run in milliseconds.</summary>
    public double LastRunMilliseconds
    {
        get => _lastRunMilliseconds;
        private set => SetProperty(ref _lastRunMilliseconds, value);
    }

    /// <summary>Number of nodes on the canvas.</summary>
    public int NodeCount
    {
        get => _nodeCount;
        private set
        {
            if (SetProperty(ref _nodeCount, value))
            {
                OnPropertyChanged(nameof(IsMinimapVisible));
                OnPropertyChanged(nameof(MinimapTooltip));
                RefreshHint();
            }
        }
    }

    /// <summary>Number of nodes in the Error state after the last run.</summary>
    public int ErrorCount
    {
        get => _errorCount;
        private set => SetProperty(ref _errorCount, value);
    }

    /// <summary>Number of nodes in the Warning state after the last run.</summary>
    public int WarningCount
    {
        get => _warningCount;
        private set => SetProperty(ref _warningCount, value);
    }

    /// <summary>
    /// Global toggle for the per-node value preview bubbles (toolbar). Default on;
    /// individual nodes can additionally be hidden via their pin.
    /// </summary>
    public bool ShowNodePreviews
    {
        get => _showNodePreviews;
        set
        {
            if (SetProperty(ref _showNodePreviews, value))
            {
                foreach (var item in Items)
                {
                    if (item is NodeViewModel node)
                    {
                        node.RefreshPreviewVisibility();
                    }
                }
            }
        }
    }

    /// <summary>
    /// True when the graph re-runs automatically (debounced) on every change.
    /// Mirrored to <see cref="GraphModel.RunType"/> so it persists in the .dyc file.
    /// </summary>
    public bool IsAutoRun
    {
        get => DocumentGraph.RunType == RunType.Automatic;
        set
        {
            var runType = value ? RunType.Automatic : RunType.Manual;
            if (DocumentGraph.RunType != runType)
            {
                DocumentGraph.RunType = runType;
                OnPropertyChanged();
                if (value)
                {
                    ScheduleAutoRun();
                }
            }
        }
    }

    /// <summary>Starts a wire drag; blocked from already-occupied inputs.</summary>
    public ICommand StartConnectionCommand { get; }

    /// <summary>Completes a wire drag (parameter: the drop-target connector, null on empty canvas).</summary>
    public ICommand CreateConnectionCommand { get; }

    /// <summary>Removes all wires touching a connector (Alt+click / Delete on a connector).</summary>
    public ICommand DisconnectConnectorCommand { get; }

    /// <summary>Removes one wire (Alt+click on the wire).</summary>
    public ICommand RemoveConnectionCommand { get; }

    /// <summary>Runs the dirty part of the graph now.</summary>
    public ICommand RunCommand { get; }

    /// <summary>Replaces the graph with a new empty one.</summary>
    public ICommand NewCommand { get; }

    /// <summary>Opens a .dyc file.</summary>
    public ICommand OpenCommand { get; }

    /// <summary>Saves to the current file (or asks for one).</summary>
    public ICommand SaveCommand { get; }

    /// <summary>Saves to a new file.</summary>
    public ICommand SaveAsCommand { get; }

    /// <summary>Renames the current .dyc file on disk (F2); asks for a name when unsaved.</summary>
    public ICommand RenameCommand { get; }

    /// <summary>Sets the empty-canvas double-click action (parameter: the chosen <see cref="ChoiceOption"/>).</summary>
    public ICommand SelectDoubleClickActionCommand { get; }

    /// <summary>Sets the UI colour palette (parameter: the chosen <see cref="ChoiceOption"/>).</summary>
    public ICommand SelectPaletteCommand { get; }

    /// <summary>Options for the empty-canvas double-click action (settings panel).</summary>
    public ObservableCollection<ChoiceOption> DoubleClickActions { get; }

    /// <summary>Available UI colour palettes (settings panel).</summary>
    public ObservableCollection<ChoiceOption> Palettes { get; }

    /// <summary>
    /// What double-clicking the empty canvas does ("string", "number", "note"
    /// or "none"). Persisted in ui-settings.json; the view reads it in the
    /// canvas double-click handler.
    /// </summary>
    public string DoubleClickAction
    {
        get => _doubleClickAction;
        set
        {
            if (SetProperty(ref _doubleClickAction, value))
            {
                _settings.SetDoubleClickAction(value);
                UpdateChoiceSelection();
            }
        }
    }

    /// <summary>
    /// Selected UI colour palette id. Persisted in ui-settings.json; the view
    /// watches this property and swaps the theme brushes when it changes.
    /// </summary>
    public string PaletteId
    {
        get => _paletteId;
        set
        {
            if (SetProperty(ref _paletteId, value))
            {
                _settings.SetPaletteId(value);
                UpdateChoiceSelection();
            }
        }
    }

    /// <summary>Deletes the selected nodes, notes, groups and wires (Delete key).</summary>
    public ICommand DeleteSelectionCommand { get; }

    // ----- node layout, level of detail, node-level toggles ----------------------

    /// <summary>True to re-run the graph while a number field is being scrubbed (Settings); off = commit on release.</summary>
    public bool LiveScrubEvaluation
    {
        get => _settings.LiveScrubEvaluation;
        set
        {
            if (_settings.LiveScrubEvaluation != value)
            {
                _settings.SetLiveScrubEvaluation(value);
                OnPropertyChanged();
            }
        }
    }


    /// <summary>Canvas level of detail, derived from the zoom with hysteresis.</summary>
    public LodLevel LodLevel
    {
        get => _lodLevel;
        private set
        {
            if (SetProperty(ref _lodLevel, value))
            {
                OnPropertyChanged(nameof(IsOverviewLod));
                OnPropertyChanged(nameof(WireLowDetail));
            }
        }
    }

    /// <summary>True at the overview level (wires draw as straight hairlines, nodes as headers).</summary>
    public bool IsOverviewLod => _lodLevel == LodLevel.Overview;

    /// <summary>Editor zoom, two-way bound to the canvas; drives <see cref="LodLevel"/>.</summary>
    public double ViewportZoom
    {
        get => _viewportZoom;
        set
        {
            if (SetProperty(ref _viewportZoom, value))
            {
                LodLevel = Lod.Next(_lodLevel, value);
            }
        }
    }

    /// <summary>Collapses/expands the selected nodes (H).</summary>
    public ICommand ToggleCollapseSelectedCommand { get; }

    /// <summary>Mutes/unmutes the selected nodes (M).</summary>
    public ICommand ToggleMuteSelectedCommand { get; }

    /// <summary>Freezes/unfreezes the selected nodes (Shift+M).</summary>
    public ICommand ToggleFreezeSelectedCommand { get; }

    /// <summary>Hides/shows unused sockets on the selected nodes (Ctrl+H).</summary>
    public ICommand ToggleHideUnusedSelectedCommand { get; }

    /// <summary>Collapses every node.</summary>
    public ICommand CollapseAllCommand { get; }

    /// <summary>Expands every node.</summary>
    public ICommand ExpandAllCommand { get; }

    /// <summary>The nodes a node-level toggle applies to: the selection when the clicked node is part of it, else just that node.</summary>
    private List<NodeViewModel> ToggleTargets(NodeViewModel? anchor)
    {
        var selected = SelectedItems.OfType<NodeViewModel>().ToList();
        if (anchor != null && !selected.Contains(anchor))
        {
            return new List<NodeViewModel> { anchor };
        }

        return selected;
    }

    internal void ToggleCollapse(NodeViewModel? anchor)
    {
        var targets = ToggleTargets(anchor);
        if (targets.Count == 0)
        {
            return;
        }

        var collapse = targets.Any(n => !n.Model.Ui.Collapsed);
        using (_undo.Begin(collapse ? "Collapse" : "Expand"))
        {
            foreach (var node in targets)
            {
                node.Model.Ui.Collapsed = collapse;
            }
        }
    }

    internal void ToggleMute(NodeViewModel? anchor)
    {
        var targets = ToggleTargets(anchor);
        var wires = anchor == null ? SelectedConnections.ToList() : new List<ConnectionViewModel>();
        if (targets.Count == 0 && wires.Count == 0)
        {
            return;
        }

        var mute = targets.Any(n => !n.Model.IsMuted) || wires.Any(w => !w.Model.IsMuted);
        using (_undo.Begin(mute ? "Mute" : "Unmute"))
        {
            foreach (var node in targets)
            {
                node.Model.IsMuted = mute;
            }

            foreach (var wire in wires)
            {
                _graph.SetConnectionMuted(wire.Model, mute);
            }
        }

        var count = targets.Count + wires.Count;
        StatusMessage = (mute ? "Muted " : "Unmuted ") + count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        (wires.Count == 0 ? " node(s)." : targets.Count == 0 ? " wire(s)." : " item(s).");
    }

    internal void ToggleFreeze(NodeViewModel? anchor)
    {
        var targets = ToggleTargets(anchor);
        if (targets.Count == 0)
        {
            return;
        }

        var freeze = targets.Any(n => !n.Model.IsFrozen);
        using (_undo.Begin(freeze ? "Freeze" : "Unfreeze"))
        {
            foreach (var node in targets)
            {
                node.Model.IsFrozen = freeze;
            }
        }

        StatusMessage = (freeze ? "Froze " : "Unfroze ") + targets.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        " node(s)" + (freeze ? " — they and everything downstream keep their last results until unfrozen." : ".");
    }

    internal void ToggleHideUnused(NodeViewModel? anchor)
    {
        var targets = ToggleTargets(anchor);
        if (targets.Count == 0)
        {
            return;
        }

        var hide = targets.Any(n => n.Model.Ui.HideUnused != true);
        using (_undo.Begin(hide ? "Hide unused sockets" : "Show unused sockets"))
        {
            foreach (var node in targets)
            {
                node.Model.Ui.HideUnused = hide ? true : (bool?)null;
            }
        }
    }

    private void SetAllCollapsed(bool collapsed)
    {
        using (_undo.Begin(collapsed ? "Collapse all" : "Expand all"))
        {
            foreach (var node in _graph.Nodes)
            {
                node.Ui.Collapsed = collapsed;
            }
        }
    }

    /// <summary>Undo/redo history of the open graph (graph edits only; never Navisworks changes).</summary>
    public UndoManager History => _undo;

    /// <summary>Reverts the most recent edit (Ctrl+Z).</summary>
    public ICommand UndoCommand { get; }

    /// <summary>Re-applies the most recently undone edit (Ctrl+Y / Ctrl+Shift+Z).</summary>
    public ICommand RedoCommand { get; }

    /// <summary>Bound to the editor's items-drag-started hook: opens the "Move" undo transaction.</summary>
    public ICommand ItemsDragStartedCommand { get; }

    /// <summary>Bound to the editor's items-drag-completed hook: closes the "Move" undo transaction.</summary>
    public ICommand ItemsDragCompletedCommand { get; }

    /// <summary>Toolbar tooltip for Undo, naming the edit it would revert.</summary>
    public string UndoTooltip => (_undo.CanUndo ? "Undo " + _undo.UndoLabel : "Nothing to undo") + ShortcutSuffix("edit.undo");

    /// <summary>Toolbar tooltip for Redo, naming the edit it would re-apply.</summary>
    public string RedoTooltip => (_undo.CanRedo ? "Redo " + _undo.RedoLabel : "Nothing to redo") + ShortcutSuffix("edit.redo");

    /// <summary>Toolbar tooltip for Run, showing its current shortcut.</summary>
    public string RunTooltip => "Run the graph" + ShortcutSuffix("graph.run");

    /// <summary>Toolbar tooltip for the minimap toggle, showing its current shortcut.</summary>
    public string MinimapTooltip => (IsMinimapVisible ? "Hide the minimap" : "Show the minimap") + ShortcutSuffix("view.minimap");

    // " (Ctrl+Z)" for the shortcut in force, or nothing when the command is unbound.
    private string ShortcutSuffix(string commandId)
    {
        var chord = _keymap.ShortcutOf(commandId);
        return chord == null ? string.Empty : " (" + chord + ")";
    }

    /// <summary>Duplicates the selected nodes including wires between them (Ctrl+D).</summary>
    public ICommand DuplicateSelectionCommand { get; }

    /// <summary>Tidies the selected nodes into non-overlapping dependency columns (Ctrl+L).</summary>
    public ICommand ArrangeSelectionCommand { get; }

    /// <summary>Copies the selected nodes and the wires among them to the in-memory clipboard (Ctrl+C).</summary>
    public ICommand CopySelectionCommand { get; }

    /// <summary>Pastes the clipboard at an increasing offset; repeat-paste keeps offsetting (Ctrl+V).</summary>
    public ICommand PasteCommand { get; }

    /// <summary>Creates a group rectangle around the current selection (Ctrl+G).</summary>
    public ICommand GroupSelectionCommand { get; }

    /// <summary>Opens a file from the recent list (parameter: the .dyc path).</summary>
    public ICommand OpenRecentFileCommand { get; }

    /// <summary>Opens a sample graph (parameter: the <see cref="SampleGraphViewModel"/>).</summary>
    public ICommand OpenSampleCommand { get; }

    /// <summary>Adds a node; parameter is a <see cref="LibraryEntryViewModel"/> (placed at origin).</summary>
    public ICommand AddNodeCommand { get; }

    /// <summary>Adds a note; parameter is a graph-space <see cref="Point"/> (or none for origin).</summary>
    public ICommand AddNoteCommand { get; }

    /// <summary>Snapshots the live host selection into a Captured Selection node.</summary>
    public ICommand CaptureSelectionCommand { get; }

    /// <summary>Forgets the stored selection of a Captured Selection node.</summary>
    public ICommand ClearCapturedSelectionCommand { get; }

    // ----- quick node search (Space bar) ---------------------------------------

    /// <summary>True while the canvas quick-search popup is open.</summary>
    public bool IsQuickSearchOpen
    {
        get => _isQuickSearchOpen;
        set => SetProperty(ref _isQuickSearchOpen, value);
    }

    /// <summary>Search text of the quick-search popup; updates the results as you type.</summary>
    public string QuickSearchText
    {
        get => _quickSearchText;
        set
        {
            if (SetProperty(ref _quickSearchText, value ?? string.Empty))
            {
                RefreshQuickSearchResults();
            }
        }
    }

    /// <summary>What the quick-search popup says under the box: how to use it, or that the list shows favourites and recent nodes.</summary>
    public string QuickSearchHint
    {
        get
        {
            if (_quickSearchText.Trim().Length == 0 && QuickSearchResults.Count > 0)
            {
                return "Starred and recently added nodes — type to search · ↑↓ choose · Enter inserts · Esc closes";
            }

            return "↑↓ choose · Enter inserts the node · Esc closes";
        }
    }

    /// <summary>Ranked quick-search hits (top 50).</summary>
    public ObservableCollection<LibraryEntryViewModel> QuickSearchResults { get; }

    /// <summary>The highlighted hit; Enter inserts it.</summary>
    public LibraryEntryViewModel? QuickSearchSelected
    {
        get => _quickSearchSelected;
        set => SetProperty(ref _quickSearchSelected, value);
    }

    /// <summary>Inserts the given (or highlighted) hit; bound to result clicks.</summary>
    public ICommand CommitQuickSearchCommand { get; }

    /// <summary>
    /// Opens the quick-search popup; inserted nodes land at
    /// <paramref name="insertLocation"/> (graph space).
    /// </summary>
    /// <param name="insertLocation">Graph-space location for the inserted node.</param>
    public void OpenQuickSearch(Point insertLocation)
    {
        OpenQuickSearch(insertLocation, null);
    }

    /// <summary>
    /// Opens the popup for a wire dropped on empty canvas: only nodes that can connect to
    /// <paramref name="source"/> are offered, and the chosen node is wired up when inserted.
    /// </summary>
    /// <param name="insertLocation">Graph-space location for the inserted node.</param>
    /// <param name="source">The socket the wire was dragged from, or null for a plain search.</param>
    public void OpenQuickSearch(Point insertLocation, ConnectorViewModel? source)
    {
        _quickSearchLocation = insertLocation;
        _quickSearchSource = source;
        QuickSearchContext = source == null ? string.Empty : DescribeSearchSource(source);
        QuickSearchText = string.Empty;
        RefreshQuickSearchResults();
        IsQuickSearchOpen = true;
    }

    /// <summary>Closes the popup without inserting anything.</summary>
    public void CloseQuickSearch()
    {
        IsQuickSearchOpen = false;
        _quickSearchSource = null;
        QuickSearchContext = string.Empty;
    }

    /// <summary>
    /// Inserts a hit at the location captured when the popup opened and closes
    /// the popup. Falls back to the highlighted (or first) hit when
    /// <paramref name="entry"/> is null.
    /// </summary>
    /// <param name="entry">The hit to insert, or null for the highlighted one.</param>
    /// <returns>The inserted node's view model, or null when nothing was inserted.</returns>
    public NodeViewModel? CommitQuickSearch(LibraryEntryViewModel? entry = null)
    {
        var chosen = entry ?? QuickSearchSelected ?? (QuickSearchResults.Count > 0 ? QuickSearchResults[0] : null);
        if (chosen == null)
        {
            return null;
        }

        var source = _quickSearchSource;
        IsQuickSearchOpen = false;
        _quickSearchSource = null;
        QuickSearchContext = string.Empty;
        if (source == null)
        {
            return AddNode(chosen.Id, _quickSearchLocation);
        }

        using (_undo.Begin("Add node and connect"))
        {
            return AddNodeConnectedTo(chosen.Id, _quickSearchLocation, source);
        }
    }

    /// <summary>Moves the highlight down (+1) or up (-1) through the results, wrapping.</summary>
    /// <param name="delta">+1 for down, -1 for up.</param>
    public void MoveQuickSearchSelection(int delta)
    {
        if (QuickSearchResults.Count == 0)
        {
            return;
        }

        int count = QuickSearchResults.Count;
        int index = QuickSearchSelected != null ? QuickSearchResults.IndexOf(QuickSearchSelected) : (delta > 0 ? -1 : 0);
        index = ((index + delta) % count + count) % count;
        QuickSearchSelected = QuickSearchResults[index];
    }

    private void RefreshQuickSearchResults()
    {
        QuickSearchResults.Clear();
        foreach (var hit in FindQuickSearchHits(_quickSearchText, _quickSearchSource, 50))
        {
            QuickSearchResults.Add(hit);
        }

        QuickSearchSelected = QuickSearchResults.Count > 0 ? QuickSearchResults[0] : null;
        OnPropertyChanged(nameof(QuickSearchHint));
    }

    /// <summary>
    /// Creates a node from a library id (zero-touch definition id or node type
    /// tag) and places it at the given graph-space location.
    /// </summary>
    /// <param name="libraryId">Definition id or node type tag.</param>
    /// <param name="location">Graph-space drop location.</param>
    /// <returns>The created node's view model, or null when the id is unknown.</returns>
    public NodeViewModel? AddNode(string libraryId, Point location)
    {
        var node = CreateNodeFromLibrary(libraryId, out var problem);
        if (node == null)
        {
            StatusMessage = problem ?? "Unknown node '" + libraryId + "'.";
            return null;
        }

        node.X = location.X;
        node.Y = location.Y;
        Dyncamelo.UI.Services.CrashGuard.NoteActivity();
        var now = DateTime.UtcNow;
        _recentlyAdded.RemoveAll(e => now - e.When > TimeSpan.FromMinutes(1));
        _recentlyAdded.Add((node, now));
        _graph.AddNode(node);
        Library.NoteUsed(libraryId);
        StatusMessage = "Added " + node.Name + ".";
        return FindNodeViewModel(node);
    }

    private readonly List<(NodeModel Node, DateTime When)> _recentlyAdded = new List<(NodeModel, DateTime)>();

    /// <summary>
    /// Takes away the nodes added from the library in the last few seconds. The crash guard calls it after a node's visual failed to
    /// build: left in the graph it would fail again at every layout pass.
    /// </summary>
    /// <returns>How many nodes were taken away.</returns>
    public int DiscardRecentlyAddedNodes()
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(15);
        var removed = 0;
        foreach (var entry in _recentlyAdded.Where(e => e.When >= cutoff).ToList())
        {
            if (_graph.RemoveNode(entry.Node))
            {
                removed++;
            }
        }

        _recentlyAdded.Clear();
        return removed;
    }

    /// <summary>Adds an empty note at the given graph-space location.</summary>
    /// <param name="location">Graph-space location.</param>
    public void AddNote(Point location)
    {
        _graph.Notes.Add(new NoteModel { Text = "Note", X = location.X, Y = location.Y });
    }

    /// <summary>
    /// Reveals a canvas node's library entry: expands the tree to its category,
    /// selects it and scrolls it into view. Works for zero-touch nodes (matched
    /// by definition id) and interactive nodes (matched by node type tag).
    /// </summary>
    /// <param name="node">The canvas node.</param>
    public void FindInLibrary(NodeViewModel? node)
    {
        if (node == null)
        {
            return;
        }

        var libraryId = node.Model is ZeroTouchNodeModel zeroTouch
            ? zeroTouch.Definition.Id
            : node.Model.NodeType;

        var entry = Library.RevealEntry(libraryId);
        StatusMessage = entry != null
            ? "Found '" + entry.Name + "' in the library."
            : "'" + node.Title + "' is not in the library.";
    }

    /// <summary>
    /// Marks every node dirty (e.g. when the host document changed) so the next
    /// run re-executes the whole graph. Triggers an automatic run when enabled.
    /// </summary>
    public void InvalidateAllNodes()
    {
        foreach (var graph in DocumentGraph.NodeGroups.AllGraphs().ToList())
        {
            foreach (var node in graph.Nodes.ToList())
            {
                node.MarkDirty();
            }
        }
    }

    /// <summary>
    /// Re-computes every connector's IsConnected flag (and, for multi-input sockets, wire count and each wire's slot)
    /// from the graph model.
    /// </summary>
    public void RefreshConnectedFlags()
    {
        // One pass over the wires instead of a scan per port.
        var into = _graph.Connections.ToLookup(c => c.Target);
        var sources = new HashSet<PortModel>(_graph.Connections.Select(c => c.Source));
        var wireViewModels = new Dictionary<ConnectionModel, ConnectionViewModel>();
        foreach (var wireViewModel in Connections)
        {
            wireViewModels[wireViewModel.Model] = wireViewModel;
        }

        foreach (var item in Items)
        {
            if (!(item is NodeViewModel node))
            {
                continue;
            }

            foreach (var connector in node.Inputs)
            {
                var wires = into[connector.Port].ToList();
                connector.WireCount = wires.Count;
                connector.IsConnected = wires.Count > 0;
                for (var slot = 0; slot < wires.Count; slot++)
                {
                    if (wireViewModels.TryGetValue(wires[slot], out var wireViewModel))
                    {
                        wireViewModel.SetSlot(slot, wires.Count);
                    }
                }

                if (wires.Count > 0)
                {
                    // An untyped input (reroute, "any") takes the colour of what feeds it.
                    var upstream = FindNodeViewModel(wires[0].SourceNode)?.FindConnector(wires[0].Source);
                    if (upstream != null)
                    {
                        connector.InheritKind(upstream.Kind);
                    }
                }
            }

            foreach (var connector in node.Outputs)
            {
                connector.IsConnected = sources.Contains(connector.Port);
            }

            node.RebuildRows();
        }
    }

    /// <summary>Loads a graph model into the editor, replacing the current one.</summary>
    /// <param name="graph">The graph to edit.</param>
    /// <param name="filePath">Backing file path, or null for unsaved graphs.</param>
    public void LoadGraph(GraphModel graph, string? filePath = null)
    {
        if (graph == null)
        {
            throw new ArgumentNullException(nameof(graph));
        }

        DetachGraph();
        ResetGroupNavigation();
        _graph = graph;
        AttachGraph(graph);
        WatchNodeGroups(graph.NodeGroups);
        CurrentFilePath = filePath;
        OnPropertyChanged(nameof(Graph));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsAutoRun));
        UpdateRunStatistics(null);
        MarkSaved();
        if (IsAutoRun)
        {
            ScheduleAutoRun();
        }
    }

    /// <summary>Runs the dirty subgraph now (no-op while a run is already in progress).</summary>
    public void RunGraph()
    {
        RunGraph(interactive: true);
    }

    /// <summary>
    /// Runs the dirty subgraph. Interactive runs raise <see cref="IsRunning"/> so
    /// the view can show a busy indicator before the (UI-thread-blocking) run; the
    /// debounced auto-run passes <c>false</c> to stay silent.
    /// </summary>
    /// <param name="interactive">True for an explicit run (Run button / open / F5).</param>
    /// <param name="upTo">When given, only these nodes and what they depend on are brought up to date.</param>
    private void RunGraph(bool interactive, IReadOnlyCollection<NodeModel>? upTo = null)
    {
        _autoRunTimer.Stop();
        if (_engine.IsRunning)
        {
            return;
        }

        if (interactive)
        {
            StatusMessage = "Running…";
            RunProgressText = "Running the graph…";
            IsRunning = true; // raised synchronously so the view paints before we block
        }

        RunResult result;
        using (var cancellation = new CancellationTokenSource())
        {
            _runCancellation = cancellation;
            try
            {
                var context = EvaluationContextFactory != null ? EvaluationContextFactory() : new EvaluationContext();
                ConfigureRunContext(context, cancellation);
                using (_undo.Suspend())
                {
                    result = upTo == null ? _engine.Run(DocumentGraph, context) : _engine.RunUpTo(DocumentGraph, upTo, context);
                }

                _hasRunThisGraph = true;
            }
            catch (Exception ex)
            {
                StatusMessage = "Run failed: " + ex.Message;
                return;
            }
            finally
            {
                _runCancellation = null;
                if (interactive)
                {
                    IsRunning = false;
                }
            }
        }

        UpdateRunStatistics(result);
        if (upTo != null && !result.Cancelled)
        {
            var waiting = DocumentGraph.Nodes.Count(n => n.IsDirty && !n.IsFrozen);
            StatusMessage = "Ran up to " + string.Join(", ", upTo.Select(t => "'" + t.Name + "'")) + ": " +
                            result.ExecutedNodes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " node(s) executed in " +
                            LastRunMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms" +
                            (waiting > 0 ? "; " + waiting.ToString(System.Globalization.CultureInfo.InvariantCulture) + " after it still waiting for Run." : ".");
        }

        RefreshPreview(); // the selected node's outputs just changed
    }

    // ----- cancelling a run ----------------------------------------------------

    private CancellationTokenSource? _runCancellation;
    private string _runProgressText = "Running the graph…";
    private readonly Stopwatch _repaintWatch = Stopwatch.StartNew();

    /// <summary>What the busy overlay says: "Running…", then "12 / 40 — node name" as the run advances, "Cancelling…" after Esc.</summary>
    public string RunProgressText
    {
        get => _runProgressText;
        private set => SetProperty(ref _runProgressText, value);
    }

    /// <summary>
    /// Asked between the steps of a run whether the user wants it stopped. The default reads the Esc key from the keyboard
    /// state (a run blocks the UI thread, so no key message can arrive); tests replace it.
    /// </summary>
    public Func<bool> CancelPoll { get; set; } = EscapeKey.WasPressed;

    /// <summary>
    /// Repaints the window; set by the view. Called a few times a second during a run so the overlay shows progress. It must
    /// only process rendering, never input, or the graph could be edited mid-run.
    /// </summary>
    public Action? RenderPump { get; set; }

    /// <summary>Stops the run in progress before its next step. Only a call from inside the run (the poll) can reach it.</summary>
    public void RequestCancel()
    {
        var source = _runCancellation;
        if (source != null && !source.IsCancellationRequested)
        {
            RunProgressText = "Cancelling…";
            source.Cancel();
        }
    }

    private void ConfigureRunContext(EvaluationContext context, CancellationTokenSource cancellation)
    {
        context.UseCancellation(cancellation.Token);
        var escape = EscCancelsRun;
        if (escape)
        {
            EscapeKey.Reset();
        }

        context.ProgressCallback = progress => RunProgressText = "Running " + progress.Describe();
        context.Heartbeat = () =>
        {
            if (escape && CancelPoll())
            {
                RequestCancel();
            }

            if (_repaintWatch.ElapsedMilliseconds >= 100)
            {
                _repaintWatch.Restart();
                RenderPump?.Invoke();
            }
        };
    }

    private void UndoLast()
    {
        _dragTransaction?.Dispose();
        _dragTransaction = null;
        var label = _undo.Undo();
        if (label != null)
        {
            StatusMessage = "Undid " + label + (_hasRunThisGraph ? " (graph only — Navisworks changes from earlier runs are not reverted)." : ".");
        }
    }

    private void RedoLast()
    {
        var label = _undo.Redo();
        if (label != null)
        {
            StatusMessage = "Redid " + label + ".";
        }
    }

    // ----- graph attachment -------------------------------------------------

    private void AttachGraph(GraphModel graph, bool resetHistory = true)
    {
        // Populate view models first, then subscribe, so pre-existing content
        // (a freshly deserialized file) is not added twice.
        foreach (var node in graph.Nodes)
        {
            Items.Add(new NodeViewModel(this, node));
        }

        foreach (var connection in graph.Connections)
        {
            AddConnectionViewModel(connection);
        }

        foreach (var note in graph.Notes)
        {
            Items.Add(new NoteViewModel(note));
        }

        foreach (var group in graph.Groups)
        {
            Items.Add(new GroupViewModel(this, group));
        }

        graph.NodeAdded += OnNodeAdded;
        graph.NodeRemoved += OnNodeRemoved;
        graph.ConnectionAdded += OnConnectionAdded;
        graph.ConnectionRemoved += OnConnectionRemoved;
        graph.ConnectionMuteChanged += OnConnectionMuteChanged;
        graph.Modified += OnGraphModified;
        graph.PropertyChanged += OnGraphPropertyChanged;
        graph.Notes.CollectionChanged += OnNotesChanged;
        graph.Groups.CollectionChanged += OnGroupsChanged;
        graph.Bookmarks.CollectionChanged += OnBookmarksChanged;

        RefreshConnectedFlags();
        NodeCount = graph.Nodes.Count;

        _dragTransaction?.Dispose();
        _dragTransaction = null;
        if (resetHistory)
        {
            _undo.Clear();
        }

        _hasRunThisGraph = false;
        _recorder = new GraphRecorder(graph, _undo);
    }

    private void DetachGraph()
    {
        _autoRunTimer.Stop();
        _recorder?.Dispose();
        _recorder = null;
        _graph.NodeAdded -= OnNodeAdded;
        _graph.NodeRemoved -= OnNodeRemoved;
        _graph.ConnectionAdded -= OnConnectionAdded;
        _graph.ConnectionRemoved -= OnConnectionRemoved;
        _graph.ConnectionMuteChanged -= OnConnectionMuteChanged;
        _graph.Modified -= OnGraphModified;
        _graph.PropertyChanged -= OnGraphPropertyChanged;
        _graph.Notes.CollectionChanged -= OnNotesChanged;
        _graph.Groups.CollectionChanged -= OnGroupsChanged;
        _graph.Bookmarks.CollectionChanged -= OnBookmarksChanged;

        foreach (var item in Items)
        {
            if (item is NodeViewModel node)
            {
                node.Detach();
            }
            else if (item is NoteViewModel note)
            {
                note.Detach();
            }
            else if (item is GroupViewModel group)
            {
                group.Detach();
            }
        }

        SelectedItems.Clear();
        SelectedConnections.Clear();
        foreach (var connection in Connections)
        {
            connection.Detach();
        }

        Connections.Clear();
        Items.Clear();
    }

    // ----- model event handlers ----------------------------------------------

    private void OnNodeAdded(object? sender, NodeEventArgs e)
    {
        Items.Add(new NodeViewModel(this, e.Node));
        NodeCount = _graph.Nodes.Count;
    }

    private void OnNodeRemoved(object? sender, NodeEventArgs e)
    {
        var viewModel = FindNodeViewModel(e.Node);
        if (viewModel != null)
        {
            viewModel.Detach();
            SelectedItems.Remove(viewModel);
            Items.Remove(viewModel);
        }

        NodeCount = _graph.Nodes.Count;
    }

    private void OnConnectionAdded(object? sender, ConnectionEventArgs e)
    {
        AddConnectionViewModel(e.Connection);
        RefreshConnectedFlags();
    }

    private void OnConnectionRemoved(object? sender, ConnectionEventArgs e)
    {
        for (int i = Connections.Count - 1; i >= 0; i--)
        {
            if (Connections[i].Model == e.Connection)
            {
                Connections[i].Detach();
                SelectedConnections.Remove(Connections[i]);
                Connections.RemoveAt(i);
            }
        }

        RefreshConnectedFlags();
    }

    /// <summary>
    /// Enables highlighting the selected node's output model items in the host
    /// viewport (mirrored into the Navisworks selection). Off by default because
    /// it overwrites the live selection, which graphs using Selection.Current read.
    /// </summary>
    public bool PreviewSelection
    {
        get => _previewSelection;
        set
        {
            if (SetProperty(ref _previewSelection, value))
            {
                _settings.SetPreviewSelection(value);
                RefreshPreview();
            }
        }
    }

    private void OnSelectedItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshPreview();
        RefreshHint();
        QueueWireFocus();
    }

    /// <summary>
    /// Pushes the currently-selected node's output model items to the host
    /// preview (a single node must be selected and previewing must be enabled),
    /// otherwise clears it. Called on selection change and after each run.
    /// </summary>
    private void RefreshPreview()
    {
        if (!_previewSelection)
        {
            _preview.ClearPreview();
            return;
        }

        var nodes = SelectedItems.OfType<NodeViewModel>().Take(2).ToList();
        if (nodes.Count == 1)
        {
            _preview.ShowPreview(nodes[0].Model.OutputValues);
        }
        else
        {
            _preview.ClearPreview();
        }
    }

    private void OnSelectedConnectionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Mirror the editor-maintained selection into per-wire flags so the
        // connection template can restyle selected wires.
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var connection in Connections)
            {
                connection.IsSelected = SelectedConnections.Contains(connection);
            }

            return;
        }

        if (e.OldItems != null)
        {
            foreach (ConnectionViewModel connection in e.OldItems)
            {
                connection.IsSelected = false;
            }
        }

        if (e.NewItems != null)
        {
            foreach (ConnectionViewModel connection in e.NewItems)
            {
                connection.IsSelected = true;
            }
        }
    }

    private void OnGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (GroupModel group in e.OldItems)
            {
                var viewModel = FindGroupViewModel(group);
                if (viewModel != null)
                {
                    viewModel.Detach();
                    SelectedItems.Remove(viewModel);
                    Items.Remove(viewModel);
                }
            }
        }

        if (e.NewItems != null)
        {
            foreach (GroupModel group in e.NewItems)
            {
                if (FindGroupViewModel(group) == null)
                {
                    Items.Add(new GroupViewModel(this, group));
                }
            }
        }
    }

    private void OnNotesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (NoteModel note in e.OldItems)
            {
                var viewModel = FindNoteViewModel(note);
                if (viewModel != null)
                {
                    viewModel.Detach();
                    SelectedItems.Remove(viewModel);
                    Items.Remove(viewModel);
                }
            }
        }

        if (e.NewItems != null)
        {
            foreach (NoteModel note in e.NewItems)
            {
                if (FindNoteViewModel(note) == null)
                {
                    Items.Add(new NoteViewModel(note));
                }
            }
        }
    }

    private void OnGraphModified(object? sender, EventArgs e)
    {
        if (IsAutoRun)
        {
            ScheduleAutoRun();
        }
    }

    private void OnGraphPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GraphModel.RunType))
        {
            OnPropertyChanged(nameof(IsAutoRun));
            NoteChange();
        }
        else if (e.PropertyName == nameof(GraphModel.Name))
        {
            OnPropertyChanged(nameof(Title));
            NoteChange();
        }
        else if (e.PropertyName == nameof(GraphModel.Description))
        {
            NoteChange();
        }
    }

    // ----- connections -------------------------------------------------------

    private void AddConnectionViewModel(ConnectionModel connection)
    {
        var sourceNode = FindNodeViewModel(connection.SourceNode);
        var targetNode = FindNodeViewModel(connection.TargetNode);
        var source = sourceNode?.FindConnector(connection.Source);
        var target = targetNode?.FindConnector(connection.Target);
        if (source != null && target != null)
        {
            Connections.Add(new ConnectionViewModel(this, connection, source, target));
            if (SelectedItems.Count > 0)
            {
                QueueWireFocus();
            }
        }
    }

    private void CompletePendingConnection(ConnectorViewModel? target)
    {
        ClearSocketDimming();
        if (_movingLink != null)
        {
            CompleteMovedLink(target);
            return;
        }

        // A wire released on empty canvas asks what to connect to instead of vanishing.
        var dragged = PendingConnection.Source;
        if (target == null && PendingConnection.Target == null && dragged != null && !IsOverNode(PendingConnection.TargetLocation))
        {
            PendingConnection.IsVisible = false;
            OpenQuickSearch(PendingConnection.TargetLocation, dragged);
            return;
        }

        // Released on the Group Input / Group Output node itself: that makes a new socket for the wire.
        if (target == null && PendingConnection.Target == null && dragged != null &&
            TryCreateGroupSocketFromDrop(dragged, PendingConnection.TargetLocation))
        {
            PendingConnection.IsVisible = false;
            return;
        }

        using (_undo.Begin("Connect"))
        {
            CompletePendingConnectionCore(target);
        }
    }

    private void CompletePendingConnectionCore(ConnectorViewModel? target)
    {
        PendingConnection.IsVisible = false;
        var source = PendingConnection.Source;
        target = target ?? PendingConnection.Target;
        if (source == null || target == null || source == target)
        {
            return;
        }

        if (source.IsInput == target.IsInput)
        {
            StatusMessage = "Connections run from an output to an input.";
            return;
        }

        var output = source.IsInput ? target : source;
        var input = source.IsInput ? source : target;
        var result = _graph.Connect(output.Port, input.Port);
        StatusMessage = result.Success
            ? "Connected " + output.Node.Title + " → " + input.Node.Title + "."
            : result.Message ?? "The connection was rejected.";
    }

    private void DisconnectConnector(ConnectorViewModel? connector)
    {
        using (_undo.Begin("Disconnect"))
        {
            DisconnectConnectorCore(connector);
        }
    }

    private void DisconnectConnectorCore(ConnectorViewModel? connector)
    {
        if (connector == null)
        {
            return;
        }

        if (connector.IsInput)
        {
            // A multi-input socket takes all of its wires off at once, like an output does.
            foreach (var connection in _graph.FindConnectionsInto(connector.Port))
            {
                _graph.Disconnect(connection);
            }
        }
        else
        {
            foreach (var connection in _graph.FindConnectionsFrom(connector.Port).ToList())
            {
                _graph.Disconnect(connection);
            }
        }
    }

    private void RemoveConnection(ConnectionViewModel? connection)
    {
        if (connection == null)
        {
            return;
        }

        // Nodify calls this once per wire the cutting line crossed. Holding Ctrl while cutting
        // mutes the wires instead of removing them.
        if (_cutTransaction != null && IsMuteCutRequested())
        {
            _graph.SetConnectionMuted(connection.Model, !connection.Model.IsMuted);
            _cutWires++;
            return;
        }

        _graph.Disconnect(connection.Model);
        if (_cutTransaction != null)
        {
            _cutWires++;
        }
    }

    // ----- editing -----------------------------------------------------------

    private void AddNodeFromParameter(object? parameter)
    {
        if (parameter is LibraryEntryViewModel entry)
        {
            AddNode(entry.Id, new Point(0, 0));
        }
    }

    private void AddNoteFromParameter(object? parameter)
    {
        AddNote(parameter is Point point ? point : new Point(0, 0));
    }

    /// <summary>
    /// Opens the colour picker for a Color Picker node and writes the result back.
    /// The node type lives in Dyncamelo.Nodes (not referenced by the UI), so its
    /// A/R/G/B channels are read and written reflectively.
    /// </summary>
    private void CaptureSelection(NodeModel? node)
    {
        if (node is ICapturedSelectionNode captured)
        {
            captured.CaptureFromCurrentSelection();
        }
    }

    private void ClearCapturedSelection(NodeModel? node)
    {
        if (node is ICapturedSelectionNode captured)
        {
            captured.ClearCapturedSelection();
        }
    }

    private void DeleteSelection()
    {
        using (_undo.Begin("Delete"))
        {
            DeleteSelectionCore();
        }
    }

    private void DeleteSelectionCore()
    {
        foreach (var connection in SelectedConnections.ToList())
        {
            _graph.Disconnect(connection.Model);
        }

        foreach (var item in SelectedItems.ToList())
        {
            if (item is NodeViewModel node)
            {
                if (node.Model is Dyncamelo.Core.Groups.GroupInputNode || node.Model is Dyncamelo.Core.Groups.GroupOutputNode)
                {
                    StatusMessage = "A node group's Group Input and Group Output cannot be deleted.";
                    continue;
                }

                // A reroute is only a bend in a wire: deleting it keeps the data flowing.
                if (node.Model is Dyncamelo.Core.Nodes.RerouteNode && DeleteReconnectsReroutes)
                {
                    GraphOps.DissolveNode(_graph, node.Model);
                }
                else
                {
                    _graph.RemoveNode(node.Model);
                }
            }
            else if (item is NoteViewModel note)
            {
                _graph.Notes.Remove(note.Model);
            }
            else if (item is GroupViewModel group)
            {
                _graph.Groups.Remove(group.Model);
            }
        }
    }

    /// <summary>
    /// Lays the selected nodes out left to right in dependency columns so they
    /// stop overlapping and the wires read in the direction the data flows.
    /// The block stays where it is (anchored on the selection's own centre),
    /// and stacking order follows the nodes' current vertical order, so the
    /// arrangement stays recognisable rather than jumping somewhere new.
    /// </summary>
    private void ArrangeSelection()
    {
        using (_undo.Begin("Arrange"))
        {
            ArrangeSelectionCore();
        }
    }

    private void ArrangeSelectionCore()
    {
        var selected = SelectedItems.OfType<NodeViewModel>().ToList();
        if (selected.Count < 2)
        {
            StatusMessage = "Select two or more nodes to arrange.";
            return;
        }

        ArrangeNodes(selected);
    }

    /// <summary>Arranges every node of the graph left to right (Ctrl+Shift+L).</summary>
    public void ArrangeAll()
    {
        var all = Items.OfType<NodeViewModel>().ToList();
        if (all.Count < 2)
        {
            StatusMessage = "Nothing to arrange.";
            return;
        }

        using (_undo.Begin("Arrange all"))
        {
            ArrangeNodes(all);
        }
    }

    /// <summary>
    /// Lays the nodes out with the layered engine (or the built-in column layout when it cannot run), keeping the
    /// block where it was: same left edge, same vertical centre.
    /// </summary>
    private void ArrangeNodes(List<NodeViewModel> nodes)
    {
        // Current vertical order (then horizontal) decides how nodes stack
        // inside a column — the user's reading order survives the tidy-up.
        nodes.Sort((left, right) =>
        {
            int byY = left.Model.Y.CompareTo(right.Model.Y);
            return byY != 0 ? byY : left.Model.X.CompareTo(right.Model.X);
        });

        var items = new List<GraphLayout.LayoutItem>(nodes.Count);
        foreach (var node in nodes)
        {
            items.Add(new GraphLayout.LayoutItem(node.Model, LayoutWidth(node), LayoutHeight(node)));
        }

        var models = new HashSet<NodeModel>(nodes.Select(n => n.Model));
        var edges = new List<(object From, object To)>();
        foreach (var connection in _graph.Connections)
        {
            var from = connection.Source.Owner;
            var to = connection.Target.Owner;
            if (models.Contains(from) && models.Contains(to))
            {
                edges.Add((from, to));
            }
        }

        // Anchor on the current bounds so the block does not move.
        double left = double.MaxValue, top = double.MaxValue, bottom = double.MinValue;
        foreach (var node in nodes)
        {
            left = Math.Min(left, node.Model.X);
            top = Math.Min(top, node.Model.Y);
            bottom = Math.Max(bottom, node.Model.Y + LayoutHeight(node));
        }

        var result = ArrangeEngine.Arrange(items, edges, left, (top + bottom) / 2.0, useMsagl: UseLayeredArrange);
        int moved = 0;
        foreach (var node in nodes)
        {
            // A non-finite position would throw out of the canvas layout pass
            // rather than land anywhere, so it is dropped instead of assigned.
            if (result.Positions.TryGetValue(node.Model, out var position) &&
                IsUsableCoordinate(position.X) && IsUsableCoordinate(position.Y))
            {
                node.Location = new Point(position.X, position.Y);
                moved++;
            }
        }

        StatusMessage = "Arranged " + moved.ToString(System.Globalization.CultureInfo.InvariantCulture) + " nodes" +
                        (result.Engine == "MSAGL" ? "." : " (simple columns: " + result.Note + ")");
    }

    // The measured size when the node has been laid out, else the estimate.
    private static double LayoutWidth(NodeViewModel node) =>
        IsUsableSize(node.Size.Width) ? Math.Max(node.Size.Width, 120d) : EstimateWidth(node);

    private static double LayoutHeight(NodeViewModel node) =>
        IsUsableSize(node.Size.Height) ? Math.Max(node.Size.Height, 30d) : EstimateHeight(node);

    /// <summary>
    /// Canvas width of a node. Only a node the user has resized reports a real
    /// size — the rest answer NaN, WPF's "size automatically" — so those fall
    /// back to a generous constant. The layout needs enough room to guarantee
    /// no overlap, not pixel accuracy.
    /// </summary>
    private static double EstimateWidth(NodeViewModel node)
    {
        const double Default = 240.0;
        var measured = node.WatchWidth;
        return IsUsableSize(measured) ? Math.Max(Default, measured) : Default;
    }

    /// <summary>Canvas height of a node: title bar plus a row per port, or its real size when it reports one.</summary>
    private static double EstimateHeight(NodeViewModel node)
    {
        var rows = Math.Max(node.Model.InPorts.Count, node.Model.OutPorts.Count);
        var estimate = 70.0 + (rows * 26.0);
        var measured = node.WatchHeight;
        return IsUsableSize(measured) ? Math.Max(estimate, measured) : estimate;
    }

    /// <summary>
    /// Whether a reported size is a real number to lay out with. An auto-sized
    /// node reports NaN, and Math.Max(240, NaN) is NaN, not 240 — left
    /// unchecked that NaN reaches the canvas as a node position and WPF throws
    /// NotFiniteNumberException, taking Navisworks down with it.
    /// </summary>
    private static bool IsUsableSize(double size)
    {
        return !double.IsNaN(size) && !double.IsInfinity(size) && size > 0;
    }

    /// <summary>Whether a computed canvas coordinate is safe to assign (finite).</summary>
    private static bool IsUsableCoordinate(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private List<NodeModel> GetSelectedNodeModels()
    {
        // A group's Group Input / Group Output belong to it: they are never copied, cut or duplicated.
        return SelectedItems.OfType<NodeViewModel>()
            .Select(n => n.Model)
            .Where(m => !(m is Dyncamelo.Core.Groups.GroupInputNode) && !(m is Dyncamelo.Core.Groups.GroupOutputNode))
            .ToList();
    }

    private void CopySelection()
    {
        var selected = GetSelectedNodeModels();
        if (selected.Count == 0)
        {
            StatusMessage = "Nothing selected to copy.";
            return;
        }

        var serializer = new GraphSerializer(Registry);
        _clipboardFragment = serializer.SerializeFragment(selected);
        _pasteGeneration = 0;
        StatusMessage = "Copied " + selected.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " node(s).";
    }

    private void Paste()
    {
        using (_undo.Begin("Paste"))
        {
            PasteCore();
        }
    }

    private void PasteCore()
    {
        if (_clipboardFragment == null)
        {
            return;
        }

        _pasteGeneration++;
        double offset = 40d * _pasteGeneration;
        var pasted = PasteFragment(_clipboardFragment, offset, offset);
        StatusMessage = "Pasted " + pasted.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " node(s).";
    }

    private void DuplicateSelection()
    {
        using (_undo.Begin("Duplicate"))
        {
            DuplicateSelectionCore();
        }
    }

    private void DuplicateSelectionCore()
    {
        var selected = GetSelectedNodeModels();
        if (selected.Count == 0)
        {
            return;
        }

        var serializer = new GraphSerializer(Registry);
        var fragment = serializer.SerializeFragment(selected);
        var duplicated = PasteFragment(fragment, 40d, 40d);
        StatusMessage = "Duplicated " + duplicated.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " node(s).";
    }

    private IReadOnlyList<NodeModel> PasteFragment(string fragment, double offsetX, double offsetY)
    {
        var serializer = new GraphSerializer(Registry);
        IReadOnlyList<NodeModel> pasted;
        try
        {
            pasted = serializer.PasteFragment(_graph, fragment, offsetX, offsetY);
        }
        catch (GraphFormatException ex)
        {
            StatusMessage = "Paste failed: " + ex.Message;
            return new List<NodeModel>();
        }

        // Move the selection to the pasted nodes.
        SelectedItems.Clear();
        foreach (var node in pasted)
        {
            var viewModel = FindNodeViewModel(node);
            if (viewModel != null)
            {
                SelectedItems.Add(viewModel);
            }
        }

        return pasted;
    }

    private void GroupSelection()
    {
        using (_undo.Begin("Group"))
        {
            GroupSelectionCore();
        }
    }

    private void GroupSelectionCore()
    {
        var members = SelectedItems.Where(item => !(item is GroupViewModel)).ToList();
        if (members.Count == 0)
        {
            StatusMessage = "Select some nodes to group.";
            return;
        }

        var frame = GraphOps.FrameAround(members.Select(ItemRect))!.Value;
        var group = new GroupModel
        {
            Title = "Group",
            X = frame.X,
            Y = frame.Y,
            Width = frame.Width,
            Height = frame.Height,
        };
        _graph.Groups.Add(group);
        StatusMessage = "Grouped " + members.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " item(s).";
    }

    /// <summary>Removes a group rectangle, leaving its nodes on the canvas.</summary>
    /// <param name="group">The group to remove.</param>
    public void Ungroup(GroupViewModel group)
    {
        if (group != null && _graph.Groups.Remove(group.Model))
        {
            StatusMessage = "Ungrouped '" + group.Title + "'.";
        }
    }

    // ----- files -------------------------------------------------------------

    private void NewGraph()
    {
        if (!ConfirmCloseDocument("New Graph"))
        {
            return;
        }

        LoadGraph(new GraphModel { Name = "Untitled" });
        StatusMessage = "New graph.";
    }

    private void OpenGraph()
    {
        var path = Dialogs.ShowOpenFile(FileFilter, "Open Dyncamelo Graph");
        if (path != null && ConfirmCloseDocument("Open Graph"))
        {
            OpenFromPath(path);
        }
    }

    private void OpenRecentFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (!System.IO.File.Exists(path))
        {
            _settings.RemoveRecentFile(path!);
            RefreshRecentFiles();
            Dialogs.ShowError("The file no longer exists:\n" + path, "Open Recent");
            return;
        }

        if (ConfirmCloseDocument("Open Graph"))
        {
            OpenFromPath(path!);
        }
    }

    /// <summary>Opens a .dyc file from an explicit path (toolbar recents, host shell).</summary>
    /// <param name="path">Full path of the .dyc file.</param>
    /// <returns>True when the graph was loaded.</returns>
    public bool OpenFromPath(string path)
    {
        try
        {
            var serializer = new GraphSerializer(Registry);
            var graph = serializer.LoadFromFile(path);
            LoadGraph(graph, path);
            StatusMessage = "Opened " + System.IO.Path.GetFileName(path) + "." + DescribeLoadWarnings(serializer.LoadWarnings);
            RecordRecentFile(path);
            return true;
        }
        catch (GraphFormatException ex)
        {
            Dialogs.ShowError(ex.Message, "Open Graph");
        }
        catch (System.IO.IOException ex)
        {
            Dialogs.ShowError(ex.Message, "Open Graph");
        }
        catch (UnauthorizedAccessException ex)
        {
            Dialogs.ShowError(ex.Message, "Open Graph");
        }
        catch (Exception ex)
        {
            // Opening a graph must never take down the host application: anything
            // unexpected (corrupt payloads, security exceptions, ...) becomes a dialog.
            Dialogs.ShowError("The graph could not be opened: " + ex.Message, "Open Graph");
        }

        return false;
    }

    /// <summary>The sentence appended to "Opened …" when a node was changed since the file was saved and some wires or values could not come back.</summary>
    private static string DescribeLoadWarnings(System.Collections.Generic.IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
        {
            return string.Empty;
        }

        return " " + warnings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " connection" + (warnings.Count == 1 ? string.Empty : "s") +
               " or value" + (warnings.Count == 1 ? string.Empty : "s") + " could not be restored — " + warnings[0] +
               (warnings.Count > 1 ? " (+" + (warnings.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " more)" : string.Empty);
    }

    private void RecordRecentFile(string path)
    {
        _settings.AddRecentFile(path);
        RefreshRecentFiles();
    }

    private void RefreshRecentFiles()
    {
        RecentFiles.Clear();
        foreach (var recent in _settings.RecentFiles)
        {
            RecentFiles.Add(recent);
        }
    }

    // ----- sample graphs -------------------------------------------------------

    /// <summary>
    /// Re-enumerates the sample .dyc files (flat, ordered by name) into
    /// <see cref="SampleGraphs"/>. Called by the view every time the open
    /// dropdown is shown, so newly deployed samples appear without a restart.
    /// Enumeration failures simply leave the menu empty.
    /// </summary>
    public void RefreshSampleGraphs()
    {
        SampleGraphs.Clear();
        string? directory = ResolveSamplesDirectory();
        if (directory == null)
        {
            return;
        }

        List<string> files;
        try
        {
            files = System.IO.Directory
                .GetFiles(directory, "*.dyc", System.IO.SearchOption.TopDirectoryOnly)
                .OrderBy(System.IO.Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            return;
        }

        foreach (var file in files)
        {
            SampleGraphs.Add(new SampleGraphViewModel(
                System.IO.Path.GetFileNameWithoutExtension(file),
                file,
                OpenSampleCommand));
        }
    }

    /// <summary>
    /// Locates the sample graphs folder: a "Samples" directory next to the
    /// deployed plugin assemblies (Dyncamelo.UI.dll sits beside Dyncamelo.App.dll
    /// in every layout), else the repository's samples folder when running from
    /// a development bin directory.
    /// </summary>
    private static string? ResolveSamplesDirectory()
    {
        try
        {
            var assemblyDirectory = System.IO.Path.GetDirectoryName(typeof(GraphEditorViewModel).Assembly.Location);
            if (string.IsNullOrEmpty(assemblyDirectory))
            {
                return null;
            }

            var deployed = System.IO.Path.Combine(assemblyDirectory, "Samples");
            if (System.IO.Directory.Exists(deployed))
            {
                return deployed;
            }

            // Dev fallback: walk up from bin\<Configuration>\net48 to the repo
            // root and use its samples folder.
            var current = new System.IO.DirectoryInfo(assemblyDirectory);
            for (int depth = 0; depth < 6 && current != null; depth++, current = current.Parent)
            {
                var dev = System.IO.Path.Combine(current.FullName, "samples");
                if (System.IO.Directory.Exists(dev))
                {
                    return dev;
                }
            }
        }
        catch (Exception)
        {
            // A broken probing path must never break the toolbar.
        }

        return null;
    }

    private void OpenSample(SampleGraphViewModel? sample)
    {
        if (sample == null)
        {
            return;
        }

        if (!System.IO.File.Exists(sample.FilePath))
        {
            Dialogs.ShowError("The sample no longer exists:\n" + sample.FilePath, "Open Sample");
            RefreshSampleGraphs();
            return;
        }

        // Unlike Ctrl+O there is no file dialog to back out of, so guard
        // against silently discarding work (same prompt as New).
        if (!ConfirmCloseDocument("Open Sample"))
        {
            return;
        }

        if (OpenFromPath(sample.FilePath))
        {
            // Samples are read-only templates: detach the file path so Ctrl+S
            // becomes Save As instead of silently overwriting the shipped
            // sample (which the Samples menu would then serve to every
            // future open, and reinstalls would conflict with).
            CurrentFilePath = null;
            StatusMessage = "Opened sample '" + sample.Name + "' (save creates a copy).";
        }
    }

    private void SaveGraph()
    {
        if (CurrentFilePath == null)
        {
            SaveGraphAs();
        }
        else
        {
            SaveTo(CurrentFilePath);
        }
    }

    private void RenameGraph()
    {
        // Nothing on disk to rename (new / sample graph): renaming is "choose a
        // file name", i.e. Save As.
        if (CurrentFilePath == null)
        {
            SaveGraphAs();
            return;
        }

        var oldPath = CurrentFilePath;
        var directory = System.IO.Path.GetDirectoryName(oldPath) ?? string.Empty;
        var current = System.IO.Path.GetFileNameWithoutExtension(oldPath);

        var input = Dialogs.Prompt("New file name:", "Rename Graph", current);
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        // IsNullOrWhiteSpace guarantees non-null here (net48's BCL lacks the
        // [NotNullWhen] annotation, so assert it explicitly).
        var name = input!.Trim();
        if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
        {
            Dialogs.ShowError("A file name can't contain any of these characters:  \\ / : * ? \" < > |", "Rename Graph");
            return;
        }

        if (!name.EndsWith(".dyc", StringComparison.OrdinalIgnoreCase))
        {
            name += ".dyc";
        }

        var newPath = System.IO.Path.Combine(directory, name);
        if (string.Equals(newPath, oldPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (System.IO.File.Exists(newPath))
        {
            Dialogs.ShowError("A file named '" + name + "' already exists in this folder.", "Rename Graph");
            return;
        }

        try
        {
            System.IO.File.Move(oldPath, newPath);
            _settings.RemoveRecentFile(oldPath);

            // Keep the graph name in sync with the file name when it tracked it
            // (SaveTo names an untitled graph after its file); leave a custom
            // graph name alone. Either way the Title refreshes below.
            if (string.Equals(DocumentGraph.Name, current, StringComparison.Ordinal))
            {
                DocumentGraph.Name = System.IO.Path.GetFileNameWithoutExtension(newPath);
            }

            CurrentFilePath = newPath;   // setter refreshes Title
            StatusMessage = "Renamed to " + name + ".";
            RecordRecentFile(newPath);
        }
        catch (System.IO.IOException ex)
        {
            Dialogs.ShowError(ex.Message, "Rename Graph");
        }
        catch (UnauthorizedAccessException ex)
        {
            Dialogs.ShowError(ex.Message, "Rename Graph");
        }
        catch (Exception ex)
        {
            // A rename must never take down the Navisworks host dispatcher.
            Dialogs.ShowError("The graph could not be renamed: " + ex.Message, "Rename Graph");
        }
    }

    private void UpdateChoiceSelection()
    {
        foreach (var option in DoubleClickActions)
        {
            option.IsSelected = string.Equals(option.Id, _doubleClickAction, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var option in Palettes)
        {
            option.IsSelected = string.Equals(option.Id, _paletteId, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void SaveGraphAs()
    {
        var defaultName = (DocumentGraph.Name.Length > 0 ? DocumentGraph.Name : "graph") + ".dyc";
        var path = Dialogs.ShowSaveFile(FileFilter, "Save Dyncamelo Graph", defaultName);
        if (path != null)
        {
            SaveTo(path);
        }
    }

    private void SaveTo(string path)
    {
        try
        {
            if (DocumentGraph.Name.Length == 0 || DocumentGraph.Name == "Untitled")
            {
                DocumentGraph.Name = System.IO.Path.GetFileNameWithoutExtension(path);
            }

            var serializer = new GraphSerializer(Registry);
            serializer.SaveToFile(DocumentGraph, path);
            CurrentFilePath = path;
            MarkSaved();
            OnPropertyChanged(nameof(Title));
            StatusMessage = "Saved " + System.IO.Path.GetFileName(path) + ".";
            RecordRecentFile(path);
        }
        catch (System.IO.IOException ex)
        {
            Dialogs.ShowError(ex.Message, "Save Graph");
        }
        catch (UnauthorizedAccessException ex)
        {
            Dialogs.ShowError(ex.Message, "Save Graph");
        }
        catch (Exception ex)
        {
            // Saving must never take down the host application: a third-party
            // node's SerializeData override can throw anything, and Ctrl+S
            // otherwise propagates it into the Navisworks dispatcher.
            Dialogs.ShowError("The graph could not be saved: " + ex.Message, "Save Graph");
        }
    }

    // ----- running -----------------------------------------------------------

    private void ScheduleAutoRun()
    {
        _autoRunTimer.Stop();
        _autoRunTimer.Start();
    }

    private void OnAutoRunTimerTick(object? sender, EventArgs e)
    {
        _autoRunTimer.Stop();
        RunGraph(interactive: false);
    }

    private void UpdateRunStatistics(RunResult? result)
    {
        int errors = 0;
        int warnings = 0;
        foreach (var node in _graph.Nodes)
        {
            if (node.State == NodeState.Error)
            {
                errors++;
            }
            else if (node.State == NodeState.Warning)
            {
                warnings++;
            }
        }

        ErrorCount = errors;
        WarningCount = warnings;
        NodeCount = _graph.Nodes.Count;
        RefreshProblems();
        RefreshHint();

        if (result != null)
        {
            LastRunMilliseconds = Math.Round(result.Elapsed.TotalMilliseconds, 1);
            StatusMessage = result.Cancelled
                ? "Run cancelled after " + result.ExecutedNodes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " of " +
                  result.PlannedCount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                  " node(s). The next run continues where it stopped; changes already made in Navisworks are kept."
                : "Run finished: " + result.ExecutedNodes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                  " node(s) executed in " + LastRunMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms." +
                  DescribeSlowest(result);
        }
        else
        {
            LastRunMilliseconds = 0;
        }
    }

    /// <summary>
    /// Names the node that ate the most time when a run is slow enough to care
    /// (≥1 s), so the status bar answers "where did the time go?" — e.g.
    /// " — slowest: Viewpoint.SaveWithOverrides 71,200 ms (17×)".
    /// </summary>
    private static string DescribeSlowest(RunResult result)
    {
        if (result.Elapsed.TotalSeconds < 1.0 || result.NodeTimings.Count == 0)
        {
            return string.Empty;
        }

        NodeTiming? slowest = null;
        foreach (var timing in result.NodeTimings)
        {
            if (slowest == null || timing.Elapsed > slowest.Elapsed)
            {
                slowest = timing;
            }
        }

        if (slowest == null || slowest.Elapsed.TotalMilliseconds < 1.0)
        {
            return string.Empty;
        }

        var ms = Math.Round(slowest.Elapsed.TotalMilliseconds, 1)
            .ToString("#,0.#", System.Globalization.CultureInfo.InvariantCulture);
        var runs = slowest.Executions > 1
            ? " (" + slowest.Executions.ToString(System.Globalization.CultureInfo.InvariantCulture) + "×)"
            : string.Empty;
        return " — slowest: " + slowest.Name + " " + ms + " ms" + runs;
    }

    // ----- lookups -----------------------------------------------------------

    private NodeViewModel? FindNodeViewModel(NodeModel model)
    {
        foreach (var item in Items)
        {
            if (item is NodeViewModel node && node.Model == model)
            {
                return node;
            }
        }

        return null;
    }

    private NoteViewModel? FindNoteViewModel(NoteModel model)
    {
        foreach (var item in Items)
        {
            if (item is NoteViewModel note && note.Model == model)
            {
                return note;
            }
        }

        return null;
    }

    private GroupViewModel? FindGroupViewModel(GroupModel model)
    {
        foreach (var item in Items)
        {
            if (item is GroupViewModel group && group.Model == model)
            {
                return group;
            }
        }

        return null;
    }
}
