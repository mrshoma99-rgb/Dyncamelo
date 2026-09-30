using System;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Graph;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// Dragging a wire: sockets that cannot take it dim, and dragging from an already-connected input picks the
/// existing link up (drop it on another input to move it, Shift to swap with what is there, on empty canvas to
/// remove it, back on its own input to leave it).
/// </summary>
public partial class GraphEditorViewModel
{
    private ConnectionViewModel? _movingLink;
    private bool _socketsDimmed;

    /// <summary>Whether the link being moved should swap with the wire already on the target input (Shift held); replaceable for tests.</summary>
    public Func<bool> IsSwapRequested { get; set; } = () => (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

    /// <summary>The pointer's position in graph space (the view supplies it); picks which wire a multi-input drag takes and where a dropped wire lands.</summary>
    public Func<System.Windows.Point>? PointerLocation { get; set; }

    /// <summary>The link picked up from a connected input while it is being dragged, or null.</summary>
    public ConnectionViewModel? MovingLink => _movingLink;

    private void BeginConnectionDrag(ConnectorViewModel? connector)
    {
        PendingConnection.IsVisible = true;
        if (connector == null)
        {
            return;
        }

        ReleaseMovingLink();
        var origin = connector;
        if (connector.IsInput && connector.IsConnected)
        {
            // A multi-input carries several wires: the one under the pointer is the one that comes off.
            var wires = Connections.Where(c => c.Target == connector).OrderBy(c => c.Slot).ToList();
            var pointer = PointerLocation?.Invoke() ?? connector.Anchor;
            var wire = wires.Count == 0 ? null : wires[Math.Min(wires.Count - 1, connector.SlotAt(pointer.Y))];
            if (wire != null)
            {
                _movingLink = wire;
                wire.IsHidden = true;
                origin = wire.Source;
                PendingConnection.SourceLocation = wire.Source.Anchor;
            }
        }

        ApplySocketDimming(origin);
    }

    private void OnPendingConnectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PendingConnectionViewModel.IsVisible) || PendingConnection.IsVisible)
        {
            return;
        }

        // The drag ended. A completed drop consumes the picked-up link straight after this (Nodify runs the
        // completion command next); if it is still pending once that has run, the drag was cancelled.
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            if (!PendingConnection.IsVisible)
            {
                ReleaseMovingLink();
                ClearSocketDimming();
            }
        }), DispatcherPriority.Background);
    }

    private void ReleaseMovingLink()
    {
        if (_movingLink != null)
        {
            _movingLink.IsHidden = false;
            _movingLink = null;
        }
    }

    private void CompleteMovedLink(ConnectorViewModel? target)
    {
        var wire = _movingLink!;
        _movingLink = null;
        wire.IsHidden = false;
        PendingConnection.IsVisible = false;

        var origin = wire.Source;
        var oldInput = wire.Target;
        if (target == oldInput)
        {
            // Dropped back on its own multi-input: the wire takes the slot it was dropped at.
            if (oldInput.IsMultiInput && oldInput.WireCount > 1)
            {
                var slot = oldInput.SlotAt((PointerLocation?.Invoke() ?? oldInput.Anchor).Y);
                using (_undo.Begin("Reorder wires"))
                {
                    if (GraphOps.MoveWire(_graph, wire.Model, slot) != null)
                    {
                        StatusMessage = "Moved the wire to position " + (slot + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";
                    }
                }
            }

            return;
        }

        if (target == null)
        {
            // Released over a node body: leave the link alone; over empty canvas: pull it off.
            if (!IsOverNode(PendingConnection.TargetLocation))
            {
                using (_undo.Begin("Remove link"))
                {
                    _graph.Disconnect(wire.Model);
                }

                StatusMessage = "Disconnected " + origin.Node.Title + " → " + oldInput.Node.Title + ".";
            }

            return;
        }

        if (!target.IsInput)
        {
            StatusMessage = "Drop a picked-up link on an input.";
            return;
        }

        var swap = IsSwapRequested();
        using (_undo.Begin(swap ? "Swap links" : "Move link"))
        {
            // A multi-input takes another wire; only a single-wire input has one to displace.
            var displaced = target.Port.IsMultiInput ? null : _graph.FindConnectionInto(target.Port);
            var displacedSource = displaced?.Source;
            var result = _graph.Connect(origin.Port, target.Port);
            if (!result.Success)
            {
                StatusMessage = result.Message ?? "The link cannot go there.";
                return;
            }

            _graph.Disconnect(wire.Model);
            if (swap && displacedSource != null)
            {
                _graph.Connect(displacedSource, oldInput.Port);
            }
        }

        StatusMessage = (swap ? "Swapped links on " : "Moved link to ") + target.Node.Title + ".";
    }

    // ----- dimming -----------------------------------------------------------------------------------

    private void ApplySocketDimming(ConnectorViewModel origin)
    {
        _socketsDimmed = true;
        foreach (var node in Items.OfType<NodeViewModel>())
        {
            foreach (var socket in node.Inputs.Concat(node.Outputs))
            {
                socket.SocketOpacity = SocketFit(origin, socket);
            }
        }
    }

    /// <summary>How visible a socket stays while <paramref name="origin"/>'s wire is dragged: 1 fits, 0.7 loosely, 0.25 not at all.</summary>
    internal static double SocketFit(ConnectorViewModel origin, ConnectorViewModel socket)
    {
        if (ReferenceEquals(origin, socket))
        {
            return 1d;
        }

        if (origin.IsInput == socket.IsInput)
        {
            return 0.25d;
        }

        var compat = origin.IsInput
            ? PortKinds.Compare(socket.Port, origin.Port)
            : PortKinds.Compare(origin.Port, socket.Port);
        switch (compat)
        {
            case Compat.Exact:
            case Compat.Convertible:
                return 1d;
            case Compat.Loose:
                return 0.7d;
            default:
                return 0.25d;
        }
    }

    private void ClearSocketDimming()
    {
        if (!_socketsDimmed)
        {
            return;
        }

        _socketsDimmed = false;
        foreach (var node in Items.OfType<NodeViewModel>())
        {
            foreach (var socket in node.Inputs.Concat(node.Outputs))
            {
                socket.SocketOpacity = 1d;
            }
        }
    }
}
