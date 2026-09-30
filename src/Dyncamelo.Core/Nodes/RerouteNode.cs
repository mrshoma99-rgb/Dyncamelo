using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;

namespace Dyncamelo.Core.Nodes;

/// <summary>
/// A wire waypoint: passes its input through untouched (no coercion, no
/// replication). Purely organisational; hidden from the node library.
/// </summary>
public class RerouteNode : NodeModel
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "Reroute";

    /// <summary>Creates the reroute with one pass-through port pair.</summary>
    public RerouteNode()
    {
        Name = "Reroute";
        Category = "Utility";
        Description = "A wire waypoint that passes its value through unchanged.";
        AddInput("in", typeof(object), defaultValue: null);
        AddOutput("out", typeof(object));
    }

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override bool ShowInLibrary => false;

    /// <inheritdoc />
    public override NodeFunction Function => NodeFunction.Info;

    /// <inheritdoc />
    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        return new object?[] { inputs.Length > 0 ? inputs[0] : null };
    }
}
