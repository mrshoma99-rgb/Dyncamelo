using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Dyncamelo.Core.Graph;

namespace Dyncamelo.Core.Execution;

/// <summary>
/// Synchronous dataflow interpreter. <see cref="Run"/> executes on the caller's
/// thread (for the Navisworks add-in that is the host main thread — see engine
/// documentation §6); the engine never spawns threads and never marshals.
/// Only dirty, non-frozen nodes execute; clean nodes serve cached values.
/// Node failures are isolated: they mark the node and null its outputs but never
/// abort the run.
/// </summary>
public class GraphEngine
{
    private bool _isRunning;

    /// <summary>Raised after each node finishes executing (success or failure).</summary>
    public event EventHandler<NodeEventArgs>? NodeExecuted;

    /// <summary>True while a run is in progress on some thread.</summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Executes the dirty subgraph of <paramref name="graph"/> in topological order.
    /// </summary>
    /// <param name="graph">The graph to run.</param>
    /// <param name="context">Ambient services and cancellation. Optional; an empty context is used when null.</param>
    /// <returns>A summary of what executed.</returns>
    /// <exception cref="InvalidOperationException">A run is already in progress (reentrancy guard).</exception>
    public RunResult Run(GraphModel graph, EvaluationContext? context = null)
    {
        if (graph == null)
        {
            throw new ArgumentNullException(nameof(graph));
        }

        if (_isRunning)
        {
            throw new InvalidOperationException("The engine is already running; reentrant Run() calls are not allowed.");
        }

        _isRunning = true;
        var stopwatch = Stopwatch.StartNew();
        var executed = new List<NodeModel>();
        bool cancelled = false;
        context = context ?? new EvaluationContext();

        try
        {
            var order = TopologicalSort(graph);
            var frozen = CollectFrozenSet(graph);

            foreach (var node in order)
            {
                if (context.CancellationToken.IsCancellationRequested)
                {
                    // Already-executed nodes are clean; the rest stay dirty, so the
                    // next run resumes exactly where this one stopped.
                    cancelled = true;
                    break;
                }

                if (frozen.Contains(node) || !node.IsDirty)
                {
                    continue;
                }

                ExecuteNode(graph, node, context);
                node.IsDirty = false;
                executed.Add(node);
                NodeExecuted?.Invoke(this, new NodeEventArgs(node));
            }
        }
        finally
        {
            _isRunning = false;
        }

        stopwatch.Stop();
        return new RunResult(executed, cancelled, stopwatch.Elapsed);
    }

    private void ExecuteNode(GraphModel graph, NodeModel node, EvaluationContext context)
    {
        node.ClearMessages();

        var inputs = new object?[node.InPorts.Count];
        var missingInputs = new List<string>();
        bool upstreamFailed = false;

        for (int i = 0; i < node.InPorts.Count; i++)
        {
            var port = node.InPorts[i];
            var connection = graph.FindConnectionInto(port);
            if (connection != null)
            {
                if (connection.SourceNode.State == NodeState.Error)
                {
                    upstreamFailed = true;
                }

                inputs[i] = connection.Source.Value;
            }
            else if (port.HasDefault && port.UsingDefaultValue)
            {
                inputs[i] = port.DefaultValue;
            }
            else
            {
                missingInputs.Add(port.Name);
            }
        }

        if (upstreamFailed)
        {
            SetOutputs(node, null);
            node.AddMessage(MessageSeverity.Warning, "Upstream failure: one or more input nodes are in an error state.");
            node.State = NodeState.Warning;
            return;
        }

        if (missingInputs.Count > 0)
        {
            SetOutputs(node, null);
            foreach (var name in missingInputs)
            {
                node.AddMessage(MessageSeverity.Info, "Input '" + name + "' is not connected.");
            }

            node.State = NodeState.Idle;
            return;
        }

        try
        {
            var outputs = Replicator.Execute(node, inputs, context);
            SetOutputs(node, outputs);

            // A node may report its own failure via AddMessage(Error, ...) without
            // throwing; that must surface as Error so downstream sees the failure.
            if (node.Messages.Any(m => m.Severity >= MessageSeverity.Error))
            {
                node.State = NodeState.Error;
            }
            else if (node.Messages.Any(m => m.Severity >= MessageSeverity.Warning))
            {
                node.State = NodeState.Warning;
            }
            else
            {
                node.State = NodeState.Executed;
            }
        }
        catch (Exception ex) when (!(ex is OutOfMemoryException) && !(ex is StackOverflowException))
        {
            // The single place where node exceptions are absorbed (§4).
            SetOutputs(node, null);
            node.AddMessage(MessageSeverity.Error, ex.Message);
            node.State = NodeState.Error;
        }
    }

    private static void SetOutputs(NodeModel node, object?[]? outputs)
    {
        for (int j = 0; j < node.OutPorts.Count; j++)
        {
            node.OutPorts[j].Value = outputs != null && j < outputs.Length ? outputs[j] : null;
        }
    }

    /// <summary>
    /// Kahn's algorithm over the whole graph, tie-broken by node creation index
    /// for deterministic execution order.
    /// </summary>
    private static List<NodeModel> TopologicalSort(GraphModel graph)
    {
        var inDegree = graph.Nodes.ToDictionary(n => n, _ => 0);
        foreach (var connection in graph.Connections)
        {
            inDegree[connection.TargetNode]++;
        }

        var ready = graph.Nodes.Where(n => inDegree[n] == 0).ToList();
        var order = new List<NodeModel>(graph.Nodes.Count);

        while (ready.Count > 0)
        {
            NodeModel next = ready[0];
            foreach (var candidate in ready)
            {
                if (candidate.CreationIndex < next.CreationIndex)
                {
                    next = candidate;
                }
            }

            ready.Remove(next);
            order.Add(next);

            foreach (var connection in graph.Connections)
            {
                if (connection.SourceNode == next && --inDegree[connection.TargetNode] == 0)
                {
                    ready.Add(connection.TargetNode);
                }
            }
        }

        if (order.Count != graph.Nodes.Count)
        {
            // Unreachable when all mutations go through GraphModel (cycles are
            // rejected at connect time); guards against hand-built graphs.
            throw new InvalidOperationException("The graph contains a cycle and cannot be executed.");
        }

        return order;
    }

    /// <summary>Frozen nodes plus everything transitively downstream of them.</summary>
    private static HashSet<NodeModel> CollectFrozenSet(GraphModel graph)
    {
        var frozen = new HashSet<NodeModel>();
        foreach (var node in graph.Nodes)
        {
            if (node.IsFrozen && !frozen.Contains(node))
            {
                foreach (var affected in graph.CollectDownstream(node))
                {
                    frozen.Add(affected);
                }
            }
        }

        return frozen;
    }
}
