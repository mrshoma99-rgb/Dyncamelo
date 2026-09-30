using System;
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

    /// <summary>Shows a problem in the status bar (used by the crash guard when a command fails).</summary>
    /// <param name="message">What to show.</param>
    public void ReportProblem(string message) => StatusMessage = message;

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

public partial class GraphEditorViewModel
{
    private UndoTransaction? _cutTransaction;
    private int _cutWires;
    private ICommand? _cuttingStartedCommand;
    private ICommand? _cuttingCompletedCommand;

    /// <summary>Whether the cut in progress should mute wires instead of removing them (Ctrl held); replaceable for tests.</summary>
    public Func<bool> IsMuteCutRequested { get; set; } = () => (Keyboard.Modifiers & ModifierKeys.Control) != 0;

    /// <summary>Nodify runs this when the cutting line starts (Alt+Shift+drag): everything it cuts is one undo step.</summary>
    public ICommand CuttingStartedCommand => _cuttingStartedCommand ??= new RelayCommand(() =>
    {
        _cutTransaction?.Dispose();
        _cutWires = 0;
        _cutTransaction = _undo.Begin("Cut wires");
    });

    /// <summary>Nodify runs this when the cutting line ends or is cancelled.</summary>
    public ICommand CuttingCompletedCommand => _cuttingCompletedCommand ??= new RelayCommand(() =>
    {
        var cut = _cutWires;
        var transaction = _cutTransaction;
        _cutTransaction = null;
        _cutWires = 0;
        transaction?.Dispose();
        if (cut > 0)
        {
            StatusMessage = "Cut " + Count(cut) + " wire(s).";
        }
    });
}

public partial class GraphEditorViewModel
{
    private ICommand? _fitFrameCommand;
    private ICommand? _ungroupSelectedCommand;
    private ICommand? _setFrameColorCommand;

    /// <summary>Shrinks or grows the selected frames to wrap the items inside them (Ctrl+Shift+G).</summary>
    public ICommand FitFrameCommand => _fitFrameCommand ??= new RelayCommand(FitSelectedFrames);

    /// <summary>Removes the selected frames, leaving their nodes (Ctrl+Shift+U).</summary>
    public ICommand UngroupSelectedCommand => _ungroupSelectedCommand ??= new RelayCommand(UngroupSelected);

    /// <summary>Colours the selected frames; the parameter is an ARGB hex string.</summary>
    public ICommand SetFrameColorCommand => _setFrameColorCommand ??= new RelayCommand<string>(SetSelectedFrameColor);

    /// <summary>The colours offered for frames (name, ARGB hex).</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> FrameColors = new[]
    {
        new KeyValuePair<string, string>("Blue", "#FF3D6A99"),
        new KeyValuePair<string, string>("Green", "#FF3F7249"),
        new KeyValuePair<string, string>("Amber", "#FF9A7B2D"),
        new KeyValuePair<string, string>("Red", "#FF8A3B3B"),
        new KeyValuePair<string, string>("Purple", "#FF6B4E8E"),
        new KeyValuePair<string, string>("Gray", "#FF5A6273"),
    };

    private static (double X, double Y, double Width, double Height) ItemRect(CanvasItemViewModel item) =>
        (item.Location.X, item.Location.Y, item.Size.Width > 0 ? item.Size.Width : 160d, item.Size.Height > 0 ? item.Size.Height : 90d);

    /// <summary>The nodes and notes whose centre lies inside <paramref name="group"/>.</summary>
    public IReadOnlyList<CanvasItemViewModel> ItemsInFrame(GroupViewModel group)
    {
        var frame = new Rect(group.Model.X, group.Model.Y, group.Model.Width, group.Model.Height);
        return Items
            .Where(i => !(i is GroupViewModel))
            .Where(i =>
            {
                var r = ItemRect(i);
                return frame.Contains(new Point(r.X + r.Width / 2d, r.Y + r.Height / 2d));
            })
            .ToList();
    }

    private IReadOnlyList<GroupViewModel> TargetFrames()
    {
        var selected = SelectedItems.OfType<GroupViewModel>().ToList();
        if (selected.Count > 0)
        {
            return selected;
        }

        // No frame selected: use the frames that wrap the selected nodes.
        var chosen = SelectedItems.Where(i => !(i is GroupViewModel)).ToList();
        return Items.OfType<GroupViewModel>().Where(g => ItemsInFrame(g).Any(chosen.Contains)).ToList();
    }

    /// <summary>Fits each target frame around the items inside it.</summary>
    public void FitSelectedFrames()
    {
        var frames = TargetFrames();
        if (frames.Count == 0)
        {
            StatusMessage = "Select a frame first.";
            return;
        }

        var fitted = 0;
        using (_undo.Begin("Fit frame"))
        {
            foreach (var group in frames)
            {
                var inside = ItemsInFrame(group);
                var box = GraphOps.FrameAround(inside.Select(ItemRect));
                if (box == null)
                {
                    continue;
                }

                group.Model.X = box.Value.X;
                group.Model.Y = box.Value.Y;
                group.Model.Width = box.Value.Width;
                group.Model.Height = box.Value.Height;
                fitted++;
            }
        }

        StatusMessage = fitted == 0 ? "Nothing inside the frame to fit." : "Fitted " + Count(fitted) + " frame(s).";
    }

    /// <summary>Removes the target frames.</summary>
    public void UngroupSelected()
    {
        var frames = TargetFrames();
        if (frames.Count == 0)
        {
            StatusMessage = "Select a frame first.";
            return;
        }

        using (_undo.Begin("Ungroup"))
        {
            foreach (var group in frames)
            {
                _graph.Groups.Remove(group.Model);
            }
        }

        StatusMessage = "Ungrouped " + Count(frames.Count) + " frame(s).";
    }

    private void SetSelectedFrameColor(string? color)
    {
        var frames = TargetFrames();
        if (string.IsNullOrEmpty(color) || frames.Count == 0)
        {
            StatusMessage = "Select a frame first.";
            return;
        }

        using (_undo.Begin("Frame colour"))
        {
            foreach (var group in frames)
            {
                group.Model.Color = color!;
            }
        }
    }
}

public partial class GraphEditorViewModel
{
    private bool _isHelpOpen;
    private ICommand? _toggleHelpCommand;
    private ICommand? _closeHelpCommand;

    /// <summary>The text of the shortcut/gesture overlay (generated from the command catalogue).</summary>
    public IReadOnlyList<HelpSection> HelpSections { get; } = HelpContent.Build();

    /// <summary>True while the keyboard and mouse help overlay is shown (F1).</summary>
    public bool IsHelpOpen
    {
        get => _isHelpOpen;
        set => SetProperty(ref _isHelpOpen, value);
    }

    /// <summary>Shows or hides the help overlay (F1).</summary>
    public ICommand ToggleHelpCommand => _toggleHelpCommand ??= new RelayCommand(() => IsHelpOpen = !IsHelpOpen);

    /// <summary>Hides the help overlay.</summary>
    public ICommand CloseHelpCommand => _closeHelpCommand ??= new RelayCommand(() => IsHelpOpen = false);
}
