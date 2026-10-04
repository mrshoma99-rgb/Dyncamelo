using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Types;

namespace CamelGraph.Core.Execution;

/// <summary>
/// How the wires of a multi-input port (<see cref="PortModel.IsMultiInput"/>) become the value the node receives.
/// </summary>
public static class MultiInput
{
    /// <summary>
    /// Combines the values of several wires, in wire order, into one list: a list-valued wire contributes its elements,
    /// any other value contributes itself, and a wire that carries nothing (null, or a branch that was switched off) contributes nothing.
    /// A dictionary or string is a single value, not a list.
    /// </summary>
    /// <param name="values">One value per wire, in the order the wires were made.</param>
    public static List<object?> Combine(IEnumerable<object?> values)
    {
        var combined = new List<object?>();
        foreach (var value in values)
        {
            if (value == null || value is InactiveValue)
            {
                continue;
            }

            if (value is IEnumerable enumerable && TypeCoercion.IsListType(value.GetType()))
            {
                foreach (var element in enumerable)
                {
                    combined.Add(element);
                }
            }
            else
            {
                combined.Add(value);
            }
        }

        return combined;
    }

    /// <summary>
    /// Reads what reaches an input port through its (unmuted) wires. Zero wires: not connected. One wire: that wire's
    /// value unchanged, exactly as for an ordinary input, so making a port multi-input never changes an existing graph.
    /// Two or more (multi-input ports only): <see cref="Combine"/>d.
    /// </summary>
    /// <param name="graph">The graph the port belongs to.</param>
    /// <param name="port">An input port.</param>
    /// <param name="value">The value arriving through the wires.</param>
    /// <param name="upstreamFailed">True when a node feeding an unmuted wire is in the error state.</param>
    /// <param name="mutedOnly">True when the port has wires but every one is muted.</param>
    /// <returns>True when at least one unmuted wire feeds the port.</returns>
    public static bool Gather(GraphModel graph, PortModel port, out object? value, out bool upstreamFailed, out bool mutedOnly)
    {
        value = null;
        upstreamFailed = false;
        mutedOnly = false;

        var wires = port.IsMultiInput
            ? graph.FindConnectionsInto(port)
            : graph.FindConnectionInto(port) is ConnectionModel single ? new List<ConnectionModel> { single } : new List<ConnectionModel>();
        if (wires.Count == 0)
        {
            return false;
        }

        var active = wires.Where(w => !w.IsMuted).ToList();
        if (active.Count == 0)
        {
            mutedOnly = true;
            return false;
        }

        upstreamFailed = active.Any(w => w.SourceNode.State == NodeState.Error || w.SourceNode.FailedUpstream);
        if (active.Count == 1)
        {
            value = active[0].Source.Value;
        }
        else if (active.All(w => w.Source.Value is InactiveValue))
        {
            // Every branch feeding the socket is switched off: the socket is off too.
            value = InactiveValue.Instance;
        }
        else
        {
            value = Combine(active.Select(w => w.Source.Value));
        }

        return true;
    }
}
