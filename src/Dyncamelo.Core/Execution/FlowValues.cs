using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Graph;

namespace Dyncamelo.Core.Execution;

/// <summary>
/// What a branch that was switched off carries (see <c>Flow.When</c>). A node that receives it on any input does not run: it is
/// left idle with an explanation and passes the same "nothing here" on, so a whole branch of side-effects can be skipped
/// without anything turning red.
/// </summary>
public sealed class InactiveValue
{
    private InactiveValue()
    {
    }

    /// <summary>The one instance.</summary>
    public static InactiveValue Instance { get; } = new InactiveValue();

    /// <inheritdoc />
    public override string ToString() => "(skipped)";
}

/// <summary>
/// What a failed input looks like to a node that catches upstream errors (<see cref="Dyncamelo.Core.Loader.CatchesUpstreamErrorsAttribute"/>).
/// </summary>
public sealed class UpstreamError
{
    /// <summary>Creates the value.</summary>
    /// <param name="message">What went wrong, naming the node that failed.</param>
    public UpstreamError(string message)
    {
        Message = message ?? string.Empty;
    }

    /// <summary>What went wrong, naming the node that failed.</summary>
    public string Message { get; }

    /// <inheritdoc />
    public override string ToString() => Message;

    /// <summary>
    /// Describes the failure behind an input port: the message of each failing node on its wires, following the chain back to the
    /// node that actually raised the error.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="port">The input port whose upstream failed.</param>
    public static UpstreamError Describe(GraphModel graph, PortModel port)
    {
        var lines = new List<string>();
        var visited = new HashSet<NodeModel>();
        var wires = port.IsMultiInput
            ? graph.FindConnectionsInto(port)
            : graph.FindConnectionInto(port) is ConnectionModel single ? new List<ConnectionModel> { single } : new List<ConnectionModel>();
        foreach (var wire in wires.Where(w => !w.IsMuted))
        {
            Collect(graph, wire.SourceNode, visited, lines);
        }

        return new UpstreamError(lines.Count == 0 ? "An upstream node failed." : string.Join(" | ", lines));
    }

    private static void Collect(GraphModel graph, NodeModel node, HashSet<NodeModel> visited, List<string> lines)
    {
        if (!visited.Add(node))
        {
            return;
        }

        if (node.State == NodeState.Error)
        {
            var text = node.Messages.Where(m => m.Severity >= MessageSeverity.Error).Select(m => m.Text).FirstOrDefault()
                       ?? node.StateMessage;
            lines.Add(node.Name + ": " + text);
        }
        else if (node.FailedUpstream)
        {
            foreach (var input in node.InPorts)
            {
                var wires = input.IsMultiInput
                    ? graph.FindConnectionsInto(input)
                    : graph.FindConnectionInto(input) is ConnectionModel single ? new List<ConnectionModel> { single } : new List<ConnectionModel>();
                foreach (var wire in wires.Where(w => !w.IsMuted))
                {
                    Collect(graph, wire.SourceNode, visited, lines);
                }
            }
        }
    }
}
