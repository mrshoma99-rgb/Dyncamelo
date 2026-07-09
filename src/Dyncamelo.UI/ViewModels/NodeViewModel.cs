using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Nodes;
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

        AddPortCommand = new RelayCommand(AddPort, () => _addPortMethod != null);
        RemovePortCommand = new RelayCommand(RemovePort, () => _removePortMethod != null && Model.InPorts.Count > 1);
        SetLacingCommand = new RelayCommand<string>(SetLacing);
        BrowseFileCommand = new RelayCommand(BrowseFile, () => Model is FilePathNode);

        HeaderBrush = GetCategoryBrush(model.Category);
        SetLocationFromModel(new Point(model.X, model.Y));
        SyncPorts();
        model.PropertyChanged += OnModelPropertyChanged;
    }

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
    }

    /// <summary>Detaches model event handlers. Call when the node leaves the canvas.</summary>
    public void Detach()
    {
        Model.PropertyChanged -= OnModelPropertyChanged;
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
