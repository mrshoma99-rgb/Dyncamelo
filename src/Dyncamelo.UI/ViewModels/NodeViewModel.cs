using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Core.Types;
using Dyncamelo.UI.Mvvm;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// Wraps one <see cref="NodeModel"/> for the canvas: ports, position, execution
/// state (colored border + tooltip), category-colored header, and the commands
/// used by the inline editors of interactive nodes.
/// </summary>
public class NodeViewModel : CanvasItemViewModel
{
    private static readonly Brush DefaultHeaderBrush = CreateFrozenBrush("#FF4B5563");

    private readonly GraphEditorViewModel _owner;
    private readonly MethodInfo? _addPortMethod;
    private readonly MethodInfo? _removePortMethod;
    private readonly PropertyInfo? _viewWidthProperty;
    private readonly PropertyInfo? _viewHeightProperty;
    private readonly HashSet<PortModel> _watchedOutputPorts = new HashSet<PortModel>();
    private bool _showPreview = true;
    private string _previewText = string.Empty;
    private string _watchCountText = string.Empty;
    private bool _hasMorePreview;
    private bool _isPreviewExpanded;
    private string _expandedPreviewText = string.Empty;
    private double _localWatchWidth;
    private double _localWatchHeight;
    private readonly HashSet<PortModel> _watchedInputPorts = new HashSet<PortModel>();

    /// <summary>Creates the wrapper, builds connector view models and syncs the initial position.</summary>
    /// <param name="owner">The editor that owns this node.</param>
    /// <param name="model">The wrapped node.</param>
    public NodeViewModel(GraphEditorViewModel owner, NodeModel model)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Model = model ?? throw new ArgumentNullException(nameof(model));

        Inputs = new ObservableCollection<ConnectorViewModel>();
        Outputs = new ObservableCollection<ConnectorViewModel>();

        // Interactive nodes with a variable port count (e.g. List.Create in
        // Dyncamelo.Nodes, which this assembly does not reference) expose
        // AddItemPort()/RemoveItemPort(); discover them reflectively.
        _addPortMethod = model.GetType().GetMethod("AddItemPort", Type.EmptyTypes);
        _removePortMethod = model.GetType().GetMethod("RemoveItemPort", Type.EmptyTypes);

        // Resizable display nodes (Core's WatchNode, and any node pack that
        // follows the same convention) persist a user-chosen view size through
        // double ViewWidth/ViewHeight properties; discover them reflectively.
        _viewWidthProperty = FindSizeProperty(model, "ViewWidth");
        _viewHeightProperty = FindSizeProperty(model, "ViewHeight");

        AddPortCommand = new RelayCommand(AddPort, () => _addPortMethod != null);
        RemovePortCommand = new RelayCommand(RemovePort, () => _removePortMethod != null && Model.InPorts.Count > 1);
        SetLacingCommand = new RelayCommand<string>(SetLacing);
        BrowseFileCommand = new RelayCommand(BrowseFile, () => Model is FilePathNode || Model is DirectoryPathNode);
        FindInLibraryCommand = new RelayCommand(() => _owner.FindInLibrary(this));
        TogglePreviewExpandCommand = new RelayCommand(TogglePreviewExpand, () => _hasMorePreview);

        Rows = new ObservableCollection<NodeRowViewModel>();
        ToggleCollapseCommand = new RelayCommand(() => _owner.ToggleCollapse(this));
        ToggleMuteCommand = new RelayCommand(() => _owner.ToggleMute(this));
        ToggleFreezeCommand = new RelayCommand(() => _owner.ToggleFreeze(this));
        ToggleHideUnusedCommand = new RelayCommand(() => _owner.ToggleHideUnused(this));
        ResetWidthCommand = new RelayCommand(() => Model.Ui.Width = null);

        HeaderBrush = GetCategoryBrush(model.Category);
        SetLocationFromModel(new Point(model.X, model.Y));
        SyncPorts();
        UpdateValueDisplay();
        model.PropertyChanged += OnModelPropertyChanged;
        model.Ui.PropertyChanged += OnUiChanged;
        _owner.PropertyChanged += OnOwnerChanged;
    }

    // ----- row layout ---------------------------------------------------------

    /// <summary>The node's rows in the row layout, in display order.</summary>
    public ObservableCollection<NodeRowViewModel> Rows { get; }

    /// <summary>True for a wire waypoint, drawn as a tiny in/out pill instead of a card.</summary>
    public bool IsReroute => Model is RerouteNode;

    /// <summary>True when the node has its own body row (input, slider, watch… nodes; not zero-touch nodes or reroutes).</summary>
    public bool HasBody => !(Model is ZeroTouchNodeModel) && !(Model is RerouteNode);


    /// <summary>True at the overview zoom level: nodes shrink to their header.</summary>
    public bool IsOverview => _owner.LodLevel == LodLevel.Overview;

    /// <summary>Collapses/expands the node (H).</summary>
    public ICommand ToggleCollapseCommand { get; }

    /// <summary>Mutes/unmutes the node (M).</summary>
    public ICommand ToggleMuteCommand { get; }

    /// <summary>Freezes/unfreezes the node (Shift+M).</summary>
    public ICommand ToggleFreezeCommand { get; }

    /// <summary>Hides/shows the node's unused sockets (Ctrl+H).</summary>
    public ICommand ToggleHideUnusedCommand { get; }

    /// <summary>Returns the node to automatic width.</summary>
    public ICommand ResetWidthCommand { get; }

    /// <summary>True when the node is collapsed to its header.</summary>
    public bool IsCollapsed => Model.Ui.Collapsed;

    /// <summary>True when the node is muted (bypassed).</summary>
    public bool IsMuted => Model.IsMuted;

    /// <summary>User-set width, or NaN for automatic width.</summary>
    public double NodeWidth
    {
        get => Model.Ui.Width ?? double.NaN;
        set => Model.Ui.Width = double.IsNaN(value) ? (double?)null : value;
    }

    /// <summary>Recomputes the rows from the ports and presentation state; reuses row objects and touches the collection only when the sequence changed.</summary>
    public void RebuildRows()
    {
        var plan = RowPlanner.Plan(Model, port => FindConnector(port)?.IsConnected ?? false, HasBody, _owner.HideUnusedByDefault);
        var old = new Dictionary<string, NodeRowViewModel>(StringComparer.Ordinal);
        foreach (var row in Rows)
        {
            old[row.Key] = row;
        }

        var next = new List<NodeRowViewModel>(plan.Count);
        foreach (var item in plan)
        {
            if (!old.TryGetValue(item.Key, out var row))
            {
                var connector = item.Port == null ? null : FindConnector(item.Port);
                row = new NodeRowViewModel(this, item.Kind, item.Key, connector, item.Panel);
            }

            row.Apply(item);
            next.Add(row);
        }

        var same = next.Count == Rows.Count;
        for (var i = 0; same && i < next.Count; i++)
        {
            same = ReferenceEquals(next[i], Rows[i]);
        }

        if (same)
        {
            return;
        }

        Rows.Clear();
        foreach (var row in next)
        {
            Rows.Add(row);
        }
    }

    /// <summary>Shows every hidden port: turns hide-unused off and clears per-port hidden flags.</summary>
    public void RevealHidden()
    {
        using (_owner.History.Begin("Show hidden sockets"))
        {
            Model.Ui.HideUnused = false;
            foreach (var port in Model.InPorts)
            {
                port.IsHidden = false;
            }

            foreach (var port in Model.OutPorts)
            {
                port.IsHidden = false;
            }
        }
    }

    private void OnUiChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(NodeUiState.Width):
                OnPropertyChanged(nameof(NodeWidth));
                break;
            case nameof(NodeUiState.Collapsed):
                OnPropertyChanged(nameof(IsCollapsed));
                RebuildRows();
                break;
            default:
                RebuildRows();
                break;
        }
    }

    private void OnOwnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GraphEditorViewModel.LodLevel):
                OnPropertyChanged(nameof(IsOverview));
                foreach (var row in Rows)
                {
                    row.RaiseHeight();
                }

                break;
            case nameof(GraphEditorViewModel.RowBaseHeight):
                foreach (var row in Rows)
                {
                    row.RaiseHeight();
                }

                break;
            case nameof(GraphEditorViewModel.HideUnusedByDefault):
                RebuildRows();
                break;
            case nameof(GraphEditorViewModel.ColourBlindGlyphs):
                foreach (var socket in Inputs.Concat(Outputs))
                {
                    socket.RefreshGlyph();
                }

                break;
        }
    }

    private void SyncInputSubscriptions()
    {
        foreach (var port in Model.InPorts)
        {
            if (_watchedInputPorts.Add(port))
            {
                port.PropertyChanged += OnInputPortChanged;
            }
        }

        _watchedInputPorts.RemoveWhere(port =>
        {
            if (!Model.InPorts.Contains(port))
            {
                port.PropertyChanged -= OnInputPortChanged;
                return true;
            }

            return false;
        });
    }

    private void OnInputPortChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Pinning a value or hiding a socket changes which rows the planner keeps.
        if (e.PropertyName == nameof(PortModel.UserValue) || e.PropertyName == nameof(PortModel.IsHidden))
        {
            RebuildRows();
        }
    }

    /// <summary>The editor that owns this node.</summary>
    internal GraphEditorViewModel Owner => _owner;

    /// <summary>The wrapped Core node. Inline editor templates bind directly to its properties.</summary>
    public NodeModel Model { get; }

    /// <summary>Stable node id.</summary>
    public Guid Id => Model.Id;

    /// <summary>Display name shown in the node header.</summary>
    public string Title
    {
        get => Model.Name;
        set => Model.Name = value;
    }

    /// <summary>Node description (header tooltip).</summary>
    public string Description => Model.Description;

    /// <summary>Functional role (Create / Modify / Info) — drives the node's tint dot.</summary>
    public NodeFunction Function => Model.Function;

    /// <summary>Current execution state; drives the state border color.</summary>
    public NodeState State => Model.State;

    /// <summary>Joined diagnostic texts of the last execution.</summary>
    public string StateMessage => Model.StateMessage;

    /// <summary>True when there is at least one diagnostic to show in the tooltip.</summary>
    public bool HasMessage => Model.StateMessage.Length > 0;

    /// <summary>Freeze toggle: frozen nodes (and downstream) are skipped by runs.</summary>
    public bool IsFrozen
    {
        get => Model.IsFrozen;
        set => Model.IsFrozen = value;
    }

    /// <summary>Replication strategy for list inputs on scalar ports.</summary>
    public LacingMode Lacing
    {
        get => Model.Lacing;
        set => Model.Lacing = value;
    }

    /// <summary>Short lacing tag rendered on the node ("" for the Auto default).</summary>
    public string LacingLabel => Model.Lacing == LacingMode.Auto ? string.Empty : Model.Lacing.ToString();

    /// <summary>
    /// Per-node preview pin: when false the value bubble stays hidden even while
    /// the global toggle is on. Defaults to true (previews are opt-out).
    /// </summary>
    public bool ShowPreview
    {
        get => _showPreview;
        set
        {
            if (SetProperty(ref _showPreview, value))
            {
                OnPropertyChanged(nameof(IsPreviewVisible));
            }
        }
    }

    /// <summary>Compact summary of the node's output values, shown in the preview bubble.</summary>
    public string PreviewText
    {
        get => _previewText;
        private set
        {
            if (SetProperty(ref _previewText, value))
            {
                OnPropertyChanged(nameof(IsPreviewVisible));
            }
        }
    }

    /// <summary>
    /// True when the preview bubble should render: the node ran (successfully or
    /// with warnings), produced a summary, and neither the per-node pin nor the
    /// global toggle hides it.
    /// </summary>
    public bool IsPreviewVisible =>
        _owner.ShowNodePreviews &&
        _showPreview &&
        _previewText.Length > 0 &&
        (Model.State == NodeState.Executed || Model.State == NodeState.Warning);

    /// <summary>
    /// True when the preview summary truncates a list — the bubble becomes
    /// clickable and expands to the full item list.
    /// </summary>
    public bool HasMorePreview
    {
        get => _hasMorePreview;
        private set
        {
            if (SetProperty(ref _hasMorePreview, value))
            {
                OnPropertyChanged(nameof(ShowExpandHint));
            }
        }
    }

    /// <summary>True while the preview bubble shows the full item list.</summary>
    public bool IsPreviewExpanded
    {
        get => _isPreviewExpanded;
        private set
        {
            if (SetProperty(ref _isPreviewExpanded, value))
            {
                OnPropertyChanged(nameof(ShowExpandHint));
            }
        }
    }

    /// <summary>True when the "click to show all" hint line renders.</summary>
    public bool ShowExpandHint => _hasMorePreview && !_isPreviewExpanded;

    /// <summary>Full (line-per-item) rendering of the outputs, built when expanding.</summary>
    public string ExpandedPreviewText
    {
        get => _expandedPreviewText;
        private set => SetProperty(ref _expandedPreviewText, value);
    }

    /// <summary>Expands/collapses the preview bubble's full item list.</summary>
    public ICommand TogglePreviewExpandCommand { get; }

    /// <summary>
    /// Item-count line for watch displays ("List — 42 items"); empty for
    /// non-list values.
    /// </summary>
    public string WatchCountText
    {
        get => _watchCountText;
        private set
        {
            if (SetProperty(ref _watchCountText, value))
            {
                OnPropertyChanged(nameof(HasWatchCount));
            }
        }
    }

    /// <summary>True when <see cref="WatchCountText"/> has content.</summary>
    public bool HasWatchCount => _watchCountText.Length > 0;

    /// <summary>
    /// User-chosen width of a watch display area; NaN sizes automatically.
    /// Backed by the model's ViewWidth property when it exists (so it persists
    /// in the .dyc payload), otherwise kept in-memory.
    /// </summary>
    public double WatchWidth
    {
        get => ToViewSize(_viewWidthProperty != null ? (double)_viewWidthProperty.GetValue(Model)! : _localWatchWidth);
        set
        {
            var stored = double.IsNaN(value) ? 0d : Math.Max(0d, value);
            if (_viewWidthProperty != null)
            {
                _viewWidthProperty.SetValue(Model, stored);
            }
            else
            {
                _localWatchWidth = stored;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// User-chosen height of a watch display area; NaN sizes automatically.
    /// Backed by the model's ViewHeight property when it exists.
    /// </summary>
    public double WatchHeight
    {
        get => ToViewSize(_viewHeightProperty != null ? (double)_viewHeightProperty.GetValue(Model)! : _localWatchHeight);
        set
        {
            var stored = double.IsNaN(value) ? 0d : Math.Max(0d, value);
            if (_viewHeightProperty != null)
            {
                _viewHeightProperty.SetValue(Model, stored);
            }
            else
            {
                _localWatchHeight = stored;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>Input connectors, in port order.</summary>
    public ObservableCollection<ConnectorViewModel> Inputs { get; }

    /// <summary>Output connectors, in port order.</summary>
    public ObservableCollection<ConnectorViewModel> Outputs { get; }

    /// <summary>Header background derived from the node's root category.</summary>
    public Brush HeaderBrush { get; }

    /// <summary>Appends an item port on variable-port nodes (List.Create style).</summary>
    public ICommand AddPortCommand { get; }

    /// <summary>Removes the last item port on variable-port nodes.</summary>
    public ICommand RemovePortCommand { get; }

    /// <summary>Sets <see cref="Lacing"/> from a string parameter (context menu).</summary>
    public ICommand SetLacingCommand { get; }

    /// <summary>Opens a file dialog for <see cref="FilePathNode"/> inline editors.</summary>
    public ICommand BrowseFileCommand { get; }

    /// <summary>Reveals this node's entry in the library browser (context menu).</summary>
    public ICommand FindInLibraryCommand { get; }

    /// <summary>Finds the connector wrapping a Core port, or null.</summary>
    /// <param name="port">The Core port.</param>
    public ConnectorViewModel? FindConnector(PortModel port)
    {
        foreach (var connector in Inputs)
        {
            if (connector.Port == port)
            {
                return connector;
            }
        }

        foreach (var connector in Outputs)
        {
            if (connector.Port == port)
            {
                return connector;
            }
        }

        return null;
    }

    /// <summary>
    /// Rebuilds <see cref="Inputs"/>/<see cref="Outputs"/> from the model's port
    /// lists, preserving existing connector view models (and their anchors).
    /// </summary>
    public void SyncPorts()
    {
        SyncPortCollection(Inputs, Model.InPorts);
        SyncPortCollection(Outputs, Model.OutPorts);
        SyncOutputSubscriptions();
        SyncInputSubscriptions();
        RebuildRows();
    }

    /// <summary>Re-raises <see cref="IsPreviewVisible"/> (e.g. after the global preview toggle changed).</summary>
    public void RefreshPreviewVisibility()
    {
        OnPropertyChanged(nameof(IsPreviewVisible));
    }

    /// <summary>Detaches model event handlers. Call when the node leaves the canvas.</summary>
    public void Detach()
    {
        Model.PropertyChanged -= OnModelPropertyChanged;
        Model.Ui.PropertyChanged -= OnUiChanged;
        _owner.PropertyChanged -= OnOwnerChanged;
        foreach (var port in _watchedInputPorts)
        {
            port.PropertyChanged -= OnInputPortChanged;
        }

        _watchedInputPorts.Clear();
        foreach (var connector in Inputs)
        {
            connector.Detach();
        }

        foreach (var connector in Outputs)
        {
            connector.Detach();
        }

        foreach (var port in _watchedOutputPorts)
        {
            port.PropertyChanged -= OnOutputPortPropertyChanged;
        }

        _watchedOutputPorts.Clear();
    }

    /// <inheritdoc />
    protected override void OnLocationChanged(Point location)
    {
        Model.X = location.X;
        Model.Y = location.Y;
    }

    private void SyncPortCollection(
        ObservableCollection<ConnectorViewModel> connectors,
        System.Collections.Generic.IReadOnlyList<PortModel> ports)
    {
        // Remove connectors whose ports are gone.
        for (int i = connectors.Count - 1; i >= 0; i--)
        {
            bool alive = false;
            for (int j = 0; j < ports.Count; j++)
            {
                if (ports[j] == connectors[i].Port)
                {
                    alive = true;
                    break;
                }
            }

            if (!alive)
            {
                connectors.RemoveAt(i);
            }
        }

        // Append connectors for new ports (ports are only ever appended/removed at the end).
        for (int i = 0; i < ports.Count; i++)
        {
            bool present = false;
            for (int j = 0; j < connectors.Count; j++)
            {
                if (connectors[j].Port == ports[i])
                {
                    present = true;
                    break;
                }
            }

            if (!present)
            {
                connectors.Add(new ConnectorViewModel(this, ports[i]));
            }
        }
    }

    /// <summary>A multi-input's pill grew or shrank with its wire count: its row makes room.</summary>
    /// <param name="connector">The input whose wire count changed.</param>
    public void RefreshRowHeight(ConnectorViewModel connector)
    {
        foreach (var row in Rows)
        {
            if (ReferenceEquals(row.Connector, connector))
            {
                row.RaiseHeight();
            }
        }
    }

    private void AddPort()
    {
        if (_addPortMethod == null)
        {
            return;
        }

        var before = Model.InPorts.Count;
        var created = _addPortMethod.Invoke(Model, null) as PortModel;
        SyncPorts();
        var port = created ?? (Model.InPorts.Count > before ? Model.InPorts[Model.InPorts.Count - 1] : null);
        if (port != null)
        {
            _owner.History.Record(new PortCountStep(this, added: true, port));
        }
    }

    private void RemovePort()
    {
        if (_removePortMethod == null || Model.InPorts.Count == 0)
        {
            return;
        }

        var last = Model.InPorts[Model.InPorts.Count - 1];
        _removePortMethod.Invoke(Model, null);
        SyncPorts();
        _owner.RefreshConnectedFlags();
        if (!Model.InPorts.Contains(last))
        {
            _owner.History.Record(new PortCountStep(this, added: false, last));
        }
    }

    // Undo/redo put the very same port object back or take it out again, so wires and pinned values recorded
    // against it stay valid.
    private void InsertPortRaw(PortModel port)
    {
        if (Model.InPorts is IList<PortModel> ports && !ports.IsReadOnly && !ports.Contains(port))
        {
            ports.Add(port);
            Model.MarkDirty();
            SyncPorts();
            _owner.RefreshConnectedFlags();
        }
    }

    private void TakePortOutRaw(PortModel port)
    {
        if (Model.InPorts is IList<PortModel> ports && !ports.IsReadOnly && ports.Remove(port))
        {
            Model.MarkDirty();
            SyncPorts();
            _owner.RefreshConnectedFlags();
        }
    }

    /// <summary>Undo step for the +/- buttons of nodes with a variable number of inputs.</summary>
    private sealed class PortCountStep : IUndoStep
    {
        private readonly NodeViewModel _node;
        private readonly bool _added;
        private readonly PortModel _port;

        public PortCountStep(NodeViewModel node, bool added, PortModel port)
        {
            _node = node;
            _added = added;
            _port = port;
        }

        public string Label => _added ? "Add input" : "Remove input";

        public void Undo()
        {
            if (_added)
            {
                _node.TakePortOutRaw(_port);
            }
            else
            {
                _node.InsertPortRaw(_port);
            }
        }

        public void Redo()
        {
            if (_added)
            {
                _node.InsertPortRaw(_port);
            }
            else
            {
                _node.TakePortOutRaw(_port);
            }
        }
    }

    private void SetLacing(string? mode)
    {
        if (mode != null && Enum.TryParse<LacingMode>(mode, ignoreCase: true, out var parsed))
        {
            Lacing = parsed;
        }
    }

    private void BrowseFile()
    {
        if (Model is FilePathNode filePathNode)
        {
            var path = _owner.Dialogs.ShowOpenFile("All files (*.*)|*.*", "Select File");
            if (path != null)
            {
                filePathNode.Path = path;
            }
        }
        else if (Model is DirectoryPathNode directoryNode)
        {
            var path = _owner.Dialogs.PickFolder("Select Folder", directoryNode.Path);
            if (path != null)
            {
                directoryNode.Path = path;
            }
        }
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(NodeModel.Name):
                OnPropertyChanged(nameof(Title));
                break;
            case nameof(NodeModel.X):
            case nameof(NodeModel.Y):
                SetLocationFromModel(new Point(Model.X, Model.Y));
                break;
            case nameof(NodeModel.State):
                OnPropertyChanged(nameof(State));
                OnPropertyChanged(nameof(IsPreviewVisible));
                break;
            case "ViewWidth":
                OnPropertyChanged(nameof(WatchWidth));
                break;
            case "ViewHeight":
                OnPropertyChanged(nameof(WatchHeight));
                break;
            case nameof(NodeModel.StateMessage):
            case nameof(NodeModel.Messages):
                OnPropertyChanged(nameof(StateMessage));
                OnPropertyChanged(nameof(HasMessage));
                break;
            case nameof(NodeModel.IsFrozen):
                OnPropertyChanged(nameof(IsFrozen));
                break;
            case nameof(NodeModel.IsMuted):
                OnPropertyChanged(nameof(IsMuted));
                break;
            case nameof(NodeModel.Lacing):
                OnPropertyChanged(nameof(Lacing));
                OnPropertyChanged(nameof(LacingLabel));
                break;
        }
    }

    private void SyncOutputSubscriptions()
    {
        // Subscribe to output port value changes so the preview bubble and
        // watch summaries refresh after every run.
        var alive = new HashSet<PortModel>();
        foreach (var port in Model.OutPorts)
        {
            alive.Add(port);
            if (_watchedOutputPorts.Add(port))
            {
                port.PropertyChanged += OnOutputPortPropertyChanged;
            }
        }

        _watchedOutputPorts.RemoveWhere(port =>
        {
            if (!alive.Contains(port))
            {
                port.PropertyChanged -= OnOutputPortPropertyChanged;
                return true;
            }

            return false;
        });
    }

    private void OnOutputPortPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PortModel.Value))
        {
            if (sender is PortModel port)
            {
                FindConnector(port)?.RefreshObservedKind();
            }

            UpdateValueDisplay();
        }
    }

    private void UpdateValueDisplay()
    {
        var outputs = Model.OutPorts;
        string preview;
        if (outputs.Count == 0)
        {
            preview = string.Empty;
        }
        else if (outputs.Count == 1)
        {
            preview = SummarizeValue(outputs[0].Value);
        }
        else
        {
            var lines = new List<string>(outputs.Count);
            foreach (var port in outputs)
            {
                lines.Add(port.Name + ": " + SummarizeValue(port.Value));
            }

            preview = string.Join(Environment.NewLine, lines);
        }

        PreviewText = preview;

        // Fresh values invalidate any expanded view; it rebuilds on next expand.
        IsPreviewExpanded = false;
        ExpandedPreviewText = string.Empty;
        bool hasMore = false;
        foreach (var port in outputs)
        {
            if (port.Value is IList portList && !(port.Value is string) && portList.Count > 3)
            {
                hasMore = true;
                break;
            }
        }

        HasMorePreview = hasMore;

        var firstValue = outputs.Count > 0 ? outputs[0].Value : null;
        WatchCountText = firstValue is IList list && !(firstValue is string)
            ? "List — " + list.Count.ToString(CultureInfo.InvariantCulture) + (list.Count == 1 ? " item" : " items")
            : string.Empty;
    }

    private void TogglePreviewExpand()
    {
        if (_isPreviewExpanded)
        {
            IsPreviewExpanded = false;
            return;
        }

        if (_expandedPreviewText.Length == 0)
        {
            ExpandedPreviewText = BuildExpandedPreviewText(Model.OutPorts);
        }

        IsPreviewExpanded = true;
    }

    /// <summary>
    /// Renders every output in full — one line per list item — capped at
    /// <c>MaxExpandedLines</c> lines so a 100k-item list cannot stall the UI.
    /// </summary>
    private static string BuildExpandedPreviewText(IReadOnlyList<PortModel> outputs)
    {
        const int MaxExpandedLines = 500;
        var lines = new List<string>();
        foreach (var port in outputs)
        {
            if (outputs.Count > 1)
            {
                lines.Add(port.Name + ":");
            }

            if (port.Value is IList list && !(port.Value is string))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (lines.Count >= MaxExpandedLines)
                    {
                        lines.Add("… " + (list.Count - i).ToString(CultureInfo.InvariantCulture) + " more items");
                        return string.Join(Environment.NewLine, lines);
                    }

                    lines.Add(i.ToString(CultureInfo.InvariantCulture) + " : " + TypeCoercion.FormatValue(list[i]));
                }
            }
            else
            {
                lines.Add(TypeCoercion.FormatValue(port.Value));
            }

            if (lines.Count >= MaxExpandedLines)
            {
                break;
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string SummarizeValue(object? value)
    {
        if (value is IList list && !(value is string))
        {
            int shown = Math.Min(list.Count, 3);
            var parts = new List<string>(shown);
            for (int i = 0; i < shown; i++)
            {
                parts.Add(Truncate(TypeCoercion.FormatValue(list[i]), 24));
            }

            var summary = "[" + string.Join(", ", parts);
            if (list.Count > shown)
            {
                summary += ", … " + (list.Count - shown).ToString(CultureInfo.InvariantCulture) + " more";
            }

            return summary + "]";
        }

        return Truncate(TypeCoercion.FormatValue(value), 64);
    }

    private static string Truncate(string text, int maxLength)
    {
        // Keep the bubble compact: single line, bounded length.
        text = text.Replace("\r", " ").Replace("\n", " ");
        return text.Length <= maxLength ? text : text.Substring(0, maxLength - 1) + "…";
    }

    private static PropertyInfo? FindSizeProperty(NodeModel model, string name)
    {
        var property = model.GetType().GetProperty(name);
        return property != null &&
               property.PropertyType == typeof(double) &&
               property.CanRead &&
               property.CanWrite
            ? property
            : null;
    }

    private static double ToViewSize(double stored)
    {
        // 0 (or less) persists as "size automatically", which WPF spells NaN.
        return stored > 0 ? stored : double.NaN;
    }

    private static Brush GetCategoryBrush(string category)
    {
        var root = category ?? string.Empty;
        int dot = root.IndexOf('.');
        if (dot >= 0)
        {
            root = root.Substring(0, dot);
        }

        switch (root)
        {
            case "Input":
                return CreateFrozenBrush("#FFB45309");
            case "Display":
                return CreateFrozenBrush("#FF6D28D9");
            case "Math":
                return CreateFrozenBrush("#FF1D4ED8");
            case "Logic":
                return CreateFrozenBrush("#FF0F766E");
            case "String":
                return CreateFrozenBrush("#FFBE185D");
            case "List":
                return CreateFrozenBrush("#FF047857");
            case "Dictionary":
                return CreateFrozenBrush("#FF15803D");
            case "Color":
                return CreateFrozenBrush("#FFBE123C");
            case "DateTime":
            case "Date":
                return CreateFrozenBrush("#FF4338CA");
            case "File":
                return CreateFrozenBrush("#FF92400E");
            case "Geometry":
                return CreateFrozenBrush("#FF0E7490");
            case "Navisworks":
                return CreateFrozenBrush("#FF15803D");
            case "Units":
                return CreateFrozenBrush("#FF525F7A");
            default:
                return DefaultHeaderBrush;
        }
    }

    private static Brush CreateFrozenBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
