using System;

namespace Dyncamelo.Core.Graph;

/// <summary>
/// A directed wire from one output port to one input port.
/// An input port accepts at most one connection.
/// </summary>
public class ConnectionModel
{
    /// <summary>Creates a connection. Use <see cref="GraphModel.Connect"/> instead of constructing directly.</summary>
    /// <param name="source">Source (output) port.</param>
    /// <param name="target">Target (input) port.</param>
    public ConnectionModel(PortModel source, PortModel target)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (source.Direction != PortDirection.Output)
        {
            throw new ArgumentException("Connection source must be an output port.", nameof(source));
        }

        if (target.Direction != PortDirection.Input)
        {
            throw new ArgumentException("Connection target must be an input port.", nameof(target));
        }

        Id = Guid.NewGuid();
        Source = source;
        Target = target;
    }

    /// <summary>Stable identifier of the connection.</summary>
    public Guid Id { get; internal set; }

    /// <summary>The upstream output port.</summary>
    public PortModel Source { get; }

    /// <summary>The downstream input port.</summary>
    public PortModel Target { get; }

    /// <summary>The node producing the value.</summary>
    public NodeModel SourceNode => Source.Owner;

    /// <summary>The node consuming the value.</summary>
    public NodeModel TargetNode => Target.Owner;

    /// <inheritdoc />
    public override string ToString() => Source + " -> " + Target;
}
