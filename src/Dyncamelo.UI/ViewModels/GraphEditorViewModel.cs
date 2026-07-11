using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
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
public class GraphEditorViewModel : ObservableObject
{
    private const string FileFilter = "Dyncamelo Graph (*.dyc)|*.dyc|All files (*.*)|*.*";

    private readonly GraphEngine _engine = new GraphEngine();
    private readonly DispatcherTimer _autoRunTimer;
    private readonly UiSettingsService _settings;

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

    /// <summary>Creates the editor with an empty untitled graph.</summary>
    /// <param name="registry">Node registry (already populated by the host).</param>
    /// <param name="dialogs">Dialog service; a default WPF implementation is used when null.</param>
    /// <param name="settings">Persisted UI settings (favourites, recent files); the default %APPDATA% store is used when null.</param>
    public GraphEditorViewModel(NodeRegistry registry, IDialogService? dialogs = null, UiSettingsService? settings = null)
    {
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Dialogs = dialogs ?? new WpfDialogService();
        _settings = settings ?? new UiSettingsService();
        Library = new LibraryViewModel(registry, _settings);
        RecentFiles = new ObservableCollection<string>(_settings.RecentFiles);
        SampleGraphs = new ObservableCollection<SampleGraphViewModel>();

        Items = new ObservableCollection<CanvasItemViewModel>();
        Connections = new ObservableCollection<ConnectionViewModel>();
        SelectedItems = new ObservableCollection<CanvasItemViewModel>();
        SelectedConnections = new ObservableCollection<ConnectionViewModel>();
        SelectedConnections.CollectionChanged += OnSelectedConnectionsChanged;
        PendingConnection = new PendingConnectionViewModel();

        StartConnectionCommand = new RelayCommand<ConnectorViewModel>(
            _ => PendingConnection.IsVisible = true,
            connector => connector != null && !(connector.IsInput && connector.IsConnected));
        CreateConnectionCommand = new RelayCommand<ConnectorViewModel>(CompletePendingConnection);
        DisconnectConnectorCommand = new RelayCommand<ConnectorViewModel>(DisconnectConnector);
        RemoveConnectionCommand = new RelayCommand<ConnectionViewModel>(RemoveConnection);

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
        OpenRecentFileCommand = new RelayCommand<string>(OpenRecentFile);
        OpenSampleCommand = new RelayCommand<SampleGraphViewModel>(OpenSample);
        AddNodeCommand = new RelayCommand<object>(AddNodeFromParameter);
        AddNoteCommand = new RelayCommand<object>(AddNoteFromParameter);
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
        UpdateChoiceSelection();

        _autoRunTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _autoRunTimer.Tick += OnAutoRunTimerTick;

        _graph = new GraphModel { Name = "Untitled" };
        AttachGraph(_graph);
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
            var name = _graph.Name.Length > 0 ? _graph.Name : "Untitled";
            return _currentFilePath == null ? name : name + " — " + System.IO.Path.GetFileName(_currentFilePath);
        }
    }

    /// <summary>Status bar text (last action or run summary).</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
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
        private set => SetProperty(ref _nodeCount, value);
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
        get => _graph.RunType == RunType.Automatic;
        set
        {
            var runType = value ? RunType.Automatic : RunType.Manual;
            if (_graph.RunType != runType)
            {
                _graph.RunType = runType;
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

    /// <summary>Duplicates the selected nodes including wires between them (Ctrl+D).</summary>
    public ICommand DuplicateSelectionCommand { get; }

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

    /// <summary>
    /// Creates a node from a library id (zero-touch definition id or node type
    /// tag) and places it at the given graph-space location.
    /// </summary>
    /// <param name="libraryId">Definition id or node type tag.</param>
    /// <param name="location">Graph-space drop location.</param>
    /// <returns>The created node's view model, or null when the id is unknown.</returns>
    public NodeViewModel? AddNode(string libraryId, Point location)
    {
        NodeModel? node = Registry.CreateZeroTouchNode(libraryId) ?? Registry.CreateNode(libraryId);
        if (node == null)
        {
            StatusMessage = "Unknown node '" + libraryId + "'.";
            return null;
        }

        node.X = location.X;
        node.Y = location.Y;
        _graph.AddNode(node);
        StatusMessage = "Added " + node.Name + ".";
        return FindNodeViewModel(node);
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
        foreach (var node in _graph.Nodes.ToList())
        {
            node.MarkDirty();
        }
    }

    /// <summary>Re-computes every connector's IsConnected flag from the graph model.</summary>
    public void RefreshConnectedFlags()
    {
        foreach (var item in Items)
        {
            if (!(item is NodeViewModel node))
            {
                continue;
            }

            foreach (var connector in node.Inputs)
            {
                connector.IsConnected = _graph.FindConnectionInto(connector.Port) != null;
            }

            foreach (var connector in node.Outputs)
            {
                connector.IsConnected = _graph.FindConnectionsFrom(connector.Port).Any();
            }
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
        _graph = graph;
        AttachGraph(graph);
        CurrentFilePath = filePath;
        OnPropertyChanged(nameof(Graph));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsAutoRun));
        UpdateRunStatistics(null);
        if (IsAutoRun)
        {
            ScheduleAutoRun();
        }
    }

    /// <summary>Runs the dirty subgraph now (no-op while a run is already in progress).</summary>
    public void RunGraph()
    {
        _autoRunTimer.Stop();
        if (_engine.IsRunning)
        {
            return;
        }

        RunResult result;
        try
        {
            var context = EvaluationContextFactory != null ? EvaluationContextFactory() : new EvaluationContext();
            result = _engine.Run(_graph, context);
        }
        catch (Exception ex)
        {
            StatusMessage = "Run failed: " + ex.Message;
            return;
        }

        UpdateRunStatistics(result);
    }

    // ----- graph attachment -------------------------------------------------

    private void AttachGraph(GraphModel graph)
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
        graph.Modified += OnGraphModified;
        graph.PropertyChanged += OnGraphPropertyChanged;
        graph.Notes.CollectionChanged += OnNotesChanged;
        graph.Groups.CollectionChanged += OnGroupsChanged;

        RefreshConnectedFlags();
        NodeCount = graph.Nodes.Count;
    }

    private void DetachGraph()
    {
        _autoRunTimer.Stop();
        _graph.NodeAdded -= OnNodeAdded;
        _graph.NodeRemoved -= OnNodeRemoved;
        _graph.ConnectionAdded -= OnConnectionAdded;
        _graph.ConnectionRemoved -= OnConnectionRemoved;
        _graph.Modified -= OnGraphModified;
        _graph.PropertyChanged -= OnGraphPropertyChanged;
        _graph.Notes.CollectionChanged -= OnNotesChanged;
        _graph.Groups.CollectionChanged -= OnGroupsChanged;

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
                SelectedConnections.Remove(Connections[i]);
                Connections.RemoveAt(i);
            }
        }

        RefreshConnectedFlags();
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
        }
        else if (e.PropertyName == nameof(GraphModel.Name))
        {
            OnPropertyChanged(nameof(Title));
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
        }
    }

    private void CompletePendingConnection(ConnectorViewModel? target)
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
        if (connector == null)
        {
            return;
        }

        if (connector.IsInput)
        {
            var connection = _graph.FindConnectionInto(connector.Port);
            if (connection != null)
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
        if (connection != null)
        {
            _graph.Disconnect(connection.Model);
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

    private void DeleteSelection()
    {
        foreach (var connection in SelectedConnections.ToList())
        {
            _graph.Disconnect(connection.Model);
        }

        foreach (var item in SelectedItems.ToList())
        {
            if (item is NodeViewModel node)
            {
                _graph.RemoveNode(node.Model);
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

    private List<NodeModel> GetSelectedNodeModels()
    {
        return SelectedItems.OfType<NodeViewModel>().Select(n => n.Model).ToList();
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
        var members = SelectedItems.Where(item => !(item is GroupViewModel)).ToList();
        if (members.Count == 0)
        {
            StatusMessage = "Select some nodes to group.";
            return;
        }

        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (var item in members)
        {
            // Fall back to a nominal size when the view has not measured the item yet.
            double width = item.Size.Width > 0 ? item.Size.Width : 160d;
            double height = item.Size.Height > 0 ? item.Size.Height : 90d;
            left = Math.Min(left, item.Location.X);
            top = Math.Min(top, item.Location.Y);
            right = Math.Max(right, item.Location.X + width);
            bottom = Math.Max(bottom, item.Location.Y + height);
        }

        const double padding = 20d;
        const double headerHeight = 42d;
        var group = new GroupModel
        {
            Title = "Group",
            X = left - padding,
            Y = top - padding - headerHeight,
            Width = right - left + padding * 2d,
            Height = bottom - top + padding * 2d + headerHeight,
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
        if (_graph.Nodes.Count > 0 &&
            !Dialogs.Confirm("Discard the current graph and start a new one?", "New Graph"))
        {
            return;
        }

        LoadGraph(new GraphModel { Name = "Untitled" });
        StatusMessage = "New graph.";
    }

    private void OpenGraph()
    {
        var path = Dialogs.ShowOpenFile(FileFilter, "Open Dyncamelo Graph");
        if (path != null)
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

        OpenFromPath(path!);
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
            StatusMessage = "Opened " + System.IO.Path.GetFileName(path) + ".";
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
        // against silently discarding work (same prompt style as New).
        if (_graph.Nodes.Count > 0 &&
            !Dialogs.Confirm("Discard the current graph and open sample '" + sample.Name + "'?", "Open Sample"))
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
        var defaultName = (_graph.Name.Length > 0 ? _graph.Name : "graph") + ".dyc";
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
            if (_graph.Name.Length == 0 || _graph.Name == "Untitled")
            {
                _graph.Name = System.IO.Path.GetFileNameWithoutExtension(path);
            }

            var serializer = new GraphSerializer(Registry);
            serializer.SaveToFile(_graph, path);
            CurrentFilePath = path;
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
        RunGraph();
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

        if (result != null)
        {
            LastRunMilliseconds = Math.Round(result.Elapsed.TotalMilliseconds, 1);
            StatusMessage = result.Cancelled
                ? "Run cancelled."
                : "Run finished: " + result.ExecutedNodes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                  " node(s) executed in " + LastRunMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms.";
        }
        else
        {
            LastRunMilliseconds = 0;
        }
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
