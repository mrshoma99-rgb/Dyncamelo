using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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
    private double _localWatchWidth;
    private double _localWatchHeight;

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
        BrowseFileCommand = new RelayCommand(BrowseFile, () => Model is FilePathNode);
        FindInLibraryCommand = new RelayCommand(() => _owner.FindInLibrary(this));

        HeaderBrush = GetCategoryBrush(model.Category);
        SetLocationFromModel(new Point(model.X, model.Y));
        SyncPorts();
        UpdateValueDisplay();
        model.PropertyChanged += OnModelPropertyChanged;
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

    private void AddPort()
    {
        if (_addPortMethod == null)
        {
            return;
        }

        _addPortMethod.Invoke(Model, null);
        SyncPorts();
    }

    private void RemovePort()
    {
        if (_removePortMethod == null)
        {
            return;
        }

        _removePortMethod.Invoke(Model, null);
        SyncPorts();
        _owner.RefreshConnectedFlags();
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

        var firstValue = outputs.Count > 0 ? outputs[0].Value : null;
        WatchCountText = firstValue is IList list && !(firstValue is string)
            ? "List — " + list.Count.ToString(CultureInfo.InvariantCulture) + (list.Count == 1 ? " item" : " items")
            : string.Empty;
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
