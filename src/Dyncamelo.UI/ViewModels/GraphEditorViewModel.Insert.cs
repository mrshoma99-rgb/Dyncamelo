using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Graph;
using Dyncamelo.UI.Services;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// Insert on wire: drag a node that has no wires over a wire and the wire lights up; drop it and the
/// node is spliced in (the wire's data goes through it), the nodes downstream shift right to make
/// room, and the whole thing — move, splice, shift — is one undo step.
/// </summary>
public partial class GraphEditorViewModel
{
    private const double InsertHitPadding = 6d;
    private ConnectionViewModel? _insertCandidate;
    private Point _lastDragCursor;

    /// <summary>The wire that would receive the node being dragged, or null.</summary>
    public ConnectionViewModel? InsertCandidate => _insertCandidate;

    /// <summary>
    /// Finds the wire that passes through the dragged node's rectangle and could take the node, preferring the
    /// one nearest the cursor. Returns null when the node has wires of its own or no wire fits.
    /// </summary>
    /// <param name="node">The dragged node.</param>
    /// <param name="rect">The node's current rectangle in graph space.</param>
    /// <param name="cursor">The pointer in graph space.</param>
    public ConnectionViewModel? FindInsertTarget(NodeModel node, Rect rect, Point cursor)
    {
        if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
        {
            return null;
        }

        var probe = new Rect(rect.X - InsertHitPadding, rect.Y - InsertHitPadding, rect.Width + 2 * InsertHitPadding, rect.Height + 2 * InsertHitPadding);
        ConnectionViewModel? best = null;
        var bestDistance = double.MaxValue;
        foreach (var wire in Connections)
        {
            if (wire.Model.SourceNode == node || wire.Model.TargetNode == node)
            {
                return null;
            }

            // Cheap reject: the curve stays inside the box of its four control points.
            var start = wire.Source.Anchor;
            var end = wire.Target.Anchor;
            var (c1, c2) = WireGeometry.ControlPoints(start, end);
            var hull = new Rect(start, end);
            hull.Union(c1);
            hull.Union(c2);
            if (!probe.IntersectsWith(hull))
            {
                continue;
            }

            var distance = double.MaxValue;
            foreach (var point in WireGeometry.Sample(start, end))
            {
                if (probe.Contains(point))
                {
                    distance = System.Math.Min(distance, (point - cursor).LengthSquared);
                }
            }

            if (distance < bestDistance && GraphOps.CanInsert(_graph, node, wire.Model, out _, out _))
            {
                best = wire;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Updates the highlighted wire while a node is being dragged (called by the view a few times a second).</summary>
    /// <param name="dragged">The dragged node, or null when more than one item moves.</param>
    /// <param name="rect">Its live rectangle in graph space.</param>
    /// <param name="cursor">The pointer in graph space.</param>
    public void UpdateInsertCandidate(NodeViewModel? dragged, Rect rect, Point cursor)
    {
        _lastDragCursor = cursor;
        var next = dragged == null ? null : FindInsertTarget(dragged.Model, rect, cursor);
        if (ReferenceEquals(next, _insertCandidate))
        {
            return;
        }

        if (_insertCandidate != null)
        {
            _insertCandidate.IsInsertTarget = false;
        }

        _insertCandidate = next;
        if (next != null)
        {
            next.IsInsertTarget = true;
        }
    }

    /// <summary>Clears the highlight.</summary>
    public void ClearInsertCandidate() => UpdateInsertCandidate(null, Rect.Empty, _lastDragCursor);

    private void CommitInsertOnDrop()
    {
        var hadCandidate = _insertCandidate != null;
        ClearInsertCandidate();
        if (!hadCandidate)
        {
            return;
        }

        var moved = SelectedItems.OfType<NodeViewModel>().ToList();
        if (moved.Count != 1 || SelectedItems.Count != 1)
        {
            return;
        }

        var node = moved[0];
        var finalRect = new Rect(node.Location, node.Size);
        finalRect.Inflate(-8d, -8d);
        var wire = FindInsertTarget(node.Model, finalRect, _lastDragCursor);
        if (wire != null && InsertNodeOnWire(node, wire))
        {
            _undo.RelabelTransaction("Insert on wire");
        }
    }

    /// <summary>Splices <paramref name="node"/> onto <paramref name="wire"/> and shifts the nodes downstream of it to leave a gap.</summary>
    /// <param name="node">A node without wires.</param>
    /// <param name="wire">The wire to splice it into.</param>
    /// <returns>True when the node was inserted.</returns>
    public bool InsertNodeOnWire(NodeViewModel node, ConnectionViewModel wire)
    {
        using (_undo.Begin("Insert on wire"))
        {
            if (!GraphOps.InsertOnWire(_graph, node.Model, wire.Model))
            {
                return false;
            }

            foreach (var move in GraphOps.MakeRoom(_graph, node.Model, WidthOf))
            {
                move.Key.X = move.Value;
            }
        }

        StatusMessage = "Inserted " + node.Title + " on the wire.";
        return true;
    }

    private double WidthOf(NodeModel model)
    {
        var viewModel = FindNodeViewModel(model);
        var width = viewModel?.Size.Width ?? 0d;
        return width > 0d ? width : (model.Ui.Width ?? DefaultNodeWidth);
    }

    /// <summary>
    /// Adds a library node at <paramref name="location"/> and, when the drop point lies on a wire the node fits,
    /// splices it in — the whole thing is one undo step.
    /// </summary>
    /// <param name="libraryId">Definition id or node type tag.</param>
    /// <param name="location">Graph-space drop location.</param>
    public NodeViewModel? AddNodeOnWire(string libraryId, Point location)
    {
        NodeViewModel? added;
        using (_undo.Begin("Add node"))
        {
            added = AddNode(libraryId, location);
            if (added == null)
            {
                return null;
            }

            var probe = new Rect(location.X - 2d, location.Y - 2d, 4d, 4d);
            var wire = FindInsertTarget(added.Model, probe, location);
            if (wire != null && InsertNodeOnWire(added, wire))
            {
                _undo.RelabelTransaction("Add node on wire");
            }
        }

        return added;
    }
}
