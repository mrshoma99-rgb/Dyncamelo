namespace Dyncamelo.Core.Graph;

/// <summary>
/// Direction of a <see cref="PortModel"/>.
/// </summary>
public enum PortDirection
{
    /// <summary>The port consumes a value (left side of a node).</summary>
    Input = 0,

    /// <summary>The port produces a value (right side of a node).</summary>
    Output = 1,
}
