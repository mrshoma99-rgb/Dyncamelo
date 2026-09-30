using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Groups;
using Dyncamelo.Core.Serialization;
using Dyncamelo.UI.Mvvm;

namespace Dyncamelo.UI.ViewModels;

/// <summary>One step of the path shown above the canvas while a node group is open ("Graph ▸ Group ▸ Inner").</summary>
public sealed class BreadcrumbItem
{
    /// <summary>Creates an item.</summary>
    public BreadcrumbItem(string title, int depth, bool isCurrent, ICommand navigate)
    {
        Title = title;
        Depth = depth;
        IsCurrent = isCurrent;
        NavigateCommand = navigate;
    }

    /// <summary>The name of the graph or group.</summary>
    public string Title { get; }

    /// <summary>0 for the document, 1 for a group opened from it, and so on.</summary>
    public int Depth { get; }

    /// <summary>True for the graph on the canvas now.</summary>
    public bool IsCurrent { get; }

    /// <summary>Goes back to this level.</summary>
    public ICommand NavigateCommand { get; }
}

/// <summary>Raised when the canvas switched to another graph: a group was opened or closed.</summary>
public sealed class GroupNavigationEventArgs : EventArgs
{
    /// <summary>Creates the payload.</summary>
    public GroupNavigationEventArgs(bool entered, int depth)
    {
        Entered = entered;
        Depth = depth;
    }

    /// <summary>True when a group was opened, false when one was closed.</summary>
    public bool Entered { get; }

    /// <summary>How many groups deep the canvas is now (0 = the document itself).</summary>
    public int Depth { get; }
}

/// <summary>
/// Node groups in the editor: opening a group to edit it (the canvas shows the group's body, with its own undo history), making
/// and dissolving groups, editing the interface and adding instances from the library.
/// </summary>
public partial class GraphEditorViewModel
{
    /// <summary>Prefix of the library id of a node group ("group:" + the group's id).</summary>
    public const string GroupLibraryPrefix = "group:";

    // What to come back to when a group is closed.
    private sealed class Level
    {
        public Level(GraphModel graph, UndoManager undo, GroupInstanceNode instance)
        {
            Graph = graph;
            Undo = undo;
            Instance = instance;
        }

        public GraphModel Graph { get; }

        public UndoManager Undo { get; }

        public GroupInstanceNode Instance { get; }
    }

    private readonly List<Level> _levels = new List<Level>();
    private NodeGroupLibrary? _watchedGroups;
    private readonly List<NodeGroup> _watchedGroupList = new List<NodeGroup>();

    /// <summary>The path to the graph on the canvas, for the bar above it; empty at the top level.</summary>
    public ObservableCollection<BreadcrumbItem> Breadcrumb { get; } = new ObservableCollection<BreadcrumbItem>();

    /// <summary>Raised after the canvas switched to a group's body or back.</summary>
    public event EventHandler<GroupNavigationEventArgs>? GroupNavigated;

    /// <summary>The document's own graph — what is saved and run — whatever the canvas shows now.</summary>
    public GraphModel DocumentGraph => _levels.Count == 0 ? _graph : _levels[0].Graph;

    /// <summary>True while the canvas shows the body of a node group.</summary>
    public bool IsInsideGroup => _levels.Count > 0;

    /// <summary>The group whose body is on the canvas, or null at the top level.</summary>
    public NodeGroup? CurrentGroup => _graph.OwnerGroup;

    /// <summary>How many groups deep the canvas is (0 = the document itself).</summary>
    public int GroupDepth => _levels.Count;

    private void InitNodeGroups()
    {
        RebuildBreadcrumb();
    }

    private void OnUndoChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(UndoTooltip));
        OnPropertyChanged(nameof(RedoTooltip));
        CommandManager.InvalidateRequerySuggested();
        if (sender is UndoManager)
        {
            NoteChange();
            if (_infoPanel == InfoPanel.History)
            {
                RefreshInfoPanel();
            }
        }
    }

    // Each level keeps its own undo history, so Ctrl+Z inside a group never reaches into the graph outside it.
    private void UseUndo(UndoManager undo)
    {
        _undo.Changed -= OnUndoChanged;
        _undo = undo;
        _undo.Changed += OnUndoChanged;
        OnPropertyChanged(nameof(History));
        OnUndoChanged(null, EventArgs.Empty);
    }

    // ----- opening and closing groups --------------------------------------------------------

    /// <summary>Opens the group an instance runs on the canvas (Tab). Not while a run is in progress.</summary>
    public bool EnterGroup(NodeViewModel? node)
    {
        if (!(node?.Model is GroupInstanceNode instance) || instance.Definition == null)
        {
            StatusMessage = "Select a node group to open it.";
            return false;
        }

        if (IsRunning)
        {
            return false;
        }

        FinishOpenGesture();
        var group = instance.Definition;
        DetachGraph();
        _levels.Add(new Level(_graph, _undo, instance));
        _graph = group.Graph;
        UseUndo(new UndoManager());
        AttachGraph(group.Graph, resetHistory: false);
        AfterNavigation(entered: true);
        StatusMessage = "Editing node group '" + group.Name + "' — Shift+Tab goes back.";
        return true;
    }

    /// <summary>Closes the open group and returns to the graph it was opened from (Shift+Tab).</summary>
    public bool ExitGroup()
    {
        if (_levels.Count == 0 || IsRunning)
        {
            return false;
        }

        FinishOpenGesture();
        var level = _levels[_levels.Count - 1];
        DetachGraph();
        _levels.RemoveAt(_levels.Count - 1);
        _graph = level.Graph;
        UseUndo(level.Undo);
        AttachGraph(level.Graph, resetHistory: false);
        AfterNavigation(entered: false);

        // The instance that was opened is selected again, so the user lands where they left.
        var instanceViewModel = level.Instance.Graph == _graph ? FindNodeViewModel(level.Instance) : null;
        if (instanceViewModel != null)
        {
            SelectedItems.Add(instanceViewModel);
        }

        return true;
    }

    /// <summary>Goes back to the level with the given depth (0 = the document), closing every group above it.</summary>
    public void NavigateToDepth(int depth)
    {
        while (_levels.Count > Math.Max(0, depth) && ExitGroup())
        {
        }
    }

    // Used by LoadGraph: a newly opened document replaces the whole stack.
    private void ResetGroupNavigation()
    {
        if (_levels.Count == 0)
        {
            return;
        }

        _levels.Clear();
        UseUndo(_documentUndo);
        RebuildBreadcrumb();
        OnPropertyChanged(nameof(IsInsideGroup));
        OnPropertyChanged(nameof(GroupDepth));
        OnPropertyChanged(nameof(CurrentGroup));
    }

    private void FinishOpenGesture()
    {
        _dragTransaction?.Dispose();
        _dragTransaction = null;
    }

    private void AfterNavigation(bool entered)
    {
        OnPropertyChanged(nameof(Graph));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsInsideGroup));
        OnPropertyChanged(nameof(GroupDepth));
        OnPropertyChanged(nameof(CurrentGroup));
        OnPropertyChanged(nameof(IsAutoRun));
        RebuildBreadcrumb();
        UpdateRunStatistics(null);
        if (_infoPanel != InfoPanel.None)
        {
            RefreshInfoPanel();
        }

        GroupNavigated?.Invoke(this, new GroupNavigationEventArgs(entered, _levels.Count));
    }

    private void RebuildBreadcrumb()
    {
        Breadcrumb.Clear();
        if (_levels.Count == 0)
        {
            return;
        }

        var document = DocumentGraph;
        Breadcrumb.Add(new BreadcrumbItem(document.Name.Length > 0 ? document.Name : "Untitled", 0, false, new RelayCommand(() => NavigateToDepth(0))));
        for (var i = 0; i < _levels.Count; i++)
        {
            var depth = i + 1;
            var name = (i + 1 < _levels.Count ? _levels[i + 1].Graph.OwnerGroup : _graph.OwnerGroup)?.Name ?? "Node Group";
            Breadcrumb.Add(new BreadcrumbItem(name, depth, depth == _levels.Count, new RelayCommand(() => NavigateToDepth(depth))));
        }
    }

    // ----- commands the catalogue names --------------------------------------------------------

    private ICommand? _makeGroupCommand;
    private ICommand? _ungroupCommand;
    private ICommand? _toggleGroupEditCommand;
    private ICommand? _exitGroupCommand;
    private ICommand? _renameGroupCommand;
    private ICommand? _singleUserCommand;
    private ICommand? _purgeGroupsCommand;
    private ICommand? _addGroupInputCommand;
    private ICommand? _addGroupOutputCommand;

    /// <summary>Makes a node group from the selected nodes.</summary>
    public ICommand MakeGroupCommand => _makeGroupCommand ??= new RelayCommand(MakeGroupFromSelection);

    /// <summary>Replaces the selected node group instances with their contents.</summary>
    public ICommand UngroupNodeGroupCommand => _ungroupCommand ??= new RelayCommand(UngroupSelectedNodeGroups);

    /// <summary>Tab: opens the selected node group, or closes the open one when no group is selected.</summary>
    public ICommand ToggleGroupEditCommand => _toggleGroupEditCommand ??= new RelayCommand(ToggleGroupEdit);

    /// <summary>Shift+Tab: closes the open node group.</summary>
    public ICommand ExitGroupCommand => _exitGroupCommand ??= new RelayCommand(() =>
    {
        if (!ExitGroup())
        {
            StatusMessage = "No node group is open.";
        }
    });

    /// <summary>Renames the selected node group (or the open one).</summary>
    public ICommand RenameNodeGroupCommand => _renameGroupCommand ??= new RelayCommand(() => RenameNodeGroup(SelectedInstance()));

    /// <summary>Gives the selected instance its own copy of its group.</summary>
    public ICommand MakeSingleUserCommand => _singleUserCommand ??= new RelayCommand(() => MakeNodeGroupSingleUser(SelectedInstance()));

    /// <summary>Removes the node groups nothing uses.</summary>
    public ICommand PurgeNodeGroupsCommand => _purgeGroupsCommand ??= new RelayCommand(PurgeUnusedGroups);

    /// <summary>Adds an input socket to the open node group.</summary>
    public ICommand AddGroupInputCommand => _addGroupInputCommand ??= new RelayCommand(() => AddSocketToOpenGroup(SocketSide.Input));

    /// <summary>Adds an output socket to the open node group.</summary>
    public ICommand AddGroupOutputCommand => _addGroupOutputCommand ??= new RelayCommand(() => AddSocketToOpenGroup(SocketSide.Output));

    private NodeViewModel? SelectedInstance()
    {
        var selected = SelectedItems.OfType<NodeViewModel>().Where(n => n.Model is GroupInstanceNode).ToList();
        return selected.Count == 1 ? selected[0] : null;
    }

    private void ToggleGroupEdit()
    {
        var instance = SelectedInstance();
        if (instance != null)
        {
            EnterGroup(instance);
        }
        else if (!ExitGroup())
        {
            StatusMessage = "Select a node group and press Tab to open it.";
        }
    }

    // ----- making, dissolving and editing groups --------------------------------------------------

    /// <summary>Makes a node group from the selected nodes and puts an instance in their place.</summary>
    public void MakeGroupFromSelection()
    {
        var nodes = SelectedItems.OfType<NodeViewModel>().Select(n => n.Model).ToList();
        var result = NodeGroupOps.MakeGroup(_graph, nodes, null, _undo);
        StatusMessage = result.Message;
        if (result.Success && result.Instance != null)
        {
            var viewModel = FindNodeViewModel(result.Instance);
            SelectedItems.Clear();
            if (viewModel != null)
            {
                SelectedItems.Add(viewModel);
            }
        }
    }

    private void UngroupSelectedNodeGroups()
    {
        var instances = SelectedItems.OfType<NodeViewModel>().Select(n => n.Model).OfType<GroupInstanceNode>().ToList();
        if (instances.Count == 0)
        {
            StatusMessage = "Select a node group to ungroup.";
            return;
        }

        var restored = new List<NodeModel>();
        using (_undo.Begin("Ungroup"))
        {
            var serializer = new GraphSerializer(Registry);
            foreach (var instance in instances)
            {
                var result = NodeGroupOps.Ungroup(_graph, instance, serializer, _undo);
                if (result.Success)
                {
                    restored.AddRange(result.Nodes);
                    StatusMessage = result.Message;
                }
            }
        }

        SelectedItems.Clear();
        foreach (var node in restored)
        {
            var viewModel = FindNodeViewModel(node);
            if (viewModel != null)
            {
                SelectedItems.Add(viewModel);
            }
        }
    }

    /// <summary>Ungroups one instance (its own menu).</summary>
    public void UngroupNode(NodeViewModel? node)
    {
        if (node != null && !SelectedItems.Contains(node))
        {
            SelectedItems.Clear();
            SelectedItems.Add(node);
        }

        UngroupSelectedNodeGroups();
    }

    /// <summary>Asks for a new name and renames the group an instance runs.</summary>
    public void RenameNodeGroup(NodeViewModel? node)
    {
        var group = (node?.Model as GroupInstanceNode)?.Definition ?? CurrentGroup;
        if (group == null)
        {
            StatusMessage = "Select a node group to rename.";
            return;
        }

        var name = Dialogs.Prompt("Name of the node group:", "Rename Node Group", group.Name);
        if (!string.IsNullOrWhiteSpace(name) && NodeGroupOps.RenameGroup(group, name!, _undo))
        {
            StatusMessage = "Renamed the node group to '" + group.Name + "'.";
        }
    }

    /// <summary>Gives one instance its own copy of its group.</summary>
    public void MakeNodeGroupSingleUser(NodeViewModel? node)
    {
        if (!(node?.Model is GroupInstanceNode instance))
        {
            StatusMessage = "Select a node group instance first.";
            return;
        }

        StatusMessage = NodeGroupOps.MakeSingleUser(_graph, instance, new GraphSerializer(Registry), _undo).Message;
    }

    private void PurgeUnusedGroups()
    {
        var library = DocumentGraph.NodeGroups;
        var unused = library.Groups.Where(g => library.InstancesOf(g).Count == 0).ToList();
        if (unused.Count == 0)
        {
            StatusMessage = "Every node group is in use.";
            return;
        }

        using (_undo.Begin("Delete unused node groups"))
        {
            foreach (var group in unused)
            {
                NodeGroupOps.RemoveUnusedGroup(group, _undo);
            }
        }

        StatusMessage = "Removed " + unused.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " unused node group(s).";
    }

    private void AddSocketToOpenGroup(SocketSide side)
    {
        var group = CurrentGroup;
        if (group == null)
        {
            StatusMessage = "Open a node group first (select it and press Tab).";
            return;
        }

        NodeGroupOps.AddSocket(group, side, side == SocketSide.Input ? "Input" : "Output", string.Empty, _undo);
    }

    /// <summary>The "+" on a Group Input / Group Output node: adds a socket to the group's interface.</summary>
    public void AddGroupSocket(NodeViewModel? node)
    {
        var side = node?.Model is GroupInputNode ? SocketSide.Input : SocketSide.Output;
        var group = (node?.Model as GroupInputNode)?.Owner ?? (node?.Model as GroupOutputNode)?.Owner;
        if (group == null)
        {
            return;
        }

        NodeGroupOps.AddSocket(group, side, side == SocketSide.Input ? "Input" : "Output", string.Empty, _undo);
    }

    /// <summary>
    /// A wire released on the body of the Group Output node (from an output socket) or of the Group Input node (from an
    /// unwired input socket) adds a socket to the group's interface, named and typed after the dragged socket, and connects
    /// to it — the same as dropping on the empty socket of Blender's group nodes.
    /// </summary>
    /// <param name="dragged">The socket the wire was dragged from.</param>
    /// <param name="location">Where the wire was released, in canvas space.</param>
    /// <returns>True when a socket was made and the wire connected; false when the drop was not on a group interface node.</returns>
    public bool TryCreateGroupSocketFromDrop(ConnectorViewModel dragged, Point location)
    {
        var host = NodeUnder(location);
        if (host == null)
        {
            return false;
        }

        NodeGroup? group;
        SocketSide side;
        if (host.Model is GroupOutputNode output && !dragged.IsInput)
        {
            group = output.Owner;
            side = SocketSide.Output;
        }
        else if (host.Model is GroupInputNode input && dragged.IsInput)
        {
            group = input.Owner;
            side = SocketSide.Input;
        }
        else
        {
            return false;
        }

        if (group == null)
        {
            return false;
        }

        // A wired input is already fed; only a multi-input takes more.
        if (side == SocketSide.Input && dragged.IsConnected && !dragged.IsMultiInput)
        {
            StatusMessage = "That input already has a wire; disconnect it first to feed it from a group input.";
            return true;
        }

        using (_undo.Begin("Add group " + (side == SocketSide.Input ? "input" : "output") + " and connect"))
        {
            var socket = NodeGroupOps.AddSocket(group, side, dragged.Port.Name, PortKinds.ToHint(dragged.Kind), _undo);
            var ports = side == SocketSide.Input ? host.Model.OutPorts : host.Model.InPorts;
            var bound = ports.FirstOrDefault(p => p.Id == socket.Id);
            if (bound == null)
            {
                return true;
            }

            var result = side == SocketSide.Input ? _graph.Connect(bound, dragged.Port) : _graph.Connect(dragged.Port, bound);
            StatusMessage = result.Success
                ? "Added group " + (side == SocketSide.Input ? "input" : "output") + " '" + socket.Name + "' and connected it."
                : result.Message ?? "The connection was rejected.";
        }

        return true;
    }

    private NodeViewModel? NodeUnder(Point location)
    {
        foreach (var node in Items.OfType<NodeViewModel>())
        {
            var size = node.Size;
            if (size.Width > 0 && size.Height > 0 && new Rect(node.Location, size).Contains(location))
            {
                return node;
            }
        }

        return null;
    }

    // ----- sockets of the Group Input / Group Output nodes --------------------------------------------

    // The group, side and socket behind a connector of a Group Input / Group Output node.
    internal bool TryFindGroupSocket(ConnectorViewModel connector, out NodeGroup group, out SocketSide side, out GroupSocket socket)
    {
        group = null!;
        socket = null!;
        side = SocketSide.Input;
        var owner = connector.Port.Owner;
        var candidate = (owner as GroupInputNode)?.Owner ?? (owner as GroupOutputNode)?.Owner;
        if (candidate == null)
        {
            return false;
        }

        side = owner is GroupInputNode ? SocketSide.Input : SocketSide.Output;
        var found = candidate.FindSocket(side, connector.Port.Id);
        if (found == null)
        {
            return false;
        }

        group = candidate;
        socket = found;
        return true;
    }

    /// <summary>Asks for a name and renames the socket behind a connector.</summary>
    public void RenameGroupSocket(ConnectorViewModel connector)
    {
        if (!TryFindGroupSocket(connector, out var group, out var side, out var socket))
        {
            return;
        }

        var name = Dialogs.Prompt("Name of the socket:", "Rename Socket", socket.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            NodeGroupOps.RenameSocket(group, side, socket.Id, name!, _undo);
        }
    }

    /// <summary>Removes the socket behind a connector (and the wires on it, everywhere the group is used).</summary>
    public void RemoveGroupSocket(ConnectorViewModel connector)
    {
        if (TryFindGroupSocket(connector, out var group, out var side, out var socket))
        {
            NodeGroupOps.RemoveSocket(group, side, socket.Id, _undo);
        }
    }

    /// <summary>Moves the socket behind a connector up (-1) or down (+1) its side.</summary>
    public void MoveGroupSocket(ConnectorViewModel connector, int delta)
    {
        if (TryFindGroupSocket(connector, out var group, out var side, out var socket))
        {
            var index = group.Sockets(side).ToList().FindIndex(s => s.Id == socket.Id);
            NodeGroupOps.MoveSocket(group, side, socket.Id, index + delta, _undo);
        }
    }

    /// <summary>Sets the kind of the socket behind a connector ("number", "text*", "" for any).</summary>
    public void SetGroupSocketKind(ConnectorViewModel connector, string? kind)
    {
        if (TryFindGroupSocket(connector, out var group, out var side, out var socket))
        {
            NodeGroupOps.SetSocketKind(group, side, socket.Id, kind ?? string.Empty, _undo);
        }
    }

    // ----- instances from the library ---------------------------------------------------------------

    // Makes the node a library id stands for: a zero-touch definition, a node type, or ("group:" + id) an instance of a group.
    private NodeModel? CreateNodeFromLibrary(string libraryId, out string? problem)
    {
        problem = null;
        if (libraryId.StartsWith(GroupLibraryPrefix, StringComparison.Ordinal))
        {
            if (!Guid.TryParse(libraryId.Substring(GroupLibraryPrefix.Length), out var id))
            {
                problem = "Unknown node group.";
                return null;
            }

            var group = DocumentGraph.NodeGroups.Find(id);
            if (group == null)
            {
                problem = "That node group is no longer in this document.";
                return null;
            }

            if (DocumentGraph.NodeGroups.WouldCreateCycle(_graph.OwnerGroup, group))
            {
                problem = "'" + group.Name + "' contains the group that is open, so it cannot be used inside it.";
                return null;
            }

            return new GroupInstanceNode(group);
        }

        if (libraryId == GroupInstanceNode.TypeName || libraryId == GroupInputNode.TypeName || libraryId == GroupOutputNode.TypeName)
        {
            problem = "Make a node group from selected nodes (Node Groups menu) to get one.";
            return null;
        }

        return Registry.CreateZeroTouchNode(libraryId) ?? Registry.CreateNode(libraryId);
    }

    // ----- keeping the library browser in step with the document's groups ------------------------------

    private void WatchNodeGroups(NodeGroupLibrary library)
    {
        if (_watchedGroups != null)
        {
            _watchedGroups.GroupAdded -= OnGroupLibraryChanged;
            _watchedGroups.GroupRemoved -= OnGroupLibraryChanged;
            foreach (var group in _watchedGroupList)
            {
                group.PropertyChanged -= OnWatchedGroupChanged;
                group.InterfaceChanged -= OnWatchedGroupInterfaceChanged;
            }

            _watchedGroupList.Clear();
        }

        _watchedGroups = library;
        library.GroupAdded += OnGroupLibraryChanged;
        library.GroupRemoved += OnGroupLibraryChanged;
        foreach (var group in library.Groups)
        {
            WatchGroup(group);
        }

        Library.SetNodeGroups(library);
    }

    private void WatchGroup(NodeGroup group)
    {
        if (!_watchedGroupList.Contains(group))
        {
            _watchedGroupList.Add(group);
            group.PropertyChanged += OnWatchedGroupChanged;
            group.InterfaceChanged += OnWatchedGroupInterfaceChanged;
        }
    }

    private void OnGroupLibraryChanged(object? sender, NodeGroupEventArgs e)
    {
        if (_watchedGroups != null && _watchedGroups.Groups.Contains(e.Group))
        {
            WatchGroup(e.Group);
        }
        else
        {
            e.Group.PropertyChanged -= OnWatchedGroupChanged;
            e.Group.InterfaceChanged -= OnWatchedGroupInterfaceChanged;
            _watchedGroupList.Remove(e.Group);
        }

        Library.Refresh();
        RebuildBreadcrumb();
    }

    private void OnWatchedGroupChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Library.Refresh();
        RebuildBreadcrumb();
        OnPropertyChanged(nameof(Title));
    }

    private void OnWatchedGroupInterfaceChanged(object? sender, EventArgs e) => Library.Refresh();
}
