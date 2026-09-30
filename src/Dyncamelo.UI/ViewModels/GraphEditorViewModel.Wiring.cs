using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Nodes;
using Dyncamelo.UI.Mvvm;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// The wiring gestures of the rope-style editor: delete-and-reconnect, auto-connect,
/// wire mute/reroute/disconnect, and selecting along links. Each one is a single undo step
/// and is reachable from a menu item, a shortcut and the command catalogue.
/// </summary>
public partial class GraphEditorViewModel
{
    private ICommand? _deleteAndReconnectCommand;
    private ICommand? _autoConnectCommand;
    private ICommand? _muteWiresCommand;
    private ICommand? _rerouteWiresCommand;
    private ICommand? _disconnectWiresCommand;
    private ICommand? _selectDownstreamCommand;
    private ICommand? _selectUpstreamCommand;
    private ICommand? _selectSimilarCommand;

    /// <summary>Deletes the selected nodes but keeps their data flowing (Ctrl+Delete).</summary>
    public ICommand DeleteAndReconnectCommand => _deleteAndReconnectCommand ??= new RelayCommand(DeleteAndReconnect);

    /// <summary>Chains the selected nodes left to right with the best matching ports (F).</summary>
    public ICommand AutoConnectCommand => _autoConnectCommand ??= new RelayCommand(AutoConnectSelection);

    /// <summary>Mutes or unmutes the selected wires.</summary>
    public ICommand MuteSelectedWiresCommand => _muteWiresCommand ??= new RelayCommand(MuteSelectedWires);

    /// <summary>Adds a reroute to every selected wire.</summary>
    public ICommand RerouteSelectedWiresCommand => _rerouteWiresCommand ??= new RelayCommand(RerouteSelectedWires);

    /// <summary>Removes the selected wires.</summary>
    public ICommand DisconnectSelectedWiresCommand => _disconnectWiresCommand ??= new RelayCommand(DisconnectSelectedWires);

    /// <summary>Selects everything downstream of the selection (L).</summary>
    public ICommand SelectDownstreamCommand => _selectDownstreamCommand ??= new RelayCommand(() => SelectAlongLinks(GraphOps.Downstream, "downstream"));

    /// <summary>Selects everything upstream of the selection (Shift+L).</summary>
    public ICommand SelectUpstreamCommand => _selectUpstreamCommand ??= new RelayCommand(() => SelectAlongLinks(GraphOps.Upstream, "upstream"));

    /// <summary>Selects every node of the same kind as the selection (Shift+G).</summary>
    public ICommand SelectSimilarCommand => _selectSimilarCommand ??= new RelayCommand(() => SelectAlongLinks(GraphOps.Similar, "similar"));

    private static string Count(int n) => n.ToString(CultureInfo.InvariantCulture);

    private void OnConnectionMuteChanged(object? sender, ConnectionEventArgs e)
    {
        foreach (var connection in Connections)
        {
            if (connection.Model == e.Connection)
            {
                connection.RefreshMuted();
            }
        }
    }

    // ----- delete and reconnect -----------------------------------------------------------------

    /// <summary>Deletes the selection; each deleted node's inputs are bridged to its outputs.</summary>
    public void DeleteAndReconnect()
    {
        var nodes = SelectedItems.OfType<NodeViewModel>().ToList();
        if (nodes.Count == 0)
        {
            DeleteSelection();
            return;
        }

        var bridged = 0;
        using (_undo.Begin("Delete and reconnect"))
        {
            foreach (var wire in SelectedConnections.ToList())
            {
                _graph.Disconnect(wire.Model);
            }

            foreach (var item in SelectedItems.ToList())
            {
                switch (item)
                {
                    case NodeViewModel node:
                        bridged += GraphOps.DissolveNode(_graph, node.Model);
                        break;
                    case NoteViewModel note:
                        _graph.Notes.Remove(note.Model);
                        break;
                    case GroupViewModel group:
                        _graph.Groups.Remove(group.Model);
                        break;
                }
            }
        }

        StatusMessage = "Deleted " + Count(nodes.Count) + " node(s), kept " + Count(bridged) + " wire(s).";
    }

    // ----- auto-connect --------------------------------------------------------------------------

    /// <summary>Connects the selected nodes left to right, best matching ports first.</summary>
    public void AutoConnectSelection()
    {
        var nodes = GetSelectedNodeModels();
        if (nodes.Count < 2)
        {
            StatusMessage = "Select two or more nodes to connect them.";
            return;
        }

        int made;
        using (_undo.Begin("Connect nodes"))
        {
            made = GraphOps.AutoConnect(_graph, nodes);
        }

        StatusMessage = made == 0 ? "Nothing to connect: no free matching sockets." : "Connected " + Count(made) + " wire(s).";
    }

    // ----- wires ---------------------------------------------------------------------------------

    /// <summary>Mutes the selected wires, or unmutes them when every one is already muted.</summary>
    public void MuteSelectedWires()
    {
        var wires = SelectedConnections.ToList();
        if (wires.Count == 0)
        {
            StatusMessage = "Select one or more wires first.";
            return;
        }

        var mute = wires.Any(w => !w.Model.IsMuted);
        using (_undo.Begin(mute ? "Mute wire" : "Unmute wire"))
        {
            foreach (var wire in wires)
            {
                _graph.SetConnectionMuted(wire.Model, mute);
            }
        }

        StatusMessage = (mute ? "Muted " : "Unmuted ") + Count(wires.Count) + " wire(s).";
    }

    /// <summary>Removes the selected wires.</summary>
    public void DisconnectSelectedWires()
    {
        var wires = SelectedConnections.ToList();
        if (wires.Count == 0)
        {
            StatusMessage = "Select one or more wires first.";
            return;
        }

        using (_undo.Begin("Disconnect"))
        {
            foreach (var wire in wires)
            {
                _graph.Disconnect(wire.Model);
            }
        }

        StatusMessage = "Disconnected " + Count(wires.Count) + " wire(s).";
    }

    /// <summary>Adds a reroute at the middle of every selected wire.</summary>
    public void RerouteSelectedWires()
    {
        var wires = SelectedConnections.ToList();
        if (wires.Count == 0)
        {
            StatusMessage = "Select one or more wires first.";
            return;
        }

        var added = 0;
        using (_undo.Begin("Add reroute"))
        {
            foreach (var wire in wires)
            {
                var a = wire.Source.Anchor;
                var b = wire.Target.Anchor;
                if (InsertRerouteOnWire(wire, new Point((a.X + b.X) / 2d, (a.Y + b.Y) / 2d)) != null)
                {
                    added++;
                }
            }
        }

        StatusMessage = "Added " + Count(added) + " reroute(s).";
    }

    /// <summary>Puts a reroute on <paramref name="wire"/> so that it sits at <paramref name="at"/> (graph space).</summary>
    /// <param name="wire">The wire to bend.</param>
    /// <param name="at">Where the reroute's centre goes.</param>
    /// <returns>The reroute node, or null when the wire could not be split.</returns>
    public NodeModel? InsertRerouteOnWire(ConnectionViewModel wire, Point at)
    {
        var reroute = new RerouteNode { X = at.X - 20d, Y = at.Y - 11d };
        using (_undo.Begin("Add reroute"))
        {
            _graph.AddNode(reroute);
            if (!GraphOps.InsertOnWire(_graph, reroute, wire.Model))
            {
                _graph.RemoveNode(reroute);
                return null;
            }
        }

        return reroute;
    }

    // ----- selecting along links -------------------------------------------------------------------

    private void SelectAlongLinks(System.Func<GraphModel, IEnumerable<NodeModel>, IReadOnlyCollection<NodeModel>> collect, string what)
    {
        var seeds = GetSelectedNodeModels();
        if (seeds.Count == 0)
        {
            StatusMessage = "Select a node first.";
            return;
        }

        var found = collect(_graph, seeds);
        SelectedItems.Clear();
        foreach (var node in found)
        {
            var viewModel = FindNodeViewModel(node);
            if (viewModel != null)
            {
                SelectedItems.Add(viewModel);
            }
        }

        StatusMessage = "Selected " + Count(found.Count) + " node(s) (" + what + ").";
    }
}
