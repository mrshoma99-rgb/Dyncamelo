using System;
using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Graph;

namespace Dyncamelo.Core.Editing;

/// <summary>Kind of one visual row of a node.</summary>
public enum RowKind
{
    /// <summary>An output socket row (right edge).</summary>
    Output,
    /// <summary>An input socket row (left edge), with its inline editor when unwired.</summary>
    Input,
    /// <summary>The node's own body (special nodes such as sliders or watches).</summary>
    Body,
    /// <summary>Header of a foldable panel.</summary>
    PanelHeader,
    /// <summary>The "+N hidden" chip.</summary>
    HiddenSummary,
}

/// <summary>One row of a node's layout, as planned from its ports and presentation state.</summary>
public sealed class PlannedRow
{
    /// <summary>What the row shows.</summary>
    public RowKind Kind { get; internal set; }

    /// <summary>The port for input/output rows.</summary>
    public PortModel? Port { get; internal set; }

    /// <summary>Panel title for panel headers and for inputs that belong to a panel.</summary>
    public string Panel { get; internal set; } = string.Empty;

    /// <summary>
    /// True when the row is laid out with zero height but its socket is kept, so a wire
    /// attached to a collapsed node or a closed panel still ends at a valid anchor.
    /// </summary>
    public bool ZeroHeight { get; internal set; }

    /// <summary>
    /// True for the rows of a collapsed node: only the socket is drawn, in a slim row, so the node still shows
    /// where its inputs and outputs are (as Blender's collapsed nodes do) without labels or editors.
    /// </summary>
    public bool Compact { get; internal set; }

    /// <summary>For panel headers: whether the panel is expanded.</summary>
    public bool IsOpen { get; internal set; }

    /// <summary>For panel headers: whether the panel starts open (before the user's choice).</summary>
    public bool PanelDefaultOpen { get; internal set; }

    /// <summary>Panel header: number of inputs with a pinned value. Hidden-summary row: number of hidden ports.</summary>
    public int Count { get; internal set; }

    /// <summary>Identity used to reuse row objects across rebuilds.</summary>
    public string Key
    {
        get
        {
            switch (Kind)
            {
                case RowKind.Output: return "o:" + Port!.Name;
                case RowKind.Input: return "i:" + Port!.Name;
                case RowKind.PanelHeader: return "p:" + Panel;
                case RowKind.Body: return "body";
                default: return "hidden";
            }
        }
    }
}

/// <summary>
/// Decides which rows a node shows and in what order: outputs, the node's own
/// body, ungrouped inputs, then foldable panels, then a "+N hidden" chip.
/// Pure and deterministic so it is unit-tested without any UI.
/// </summary>
/// <remarks>
/// Invariants: a connected port and a port with a pinned value are never
/// hidden; a connected port that would be hidden (closed panel) keeps a
/// zero-height row so its wire still has an anchor. A collapsed node keeps a
/// slim socket-only row for every port it would show (outputs first, then inputs).
/// </remarks>
public static class RowPlanner
{
    /// <summary>Plans the rows of <paramref name="node"/>.</summary>
    /// <param name="node">The node.</param>
    /// <param name="isConnected">Whether a port currently has a wire.</param>
    /// <param name="hasBody">True when the node renders its own body row.</param>
    /// <param name="hideUnusedDefault">The preference for nodes without an explicit hide-unused choice.</param>
    public static IReadOnlyList<PlannedRow> Plan(NodeModel node, Func<PortModel, bool> isConnected, bool hasBody, bool hideUnusedDefault = false)
    {
        if (node == null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        if (isConnected == null)
        {
            throw new ArgumentNullException(nameof(isConnected));
        }

        var rows = new List<PlannedRow>();
        var collapsed = node.Ui.Collapsed;

        if (collapsed)
        {
            foreach (var port in node.OutPorts.Where(p => !IsHidden(node, p, isConnected(p), hideUnusedDefault)))
            {
                rows.Add(new PlannedRow { Kind = RowKind.Output, Port = port, Compact = true });
            }

            foreach (var port in node.InPorts.Where(p => !IsHidden(node, p, isConnected(p), hideUnusedDefault)))
            {
                rows.Add(new PlannedRow { Kind = RowKind.Input, Port = port, Compact = true, Panel = port.Panel });
            }

            return rows;
        }

        var hidden = 0;

        foreach (var port in node.OutPorts)
        {
            if (IsHidden(node, port, isConnected(port), hideUnusedDefault))
            {
                hidden++;
                continue;
            }

            rows.Add(new PlannedRow { Kind = RowKind.Output, Port = port });
        }

        if (hasBody)
        {
            rows.Add(new PlannedRow { Kind = RowKind.Body });
        }

        var panels = new List<string>();
        var visibleByPanel = new Dictionary<string, List<PortModel>>(StringComparer.Ordinal);
        var connectedByPanel = new Dictionary<string, List<PortModel>>(StringComparer.Ordinal);
        foreach (var port in node.InPorts)
        {
            var connected = isConnected(port);
            if (IsHidden(node, port, connected, hideUnusedDefault))
            {
                hidden++;
                continue;
            }

            if (port.Panel.Length == 0)
            {
                rows.Add(new PlannedRow { Kind = RowKind.Input, Port = port });
                continue;
            }

            if (!visibleByPanel.TryGetValue(port.Panel, out var list))
            {
                panels.Add(port.Panel);
                list = new List<PortModel>();
                visibleByPanel[port.Panel] = list;
                connectedByPanel[port.Panel] = new List<PortModel>();
            }

            list.Add(port);
            if (connected)
            {
                connectedByPanel[port.Panel].Add(port);
            }
        }

        foreach (var panel in panels)
        {
            var members = visibleByPanel[panel];
            var defaultOpen = members.Any(p => p.PanelDefaultOpen);
            var open = IsPanelOpen(node, panel, defaultOpen);
            rows.Add(new PlannedRow
            {
                Kind = RowKind.PanelHeader,
                Panel = panel,
                IsOpen = open,
                PanelDefaultOpen = defaultOpen,
                Count = members.Count(p => p.HasUserValue),
            });

            foreach (var port in members)
            {
                if (open)
                {
                    rows.Add(new PlannedRow { Kind = RowKind.Input, Port = port, Panel = panel });
                }
                else if (connectedByPanel[panel].Contains(port))
                {
                    rows.Add(new PlannedRow { Kind = RowKind.Input, Port = port, Panel = panel, ZeroHeight = true });
                }
            }
        }

        if (hidden > 0)
        {
            rows.Add(new PlannedRow { Kind = RowKind.HiddenSummary, Count = hidden });
        }

        return rows;
    }

    /// <summary>Whether a panel is expanded: its default, flipped by the user's recorded choice.</summary>
    public static bool IsPanelOpen(NodeModel node, string panel, bool defaultOpen)
    {
        return defaultOpen ? !node.Ui.ClosedPanels.Contains(panel) : node.Ui.OpenPanels.Contains(panel);
    }

    /// <summary>Opens or closes a panel, recording only the deviation from its default.</summary>
    public static void SetPanelOpen(NodeModel node, string panel, bool defaultOpen, bool open)
    {
        if (defaultOpen)
        {
            if (open)
            {
                node.Ui.ClosedPanels.Remove(panel);
            }
            else
            {
                node.Ui.ClosedPanels.Add(panel);
            }
        }
        else if (open)
        {
            node.Ui.OpenPanels.Add(panel);
        }
        else
        {
            node.Ui.OpenPanels.Remove(panel);
        }

        node.Ui.NotifyPanelsChanged();
    }

    /// <summary>
    /// True when the port is left off the node: never for wired or pinned ports; otherwise
    /// when the user hid it, when "hide unused" is on (optional inputs and all outputs), or
    /// for the optional document-context input unless the node explicitly shows everything.
    /// </summary>
    public static bool IsHidden(NodeModel node, PortModel port, bool connected, bool hideUnusedDefault = false)
    {
        if (connected || port.HasUserValue)
        {
            return false;
        }

        if (port.IsHidden)
        {
            return true;
        }

        // A node the user never touched follows the preference; an explicit choice always wins.
        var hideUnused = node.Ui.HideUnused ?? (hideUnusedDefault ? true : (bool?)null);
        if (port.Direction == PortDirection.Output)
        {
            return hideUnused == true;
        }

        if (hideUnused == true && port.HasDefault)
        {
            return true;
        }

        return hideUnused != false && port.HasDefault && PortKinds.FromPort(port).Family == PortFamily.Document;
    }
}
