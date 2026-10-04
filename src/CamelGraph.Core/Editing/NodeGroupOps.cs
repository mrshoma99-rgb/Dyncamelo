using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Groups;
using CamelGraph.Core.Nodes;
using CamelGraph.Core.Serialization;

namespace CamelGraph.Core.Editing;

/// <summary>Outcome of a node group operation: what happened, or why nothing did.</summary>
public sealed class NodeGroupResult
{
    private NodeGroupResult(bool success, string message, NodeGroup? group, GroupInstanceNode? instance, IReadOnlyList<NodeModel> nodes)
    {
        Success = success;
        Message = message;
        Group = group;
        Instance = instance;
        Nodes = nodes;
    }

    /// <summary>True when the operation was done.</summary>
    public bool Success { get; }

    /// <summary>A sentence for the status bar.</summary>
    public string Message { get; }

    /// <summary>The group made, or the one the instance runs.</summary>
    public NodeGroup? Group { get; }

    /// <summary>The instance made (make group) or removed (ungroup).</summary>
    public GroupInstanceNode? Instance { get; }

    /// <summary>The nodes that took the instance's place (ungroup).</summary>
    public IReadOnlyList<NodeModel> Nodes { get; }

    internal static NodeGroupResult Fail(string message) =>
        new NodeGroupResult(false, message, null, null, new NodeModel[0]);

    internal static NodeGroupResult Ok(string message, NodeGroup? group, GroupInstanceNode? instance, IReadOnlyList<NodeModel>? nodes = null) =>
        new NodeGroupResult(true, message, group, instance, nodes ?? new NodeModel[0]);
}

/// <summary>
/// The things an author does with node groups: make one from a selection, ungroup an instance, edit the interface, make an
/// instance single-user, rename and remove groups. Every operation is a single undo step when an <see cref="UndoManager"/> is given
/// (the editor's <see cref="GraphRecorder"/> records the changes to the graph being edited; the steps here cover the parts it
/// cannot see — the library and the body of a group).
/// </summary>
public static class NodeGroupOps
{
    // ----- make a group ---------------------------------------------------------------------------

    /// <summary>
    /// Whether <paramref name="selection"/> can become a group. It cannot when it is empty, holds a group's own Group Input /
    /// Group Output, splits a loop from its collect, or has an unselected node sitting between selected ones (that node would
    /// have to be both inside and outside).
    /// </summary>
    public static bool CanMakeGroup(GraphModel graph, IReadOnlyCollection<NodeModel> selection, out string reason)
    {
        if (graph == null)
        {
            throw new ArgumentNullException(nameof(graph));
        }

        var set = new HashSet<NodeModel>(selection ?? new NodeModel[0]);
        if (set.Count == 0)
        {
            reason = "Select the nodes to put in a group first.";
            return false;
        }

        if (set.Any(n => n.Graph != graph))
        {
            reason = "Those nodes are not on this canvas.";
            return false;
        }

        if (set.Any(n => n is GroupInputNode || n is GroupOutputNode))
        {
            reason = "Group Input and Group Output belong to the group they are in; select the other nodes.";
            return false;
        }

        foreach (var item in set.OfType<LoopItemNode>())
        {
            if (graph.FindConnectionsFrom(item.OutPorts[3]).Any(c => !set.Contains(c.TargetNode)))
            {
                reason = "A loop must be grouped together with its Loop.Collect (and the nodes between them).";
                return false;
            }
        }

        foreach (var collect in set.OfType<LoopCollectNode>())
        {
            var source = graph.FindConnectionInto(collect.InPorts[0])?.SourceNode;
            if (source != null && !set.Contains(source))
            {
                reason = "A loop must be grouped together with its Loop.Item (and the nodes between them).";
                return false;
            }
        }

        var between = GraphOps.Downstream(graph, set).Intersect(GraphOps.Upstream(graph, set)).FirstOrDefault(n => !set.Contains(n));
        if (between != null)
        {
            reason = "'" + between.Name + "' sits between the selected nodes; select it too.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Replaces the selected nodes with one instance of a new group that contains them. A wire coming in from outside becomes an
    /// input socket (one per distinct source), a wire going out becomes an output socket (one per output port); the instance is
    /// wired to the old neighbours, so the graph computes exactly what it did.
    /// </summary>
    /// <param name="graph">The graph being edited (a document's own or the body of another group).</param>
    /// <param name="selection">The nodes to group.</param>
    /// <param name="name">Name for the group, or null for "Node Group".</param>
    /// <param name="undo">Receives the undo step; null for none.</param>
    public static NodeGroupResult MakeGroup(GraphModel graph, IReadOnlyCollection<NodeModel> selection, string? name, UndoManager? undo)
    {
        if (!CanMakeGroup(graph, selection, out var reason))
        {
            return NodeGroupResult.Fail(reason);
        }

        var set = new HashSet<NodeModel>(selection);
        var nodes = graph.Nodes.Where(set.Contains).ToList();
        var within = graph.Connections.Where(c => set.Contains(c.SourceNode) && set.Contains(c.TargetNode)).ToList();
        var entering = graph.Connections.Where(c => !set.Contains(c.SourceNode) && set.Contains(c.TargetNode))
            .OrderBy(c => c.TargetNode.Y).ThenBy(c => c.TargetNode.X).ThenBy(c => c.Sequence).ToList();
        var leaving = graph.Connections.Where(c => set.Contains(c.SourceNode) && !set.Contains(c.TargetNode))
            .OrderBy(c => c.SourceNode.Y).ThenBy(c => c.SourceNode.X).ThenBy(c => c.Sequence).ToList();

        using (Open(undo, "Make node group"))
        {
            var library = graph.NodeGroups;
            var group = library.Create(string.IsNullOrWhiteSpace(name) ? "Node Group" : name!);
            Record(undo, new CreateGroupStep(library, group));

            // The interface: one input socket per outside source, one output socket per output port that leaves.
            var inputSocket = new Dictionary<PortModel, GroupSocket>();
            foreach (var wire in entering)
            {
                if (!inputSocket.ContainsKey(wire.Source))
                {
                    inputSocket[wire.Source] = group.AddSocket(SocketSide.Input, wire.Target.Name, KindOf(wire.Target));
                }
            }

            var outputSocket = new Dictionary<PortModel, GroupSocket>();
            foreach (var wire in leaving)
            {
                if (!outputSocket.ContainsKey(wire.Source))
                {
                    var wanted = group.Outputs.Any(s => string.Equals(s.Name, wire.Source.Name, StringComparison.OrdinalIgnoreCase))
                        ? wire.SourceNode.Name + " " + wire.Source.Name
                        : wire.Source.Name;
                    outputSocket[wire.Source] = group.AddSocket(SocketSide.Output, wanted, KindOf(wire.Source));
                }
            }

            // Take the nodes out of this graph (the recorder notes the removals and the wires that went with them) and put
            // them, and the wires between them, into the body.
            foreach (var node in nodes)
            {
                graph.RemoveNode(node);
            }

            var inner = group.Graph;
            foreach (var node in nodes)
            {
                inner.ReinsertNode(node);
            }

            var bodyWires = new List<ConnectionModel>();
            foreach (var wire in within)
            {
                if (inner.ReinsertConnection(wire))
                {
                    bodyWires.Add(wire);
                }
            }

            var left = nodes.Min(n => n.X);
            var right = nodes.Max(n => n.X);
            var top = nodes.Min(n => n.Y);
            group.InputNode.X = left - 340;
            group.InputNode.Y = top;
            group.OutputNode.X = right + 460;
            group.OutputNode.Y = top;

            foreach (var wire in entering)
            {
                var socketPort = group.InputNode.OutPorts[IndexOf(group.Inputs, inputSocket[wire.Source])];
                var made = inner.Connect(socketPort, wire.Target, wire.Sequence);
                if (made.Success)
                {
                    bodyWires.Add(made.Connection!);
                    if (wire.IsMuted)
                    {
                        inner.SetConnectionMuted(made.Connection!, true);
                    }
                }
            }

            foreach (var pair in outputSocket)
            {
                var socketPort = group.OutputNode.InPorts[IndexOf(group.Outputs, pair.Value)];
                var made = inner.Connect(pair.Key, socketPort);
                if (made.Success)
                {
                    bodyWires.Add(made.Connection!);
                }
            }

            Record(undo, new MoveIntoGroupStep(inner, nodes, bodyWires));

            // The instance takes the nodes' place in this graph.
            var instance = new GroupInstanceNode(group)
            {
                X = nodes.Average(n => n.X),
                Y = nodes.Average(n => n.Y),
            };
            graph.AddNode(instance);

            var wired = new HashSet<PortModel>();
            foreach (var wire in entering)
            {
                if (wired.Add(wire.Source))
                {
                    var made = graph.Connect(wire.Source, instance.InPorts[IndexOf(group.Inputs, inputSocket[wire.Source])], wire.Sequence);
                    if (made.Success && wire.IsMuted)
                    {
                        graph.SetConnectionMuted(made.Connection!, true);
                    }
                }
            }

            foreach (var wire in leaving)
            {
                var made = graph.Connect(instance.OutPorts[IndexOf(group.Outputs, outputSocket[wire.Source])], wire.Target, wire.Sequence);
                if (made.Success && wire.IsMuted)
                {
                    graph.SetConnectionMuted(made.Connection!, true);
                }
            }

            return NodeGroupResult.Ok(
                "Made node group '" + group.Name + "' from " + nodes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                " node(s): " + group.Inputs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " input(s), " +
                group.Outputs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " output(s).",
                group,
                instance);
        }
    }

    // ----- ungroup --------------------------------------------------------------------------------

    /// <summary>
    /// Puts a copy of the instance's nodes into the graph in its place, wired through the interface to whatever the instance was
    /// wired to. The group itself stays in the library (other instances still use it).
    /// </summary>
    public static NodeGroupResult Ungroup(GraphModel graph, GroupInstanceNode instance, GraphSerializer serializer, UndoManager? undo)
    {
        if (graph == null)
        {
            throw new ArgumentNullException(nameof(graph));
        }

        if (serializer == null)
        {
            throw new ArgumentNullException(nameof(serializer));
        }

        var group = instance?.Definition;
        if (instance == null || group == null || instance.Graph != graph)
        {
            return NodeGroupResult.Fail("Select a node group to ungroup.");
        }

        var body = group.Graph.Nodes.Where(n => n != group.InputNode && n != group.OutputNode).ToList();

        using (Open(undo, "Ungroup"))
        {
            // What the instance was connected to, and the values pinned on its inputs, before it goes.
            var outerIn = new List<List<(PortModel Source, int Sequence, bool Muted)>>();
            var pinned = new List<(bool Has, object? Value)>();
            for (var k = 0; k < group.Inputs.Count; k++)
            {
                outerIn.Add(graph.FindConnectionsInto(instance.InPorts[k]).Select(c => (c.Source, c.Sequence, c.IsMuted)).ToList());
                pinned.Add((instance.InPorts[k].HasUserValue, instance.InPorts[k].UserValue));
            }

            var outerOut = new List<List<(PortModel Target, int Sequence, bool Muted)>>();
            for (var j = 0; j < group.Outputs.Count; j++)
            {
                outerOut.Add(graph.FindConnectionsFrom(instance.OutPorts[j]).Select(c => (c.Target, c.Sequence, c.IsMuted)).ToList());
            }

            var x = instance.X;
            var y = instance.Y;
            graph.RemoveNode(instance);

            var pasted = new List<NodeModel>();
            if (body.Count > 0)
            {
                var offsetX = x - body.Min(n => n.X);
                var offsetY = y - body.Min(n => n.Y);
                var json = serializer.SerializeFragment(body);
                pasted.AddRange(serializer.PasteFragment(graph, json, offsetX, offsetY));
            }

            var copyOf = new Dictionary<NodeModel, NodeModel>();
            for (var i = 0; i < body.Count && i < pasted.Count; i++)
            {
                copyOf[body[i]] = pasted[i];
            }

            // Inputs: every wire inside the group that started at an input socket now starts at what fed the instance.
            foreach (var wire in group.Graph.Connections.Where(c => c.SourceNode == group.InputNode).ToList())
            {
                var k = group.InputNode.OutPorts.ToList().IndexOf(wire.Source);
                if (k < 0 || k >= outerIn.Count)
                {
                    continue;
                }

                if (wire.TargetNode == group.OutputNode)
                {
                    var j = group.OutputNode.InPorts.ToList().IndexOf(wire.Target);
                    if (j >= 0 && j < outerOut.Count)
                    {
                        foreach (var from in outerIn[k])
                        {
                            foreach (var to in outerOut[j])
                            {
                                Bridge(graph, from.Source, to.Target, to.Sequence, from.Muted || to.Muted);
                            }
                        }
                    }

                    continue;
                }

                if (!copyOf.TryGetValue(wire.TargetNode, out var target))
                {
                    continue;
                }

                var port = target.InPorts.FirstOrDefault(p => p.Name == wire.Target.Name);
                if (port == null)
                {
                    continue;
                }

                if (outerIn[k].Count == 0)
                {
                    if (pinned[k].Has)
                    {
                        port.SetUserValue(pinned[k].Value);
                    }

                    continue;
                }

                foreach (var from in outerIn[k])
                {
                    Bridge(graph, from.Source, port, from.Sequence, from.Muted);
                }
            }

            // Outputs: what the group handed out at a socket now leaves from the node that fed that socket.
            foreach (var wire in group.Graph.Connections.Where(c => c.TargetNode == group.OutputNode && c.SourceNode != group.InputNode).ToList())
            {
                var j = group.OutputNode.InPorts.ToList().IndexOf(wire.Target);
                if (j < 0 || j >= outerOut.Count || !copyOf.TryGetValue(wire.SourceNode, out var source))
                {
                    continue;
                }

                var port = source.OutPorts.FirstOrDefault(p => p.Name == wire.Source.Name);
                if (port == null)
                {
                    continue;
                }

                foreach (var to in outerOut[j])
                {
                    Bridge(graph, port, to.Target, to.Sequence, to.Muted);
                }
            }

            return NodeGroupResult.Ok(
                "Ungrouped '" + group.Name + "' into " + pasted.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " node(s).",
                group,
                instance,
                pasted);
        }
    }

    // ----- the interface --------------------------------------------------------------------------

    /// <summary>Adds a socket to a group's interface.</summary>
    public static GroupSocket AddSocket(NodeGroup group, SocketSide side, string name, string kind, UndoManager? undo, int? index = null)
    {
        using (Open(undo, "Add group " + SideWord(side)))
        {
            var socket = group.AddSocket(side, name, kind, index);
            Record(undo, new SocketStep(group, side, "Add group " + SideWord(side), null, State(group, side, socket), new List<(GraphModel, ConnectionModel)>()));
            return socket;
        }
    }

    /// <summary>Removes a socket; wires on it (inside the group and on every instance) go with it and come back on undo.</summary>
    public static bool RemoveSocket(NodeGroup group, SocketSide side, Guid id, UndoManager? undo)
    {
        var socket = group.FindSocket(side, id);
        if (socket == null)
        {
            return false;
        }

        using (Open(undo, "Remove group " + SideWord(side)))
        {
            var before = State(group, side, socket);
            var wires = WiresOn(group, id);
            group.RemoveSocket(side, id);
            Record(undo, new SocketStep(group, side, "Remove group " + SideWord(side), before, null, wires));
            return true;
        }
    }

    /// <summary>Renames a socket (the name is made unique on its side). Wires stay.</summary>
    public static bool RenameSocket(NodeGroup group, SocketSide side, Guid id, string name, UndoManager? undo)
    {
        var socket = group.FindSocket(side, id);
        if (socket == null)
        {
            return false;
        }

        using (Open(undo, "Rename group " + SideWord(side)))
        {
            var before = State(group, side, socket);
            if (!group.RenameSocket(side, id, name))
            {
                return false;
            }

            Record(undo, new SocketStep(group, side, "Rename group " + SideWord(side), before, State(group, side, socket), new List<(GraphModel, ConnectionModel)>()));
            return true;
        }
    }

    /// <summary>Changes the kind hint of a socket.</summary>
    public static bool SetSocketKind(NodeGroup group, SocketSide side, Guid id, string kind, UndoManager? undo)
    {
        var socket = group.FindSocket(side, id);
        if (socket == null)
        {
            return false;
        }

        using (Open(undo, "Change group " + SideWord(side) + " type"))
        {
            var before = State(group, side, socket);
            if (!group.SetSocketKind(side, id, kind))
            {
                return false;
            }

            Record(undo, new SocketStep(group, side, "Change group " + SideWord(side) + " type", before, State(group, side, socket), new List<(GraphModel, ConnectionModel)>()));
            return true;
        }
    }

    /// <summary>Moves a socket to another position on its side.</summary>
    public static bool MoveSocket(NodeGroup group, SocketSide side, Guid id, int newIndex, UndoManager? undo)
    {
        var socket = group.FindSocket(side, id);
        if (socket == null)
        {
            return false;
        }

        using (Open(undo, "Reorder group " + SideWord(side) + "s"))
        {
            var before = State(group, side, socket);
            if (!group.MoveSocket(side, id, newIndex))
            {
                return false;
            }

            Record(undo, new SocketStep(group, side, "Reorder group " + SideWord(side) + "s", before, State(group, side, socket), new List<(GraphModel, ConnectionModel)>()));
            return true;
        }
    }

    // ----- groups themselves ----------------------------------------------------------------------

    /// <summary>Renames a group (made unique among the document's groups).</summary>
    public static bool RenameGroup(NodeGroup group, string name, UndoManager? undo)
    {
        var unique = group.Library.UniqueName(name, group);
        if (string.Equals(unique, group.Name, StringComparison.Ordinal))
        {
            return false;
        }

        using (Open(undo, "Rename node group"))
        {
            var old = group.Name;
            group.Name = unique;
            Record(undo, new RenameGroupStep(group, old, unique));
            return true;
        }
    }

    /// <summary>Removes a group nothing uses any more from the document.</summary>
    public static bool RemoveUnusedGroup(NodeGroup group, UndoManager? undo)
    {
        using (Open(undo, "Delete node group"))
        {
            if (!group.Library.Remove(group))
            {
                return false;
            }

            Record(undo, new DeleteGroupStep(group.Library, group));
            return true;
        }
    }

    /// <summary>
    /// Gives an instance its own copy of its group, so editing the group no longer changes the other instances ("make single
    /// user"). The copy keeps the socket ids, so the instance's wires stay where they are.
    /// </summary>
    public static NodeGroupResult MakeSingleUser(GraphModel graph, GroupInstanceNode instance, GraphSerializer serializer, UndoManager? undo)
    {
        var source = instance?.Definition;
        if (instance == null || source == null || instance.Graph != graph)
        {
            return NodeGroupResult.Fail("Select a node group instance first.");
        }

        if (source.Library.InstancesOf(source).Count < 2)
        {
            return NodeGroupResult.Fail("'" + source.Name + "' is only used here already.");
        }

        using (Open(undo, "Make node group single user"))
        {
            var copy = CloneGroup(source, serializer);
            Record(undo, new CreateGroupStep(source.Library, copy));
            var wasNamed = instance.Name == source.Name;
            var oldName = instance.Name;
            instance.Bind(copy);
            if (wasNamed)
            {
                instance.Name = copy.Name;
            }

            Record(undo, new RebindStep(instance, source, copy, oldName, instance.Name));
            return NodeGroupResult.Ok("'" + instance.Name + "' now has its own copy of the group.", copy, instance);
        }
    }

    /// <summary>A copy of a group in the same library: same sockets (same ids), a copy of the body, a name made unique.</summary>
    public static NodeGroup CloneGroup(NodeGroup source, GraphSerializer serializer)
    {
        var library = source.Library;
        var copy = new NodeGroup(library, Guid.NewGuid(), library.UniqueName(source.Name + " copy"))
        {
            Description = source.Description,
        };
        foreach (var socket in source.Inputs)
        {
            copy.LoadSocket(SocketSide.Input, socket.Id, socket.Name, socket.Kind);
        }

        foreach (var socket in source.Outputs)
        {
            copy.LoadSocket(SocketSide.Output, socket.Id, socket.Name, socket.Kind);
        }

        // The body travels as a fragment; its Group Input/Output nodes arrive too and become the copy's own.
        var json = serializer.SerializeFragment(source.Graph.Nodes.ToList());
        serializer.PasteFragment(copy.Graph, json, 0, 0);
        copy.AdoptInterfaceNodes();
        foreach (var note in source.Graph.Notes)
        {
            copy.Graph.Notes.Add(new NoteModel { Text = note.Text, X = note.X, Y = note.Y });
        }

        foreach (var frame in source.Graph.Groups)
        {
            copy.Graph.Groups.Add(new GroupModel { Title = frame.Title, X = frame.X, Y = frame.Y, Width = frame.Width, Height = frame.Height, Color = frame.Color });
        }

        library.Add(copy);
        return copy;
    }

    // ----- helpers ---------------------------------------------------------------------------------

    private static string SideWord(SocketSide side) => side == SocketSide.Input ? "input" : "output";

    private static string KindOf(PortModel port) => PortKinds.ToHint(PortKinds.FromPort(port));

    private static int IndexOf(IReadOnlyList<GroupSocket> sockets, GroupSocket socket)
    {
        for (var i = 0; i < sockets.Count; i++)
        {
            if (ReferenceEquals(sockets[i], socket))
            {
                return i;
            }
        }

        return -1;
    }

    private static void Bridge(GraphModel graph, PortModel source, PortModel target, int sequence, bool muted)
    {
        var made = graph.Connect(source, target, target.IsMultiInput ? sequence : (int?)null);
        if (made.Success && muted)
        {
            graph.SetConnectionMuted(made.Connection!, true);
        }
    }

    private static SocketState State(NodeGroup group, SocketSide side, GroupSocket socket) =>
        new SocketState(socket.Id, socket.Name, socket.Kind, IndexOf(group.Sockets(side), socket));

    // Every wire on the ports a socket has — inside the group and on each instance — so undo can put them back.
    private static List<(GraphModel Graph, ConnectionModel Wire)> WiresOn(NodeGroup group, Guid socketId)
    {
        bool Ours(PortModel port) =>
            port.Id == socketId &&
            (port.Owner == group.InputNode || port.Owner == group.OutputNode ||
             (port.Owner is GroupInstanceNode instance && ReferenceEquals(instance.Definition, group)));

        var found = new List<(GraphModel, ConnectionModel)>();
        foreach (var graph in group.Library.AllGraphs())
        {
            foreach (var wire in graph.Connections)
            {
                if (Ours(wire.Source) || Ours(wire.Target))
                {
                    found.Add((graph, wire));
                }
            }
        }

        return found;
    }

    private static IDisposable Open(UndoManager? undo, string label) =>
        undo != null ? (IDisposable)undo.Begin(label) : new Nothing();

    private static void Record(UndoManager? undo, IUndoStep step) => undo?.Record(step);

    private sealed class Nothing : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class SocketState
    {
        public SocketState(Guid id, string name, string kind, int index)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Index = index;
        }

        public Guid Id { get; }

        public string Name { get; }

        public string Kind { get; }

        public int Index { get; }
    }

    // ----- undo steps ------------------------------------------------------------------------------

    private sealed class CreateGroupStep : IUndoStep
    {
        private readonly NodeGroupLibrary _library;
        private readonly NodeGroup _group;

        public CreateGroupStep(NodeGroupLibrary library, NodeGroup group)
        {
            _library = library;
            _group = group;
        }

        public string Label => "Make node group";

        public void Undo() => _library.Remove(_group);

        public void Redo() => _library.Add(_group);
    }

    private sealed class DeleteGroupStep : IUndoStep
    {
        private readonly NodeGroupLibrary _library;
        private readonly NodeGroup _group;

        public DeleteGroupStep(NodeGroupLibrary library, NodeGroup group)
        {
            _library = library;
            _group = group;
        }

        public string Label => "Delete node group";

        public void Undo() => _library.Add(_group);

        public void Redo() => _library.Remove(_group);
    }

    private sealed class RenameGroupStep : IUndoStep
    {
        private readonly NodeGroup _group;
        private readonly string _old;
        private readonly string _new;

        public RenameGroupStep(NodeGroup group, string oldName, string newName)
        {
            _group = group;
            _old = oldName;
            _new = newName;
        }

        public string Label => "Rename node group";

        public void Undo() => _group.Name = _old;

        public void Redo() => _group.Name = _new;
    }

    // Nodes (and the wires among them and to the Group Input/Output) that were moved into a group's body.
    private sealed class MoveIntoGroupStep : IUndoStep
    {
        private readonly GraphModel _body;
        private readonly List<NodeModel> _nodes;
        private readonly List<ConnectionModel> _wires;

        public MoveIntoGroupStep(GraphModel body, List<NodeModel> nodes, List<ConnectionModel> wires)
        {
            _body = body;
            _nodes = nodes;
            _wires = wires;
        }

        public string Label => "Make node group";

        public void Undo()
        {
            foreach (var wire in _wires.AsEnumerable().Reverse())
            {
                _body.Disconnect(wire);
            }

            foreach (var node in _nodes)
            {
                _body.RemoveNode(node);
            }
        }

        public void Redo()
        {
            foreach (var node in _nodes)
            {
                if (node.Graph == null)
                {
                    _body.ReinsertNode(node);
                }
            }

            foreach (var wire in _wires)
            {
                _body.ReinsertConnection(wire);
            }
        }
    }

    private sealed class SocketStep : IUndoStep
    {
        private readonly NodeGroup _group;
        private readonly SocketSide _side;
        private readonly SocketState? _before;
        private readonly SocketState? _after;
        private readonly List<(GraphModel Graph, ConnectionModel Wire)> _wires;

        public SocketStep(NodeGroup group, SocketSide side, string label, SocketState? before, SocketState? after, List<(GraphModel, ConnectionModel)> wires)
        {
            _group = group;
            _side = side;
            Label = label;
            _before = before;
            _after = after;
            _wires = wires;
        }

        public string Label { get; }

        public void Undo()
        {
            Apply(_after, _before);
            foreach (var (graph, wire) in _wires)
            {
                graph.ReinsertConnection(wire);
            }
        }

        public void Redo() => Apply(_before, _after);

        // Brings the socket from the state it is in now (<paramref name="from"/>) to <paramref name="to"/>; null means "absent".
        private void Apply(SocketState? from, SocketState? to)
        {
            if (to == null)
            {
                if (from != null)
                {
                    _group.RemoveSocket(_side, from.Id);
                }

                return;
            }

            if (from == null)
            {
                _group.AddSocket(_side, to.Name, to.Kind, to.Index, to.Id);
                return;
            }

            _group.RenameSocket(_side, to.Id, to.Name);
            _group.SetSocketKind(_side, to.Id, to.Kind);
            _group.MoveSocket(_side, to.Id, to.Index);
        }
    }

    private sealed class RebindStep : IUndoStep
    {
        private readonly GroupInstanceNode _instance;
        private readonly NodeGroup _from;
        private readonly NodeGroup _to;
        private readonly string _oldName;
        private readonly string _newName;

        public RebindStep(GroupInstanceNode instance, NodeGroup from, NodeGroup to, string oldName, string newName)
        {
            _instance = instance;
            _from = from;
            _to = to;
            _oldName = oldName;
            _newName = newName;
        }

        public string Label => "Make node group single user";

        public void Undo()
        {
            _instance.Bind(_from);
            _instance.Name = _oldName;
        }

        public void Redo()
        {
            _instance.Bind(_to);
            _instance.Name = _newName;
        }
    }
}
