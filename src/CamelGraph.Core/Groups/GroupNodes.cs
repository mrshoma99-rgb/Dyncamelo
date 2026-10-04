using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using Newtonsoft.Json.Linq;

namespace CamelGraph.Core.Groups;

/// <summary>
/// Base of the three node types that mirror a node group's interface: their ports follow the group's sockets, matched by the
/// socket's stable id, so a renamed or reordered socket keeps its wires. A socket that is removed and comes back (undo) gets the
/// very same port object again.
/// </summary>
public abstract class GroupBoundNode : NodeModel
{
    private readonly Dictionary<Guid, PortModel> _ports = new Dictionary<Guid, PortModel>();

    /// <summary>Makes the ports of one side match <paramref name="sockets"/>.</summary>
    /// <param name="side">Which side of this node.</param>
    /// <param name="sockets">The group's sockets for that side, in order.</param>
    /// <param name="optionalInputs">True when unwired input ports supply null instead of stopping the node.</param>
    protected void SyncSide(PortDirection side, IReadOnlyList<GroupSocket> sockets, bool optionalInputs)
    {
        var current = side == PortDirection.Input ? InPorts : OutPorts;
        var wanted = new List<PortModel>(sockets.Count);
        foreach (var socket in sockets)
        {
            if (!_ports.TryGetValue(socket.Id, out var port))
            {
                port = side == PortDirection.Input
                    ? (optionalInputs ? AddInput(socket.Name, typeof(object), (object?)null) : AddInput(socket.Name, typeof(object)))
                    : AddOutput(socket.Name, typeof(object));
                port.Id = socket.Id;
                _ports[socket.Id] = port;
            }
            else
            {
                AttachPort(port);
            }

            port.Rename(socket.Name);
            port.KindHint = socket.Kind;
            wanted.Add(port);
        }

        foreach (var stale in current.Where(p => !wanted.Contains(p)).ToList())
        {
            if (Graph != null)
            {
                foreach (var wire in Graph.Connections.Where(c => c.Source == stale || c.Target == stale).ToList())
                {
                    Graph.Disconnect(wire);
                }
            }

            RemovePort(stale);
        }

        SetPortOrder(side, wanted);
    }

    /// <summary>Tells the editor the ports changed (call once after syncing both sides).</summary>
    protected void NotifyPortsChanged() => RaisePortsChanged();

    /// <inheritdoc />
    public override bool ShowInLibrary => false;
}

/// <summary>
/// Inside a node group: hands the values that came in to the nodes of the body, one output per input socket of the group.
/// Wire from an empty socket's "+" (or add sockets in the group's interface) to extend it.
/// </summary>
public sealed class GroupInputNode : GroupBoundNode
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "GroupInput";

    private object?[] _fed = new object?[0];

    /// <summary>Creates the node (unbound until a group adopts it).</summary>
    public GroupInputNode()
    {
        Name = "Group Input";
        Category = "Node Groups";
        Description = "The values that enter this node group. Add sockets to give the group more inputs.";
    }

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override NodeFunction Function => NodeFunction.Create;

    /// <summary>The group this node is the input of.</summary>
    public NodeGroup? Owner { get; private set; }

    internal void Bind(NodeGroup group)
    {
        Owner = group;
        SyncPorts();
    }

    internal void SyncPorts()
    {
        if (Owner == null)
        {
            return;
        }

        SyncSide(PortDirection.Output, Owner.Inputs, optionalInputs: false);
        NotifyPortsChanged();
    }

    internal void Feed(object?[] values)
    {
        // No MarkDirty: the group marks its whole body to run right after, and feeding values is not an edit.
        _fed = values;
    }

    /// <inheritdoc />
    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        var values = new object?[OutPorts.Count];
        for (var i = 0; i < values.Length && i < _fed.Length; i++)
        {
            values[i] = _fed[i];
        }

        return values;
    }
}

/// <summary>
/// Inside a node group: collects the values that leave it, one input per output socket of the group. A socket nothing is wired
/// to leaves as null.
/// </summary>
public sealed class GroupOutputNode : GroupBoundNode
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "GroupOutput";

    private object?[] _captured = new object?[0];

    /// <summary>Creates the node (unbound until a group adopts it).</summary>
    public GroupOutputNode()
    {
        Name = "Group Output";
        Category = "Node Groups";
        Description = "The values that leave this node group. Add sockets to give the group more outputs.";
    }

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override NodeFunction Function => NodeFunction.Info;

    /// <summary>The group this node is the output of.</summary>
    public NodeGroup? Owner { get; private set; }

    /// <summary>What reached the node during the last run.</summary>
    internal object?[] Captured => _captured;

    internal void Bind(NodeGroup group)
    {
        Owner = group;
        SyncPorts();
    }

    internal void SyncPorts()
    {
        if (Owner == null)
        {
            return;
        }

        SyncSide(PortDirection.Input, Owner.Outputs, optionalInputs: true);
        NotifyPortsChanged();
    }

    internal void Reset(int outputs)
    {
        _captured = new object?[outputs];
    }

    /// <inheritdoc />
    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        _captured = (object?[])inputs.Clone();
        return new object?[] { null };
    }
}

/// <summary>
/// One use of a <see cref="NodeGroup"/>: an ordinary-looking node whose sockets are the group's interface and whose work is to run
/// the group's body. All instances share the definition, so editing the group changes them all.
/// </summary>
public sealed class GroupInstanceNode : GroupBoundNode
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "NodeGroup";

    private NodeGroup? _definition;
    private string _lastDefinitionName = string.Empty;

    /// <summary>Creates an unbound instance (used when reading a file; <see cref="Bind"/> attaches the definition).</summary>
    public GroupInstanceNode()
    {
        Name = "Node Group";
        Category = "Node Groups";
    }

    /// <summary>Creates an instance of <paramref name="definition"/>.</summary>
    public GroupInstanceNode(NodeGroup definition)
        : this()
    {
        Bind(definition);
    }

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override NodeFunction Function => NodeFunction.Modify;

    /// <summary>True when the group's body holds a node that reads live host state: the instance then runs on every run.</summary>
    public override bool IsLiveState => _definition != null && _definition.ContainsLiveState;

    /// <summary>The group this node runs.</summary>
    public NodeGroup? Definition => _definition;

    /// <summary>Id of the group this node runs (kept while the definition is not resolved).</summary>
    public Guid GroupId { get; private set; }

    /// <summary>Attaches the definition: ports now follow its interface, and it marks this node out of date whenever it changes.</summary>
    /// <param name="definition">The group to run.</param>
    public void Bind(NodeGroup definition)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        if (_definition != null)
        {
            Unbind();
        }

        _definition = definition;
        GroupId = definition.Id;
        Description = string.IsNullOrEmpty(definition.Description) ? "An instance of the node group '" + definition.Name + "'." : definition.Description;
        if (Name == "Node Group" || Name.Length == 0)
        {
            Name = definition.Name;
        }

        _lastDefinitionName = definition.Name;
        definition.Changed += OnDefinitionChanged;
        definition.InterfaceChanged += OnInterfaceChanged;
        definition.PropertyChanged += OnDefinitionPropertyChanged;
        SyncFromDefinition();
    }

    /// <summary>Detaches from the definition (the node is going away).</summary>
    public void Unbind()
    {
        if (_definition == null)
        {
            return;
        }

        _definition.Changed -= OnDefinitionChanged;
        _definition.InterfaceChanged -= OnInterfaceChanged;
        _definition.PropertyChanged -= OnDefinitionPropertyChanged;
        _definition = null;
    }

    /// <inheritdoc />
    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        var definition = _definition ?? throw new InvalidOperationException("This node group instance has no definition.");
        return definition.Run(this, inputs, context);
    }

    /// <inheritdoc />
    public override void SerializeData(JObject data)
    {
        data["GroupId"] = GroupId.ToString("N");
    }

    /// <inheritdoc />
    public override void DeserializeData(JObject data)
    {
        if (Guid.TryParse(data.Value<string>("GroupId"), out var id))
        {
            GroupId = id;
        }
    }

    private void SyncFromDefinition()
    {
        if (_definition == null)
        {
            return;
        }

        SyncSide(PortDirection.Input, _definition.Inputs, optionalInputs: false);
        SyncSide(PortDirection.Output, _definition.Outputs, optionalInputs: false);
        NotifyPortsChanged();
    }

    private void OnInterfaceChanged(object? sender, EventArgs e) => SyncFromDefinition();

    private void OnDefinitionChanged(object? sender, EventArgs e) => MarkDirty();

    private void OnDefinitionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_definition == null || e.PropertyName != nameof(NodeGroup.Name))
        {
            return;
        }

        // An instance still called after its group follows a rename; one the user named differently keeps its name.
        if (Name == _lastDefinitionName)
        {
            Name = _definition.Name;
        }

        _lastDefinitionName = _definition.Name;
    }
}
