using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Nodes;

namespace CamelGraph.Core.Execution;

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

    // Per-node evaluation time for the current run, summed across loop iterations.
    // Reset at the start of every Run; consumed into the RunResult at the end.
    private readonly Dictionary<NodeModel, TimingAccumulator> _timings =
        new Dictionary<NodeModel, TimingAccumulator>();

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
        return RunCore(graph, context, null);
    }

    /// <summary>
    /// Like <see cref="Run"/>, but only executes the dirty nodes that the <paramref name="targets"/> depend on (and the
    /// targets themselves). Everything else stays exactly as it was — still dirty, with its previous outputs — so the next
    /// ordinary run picks it up.
    /// </summary>
    /// <param name="graph">The graph to run.</param>
    /// <param name="targets">The nodes to bring up to date.</param>
    /// <param name="context">Ambient services and cancellation. Optional.</param>
    /// <returns>A summary of what executed.</returns>
    public RunResult RunUpTo(GraphModel graph, IEnumerable<NodeModel> targets, EvaluationContext? context = null)
    {
        if (graph == null)
        {
            throw new ArgumentNullException(nameof(graph));
        }

        if (targets == null)
        {
            throw new ArgumentNullException(nameof(targets));
        }

        var scope = new HashSet<NodeModel>(CamelGraph.Core.Editing.GraphOps.Upstream(graph, targets.Where(t => t.Graph == graph)));
        return RunCore(graph, context, scope);
    }

    private RunResult RunCore(GraphModel graph, EvaluationContext? context, HashSet<NodeModel>? scope)
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
        _timings.Clear();
        var stopwatch = Stopwatch.StartNew();
        var executed = new List<NodeModel>();
        bool cancelled = false;
        int planned = 0;
        context = context ?? new EvaluationContext();

        try
        {
            var plan = LoopPlanner.Plan(graph);
            var frozen = CollectFrozenSet(graph);
            var units = OrderUnits(graph, plan);

            // Nodes that read live host state run every time (without telling the editor the graph was edited).
            var liveOnly = MarkLiveNodes(graph, plan, frozen, scope);
            planned = units.Count(unit => WillRun(unit, frozen, scope));
            var done = 0;

            try
            {
                for (var unitIndex = 0; unitIndex < units.Count; unitIndex++)
                {
                    var unit = units[unitIndex];
                    if (!WillRun(unit, frozen, scope))
                    {
                        continue;
                    }

                    // Stop here (not mid-node) when a cancel was requested. Already-executed nodes are clean and the rest
                    // stay dirty, so the next run resumes exactly where this one stopped.
                    context.ReportProgress(done, planned, UnitName(unit));
                    context.Checkpoint();

                    if (unit is LoopRegion region)
                    {
                        ExecuteLoop(graph, region, context, executed);
                        done++;
                        continue;
                    }

                    var node = (NodeModel)unit;
                    var before = liveOnly.Contains(node) ? node.OutPorts.Select(p => p.Value).ToArray() : null;
                    ExecuteNode(graph, node, context);
                    ApplyPlanProblem(plan, node);
                    node.IsDirty = false;
                    executed.Add(node);
                    NodeExecuted?.Invoke(this, new NodeEventArgs(node));
                    done++;

                    if (before != null && !SameValues(before, node))
                    {
                        // A live-state node produced something new: what is wired after it has to run again too.
                        foreach (var affected in graph.CollectDownstream(node))
                        {
                            affected.IsDirty = true;
                        }

                        node.IsDirty = false;
                        var remaining = 0;
                        for (var next = unitIndex + 1; next < units.Count; next++)
                        {
                            if (WillRun(units[next], frozen, scope))
                            {
                                remaining++;
                            }
                        }

                        planned = done + remaining;
                    }
                }
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                // The node (or loop) that was interrupted never reached "clean": it stays dirty with its old outputs.
                cancelled = true;
            }
        }
        finally
        {
            _isRunning = false;
        }

        stopwatch.Stop();
        return new RunResult(executed, cancelled, stopwatch.Elapsed, BuildTimings(), planned);
    }

    private List<NodeTiming> BuildTimings()
    {
        var timings = new List<NodeTiming>(_timings.Count);
        foreach (var pair in _timings)
        {
            timings.Add(new NodeTiming(pair.Key, pair.Value.Elapsed, pair.Value.Count));
        }

        return timings;
    }

    /// <summary>
    /// Makes every node that reads live host state due to run, whatever its inputs did. A live node that nothing else made
    /// dirty is returned: the engine runs it and re-runs what is wired after it only when its output turned out different.
    /// One inside a loop is not compared (the loop runs as a whole), so what is wired after it is simply made due as well.
    /// Nothing here raises the graph's Modified event, so Auto-run is not set off by it.
    /// </summary>
    private static HashSet<NodeModel> MarkLiveNodes(GraphModel graph, LoopPlan plan, HashSet<NodeModel> frozen, HashSet<NodeModel>? scope)
    {
        var liveOnly = new HashSet<NodeModel>();
        foreach (var node in graph.Nodes)
        {
            if (!node.IsLiveState || node.IsMuted || frozen.Contains(node) || (scope != null && !scope.Contains(node)))
            {
                continue;
            }

            if (plan.NodeToRegion.ContainsKey(node))
            {
                foreach (var affected in graph.CollectDownstream(node))
                {
                    affected.IsDirty = true;
                }

                continue;
            }

            if (!node.IsDirty)
            {
                node.IsDirty = true;
                liveOnly.Add(node);
            }
        }

        return liveOnly;
    }

    /// <summary>True when the outputs a node has now are the same values as <paramref name="before"/> (lists compared element by element).</summary>
    private static bool SameValues(object?[] before, NodeModel node)
    {
        if (before.Length != node.OutPorts.Count)
        {
            return false;
        }

        for (var i = 0; i < before.Length; i++)
        {
            if (!SameValue(before[i], node.OutPorts[i].Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameValue(object? a, object? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a == null || b == null || a is string || b is string)
        {
            return a != null && b != null && a.Equals(b);
        }

        if (a is IList left && b is IList right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (var i = 0; i < left.Count; i++)
            {
                if (!SameValue(left[i], right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return a.Equals(b);
    }

    private static bool WillRun(object unit, HashSet<NodeModel> frozen, HashSet<NodeModel>? scope)
    {
        if (unit is LoopRegion region)
        {
            return !region.AllNodes().Any(n => frozen.Contains(n)) && region.AllNodes().Any(n => n.IsDirty) &&
                   (scope == null || region.AllNodes().Any(scope.Contains));
        }

        var node = (NodeModel)unit;
        return !frozen.Contains(node) && node.IsDirty && (scope == null || scope.Contains(node));
    }

    private static string UnitName(object unit) =>
        unit is LoopRegion region ? region.Item.Name + " (loop)" : ((NodeModel)unit).Name;

    private void RecordTiming(NodeModel node, TimeSpan elapsed)
    {
        if (_timings.TryGetValue(node, out var accumulator))
        {
            accumulator.Add(elapsed);
        }
        else
        {
            _timings[node] = new TimingAccumulator(elapsed);
        }
    }

    /// <summary>Mutable per-node time+count accumulator (summed across loop iterations).</summary>
    private sealed class TimingAccumulator
    {
        public TimingAccumulator(TimeSpan elapsed)
        {
            Elapsed = elapsed;
            Count = 1;
        }

        public TimeSpan Elapsed { get; private set; }

        public int Count { get; private set; }

        public void Add(TimeSpan elapsed)
        {
            Elapsed += elapsed;
            Count++;
        }
    }

    private void ExecuteNode(GraphModel graph, NodeModel node, EvaluationContext context)
    {
        node.ClearMessages();

        node.FailedUpstream = false;
        var inputs = new object?[node.InPorts.Count];
        var missingInputs = new List<string>();
        var failedPorts = new List<int>();
        bool upstreamFailed = false;

        for (int i = 0; i < node.InPorts.Count; i++)
        {
            var port = node.InPorts[i];
            var wired = MultiInput.Gather(graph, port, out var wiredValue, out var failed, out var wireMuted);
            if (failed)
            {
                upstreamFailed = true;
                failedPorts.Add(i);
            }

            // A muted wire is invisible to evaluation: fall through to the
            // pinned value / default exactly as if it were unconnected.
            // (Connect() switched UsingDefaultValue off, so a muted wire
            // must be allowed to use the default explicitly.)
            if (wired)
            {
                inputs[i] = wiredValue;
            }
            else if (port.HasUserValue)
            {
                // An inline value pinned by the editor (e.g. a choice dropdown)
                // wins over the compile-time default on an unconnected port.
                inputs[i] = port.UserValue;
            }
            else if (port.HasDefault && (port.UsingDefaultValue || wireMuted))
            {
                inputs[i] = port.DefaultValue;
            }
            else
            {
                missingInputs.Add(port.Name);
            }
        }

        if (upstreamFailed && node.CatchesUpstreamErrors)
        {
            // The node asked to see the failure instead of being stopped by it (Flow.Try).
            foreach (var index in failedPorts)
            {
                inputs[index] = UpstreamError.Describe(graph, node.InPorts[index]);
            }
        }
        else if (upstreamFailed)
        {
            SetOutputs(node, null);
            node.FailedUpstream = true;
            node.AddMessage(MessageSeverity.Warning, "Upstream failure: one or more input nodes are in an error state.");
            node.State = NodeState.Warning;
            node.OnNotRun();
            return;
        }

        if (!node.IsMuted && !node.AcceptsInactiveInputs && inputs.Any(value => value is InactiveValue))
        {
            // A branch that was switched off (Flow.When): nothing to do here, and nothing for the nodes after it to do either.
            SetOutputs(node, Enumerable.Repeat<object?>(InactiveValue.Instance, node.OutPorts.Count).ToArray());
            node.AddMessage(MessageSeverity.Info, "Skipped: an input comes from a branch that was switched off (Flow.When was false).");
            node.State = NodeState.Idle;
            node.OnNotRun();
            return;
        }

        if (node.IsMuted)
        {
            // Bypass: no execution, outputs pass through compatible inputs.
            SetOutputs(node, MutePassThrough.Resolve(node, inputs));
            node.State = NodeState.Executed;
            node.OnNotRun();
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
            node.OnNotRun();
            return;
        }

        // Time only the evaluation (the node's real work — a Navisworks call, a
        // file write, ...), not the cheap input gathering above; accumulate so a
        // loop-body node's per-item cost sums into one figure for profiling.
        var evalWatch = Stopwatch.StartNew();
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
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // Not a failure: the user stopped the run. The node keeps its previous outputs and stays dirty.
            node.State = NodeState.Idle;
            throw;
        }
        catch (OutOfMemoryException)
        {
            // A node asked for more memory than there is (a list of billions of items). It is that node's failure, not the
            // run's: whatever it held is unreachable now, so give the memory back and let the rest of the graph run.
            SetOutputs(node, null);
            node.AddMessage(
                MessageSeverity.Error,
                "Ran out of memory. The result would be too large for the memory that is available; use a smaller size, count or step, or fewer items, and run again.");
            node.State = NodeState.Error;
            node.OnNotRun();
            GC.Collect();
        }
        catch (Exception ex) when (!(ex is StackOverflowException))
        {
            // The single place where node exceptions are absorbed (§4).
            SetOutputs(node, null);
            node.AddMessage(MessageSeverity.Error, ex.Message);
            node.State = NodeState.Error;
            node.OnNotRun();
        }
        finally
        {
            evalWatch.Stop();
            RecordTiming(node, evalWatch.Elapsed);
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
    /// Runs one loop region: reads the item boundary's list, then re-executes the
    /// body in topological order once per item — binding the current item and
    /// collecting the collect boundary's 'value' input each pass — and finally
    /// publishes the collected results on the collect boundary's output. A source that
    /// failed or was switched off (Flow.When) stops the whole region the way it stops any other node.
    /// </summary>
    private void ExecuteLoop(GraphModel graph, LoopRegion region, EvaluationContext context, List<NodeModel> executed)
    {
        var item = region.Item;
        var collect = region.Collect;
        item.ClearMessages();
        collect.ClearMessages();

        var source = ReadPortValue(graph, item.InPorts[0], out var sourceFailed);
        if (sourceFailed)
        {
            StopRegion(region, failed: true);
        }
        else if (source is InactiveValue)
        {
            StopRegion(region, failed: false);
        }
        else
        {
            RunIterations(graph, region, MaterializeItems(source), context);
        }

        foreach (var node in region.AllNodes())
        {
            node.IsDirty = false;
            executed.Add(node);
            NodeExecuted?.Invoke(this, new NodeEventArgs(node));
        }
    }

    private void RunIterations(GraphModel graph, LoopRegion region, List<object?> items, EvaluationContext context)
    {
        var item = region.Item;
        var collect = region.Collect;
        var valuePort = collect.InPorts[1];
        var results = new List<object?>(items.Count);

        // Failures are counted over the whole loop: only the last pass leaves its state on a node, so without this a
        // bad item in the middle of 200 would be invisible.
        var failedPasses = 0;
        string? firstFailure = null;
        var failedNodes = new Dictionary<NodeModel, LoopFailure>();

        for (int index = 0; index < items.Count; index++)
        {
            // A cancel stops the whole loop: partial results are not published and the region stays dirty.
            context.Checkpoint();

            item.BindIteration(items[index], index, items.Count);
            SetOutputs(item, new object?[] { items[index], index, items.Count, item });
            item.FailedUpstream = false;
            item.State = NodeState.Executed;

            // Force each body node to re-run this iteration (bypass the dirty cache).
            foreach (var body in region.Body)
            {
                ExecuteNode(graph, body, context);
            }

            var passFailed = false;
            foreach (var body in region.Body)
            {
                if (body.State != NodeState.Error)
                {
                    continue;
                }

                passFailed = true;
                var text = UpstreamError.Prefixed(body.Name, body.Messages.FirstOrDefault(m => m.Severity >= MessageSeverity.Error)?.Text ?? "it failed");
                var failure = "item " + (index + 1).ToString(CultureInfo.InvariantCulture) + ": " + text;
                firstFailure = firstFailure ?? failure;
                if (failedNodes.TryGetValue(body, out var known))
                {
                    known.Count++;
                }
                else
                {
                    failedNodes[body] = new LoopFailure(failure);
                }
            }

            var value = ReadPortValue(graph, valuePort, out var valueFailed);
            if (valueFailed && !passFailed)
            {
                passFailed = true;
                firstFailure = firstFailure ?? "item " + (index + 1).ToString(CultureInfo.InvariantCulture) + ": " + UpstreamError.Describe(graph, valuePort).Message;
            }

            if (passFailed)
            {
                failedPasses++;
            }

            // A pass a Flow.When switched off contributes nothing (it is not a value, and not a failure either).
            if (!(value is InactiveValue))
            {
                results.Add(value);
            }
        }

        var total = items.Count.ToString(CultureInfo.InvariantCulture);
        foreach (var pair in failedNodes)
        {
            // The node's own state is the last pass's: keep an earlier failure from looking green.
            if (pair.Key.State != NodeState.Error)
            {
                pair.Key.State = NodeState.Warning;
            }

            pair.Key.AddMessage(
                MessageSeverity.Warning,
                "Failed in " + pair.Value.Count.ToString(CultureInfo.InvariantCulture) + " of " + total + " iterations. First: " + pair.Value.First);
        }

        SetOutputs(collect, new object?[] { results });
        collect.FailedUpstream = false;
        collect.State = NodeState.Executed;
        if (failedPasses > 0)
        {
            collect.AddMessage(
                MessageSeverity.Warning,
                failedPasses.ToString(CultureInfo.InvariantCulture) + " of " + total + " iterations failed. First: " + firstFailure);
            collect.State = NodeState.Warning;
        }
    }

    /// <summary>How often a body node failed across the passes of a loop, and the first time.</summary>
    private sealed class LoopFailure
    {
        public LoopFailure(string first)
        {
            First = first;
            Count = 1;
        }

        public string First { get; }

        public int Count { get; set; }
    }

    /// <summary>
    /// The loop did not run: its source failed (nothing may run on it, exactly like a node after a failure) or it was switched
    /// off by a Flow.When (idle, passing the "nothing here" on). Every node of the region says so and hands the same state on.
    /// </summary>
    private static void StopRegion(LoopRegion region, bool failed)
    {
        foreach (var node in region.AllNodes())
        {
            node.ClearMessages();
            if (failed)
            {
                SetOutputs(node, null);
                node.FailedUpstream = true;
                node.AddMessage(MessageSeverity.Warning, "Upstream failure: one or more input nodes are in an error state.");
                node.State = NodeState.Warning;
            }
            else
            {
                SetOutputs(node, Enumerable.Repeat<object?>(InactiveValue.Instance, node.OutPorts.Count).ToArray());
                node.FailedUpstream = false;
                node.AddMessage(MessageSeverity.Info, "Skipped: an input comes from a branch that was switched off (Flow.When was false).");
                node.State = NodeState.Idle;
            }

            node.OnNotRun();
        }
    }

    /// <summary>Reads one input port's current value: the wired source value, else its default, else null.</summary>
    private static object? ReadPortValue(GraphModel graph, PortModel port, out bool upstreamFailed)
    {
        var wired = MultiInput.Gather(graph, port, out var wiredValue, out upstreamFailed, out var mutedOnly);
        if (wired)
        {
            return wiredValue;
        }

        if (port.HasUserValue)
        {
            return port.UserValue;
        }

        return port.HasDefault && (port.UsingDefaultValue || mutedOnly) ? port.DefaultValue : null;
    }

    /// <summary>Materializes a loop's item source into an indexable list (a scalar becomes a single item).</summary>
    private static List<object?> MaterializeItems(object? value)
    {
        var list = new List<object?>();
        if (value == null)
        {
            return list;
        }

        if (value is IEnumerable enumerable && !(value is string))
        {
            foreach (var element in enumerable)
            {
                list.Add(element);
            }

            return list;
        }

        list.Add(value);
        return list;
    }

    /// <summary>After a boundary node executes, surfaces any structural loop problem as a warning on it.</summary>
    private static void ApplyPlanProblem(LoopPlan plan, NodeModel node)
    {
        if (plan.Problems.Count == 0)
        {
            return;
        }

        foreach (var problem in plan.Problems)
        {
            if (problem.Key == node)
            {
                node.AddMessage(MessageSeverity.Warning, problem.Value);
                node.State = NodeState.Warning;
            }
        }
    }

    /// <summary>
    /// Orders execution units — standalone nodes and whole loop regions — in
    /// dependency order (Kahn's algorithm). Ties between independent units are
    /// broken by creation index: arbitrary but stable, the same policy as
    /// Grasshopper's document order and Dynamo's unspecified branch order.
    /// Users who need a specific side-effect order express it as a data
    /// dependency (chain pass-through outputs, or Flow.Then). A region
    /// collapses to a single unit so its external inputs run before it and its
    /// results feed downstream after it. Reduces to a plain node topological
    /// sort when the graph has no loops.
    /// </summary>
    private static List<object> OrderUnits(GraphModel graph, LoopPlan plan)
    {
        var unitOfNode = new Dictionary<NodeModel, object>();
        foreach (var node in graph.Nodes)
        {
            unitOfNode[node] = plan.NodeToRegion.TryGetValue(node, out var region) ? (object)region : node;
        }

        var units = unitOfNode.Values.Distinct().ToList();
        var inDegree = units.ToDictionary(u => u, _ => 0);
        var edges = new HashSet<(object From, object To)>();
        foreach (var connection in graph.Connections)
        {
            var from = unitOfNode[connection.SourceNode];
            var to = unitOfNode[connection.TargetNode];
            if (!ReferenceEquals(from, to) && edges.Add((from, to)))
            {
                inDegree[to]++;
            }
        }

        var ready = units.Where(u => inDegree[u] == 0).ToList();
        var order = new List<object>(units.Count);
        while (ready.Count > 0)
        {
            var next = ready[0];
            foreach (var candidate in ready)
            {
                if (UnitIndex(candidate) < UnitIndex(next))
                {
                    next = candidate;
                }
            }

            ready.Remove(next);
            order.Add(next);

            foreach (var edge in edges)
            {
                if (ReferenceEquals(edge.From, next) && --inDegree[edge.To] == 0)
                {
                    ready.Add(edge.To);
                }
            }
        }

        if (order.Count != units.Count)
        {
            // Unreachable when all mutations go through GraphModel (cycles are
            // rejected at connect time); guards against hand-built graphs.
            throw new InvalidOperationException("The graph contains a cycle and cannot be executed.");
        }

        return order;
    }

    private static int UnitIndex(object unit) =>
        unit is LoopRegion region ? region.MinCreationIndex : ((NodeModel)unit).CreationIndex;

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
