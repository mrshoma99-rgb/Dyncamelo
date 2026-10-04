using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;

namespace CamelGraph.Core.Groups;

/// <summary>
/// A reusable subgraph — the definition behind any number of <see cref="GroupInstanceNode"/>s. Its interface is a list of named
/// sockets (<see cref="Inputs"/>, <see cref="Outputs"/>); inside, the <see cref="InputNode"/> supplies the values that came in
/// and the <see cref="OutputNode"/> collects the values that leave. Editing the body or the interface changes every instance.
/// </summary>
public sealed class NodeGroup : INotifyPropertyChanged
{
    private readonly List<GroupSocket> _inputs = new List<GroupSocket>();
    private readonly List<GroupSocket> _outputs = new List<GroupSocket>();
    private string _name;
    private string _description = string.Empty;
    private bool _running;
    private bool _notifying;

    internal NodeGroup(NodeGroupLibrary library, Guid id, string name)
    {
        Library = library;
        Id = id;
        _name = name;
        Graph = new GraphModel { Name = name };
        Graph.NodeGroups = library;
        Graph.OwnerGroup = this;
        Graph.Modified += OnInnerModified;
    }

    /// <summary>The document's library this group belongs to.</summary>
    public NodeGroupLibrary Library { get; }

    /// <summary>Stable identity, saved in the file.</summary>
    public Guid Id { get; internal set; }

    /// <summary>Name shown on instances and in the library.</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (!string.Equals(_name, value, StringComparison.Ordinal))
            {
                _name = value ?? throw new ArgumentNullException(nameof(value));
                Graph.Name = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>What the group does (tooltip of its library entry).</summary>
    public string Description
    {
        get => _description;
        set
        {
            if (!string.Equals(_description, value, StringComparison.Ordinal))
            {
                _description = value ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The body.</summary>
    public GraphModel Graph { get; }

    /// <summary>The body's Group Input node (present once the group is set up).</summary>
    public GroupInputNode InputNode { get; private set; } = null!;

    /// <summary>The body's Group Output node.</summary>
    public GroupOutputNode OutputNode { get; private set; } = null!;

    /// <summary>The sockets values come in through.</summary>
    public IReadOnlyList<GroupSocket> Inputs => _inputs;

    /// <summary>The sockets values leave through.</summary>
    public IReadOnlyList<GroupSocket> Outputs => _outputs;

    /// <summary>Raised when anything about the group changed that makes its instances out of date (body or interface).</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when sockets were added, removed, renamed, retyped or reordered; instances re-read their ports.</summary>
    public event EventHandler? InterfaceChanged;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The sockets of one side.</summary>
    public IReadOnlyList<GroupSocket> Sockets(SocketSide side) => side == SocketSide.Input ? _inputs : _outputs;

    /// <summary>The socket with the given id, or null.</summary>
    public GroupSocket? FindSocket(SocketSide side, Guid id) => Sockets(side).FirstOrDefault(s => s.Id == id);

    /// <summary>Adds a socket (at the end unless <paramref name="index"/> says otherwise); its name is made unique on its side.</summary>
    /// <param name="side">Input or output.</param>
    /// <param name="name">Wanted name.</param>
    /// <param name="kind">Kind hint ("number", "text*"…), empty for any.</param>
    /// <param name="index">Position, or null for the end.</param>
    /// <param name="id">A specific id (undo brings a removed socket back under its old one).</param>
    public GroupSocket AddSocket(SocketSide side, string name, string kind = "", int? index = null, Guid? id = null)
    {
        var list = Mutable(side);
        var socket = new GroupSocket(id ?? Guid.NewGuid(), UniqueSocketName(side, name, null), kind ?? string.Empty);
        var at = index.HasValue ? Math.Max(0, Math.Min(index.Value, list.Count)) : list.Count;
        list.Insert(at, socket);
        RaiseInterfaceChanged();
        return socket;
    }

    /// <summary>Removes a socket; wires attached to it on the Group Input/Output node and on every instance are disconnected.</summary>
    public bool RemoveSocket(SocketSide side, Guid id)
    {
        var socket = FindSocket(side, id);
        if (socket == null)
        {
            return false;
        }

        Mutable(side).Remove(socket);
        RaiseInterfaceChanged();
        return true;
    }

    /// <summary>Renames a socket (made unique on its side). Wires stay.</summary>
    public bool RenameSocket(SocketSide side, Guid id, string name)
    {
        var socket = FindSocket(side, id);
        if (socket == null)
        {
            return false;
        }

        var unique = UniqueSocketName(side, name, id);
        if (unique == socket.Name)
        {
            return false;
        }

        socket.Name = unique;
        RaiseInterfaceChanged();
        return true;
    }

    /// <summary>Changes the kind hint of a socket.</summary>
    public bool SetSocketKind(SocketSide side, Guid id, string kind)
    {
        var socket = FindSocket(side, id);
        if (socket == null || socket.Kind == (kind ?? string.Empty))
        {
            return false;
        }

        socket.Kind = kind ?? string.Empty;
        RaiseInterfaceChanged();
        return true;
    }

    /// <summary>Moves a socket to a new position on its side.</summary>
    public bool MoveSocket(SocketSide side, Guid id, int newIndex)
    {
        var list = Mutable(side);
        var socket = FindSocket(side, id);
        if (socket == null)
        {
            return false;
        }

        var target = Math.Max(0, Math.Min(newIndex, list.Count - 1));
        var from = list.IndexOf(socket);
        if (from == target)
        {
            return false;
        }

        list.RemoveAt(from);
        list.Insert(target, socket);
        RaiseInterfaceChanged();
        return true;
    }

    /// <summary>True when the body contains an instance of <paramref name="other"/>, directly or through another group.</summary>
    public bool Uses(NodeGroup other) => Uses(other, new HashSet<NodeGroup>());

    /// <summary>
    /// True when the body (or a group used inside it) holds a node that reads live host state, so its instances must run on every
    /// run instead of serving their cached outputs.
    /// </summary>
    public bool ContainsLiveState => HasLiveNode(new HashSet<NodeGroup>());

    private bool HasLiveNode(HashSet<NodeGroup> seen)
    {
        if (!seen.Add(this))
        {
            return false;
        }

        foreach (var node in Graph.Nodes)
        {
            if (node is GroupInstanceNode instance)
            {
                if (instance.Definition != null && instance.Definition.HasLiveNode(seen))
                {
                    return true;
                }
            }
            else if (node.IsLiveState && !node.IsMuted)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Runs the body for one set of inputs with a fresh engine on the caller's thread: every body node is marked to run, the
    /// Group Input node supplies <paramref name="inputs"/>, and what reaches the Group Output node is returned.
    /// Cancelling the run, progress reports and the group path all pass through <paramref name="context"/>.
    /// </summary>
    /// <param name="caller">The instance being evaluated; problems inside are reported on it.</param>
    /// <param name="inputs">One value per input socket.</param>
    /// <param name="context">The run's context.</param>
    /// <returns>One value per output socket.</returns>
    internal object?[] Run(GroupInstanceNode? caller, object?[] inputs, EvaluationContext context)
    {
        if (_running)
        {
            throw new InvalidOperationException("The node group '" + Name + "' contains itself, so it cannot run.");
        }

        _running = true;
        try
        {
            using (context.EnterScope(Name))
            {
                InputNode.Feed(inputs);
                OutputNode.Reset(_outputs.Count);
                Graph.ResetForRun();
                var result = new GraphEngine().Run(Graph, context);
                if (result.Cancelled)
                {
                    throw new OperationCanceledException(context.CancellationToken);
                }

                if (!result.ExecutedNodes.Contains(OutputNode))
                {
                    // The Group Output sits behind a frozen node, so nothing new reached it. Like a frozen node on the canvas,
                    // the instance keeps the results it had and says why (instead of delivering empty outputs as if all was well).
                    caller?.AddMessage(
                        MessageSeverity.Warning,
                        "A node inside '" + Name + "' is frozen, so the outputs of this group were not updated.");
                    ReportInnerProblems(caller);
                    return KeptOutputs(caller);
                }

                ReportInnerProblems(caller);
                return OutputNode.Captured;
            }
        }
        finally
        {
            _running = false;
        }
    }

    internal void CreateInterfaceNodes()
    {
        InputNode = new GroupInputNode { X = -320, Y = 0 };
        InputNode.Bind(this);
        Graph.AddNode(InputNode);
        OutputNode = new GroupOutputNode { X = 320, Y = 0 };
        OutputNode.Bind(this);
        Graph.AddNode(OutputNode);
    }

    // After a body was loaded from a file: use the interface nodes it contains, making any that are missing.
    internal void AdoptInterfaceNodes()
    {
        var inputs = Graph.Nodes.OfType<GroupInputNode>().ToList();
        var outputs = Graph.Nodes.OfType<GroupOutputNode>().ToList();
        foreach (var extra in inputs.Skip(1).Cast<NodeModel>().Concat(outputs.Skip(1)))
        {
            Graph.RemoveNode(extra);
        }

        if (inputs.Count == 0)
        {
            InputNode = new GroupInputNode { X = -320, Y = 0 };
            InputNode.Bind(this);
            Graph.AddNode(InputNode);
        }
        else
        {
            InputNode = inputs[0];
            InputNode.Bind(this);
        }

        if (outputs.Count == 0)
        {
            OutputNode = new GroupOutputNode { X = 320, Y = 0 };
            OutputNode.Bind(this);
            Graph.AddNode(OutputNode);
        }
        else
        {
            OutputNode = outputs[0];
            OutputNode.Bind(this);
        }
    }

    // A socket read from a file: no events, the interface nodes are synced once the body is in.
    internal void LoadSocket(SocketSide side, Guid id, string name, string kind)
    {
        Mutable(side).Add(new GroupSocket(id, UniqueSocketName(side, name, null), kind ?? string.Empty));
    }

    internal string UniqueSocketName(SocketSide side, string wanted, Guid? except)
    {
        var baseName = string.IsNullOrWhiteSpace(wanted) ? (side == SocketSide.Input ? "Input" : "Output") : wanted.Trim();
        var name = baseName;
        var n = 2;
        var list = Sockets(side);
        while (list.Any(s => (!except.HasValue || s.Id != except.Value) && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = baseName + " " + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            n++;
        }

        return name;
    }

    private List<GroupSocket> Mutable(SocketSide side) => side == SocketSide.Input ? _inputs : _outputs;

    private bool Uses(NodeGroup other, HashSet<NodeGroup> seen)
    {
        if (!seen.Add(this))
        {
            return false;
        }

        foreach (var node in Graph.Nodes)
        {
            if (node is GroupInstanceNode instance && instance.Definition is NodeGroup used &&
                (ReferenceEquals(used, other) || used.Uses(other, seen)))
            {
                return true;
            }
        }

        return false;
    }

    private void RaiseInterfaceChanged()
    {
        InputNode?.SyncPorts();
        OutputNode?.SyncPorts();
        InterfaceChanged?.Invoke(this, EventArgs.Empty);
        RaiseChanged();
    }

    private void OnInnerModified(object? sender, EventArgs e)
    {
        // Running the body sets states and values but is not an edit; only real edits make instances stale.
        if (!_running)
        {
            RaiseChanged();
        }
    }

    // Instances mark themselves dirty, which can reach this group again only if it (wrongly) contains itself: stop there.
    private void RaiseChanged()
    {
        if (_notifying)
        {
            return;
        }

        _notifying = true;
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _notifying = false;
        }
    }

    // What the instance showed before: the values to hand out again when the body did not deliver new ones.
    private object?[] KeptOutputs(GroupInstanceNode? caller)
    {
        var kept = new object?[_outputs.Count];
        if (caller != null)
        {
            for (var i = 0; i < kept.Length && i < caller.OutPorts.Count; i++)
            {
                kept[i] = caller.OutPorts[i].Value;
            }
        }

        return kept;
    }

    private void ReportInnerProblems(GroupInstanceNode? caller)
    {
        if (caller == null)
        {
            return;
        }

        // A failure that a Flow.Try (or another node that catches errors) took care of is not a problem of the group.
        var failed = Graph.Nodes.Where(n => n.State == NodeState.Error && !IsRecovered(n)).ToList();
        foreach (var node in failed.Take(3))
        {
            var why = node.Messages.FirstOrDefault(m => m.Severity >= MessageSeverity.Error)?.Text ?? "it failed";
            caller.AddMessage(MessageSeverity.Error, "Inside '" + Name + "', " + UpstreamError.Prefixed(node.Name, why));
        }

        if (failed.Count > 3)
        {
            caller.AddMessage(MessageSeverity.Error, "… and " + (failed.Count - 3).ToString(System.Globalization.CultureInfo.InvariantCulture) + " more node(s) failed inside '" + Name + "'.");
        }

        var captured = OutputNode.Captured;
        if (failed.Count == 0 && captured.Any(v => v is UpstreamError))
        {
            // The failure came in from outside and reached an output: this instance did not deliver it, like any node stopped upstream.
            caller.FailedUpstream = true;
            caller.AddMessage(MessageSeverity.Warning, "Upstream failure: one or more input nodes are in an error state.");
        }
        else if (captured.Length > 0 && captured.All(v => v is InactiveValue))
        {
            caller.AddMessage(MessageSeverity.Info, "Skipped: every output comes from a branch that was switched off (Flow.When was false).");
        }

        // The Group Input / Output nodes and the nodes stopped by a failure upstream are not warnings of their own.
        var warned = Graph.Nodes.Count(n => n.State == NodeState.Warning && !n.FailedUpstream && !(n is GroupBoundNode));
        if (warned > 0)
        {
            caller.AddMessage(MessageSeverity.Warning, warned.ToString(System.Globalization.CultureInfo.InvariantCulture) + " node(s) inside '" + Name + "' have warnings.");
        }
    }

    // A failed body node is recovered when it feeds something and everything it feeds catches the failure (Flow.Try).
    // The Group Output also "catches" so that the failure can cross the border, and so does a group used inside this one; neither
    // makes the failure recovered unless the failure stopped there (a nested group that passes it on has FailedUpstream set).
    private bool IsRecovered(NodeModel node)
    {
        var consumers = Graph.Connections.Where(c => c.SourceNode == node && !c.IsMuted).Select(c => c.TargetNode).ToList();
        return consumers.Count > 0 && consumers.All(c => c.CatchesUpstreamErrors && !(c is GroupOutputNode) && !c.FailedUpstream);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <inheritdoc />
    public override string ToString() => "Node group '" + Name + "'";
}
