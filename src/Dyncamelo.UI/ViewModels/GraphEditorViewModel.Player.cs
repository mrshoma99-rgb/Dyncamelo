using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Player;
using Dyncamelo.UI.Mvvm;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// The author's side of the Player: which nodes and inputs the script runner offers, and what the script says about itself.
/// </summary>
public partial class GraphEditorViewModel
{
    private ICommand? _togglePlayerNodeCommand;
    private ICommand? _togglePlayerInputsCommand;
    private ICommand? _describeGraphCommand;
    private ICommand? _openPlayerCommand;

    /// <summary>Asks the host to show the Script Player (the editor has no pane of its own for it).</summary>
    public event EventHandler? OpenPlayerRequested;

    /// <summary>Shows or hides the selected nodes in the Player: input nodes as fields, Watch nodes (and any node you mark) as results.</summary>
    public ICommand TogglePlayerNodeCommand => _togglePlayerNodeCommand ??= new RelayCommand(TogglePlayerNode);

    /// <summary>Offers or withdraws the unwired inputs of the selected nodes as fields of the Player's form.</summary>
    public ICommand TogglePlayerInputsCommand => _togglePlayerInputsCommand ??= new RelayCommand(TogglePlayerInputs);

    /// <summary>Asks for the text the Player shows under the script's name.</summary>
    public ICommand DescribeGraphCommand => _describeGraphCommand ??= new RelayCommand(DescribeGraph);

    /// <summary>Opens the Script Player pane.</summary>
    public ICommand OpenPlayerCommand => _openPlayerCommand ??= new RelayCommand(() => OpenPlayerRequested?.Invoke(this, EventArgs.Empty));

    private List<NodeModel> SelectedPlayerCandidates() =>
        SelectedItems.OfType<NodeViewModel>()
            .Select(n => n.Model)
            .Where(m => !(m is Dyncamelo.Core.Groups.GroupBoundNode) || m is Dyncamelo.Core.Groups.GroupInstanceNode)
            .ToList();

    private void TogglePlayerNode()
    {
        var nodes = SelectedPlayerCandidates();
        if (nodes.Count == 0)
        {
            StatusMessage = "Select the nodes to show in the Player.";
            return;
        }

        var show = !nodes.All(PlayerExposure.IsShown);
        using (_undo.Begin(show ? "Show in Player" : "Hide from Player"))
        {
            foreach (var node in nodes)
            {
                PlayerExposure.SetShown(node, show);
            }
        }

        StatusMessage = (show ? "Shown in the Player: " : "Hidden from the Player: ") + string.Join(", ", nodes.Select(n => n.Name)) + ".";
    }

    private void TogglePlayerInputs()
    {
        var ports = SelectedPlayerCandidates()
            .SelectMany(n => PlayerExposure.OfferableInputs(n, p => _graph.FindConnectionInto(p) != null))
            .ToList();
        if (ports.Count == 0)
        {
            StatusMessage = "The selected nodes have no unwired inputs with an editor to offer.";
            return;
        }

        var expose = !ports.All(p => p.PlayerExposed);
        using (_undo.Begin(expose ? "Show inputs in Player" : "Hide inputs from Player"))
        {
            foreach (var port in ports)
            {
                port.PlayerExposed = expose;
            }
        }

        StatusMessage = (expose ? "Offered in the Player: " : "Withdrawn from the Player: ") +
                        ports.Count.ToString(CultureInfo.InvariantCulture) + (ports.Count == 1 ? " input." : " inputs.");
    }

    /// <summary>Offers or withdraws one input (the socket's context menu).</summary>
    /// <param name="connector">The input socket.</param>
    public void TogglePlayerInput(ConnectorViewModel? connector)
    {
        if (connector == null || !connector.IsInput)
        {
            return;
        }

        if (connector.IsConnected)
        {
            StatusMessage = "An input with a wire is fed by the graph; only an unwired input can be a field of the Player.";
            return;
        }

        if (!PlayerExposure.CanOffer(connector.Port))
        {
            StatusMessage = "The Player has no editor for '" + connector.Title + "' (lists and objects cannot be typed in).";
            return;
        }

        using (_undo.Begin(connector.Port.PlayerExposed ? "Hide input from Player" : "Show input in Player"))
        {
            connector.Port.PlayerExposed = !connector.Port.PlayerExposed;
        }

        StatusMessage = connector.Port.PlayerExposed
            ? "'" + connector.Title + "' is now a field of the Player."
            : "'" + connector.Title + "' is no longer in the Player.";
    }

    private void DescribeGraph()
    {
        var document = DocumentGraph;
        var text = Dialogs.Prompt("What does this script do? (shown under its name in the Player)", "Script Description", document.Description);
        if (text == null)
        {
            return;
        }

        document.Description = text.Trim();
        StatusMessage = document.Description.Length == 0 ? "Removed the script description." : "Described the script.";
    }
}
