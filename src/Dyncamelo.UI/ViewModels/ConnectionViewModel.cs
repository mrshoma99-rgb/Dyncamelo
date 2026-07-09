using Dyncamelo.Core.Graph;
using Dyncamelo.UI.Mvvm;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// Wraps one <see cref="ConnectionModel"/> wire. The Nodify connection shape
/// binds its endpoints to <c>Source.Anchor</c>/<c>Target.Anchor</c>.
/// </summary>
public class ConnectionViewModel : ObservableObject
{
    /// <summary>Creates the wrapper.</summary>
    /// <param name="model">The wrapped Core connection.</param>
    /// <param name="source">The upstream (output) connector.</param>
    /// <param name="target">The downstream (input) connector.</param>
    public ConnectionViewModel(ConnectionModel model, ConnectorViewModel source, ConnectorViewModel target)
    {
        Model = model;
        Source = source;
        Target = target;
    }

    /// <summary>The wrapped Core connection.</summary>
    public ConnectionModel Model { get; }

    /// <summary>The upstream (output) connector.</summary>
    public ConnectorViewModel Source { get; }

    /// <summary>The downstream (input) connector.</summary>
    public ConnectorViewModel Target { get; }
}
