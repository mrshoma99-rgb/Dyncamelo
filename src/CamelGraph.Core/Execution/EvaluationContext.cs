using System;
using System.Collections.Generic;
using System.Threading;

namespace CamelGraph.Core.Execution;

/// <summary>
/// Ambient state for one graph run: a tiny service container (the host app
/// registers things like a Navisworks document provider here) plus the
/// cancellation token checked between nodes. Nodes must obtain host services
/// exclusively through this context — never through statics — so node packs
/// stay testable and hosts stay swappable.
/// </summary>
public class EvaluationContext
{
    private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
    private readonly List<string> _scope = new List<string>();

    /// <summary>Creates a context.</summary>
    /// <param name="cancellationToken">Token checked by the engine between nodes.</param>
    public EvaluationContext(CancellationToken cancellationToken = default)
    {
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// Token checked by the engine between node executions and between the calls of a replicated node. Cancelling it stops
    /// the run at the next such point; whatever was interrupted stays dirty, so the next run resumes there.
    /// </summary>
    public CancellationToken CancellationToken { get; private set; }

    /// <summary>
    /// Called by the engine before every node, every replicated call and every loop pass — the points where a run can be
    /// stopped. A host uses it to poll for a cancel request (and to repaint) while the thread is otherwise blocked; it may
    /// cancel the token from inside. Keep it cheap: it runs very often.
    /// </summary>
    public Action? Heartbeat { get; set; }

    /// <summary>Called before each node executes with how far the run is. Also receives the nodes of nested groups.</summary>
    public Action<RunProgress>? ProgressCallback { get; set; }

    /// <summary>Names of the node groups currently being run, outermost first; empty at the top level.</summary>
    public IReadOnlyList<string> Scope => _scope;

    /// <summary>Replaces the cancellation token (the host creates the context before it knows the run's token).</summary>
    /// <param name="token">The token for this run.</param>
    public void UseCancellation(CancellationToken token)
    {
        CancellationToken = token;
    }

    /// <summary>Runs the heartbeat and then stops the run when it has been cancelled.</summary>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public void Checkpoint()
    {
        Heartbeat?.Invoke();
        CancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Tells the host which node is about to run.</summary>
    /// <param name="completed">Units finished so far in the current graph.</param>
    /// <param name="total">Units the current graph will run.</param>
    /// <param name="node">The node about to run.</param>
    public void ReportProgress(int completed, int total, string node)
    {
        ProgressCallback?.Invoke(new RunProgress(completed, total, node, _scope.ToArray()));
    }

    /// <summary>Enters a node group while it is being evaluated; dispose to leave it.</summary>
    /// <param name="name">The group's name.</param>
    public IDisposable EnterScope(string name)
    {
        _scope.Add(name);
        return new ScopeExit(this);
    }

    private sealed class ScopeExit : IDisposable
    {
        private EvaluationContext? _owner;

        public ScopeExit(EvaluationContext owner) => _owner = owner;

        public void Dispose()
        {
            if (_owner != null && _owner._scope.Count > 0)
            {
                _owner._scope.RemoveAt(_owner._scope.Count - 1);
            }

            _owner = null;
        }
    }

    /// <summary>Registers (or replaces) a service instance under type <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">Service contract type used for lookup.</typeparam>
    /// <param name="service">The instance to register.</param>
    public void RegisterService<T>(T service) where T : class
    {
        if (service == null)
        {
            throw new ArgumentNullException(nameof(service));
        }

        _services[typeof(T)] = service;
    }

    /// <summary>Returns the registered service, or null when absent.</summary>
    /// <typeparam name="T">Service contract type.</typeparam>
    public T? GetService<T>() where T : class
    {
        return _services.TryGetValue(typeof(T), out var service) ? (T)service : null;
    }

    /// <summary>Returns the registered service or throws.</summary>
    /// <typeparam name="T">Service contract type.</typeparam>
    /// <exception cref="InvalidOperationException">No service of the requested type is registered.</exception>
    public T GetRequiredService<T>() where T : class
    {
        return GetService<T>() ?? throw new InvalidOperationException(
            "No service of type '" + typeof(T).FullName + "' is registered on the evaluation context.");
    }
}
