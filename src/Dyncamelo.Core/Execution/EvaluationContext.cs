using System;
using System.Collections.Generic;
using System.Threading;

namespace Dyncamelo.Core.Execution;

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

    /// <summary>Creates a context.</summary>
    /// <param name="cancellationToken">Token checked by the engine between nodes.</param>
    public EvaluationContext(CancellationToken cancellationToken = default)
    {
        CancellationToken = cancellationToken;
    }

    /// <summary>Token checked by the engine between node executions.</summary>
    public CancellationToken CancellationToken { get; }

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
