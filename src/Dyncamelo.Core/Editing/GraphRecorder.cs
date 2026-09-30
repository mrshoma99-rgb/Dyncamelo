using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Dyncamelo.Core.Graph;

namespace Dyncamelo.Core.Editing;

/// <summary>
/// Watches a <see cref="GraphModel"/> and records every user-visible edit into
/// an <see cref="UndoManager"/> as it happens: nodes and wires added/removed,
/// notes and groups added/removed, node position, name, freeze/mute/lacing,
/// node data properties (an input node's value…), per-node presentation state,
/// and per-port pinned values / list levels / hidden flags. Because it listens
/// to the model rather than to individual commands, no edit path can be missed.
/// Recording is muted while the manager replays and while it is suspended
/// (graph runs write properties that are not user edits).
/// </summary>
public sealed class GraphRecorder : IDisposable
{
    private static readonly HashSet<string> NodeBaseProps = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(NodeModel.Name), nameof(NodeModel.IsFrozen), nameof(NodeModel.IsMuted), nameof(NodeModel.Lacing),
    };

    private readonly GraphModel _graph;
    private readonly UndoManager _undo;
    private readonly Dictionary<object, Tracked> _tracked = new Dictionary<object, Tracked>();
    private bool _disposed;

    /// <summary>Starts recording <paramref name="graph"/> into <paramref name="undo"/>.</summary>
    public GraphRecorder(GraphModel graph, UndoManager undo)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _undo = undo ?? throw new ArgumentNullException(nameof(undo));

        foreach (var node in graph.Nodes)
        {
            TrackNode(node);
        }

        foreach (var note in graph.Notes)
        {
            TrackObject(note, null);
        }

        foreach (var group in graph.Groups)
        {
            TrackObject(group, null);
        }

        foreach (var bookmark in graph.Bookmarks)
        {
            TrackObject(bookmark, null);
        }

        graph.NodeAdded += OnNodeAdded;
        graph.NodeRemoved += OnNodeRemoved;
        graph.ConnectionAdded += OnConnectionAdded;
        graph.ConnectionRemoved += OnConnectionRemoved;
        graph.ConnectionMuteChanged += OnConnectionMuteChanged;
        graph.Notes.CollectionChanged += OnNotesChanged;
        graph.Groups.CollectionChanged += OnGroupsChanged;
        graph.Bookmarks.CollectionChanged += OnBookmarksChanged;
    }

    /// <summary>Stops recording and detaches every handler.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _graph.NodeAdded -= OnNodeAdded;
        _graph.NodeRemoved -= OnNodeRemoved;
        _graph.ConnectionAdded -= OnConnectionAdded;
        _graph.ConnectionRemoved -= OnConnectionRemoved;
        _graph.ConnectionMuteChanged -= OnConnectionMuteChanged;
        _graph.Notes.CollectionChanged -= OnNotesChanged;
        _graph.Groups.CollectionChanged -= OnGroupsChanged;
        _graph.Bookmarks.CollectionChanged -= OnBookmarksChanged;
        foreach (var t in _tracked.Values.ToList())
        {
            t.Detach();
        }

        _tracked.Clear();
    }

    // ----- structure -------------------------------------------------------

    private void OnNodeAdded(object? sender, NodeEventArgs e)
    {
        TrackNode(e.Node);
        _undo.Record(new NodeStep(_graph, e.Node, added: true));
    }

    private void OnNodeRemoved(object? sender, NodeEventArgs e)
    {
        UntrackNode(e.Node);
        _undo.Record(new NodeStep(_graph, e.Node, added: false));
    }

    private void OnConnectionAdded(object? sender, ConnectionEventArgs e) =>
        _undo.Record(new WireStep(_graph, e.Connection, added: true));

    private void OnConnectionRemoved(object? sender, ConnectionEventArgs e) =>
        _undo.Record(new WireStep(_graph, e.Connection, added: false));

    private void OnConnectionMuteChanged(object? sender, ConnectionEventArgs e) =>
        _undo.Record(new WireMuteStep(_graph, e.Connection, e.Connection.IsMuted));

    private void OnNotesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnCollectionChanged(_graph.Notes, e);

    private void OnGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnCollectionChanged(_graph.Groups, e);

    private void OnBookmarksChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnCollectionChanged(_graph.Bookmarks, e);

    private void OnCollectionChanged<T>(System.Collections.ObjectModel.ObservableCollection<T> list, NotifyCollectionChangedEventArgs e)
        where T : class
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            var index = e.NewStartingIndex;
            foreach (T item in e.NewItems)
            {
                TrackObject(item, null);
                _undo.Record(new ListItemStep<T>(list, item, index < 0 ? list.IndexOf(item) : index++, added: true));
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            var index = e.OldStartingIndex;
            foreach (T item in e.OldItems)
            {
                Untrack(item);
                _undo.Record(new ListItemStep<T>(list, item, index < 0 ? 0 : index++, added: false));
            }
        }
    }

    // ----- property tracking -------------------------------------------------

    private void TrackNode(NodeModel node)
    {
        TrackObject(node, p =>
            (p.DeclaringType != typeof(NodeModel) && p.DeclaringType != typeof(object)) || NodeBaseProps.Contains(p.Name));
        TrackObject(node.Ui, null);
        foreach (var port in node.InPorts)
        {
            TrackPort(port);
        }
    }

    private void UntrackNode(NodeModel node)
    {
        Untrack(node);
        Untrack(node.Ui);
        foreach (var port in node.InPorts)
        {
            Untrack(port);
        }
    }

    private void TrackObject(object target, Func<PropertyInfo, bool>? filter)
    {
        if (_tracked.ContainsKey(target) || !(target is INotifyPropertyChanged notify))
        {
            return;
        }

        var t = new ObjectTracked(this, target, notify, filter);
        _tracked[target] = t;
        t.Attach();
    }

    private void TrackPort(PortModel port)
    {
        if (_tracked.ContainsKey(port))
        {
            return;
        }

        var t = new PortTracked(this, port);
        _tracked[port] = t;
        t.Attach();
    }

    private void Untrack(object target)
    {
        if (_tracked.TryGetValue(target, out var t))
        {
            t.Detach();
            _tracked.Remove(target);
        }
    }

    private abstract class Tracked
    {
        public abstract void Attach();

        public abstract void Detach();
    }

    private sealed class ObjectTracked : Tracked
    {
        private readonly GraphRecorder _owner;
        private readonly object _target;
        private readonly INotifyPropertyChanged _notify;
        private readonly Dictionary<string, PropertyInfo> _props = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
        private readonly Dictionary<string, object?> _shadow = new Dictionary<string, object?>(StringComparer.Ordinal);

        public ObjectTracked(GraphRecorder owner, object target, INotifyPropertyChanged notify, Func<PropertyInfo, bool>? filter)
        {
            _owner = owner;
            _target = target;
            _notify = notify;
            foreach (var p in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || p.GetIndexParameters().Length > 0 || p.GetSetMethod(nonPublic: false) == null ||
                    !IsSimple(p.PropertyType) || (filter != null && !filter(p)))
                {
                    continue;
                }

                _props[p.Name] = p;
                _shadow[p.Name] = p.GetValue(target);
            }

            if (target is NodeModel n)
            {
                _shadow[nameof(NodeModel.X)] = n.X;
                _shadow[nameof(NodeModel.Y)] = n.Y;
            }
        }

        public override void Attach() => _notify.PropertyChanged += OnChanged;

        public override void Detach() => _notify.PropertyChanged -= OnChanged;

        private void OnChanged(object? sender, PropertyChangedEventArgs e)
        {
            var name = e.PropertyName;
            if (name == null)
            {
                return;
            }

            if (_target is NodeModel node && (name == nameof(NodeModel.X) || name == nameof(NodeModel.Y)))
            {
                // Position is one logical property: X then Y arrive separately and merge.
                var oldX = _shadow.TryGetValue(nameof(NodeModel.X), out var ox) ? (double)(ox ?? 0d) : node.X;
                var oldY = _shadow.TryGetValue(nameof(NodeModel.Y), out var oy) ? (double)(oy ?? 0d) : node.Y;
                _shadow[nameof(NodeModel.X)] = node.X;
                _shadow[nameof(NodeModel.Y)] = node.Y;
                if (oldX != node.X || oldY != node.Y)
                {
                    _owner._undo.Record(new MoveStep(node, oldX, oldY, node.X, node.Y));
                }

                return;
            }

            if (!_props.TryGetValue(name, out var prop))
            {
                return;
            }

            var old = _shadow[name];
            var current = prop.GetValue(_target);
            _shadow[name] = current;
            if (!Equals(old, current))
            {
                _owner._undo.Record(new PropertyStep(_target, prop, old, current));
            }
        }

        private static bool IsSimple(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal);
        }
    }

    private sealed class PortTracked : Tracked
    {
        private readonly GraphRecorder _owner;
        private readonly PortModel _port;
        private bool _hasUser;
        private object? _user;
        private bool _useLevels;
        private int _level;
        private bool _keep;
        private bool _hidden;

        public PortTracked(GraphRecorder owner, PortModel port)
        {
            _owner = owner;
            _port = port;
            Snapshot();
        }

        public override void Attach() => _port.PropertyChanged += OnChanged;

        public override void Detach() => _port.PropertyChanged -= OnChanged;

        private void Snapshot()
        {
            _hasUser = _port.HasUserValue;
            _user = _port.UserValue;
            _useLevels = _port.UseLevels;
            _level = _port.Level;
            _keep = _port.KeepListStructure;
            _hidden = _port.IsHidden;
        }

        private void OnChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(PortModel.UserValue):
                    if (_hasUser != _port.HasUserValue || !Equals(_user, _port.UserValue))
                    {
                        _owner._undo.Record(new PortValueStep(_port, _hasUser, _user, _port.HasUserValue, _port.UserValue));
                    }

                    _hasUser = _port.HasUserValue;
                    _user = _port.UserValue;
                    break;
                case nameof(PortModel.UseLevels):
                    if (_useLevels != _port.UseLevels || _level != _port.Level || _keep != _port.KeepListStructure)
                    {
                        _owner._undo.Record(new PortLevelsStep(_port, _useLevels, _level, _keep, _port.UseLevels, _port.Level, _port.KeepListStructure));
                    }

                    _useLevels = _port.UseLevels;
                    _level = _port.Level;
                    _keep = _port.KeepListStructure;
                    break;
                case nameof(PortModel.IsHidden):
                    if (_hidden != _port.IsHidden)
                    {
                        _owner._undo.Record(new PropertyStep(_port, typeof(PortModel).GetProperty(nameof(PortModel.IsHidden))!, _hidden, _port.IsHidden));
                    }

                    _hidden = _port.IsHidden;
                    break;
            }
        }
    }

    // ----- steps -------------------------------------------------------------

    private sealed class NodeStep : IUndoStep
    {
        private readonly GraphModel _graph;
        private readonly NodeModel _node;
        private readonly bool _added;

        public NodeStep(GraphModel graph, NodeModel node, bool added)
        {
            _graph = graph;
            _node = node;
            _added = added;
        }

        public string Label => (_added ? "Add " : "Delete ") + (string.IsNullOrEmpty(_node.Name) ? "node" : _node.Name);

        public void Undo() => Apply(!_added);

        public void Redo() => Apply(_added);

        private void Apply(bool present)
        {
            if (present)
            {
                if (_node.Graph == null)
                {
                    _graph.ReinsertNode(_node);
                }
            }
            else if (_node.Graph == _graph)
            {
                _graph.RemoveNode(_node);
            }
        }
    }

    private sealed class WireStep : IUndoStep
    {
        private readonly GraphModel _graph;
        private readonly ConnectionModel _connection;
        private readonly bool _added;

        public WireStep(GraphModel graph, ConnectionModel connection, bool added)
        {
            _graph = graph;
            _connection = connection;
            _added = added;
        }

        public string Label => _added ? "Connect" : "Disconnect";

        public void Undo() => Apply(!_added);

        public void Redo() => Apply(_added);

        private void Apply(bool present)
        {
            if (present)
            {
                _graph.ReinsertConnection(_connection);
            }
            else if (_graph.Connections.Contains(_connection))
            {
                _graph.Disconnect(_connection);
            }
        }
    }

    private sealed class WireMuteStep : IUndoStep
    {
        private readonly GraphModel _graph;
        private readonly ConnectionModel _connection;
        private readonly bool _muted;

        public WireMuteStep(GraphModel graph, ConnectionModel connection, bool muted)
        {
            _graph = graph;
            _connection = connection;
            _muted = muted;
        }

        public string Label => _muted ? "Mute wire" : "Unmute wire";

        public void Undo() => _graph.SetConnectionMuted(_connection, !_muted);

        public void Redo() => _graph.SetConnectionMuted(_connection, _muted);
    }

    private sealed class ListItemStep<T> : IUndoStep
        where T : class
    {
        private readonly System.Collections.ObjectModel.ObservableCollection<T> _list;
        private readonly T _item;
        private readonly int _index;
        private readonly bool _added;

        public ListItemStep(System.Collections.ObjectModel.ObservableCollection<T> list, T item, int index, bool added)
        {
            _list = list;
            _item = item;
            _index = index;
            _added = added;
        }

        public string Label => (_added ? "Add " : "Delete ") + (typeof(T).Name.Replace("Model", string.Empty).ToLowerInvariant());

        public void Undo() => Apply(!_added);

        public void Redo() => Apply(_added);

        private void Apply(bool present)
        {
            if (present)
            {
                if (!_list.Contains(_item))
                {
                    _list.Insert(Math.Max(0, Math.Min(_index, _list.Count)), _item);
                }
            }
            else
            {
                _list.Remove(_item);
            }
        }
    }

    private sealed class MoveStep : ICoalescingStep
    {
        private readonly NodeModel _node;
        private readonly double _oldX;
        private readonly double _oldY;
        private double _newX;
        private double _newY;

        public MoveStep(NodeModel node, double oldX, double oldY, double newX, double newY)
        {
            _node = node;
            _oldX = oldX;
            _oldY = oldY;
            _newX = newX;
            _newY = newY;
        }

        public string Label => "Move " + (string.IsNullOrEmpty(_node.Name) ? "node" : _node.Name);

        public void Undo()
        {
            _node.X = _oldX;
            _node.Y = _oldY;
        }

        public void Redo()
        {
            _node.X = _newX;
            _node.Y = _newY;
        }

        public bool TryMerge(IUndoStep newer)
        {
            if (newer is MoveStep m && ReferenceEquals(m._node, _node))
            {
                _newX = m._newX;
                _newY = m._newY;
                return true;
            }

            return false;
        }
    }

    private sealed class PropertyStep : ICoalescingStep
    {
        private readonly object _target;
        private readonly PropertyInfo _property;
        private readonly object? _old;
        private object? _new;

        public PropertyStep(object target, PropertyInfo property, object? oldValue, object? newValue)
        {
            _target = target;
            _property = property;
            _old = oldValue;
            _new = newValue;
        }

        public string Label
        {
            get
            {
                switch (_property.Name)
                {
                    case nameof(NodeModel.Name): return "Rename";
                    case nameof(NodeModel.IsFrozen): return (bool)(_new ?? false) ? "Freeze" : "Unfreeze";
                    case nameof(NodeModel.IsMuted): return (bool)(_new ?? false) ? "Mute" : "Unmute";
                    case nameof(NodeModel.Lacing): return "Change lacing";
                    case nameof(NodeUiState.Collapsed): return (bool)(_new ?? false) ? "Collapse" : "Expand";
                    case nameof(NodeUiState.Width): return "Resize";
                    default: return "Edit " + _property.Name;
                }
            }
        }

        public void Undo() => _property.SetValue(_target, _old);

        public void Redo() => _property.SetValue(_target, _new);

        public bool TryMerge(IUndoStep newer)
        {
            if (newer is PropertyStep p && ReferenceEquals(p._target, _target) && p._property == _property)
            {
                _new = p._new;
                return true;
            }

            return false;
        }
    }

    private sealed class PortValueStep : ICoalescingStep
    {
        private readonly PortModel _port;
        private readonly bool _hadOld;
        private readonly object? _old;
        private bool _hasNew;
        private object? _new;

        public PortValueStep(PortModel port, bool hadOld, object? oldValue, bool hasNew, object? newValue)
        {
            _port = port;
            _hadOld = hadOld;
            _old = oldValue;
            _hasNew = hasNew;
            _new = newValue;
        }

        public string Label => "Edit " + _port.Name;

        public void Undo() => Apply(_hadOld, _old);

        public void Redo() => Apply(_hasNew, _new);

        public bool TryMerge(IUndoStep newer)
        {
            if (newer is PortValueStep p && ReferenceEquals(p._port, _port))
            {
                _hasNew = p._hasNew;
                _new = p._new;
                return true;
            }

            return false;
        }

        private void Apply(bool has, object? value)
        {
            if (has)
            {
                _port.SetUserValue(value);
            }
            else
            {
                _port.ClearUserValue();
            }
        }
    }

    private sealed class PortLevelsStep : IUndoStep
    {
        private readonly PortModel _port;
        private readonly bool _oldUse;
        private readonly int _oldLevel;
        private readonly bool _oldKeep;
        private readonly bool _newUse;
        private readonly int _newLevel;
        private readonly bool _newKeep;

        public PortLevelsStep(PortModel port, bool oldUse, int oldLevel, bool oldKeep, bool newUse, int newLevel, bool newKeep)
        {
            _port = port;
            _oldUse = oldUse;
            _oldLevel = oldLevel;
            _oldKeep = oldKeep;
            _newUse = newUse;
            _newLevel = newLevel;
            _newKeep = newKeep;
        }

        public string Label => "Change list level";

        public void Undo() => _port.SetLevels(_oldUse, _oldLevel, _oldKeep);

        public void Redo() => _port.SetLevels(_newUse, _newLevel, _newKeep);
    }
}
