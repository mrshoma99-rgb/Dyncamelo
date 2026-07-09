using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Dyncamelo.Core.Execution;
using Newtonsoft.Json.Linq;

namespace Dyncamelo.Core.Graph;

/// <summary>
/// Base class for every node on the canvas. Subclasses declare ports in their
/// constructor (via <see cref="AddInput(string, Type, string?)"/>/<see cref="AddOutput"/>) and implement
/// <see cref="Evaluate"/>, which receives one already-coerced scalar argument per
/// input port and returns one value per output port. List handling (replication)
/// is performed by the engine around <see cref="Evaluate"/> based on the ports'
/// declared types and the node's <see cref="Lacing"/>.
/// </summary>
public abstract class NodeModel : INotifyPropertyChanged
{
    private readonly List<PortModel> _inPorts = new List<PortModel>();
    private readonly List<PortModel> _outPorts = new List<PortModel>();
    private readonly List<NodeMessage> _messages = new List<NodeMessage>();
    private string _name = string.Empty;
    private double _x;
    private double _y;
    private bool _isFrozen;
    private LacingMode _lacing = LacingMode.Auto;
    private NodeState _state = NodeState.Idle;

    /// <summary>Initializes the node with a fresh identifier.</summary>
    protected NodeModel()
    {
        Id = Guid.NewGuid();
        IsDirty = true;
    }

    /// <summary>Stable identifier, persisted in .dyc files.</summary>
    public Guid Id { get; internal set; }

    /// <summary>
    /// Logical type tag used by serialization to re-instantiate the node
    /// (e.g. "ZeroTouch", "NumberSlider"). Must be stable across versions.
    /// </summary>
    public abstract string NodeType { get; }

    /// <summary>Display name.</summary>
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    /// <summary>Dot-separated library category path (e.g. "Math.Trig").</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Description shown in the library browser and node tooltip.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Input ports, in declaration order.</summary>
    public IReadOnlyList<PortModel> InPorts => _inPorts;

    /// <summary>Output ports, in declaration order.</summary>
    public IReadOnlyList<PortModel> OutPorts => _outPorts;

    /// <summary>Canvas X position.</summary>
    public double X
    {
        get => _x;
        set => SetField(ref _x, value);
    }

    /// <summary>Canvas Y position.</summary>
    public double Y
    {
        get => _y;
        set => SetField(ref _y, value);
    }

    /// <summary>
    /// Replication strategy applied when scalar inputs receive lists.
    /// Changing it dirties the node.
    /// </summary>
    public LacingMode Lacing
    {
        get => _lacing;
        set
        {
            if (SetField(ref _lacing, value))
            {
                MarkDirty();
            }
        }
    }

    /// <summary>
    /// Frozen nodes (and everything downstream of them) are excluded from
    /// execution and keep their stale cached values. Un-freezing dirties the node.
    /// </summary>
    public bool IsFrozen
    {
        get => _isFrozen;
        set
        {
            if (SetField(ref _isFrozen, value))
            {
                MarkDirty();
            }
        }
    }

    /// <summary>True when the node must re-execute on the next run.</summary>
    public bool IsDirty { get; internal set; }

    /// <summary>Current execution state.</summary>
    public NodeState State
    {
        get => _state;
        internal set
        {
            if (SetField(ref _state, value))
            {
                OnPropertyChanged(nameof(StateMessage));
                NodeStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Diagnostics produced by the most recent execution.</summary>
    public IReadOnlyList<NodeMessage> Messages => _messages;

    /// <summary>All diagnostic texts joined into one display string (empty when clean).</summary>
    public string StateMessage => string.Join(Environment.NewLine, _messages.Select(m => m.Text));

    /// <summary>Last computed value of each output port, in port order.</summary>
    public object?[] OutputValues => _outPorts.Select(p => p.Value).ToArray();

    /// <summary>The graph the node currently belongs to (null while detached).</summary>
    public GraphModel? Graph { get; internal set; }

    /// <summary>Monotonic insertion index used for deterministic scheduling.</summary>
    internal int CreationIndex { get; set; }

    /// <summary>Raised whenever <see cref="State"/> changes, so UIs can update badges without polling.</summary>
    public event EventHandler? NodeStateChanged;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Computes the node's outputs for a single (scalar-level) invocation.
    /// </summary>
    /// <param name="inputs">One value per input port, already coerced to the declared port types.</param>
    /// <param name="context">Ambient services and cancellation.</param>
    /// <returns>One value per output port.</returns>
    /// <remarks>
    /// Exceptions thrown here are caught by the engine and turned into an
    /// <see cref="NodeState.Error"/> state on this node; they never abort the run.
    /// </remarks>
    public abstract object?[] Evaluate(object?[] inputs, EvaluationContext context);

    /// <summary>
    /// Writes node-specific payload (slider range, literal value, ...) into the
    /// .dyc "Data" object. Base implementation writes nothing.
    /// </summary>
    /// <param name="data">Mutable JSON object persisted with the node.</param>
    public virtual void SerializeData(JObject data)
    {
    }

    /// <summary>
    /// Restores node-specific payload from the .dyc "Data" object.
    /// Base implementation reads nothing.
    /// </summary>
    /// <param name="data">JSON object saved by <see cref="SerializeData"/>.</param>
    public virtual void DeserializeData(JObject data)
    {
    }

    /// <summary>
    /// Marks this node (and, when attached to a graph, its transitive downstream)
    /// as needing re-execution. Call from property setters that affect outputs.
    /// </summary>
    public void MarkDirty()
    {
        if (Graph != null)
        {
            Graph.MarkDirty(this);
        }
        else
        {
            IsDirty = true;
        }
    }

    /// <summary>Declares an input port. Call from the subclass constructor.</summary>
    /// <param name="name">Port name, unique among the node's inputs.</param>
    /// <param name="declaredType">Declared CLR type.</param>
    /// <param name="description">Optional tooltip.</param>
    protected PortModel AddInput(string name, Type declaredType, string? description = null)
    {
        var port = new PortModel(this, name, declaredType, PortDirection.Input)
        {
            Description = description ?? string.Empty,
        };
        _inPorts.Add(port);
        return port;
    }

    /// <summary>Declares an input port with a default value used while unconnected.</summary>
    /// <param name="name">Port name, unique among the node's inputs.</param>
    /// <param name="declaredType">Declared CLR type.</param>
    /// <param name="defaultValue">Value supplied when the port is unconnected.</param>
    /// <param name="description">Optional tooltip.</param>
    protected PortModel AddInput(string name, Type declaredType, object? defaultValue, string? description = null)
    {
        var port = AddInput(name, declaredType, description);
        port.HasDefault = true;
        port.DefaultValue = defaultValue;
        port.UsingDefaultValue = true;
        return port;
    }

    /// <summary>Declares an output port. Call from the subclass constructor.</summary>
    /// <param name="name">Port name, unique among the node's outputs.</param>
    /// <param name="declaredType">Declared CLR type (use <see cref="object"/> when unknown).</param>
    /// <param name="description">Optional tooltip.</param>
    protected PortModel AddOutput(string name, Type declaredType, string? description = null)
    {
        var port = new PortModel(this, name, declaredType, PortDirection.Output)
        {
            Description = description ?? string.Empty,
        };
        _outPorts.Add(port);
        return port;
    }

    /// <summary>Adds a diagnostic to the node. Used by the engine and by <see cref="Evaluate"/> implementations.</summary>
    /// <param name="severity">Severity of the diagnostic.</param>
    /// <param name="text">Message text.</param>
    public void AddMessage(MessageSeverity severity, string text)
    {
        _messages.Add(new NodeMessage(severity, text));
        OnPropertyChanged(nameof(Messages));
        OnPropertyChanged(nameof(StateMessage));
    }

    /// <summary>Clears all diagnostics (called by the engine at the start of each execution).</summary>
    internal void ClearMessages()
    {
        if (_messages.Count > 0)
        {
            _messages.Clear();
            OnPropertyChanged(nameof(Messages));
            OnPropertyChanged(nameof(StateMessage));
        }
    }

    /// <summary>Sets a field and raises <see cref="PropertyChanged"/> when the value changed.</summary>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>Raises <see cref="PropertyChanged"/>.</summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <inheritdoc />
    public override string ToString() => Name + " [" + NodeType + "]";
}
