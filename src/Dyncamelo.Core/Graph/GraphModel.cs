using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Dyncamelo.Core.Types;

namespace Dyncamelo.Core.Graph;

/// <summary>
/// The workspace: a directed acyclic graph of nodes plus canvas notes.
/// All structural mutations go through this class so that dirty propagation
/// stays eager and cycles are rejected at connect time, never at run time.
/// </summary>
public class GraphModel : INotifyPropertyChanged
{
    private readonly List<NodeModel> _nodes = new List<NodeModel>();
    private readonly List<ConnectionModel> _connections = new List<ConnectionModel>();
    private int _nextCreationIndex;
    private string _name = string.Empty;
    private RunType _runType = RunType.Automatic;

    /// <summary>Creates an empty graph.</summary>
    public GraphModel()
    {
        Uuid = Guid.NewGuid();
        Notes = new ObservableCollection<NoteModel>();
        Groups = new ObservableCollection<GroupModel>();
    }

    /// <summary>Stable identifier of the graph, persisted in .dyc files.</summary>
    public Guid Uuid { get; internal set; }

    /// <summary>Graph display name.</summary>
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    /// <summary>Graph description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// How runs are triggered. The engine never reads this; it is a contract
    /// between the graph file and the hosting UI (see engine documentation §6).
    /// </summary>
    public RunType RunType
    {
        get => _runType;
        set => SetField(ref _runType, value);
    }

    /// <summary>Nodes in the graph.</summary>
    public IReadOnlyList<NodeModel> Nodes => _nodes;

    /// <summary>Connections in the graph.</summary>
    public IReadOnlyList<ConnectionModel> Connections => _connections;

    /// <summary>Canvas notes (no execution semantics).</summary>
    public ObservableCollection<NoteModel> Notes { get; }

    /// <summary>Canvas groups (annotation rectangles, no execution semantics).</summary>
    public ObservableCollection<GroupModel> Groups { get; }

    /// <summary>Raised after a node is added.</summary>
    public event EventHandler<NodeEventArgs>? NodeAdded;

    /// <summary>Raised after a node (and its connections) is removed.</summary>
    public event EventHandler<NodeEventArgs>? NodeRemoved;

    /// <summary>Raised after a connection is created.</summary>
    public event EventHandler<ConnectionEventArgs>? ConnectionAdded;

    /// <summary>Raised after a connection is removed.</summary>
    public event EventHandler<ConnectionEventArgs>? ConnectionRemoved;

    /// <summary>
    /// Raised on every dirty-marking mutation (add/remove/connect/disconnect/value change).
    /// The UI layer uses this as the trigger for debounced automatic runs.
    /// </summary>
    public event EventHandler? Modified;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Adds a node to the graph and marks it dirty.</summary>
    /// <param name="node">The node to add.</param>
    public void AddNode(NodeModel node)
    {
        if (node == null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        if (node.Graph != null)
        {
            throw new InvalidOperationException("Node already belongs to a graph.");
        }

        node.Graph = this;
        node.CreationIndex = _nextCreationIndex++;
        _nodes.Add(node);
        NodeAdded?.Invoke(this, new NodeEventArgs(node));
        MarkDirty(node);
    }

    /// <summary>
    /// Removes a node and all connections touching it. Nodes that consumed its
    /// outputs are marked dirty.
    /// </summary>
    /// <param name="node">The node to remove.</param>
    /// <returns>True when the node was present.</returns>
    public bool RemoveNode(NodeModel node)
    {
        if (node == null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        if (!_nodes.Remove(node))
        {
            return false;
        }

        foreach (var connection in _connections.Where(c => c.SourceNode == node || c.TargetNode == node).ToList())
        {
            RemoveConnectionCore(connection, dirtyTarget: connection.SourceNode == node);
        }

        node.Graph = null;
        NodeRemoved?.Invoke(this, new NodeEventArgs(node));
        Modified?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Connects an output port to an input port. Validates directions, membership,
    /// loose type compatibility and acyclicity. An existing connection into the
    /// target port is replaced (an input accepts at most one wire).
    /// </summary>
    /// <param name="source">Output port.</param>
    /// <param name="target">Input port.</param>
    /// <returns>A result carrying either the created connection or a failure reason.</returns>
    public ConnectionResult Connect(PortModel source, PortModel target)
    {
        if (source == null || target == null)
        {
            return ConnectionResult.Fail("Both ports must be provided.");
        }

        if (source.Direction != PortDirection.Output || target.Direction != PortDirection.Input)
        {
            return ConnectionResult.Fail("Connections run from an output port to an input port.");
        }

        if (source.Owner.Graph != this || target.Owner.Graph != this)
        {
            return ConnectionResult.Fail("Both ports must belong to nodes in this graph.");
        }

        if (source.Owner == target.Owner)
        {
            return ConnectionResult.Fail("A node cannot be connected to itself.");
        }

        if (!TypeCoercion.CanConvert(source.DeclaredType, target.DeclaredType))
        {
            return ConnectionResult.Fail(
                "Type '" + source.DeclaredType.Name + "' cannot be converted to '" + target.DeclaredType.Name + "'.");
        }

        if (IsReachable(target.Owner, source.Owner))
        {
            return ConnectionResult.Fail("The connection would create a cycle.");
        }

        var existing = FindConnectionInto(target);
        if (existing != null)
        {
            RemoveConnectionCore(existing, dirtyTarget: false);
        }

        var connection = new ConnectionModel(source, target);
        _connections.Add(connection);
        target.UsingDefaultValue = false;
        ConnectionAdded?.Invoke(this, new ConnectionEventArgs(connection));
        MarkDirty(target.Owner);
        return ConnectionResult.Ok(connection);
    }

    /// <summary>Removes a connection; the consuming node is marked dirty.</summary>
    /// <param name="connection">The connection to remove.</param>
    /// <returns>True when the connection was present.</returns>
    public bool Disconnect(ConnectionModel connection)
    {
        if (connection == null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        if (!_connections.Contains(connection))
        {
            return false;
        }

        RemoveConnectionCore(connection, dirtyTarget: true);
        return true;
    }

    /// <summary>Returns the single connection feeding an input port, or null.</summary>
    /// <param name="input">An input port.</param>
    public ConnectionModel? FindConnectionInto(PortModel input)
    {
        return _connections.FirstOrDefault(c => c.Target == input);
    }

    /// <summary>Returns all connections leaving an output port.</summary>
    /// <param name="output">An output port.</param>
    public IEnumerable<ConnectionModel> FindConnectionsFrom(PortModel output)
    {
        return _connections.Where(c => c.Source == output);
    }

    /// <summary>
    /// Marks a node and its transitive downstream dirty (eager propagation, so the
    /// UI can always show exactly what the next run will execute) and raises
    /// <see cref="Modified"/>.
    /// </summary>
    /// <param name="node">Origin of the change.</param>
    public void MarkDirty(NodeModel node)
    {
        if (node == null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        foreach (var affected in CollectDownstream(node))
        {
            affected.IsDirty = true;
        }

        Modified?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Returns a node and everything transitively downstream of it (each node once).</summary>
    /// <param name="origin">Start node (included in the result).</param>
    public IReadOnlyCollection<NodeModel> CollectDownstream(NodeModel origin)
    {
        var visited = new HashSet<NodeModel>();
        var stack = new Stack<NodeModel>();
        stack.Push(origin);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            foreach (var connection in _connections)
            {
                if (connection.SourceNode == current && !visited.Contains(connection.TargetNode))
                {
                    stack.Push(connection.TargetNode);
                }
            }
        }

        return visited;
    }

    private void RemoveConnectionCore(ConnectionModel connection, bool dirtyTarget)
    {
        _connections.Remove(connection);
        var target = connection.Target;
        if (target.HasDefault)
        {
            target.UsingDefaultValue = true;
        }

        ConnectionRemoved?.Invoke(this, new ConnectionEventArgs(connection));
        if (dirtyTarget && connection.TargetNode.Graph == this)
        {
            MarkDirty(connection.TargetNode);
        }
    }

    /// <summary>True when <paramref name="to"/> is reachable from <paramref name="from"/> following connections downstream.</summary>
    private bool IsReachable(NodeModel from, NodeModel to)
    {
        return CollectDownstream(from).Contains(to);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

/// <summary>Event payload carrying a node.</summary>
public class NodeEventArgs : EventArgs
{
    /// <summary>Creates the payload.</summary>
    /// <param name="node">The affected node.</param>
    public NodeEventArgs(NodeModel node)
    {
        Node = node;
    }

    /// <summary>The affected node.</summary>
    public NodeModel Node { get; }
}

/// <summary>Event payload carrying a connection.</summary>
public class ConnectionEventArgs : EventArgs
{
    /// <summary>Creates the payload.</summary>
    /// <param name="connection">The affected connection.</param>
    public ConnectionEventArgs(ConnectionModel connection)
    {
        Connection = connection;
    }

    /// <summary>The affected connection.</summary>
    public ConnectionModel Connection { get; }
}
