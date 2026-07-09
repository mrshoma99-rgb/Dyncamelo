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
using Newtonsoft.Json.Linq;

namespace Dyncamelo.UI.ViewModels;

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

    private GraphModel _graph;
    private string? _currentFilePath;
    private string _statusMessage = "Ready";
    private double _lastRunMilliseconds;
    private int _nodeCount;
    private int _errorCount;
    private int _warningCount;

    /// <summary>Creates the editor with an empty untitled graph.</summary>
    /// <param name="registry">Node registry (already populated by the host).</param>
    /// <param name="dialogs">Dialog service; a default WPF implementation is used when null.</param>
    public GraphEditorViewModel(NodeRegistry registry, IDialogService? dialogs = null)
    {
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Dialogs = dialogs ?? new WpfDialogService();
        Library = new LibraryViewModel(registry);

        Items = new ObservableCollection<CanvasItemViewModel>();
        Connections = new ObservableCollection<ConnectionViewModel>();
        SelectedItems = new ObservableCollection<CanvasItemViewModel>();
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
        DeleteSelectionCommand = new RelayCommand(DeleteSelection);
        DuplicateSelectionCommand = new RelayCommand(DuplicateSelection);
        AddNodeCommand = new RelayCommand<object>(AddNodeFromParameter);
        AddNoteCommand = new RelayCommand<object>(AddNoteFromParameter);

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

    /// <summary>Canvas items (nodes and notes); bound to the editor's ItemsSource.</summary>
    public ObservableCollection<CanvasItemViewModel> Items { get; }

    /// <summary>Wires; bound to the editor's Connections.</summary>
    public ObservableCollection<ConnectionViewModel> Connections { get; }

    /// <summary>Current canvas selection; kept in sync by the editor.</summary>
    public ObservableCollection<CanvasItemViewModel> SelectedItems { get; }

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

    /// <summary>Deletes the selected nodes and notes (Delete key).</summary>
    public ICommand DeleteSelectionCommand { get; }

    /// <summary>Duplicates the selected nodes including wires between them (Ctrl+D).</summary>
    public ICommand DuplicateSelectionCommand { get; }

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

        graph.NodeAdded += OnNodeAdded;
        graph.NodeRemoved += OnNodeRemoved;
        graph.ConnectionAdded += OnConnectionAdded;
        graph.ConnectionRemoved += OnConnectionRemoved;
        graph.Modified += OnGraphModified;
        graph.PropertyChanged += OnGraphPropertyChanged;
        graph.Notes.CollectionChanged += OnNotesChanged;

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
        }

        SelectedItems.Clear();
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
                Connections.RemoveAt(i);
            }
        }

        RefreshConnectedFlags();
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
            Connections.Add(new ConnectionViewModel(connection, source, target));
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
        }
    }

    private void DuplicateSelection()
    {
        var selected = SelectedItems.OfType<NodeViewModel>()
            .Where(n => !(n.Model is MissingNodeModel))
            .Select(n => n.Model)
            .ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var clones = new Dictionary<NodeModel, NodeModel>();
        foreach (var original in selected)
        {
            var clone = CreateClone(original);
            if (clone == null)
            {
                continue;
            }

            clone.Name = original.Name;
            clone.X = original.X + 40;
            clone.Y = original.Y + 40;
            clone.Lacing = original.Lacing;
            clone.IsFrozen = original.IsFrozen;

            var data = new JObject();
            original.SerializeData(data);
            clone.DeserializeData(data);

            _graph.AddNode(clone);
            clones[original] = clone;

            // Preserve per-port default-value opt-outs.
            for (int i = 0; i < original.InPorts.Count && i < clone.InPorts.Count; i++)
            {
                if (clone.InPorts[i].HasDefault)
                {
                    clone.InPorts[i].UsingDefaultValue = original.InPorts[i].UsingDefaultValue;
                }
            }
        }

        // Re-create wires that ran between duplicated nodes.
        foreach (var connection in _graph.Connections.ToList())
        {
            if (clones.TryGetValue(connection.SourceNode, out var newSource) &&
                clones.TryGetValue(connection.TargetNode, out var newTarget))
            {
                var fromPort = newSource.OutPorts.FirstOrDefault(p => p.Name == connection.Source.Name);
                var toPort = newTarget.InPorts.FirstOrDefault(p => p.Name == connection.Target.Name);
                if (fromPort != null && toPort != null)
                {
                    _graph.Connect(fromPort, toPort);
                }
            }
        }

        // Move the selection to the duplicates.
        SelectedItems.Clear();
        foreach (var clone in clones.Values)
        {
            var viewModel = FindNodeViewModel(clone);
            if (viewModel != null)
            {
                SelectedItems.Add(viewModel);
            }
        }

        StatusMessage = "Duplicated " + clones.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " node(s).";
    }

    private NodeModel? CreateClone(NodeModel original)
    {
        if (original is ZeroTouchNodeModel zeroTouch)
        {
            return Registry.CreateZeroTouchNode(zeroTouch.Definition.Id);
        }

        return Registry.CreateNode(original.NodeType);
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
        if (path == null)
        {
            return;
        }

        try
        {
            var serializer = new GraphSerializer(Registry);
            var graph = serializer.LoadFromFile(path);
            LoadGraph(graph, path);
            StatusMessage = "Opened " + System.IO.Path.GetFileName(path) + ".";
        }
        catch (GraphFormatException ex)
        {
            Dialogs.ShowError(ex.Message, "Open Graph");
        }
        catch (System.IO.IOException ex)
        {
            Dialogs.ShowError(ex.Message, "Open Graph");
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
        }
        catch (System.IO.IOException ex)
        {
            Dialogs.ShowError(ex.Message, "Save Graph");
        }
        catch (UnauthorizedAccessException ex)
        {
            Dialogs.ShowError(ex.Message, "Save Graph");
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
}
