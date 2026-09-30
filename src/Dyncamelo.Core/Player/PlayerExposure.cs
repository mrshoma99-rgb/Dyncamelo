using System;
using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Graph;

namespace Dyncamelo.Core.Player;

/// <summary>What the editor can tell the Player about a node: whether it appears, and how to change that.</summary>
public static class PlayerExposure
{
    /// <summary>True when the Player shows the node: as a field (an input node), as a result (a Watch node, or one marked to show).</summary>
    public static bool IsShown(NodeModel node) => ScriptSession.IsFormNode(node) || ScriptSession.IsResultNode(node);

    /// <summary>True when the node appears in the Player without being asked (input nodes and Watch nodes).</summary>
    public static bool IsShownByDefault(NodeModel node) => node is IPlayerInputNode || node is IPlayerOutputNode;

    /// <summary>
    /// Shows or hides a node in the Player. For a node that shows by default the stored choice is only kept when it hides it;
    /// for any other node it is only kept when it shows it, so the file stays free of flags that change nothing.
    /// </summary>
    public static void SetShown(NodeModel node, bool shown)
    {
        if (node == null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        if (IsShownByDefault(node))
        {
            node.PlayerExposed = shown ? (bool?)null : false;
        }
        else
        {
            node.PlayerExposed = shown ? (bool?)true : null;
        }
    }

    /// <summary>The unwired inputs of a node that have an inline editor, so the Player could offer them.</summary>
    /// <param name="node">The node.</param>
    /// <param name="isWired">Whether an input has a wire.</param>
    public static IReadOnlyList<PortModel> OfferableInputs(NodeModel node, Func<PortModel, bool> isWired) =>
        node.InPorts.Where(p => !isWired(p) && CanOffer(p)).ToList();

    /// <summary>True when an input could be a field of the form: it has an editor the Player can show.</summary>
    public static bool CanOffer(PortModel port) => PortEditors.Resolve(port) != PortEditorKind.None;

    /// <summary>True when the node takes part in the Player in any way the author chose (marked, or has exposed inputs).</summary>
    public static bool HasExplicitChoice(NodeModel node) => node.PlayerExposed == true || node.InPorts.Any(p => p.PlayerExposed);
}
