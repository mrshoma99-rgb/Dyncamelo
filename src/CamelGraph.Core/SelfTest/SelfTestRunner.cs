using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;

namespace CamelGraph.Core.SelfTest;

/// <summary>What the runner needs to know about the host application.</summary>
public sealed class SelfTestHost
{
    /// <summary>Says whether at least one model is loaded. Cases that need a model are skipped when this is false.</summary>
    public Func<bool> ModelAvailable { get; set; } = () => true;

    /// <summary>The host as the host reports it ("Autodesk Navisworks Manage 2024 (API 21.0)"); used to skip edition-specific cases.</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Runs self-test cases against the live host: each case is a tiny graph of read-only nodes, built from the registry and run by the real
/// engine, so what is exercised is the same code a user's graph runs. A node that is not on <see cref="SelfTestCatalog.ReadOnlyNodes"/> is
/// refused, so a self-test can never change the model, the saved items or any file.
/// </summary>
public sealed class SelfTestRunner
{
    private readonly NodeRegistry _registry;
    private readonly Func<EvaluationContext> _contextFactory;
    private readonly SelfTestHost _host;
    private readonly ISet<string> _allowed;

    /// <summary>Creates a runner.</summary>
    /// <param name="registry">The registry that holds the host's nodes.</param>
    /// <param name="contextFactory">Makes the evaluation context the host's nodes need (the document provider).</param>
    /// <param name="host">What the runner may ask about the host.</param>
    /// <param name="allowedNodes">The nodes a case may use; the catalogue's read-only list when null (tests pass their own).</param>
    public SelfTestRunner(NodeRegistry registry, Func<EvaluationContext> contextFactory, SelfTestHost host, ISet<string>? allowedNodes = null)
    {
        _allowed = allowedNodes ?? SelfTestCatalog.ReadOnlyNodes;
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>Runs the cases in order.</summary>
    /// <param name="cases">The cases.</param>
    /// <param name="progress">Called before each case with its index, the total and its title.</param>
    /// <param name="cancelled">Polled between cases; true stops the run.</param>
    public SelfTestReport Run(IReadOnlyList<SelfTestCase> cases, Action<int, int, string>? progress = null, Func<bool>? cancelled = null)
    {
        if (cases == null)
        {
            throw new ArgumentNullException(nameof(cases));
        }

        var results = new List<SelfTestResult>();
        var stopped = false;
        bool? modelAvailable = null;
        for (var i = 0; i < cases.Count; i++)
        {
            if (cancelled != null && cancelled())
            {
                stopped = true;
                break;
            }

            var testCase = cases[i];
            progress?.Invoke(i, cases.Count, testCase.Area + ": " + testCase.Title);

            if (testCase.NeedsModel)
            {
                if (modelAvailable == null)
                {
                    modelAvailable = SafeModelAvailable();
                }

                if (modelAvailable == false)
                {
                    results.Add(new SelfTestResult(testCase, SelfTestOutcome.Skipped, "open a model first (File > Open) and run the self-test again", TimeSpan.Zero));
                    continue;
                }
            }

            if (testCase.NeedsEdition != null && _host.Description.IndexOf(testCase.NeedsEdition, StringComparison.OrdinalIgnoreCase) < 0)
            {
                results.Add(new SelfTestResult(testCase, SelfTestOutcome.Skipped, "needs Navisworks " + testCase.NeedsEdition + " (running: " +
                    (string.IsNullOrWhiteSpace(_host.Description) ? "unknown" : _host.Description) + ")", TimeSpan.Zero));
                continue;
            }

            results.Add(RunCase(testCase));
        }

        return new SelfTestReport(results, stopped);
    }

    private bool SafeModelAvailable()
    {
        try
        {
            return _host.ModelAvailable();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private SelfTestResult RunCase(SelfTestCase testCase)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            foreach (var step in testCase.Steps)
            {
                if (!_allowed.Contains(step.Node))
                {
                    return Done(testCase, watch, SelfTestOutcome.Failed, "the self-test refuses to run '" + step.Node + "': it is not on the list of nodes that only read");
                }
            }

            var graph = new GraphModel { Name = "Self-test: " + testCase.Title };
            var nodes = new List<NodeModel>();
            foreach (var step in testCase.Steps)
            {
                var definition = _registry.Definitions.FirstOrDefault(d => string.Equals(d.Name, step.Node, StringComparison.Ordinal));
                var node = definition == null ? null : _registry.CreateZeroTouchNode(definition.Id);
                if (node == null)
                {
                    return Done(testCase, watch, SelfTestOutcome.Failed, "the node '" + step.Node + "' is not in the node library (is the Navisworks node pack loaded?)");
                }

                graph.AddNode(node);
                nodes.Add(node);

                foreach (var input in step.Inputs)
                {
                    var port = node.InPorts.FirstOrDefault(p => string.Equals(p.Name, input.Key, StringComparison.Ordinal));
                    if (port == null)
                    {
                        return Done(testCase, watch, SelfTestOutcome.Failed, "'" + step.Node + "' has no input called '" + input.Key + "' (the node changed since this check was written)");
                    }

                    port.SetUserValue(input.Value);
                }
            }

            for (var i = 0; i < testCase.Steps.Count; i++)
            {
                foreach (var wire in testCase.Steps[i].Wires)
                {
                    var target = nodes[i].InPorts.FirstOrDefault(p => string.Equals(p.Name, wire.Key, StringComparison.Ordinal));
                    var source = nodes[wire.Value.Key].OutPorts.FirstOrDefault(p => string.Equals(p.Name, wire.Value.Value, StringComparison.Ordinal));
                    if (target == null || source == null)
                    {
                        return Done(testCase, watch, SelfTestOutcome.Failed, "cannot wire '" + testCase.Steps[wire.Value.Key].Node + "." + wire.Value.Value + "' into '" +
                                                                                 testCase.Steps[i].Node + "." + wire.Key + "' (a port was renamed since this check was written)");
                    }

                    var connected = graph.Connect(source, target);
                    if (!connected.Success)
                    {
                        return Done(testCase, watch, SelfTestOutcome.Failed, "cannot wire '" + source.Name + "' into '" + target.Name + "': " + connected.Message);
                    }
                }
            }

            new GraphEngine().Run(graph, _contextFactory());

            var notes = new List<string>();
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                switch (node.State)
                {
                    case NodeState.Error:
                        return Done(testCase, watch, SelfTestOutcome.Failed, "'" + testCase.Steps[i].Node + "' failed: " + FirstLine(node.StateMessage));
                    case NodeState.Idle:
                        return Done(testCase, watch, SelfTestOutcome.Failed, "'" + testCase.Steps[i].Node + "' did not run" + (node.StateMessage.Length > 0 ? ": " + FirstLine(node.StateMessage) : string.Empty));
                    case NodeState.Warning:
                        notes.Add("'" + testCase.Steps[i].Node + "' warned: " + FirstLine(node.StateMessage));
                        break;
                }
            }

            var problem = testCase.Check?.Invoke(nodes);
            if (problem != null)
            {
                return Done(testCase, watch, SelfTestOutcome.Failed, problem);
            }

            return Done(testCase, watch, SelfTestOutcome.Passed, string.Join("; ", notes));
        }
        catch (Exception ex)
        {
            return Done(testCase, watch, SelfTestOutcome.Failed, "threw " + ex.GetType().Name + ": " + FirstLine(ex.Message));
        }
    }

    private static SelfTestResult Done(SelfTestCase testCase, Stopwatch watch, SelfTestOutcome outcome, string message) =>
        new SelfTestResult(testCase, outcome, message, watch.Elapsed);

    private static string FirstLine(string text)
    {
        var line = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? string.Empty;
        return line.Length > 240 ? line.Substring(0, 240) + "…" : line;
    }
}
