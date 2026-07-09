using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Types;

namespace Dyncamelo.Core.Nodes;

/// <summary>
/// Displays whatever flows into it and passes the value through unchanged.
/// The input is declared as <see cref="object"/>, so lists arrive whole
/// (no replication) and <see cref="FormattedValue"/> shows the full structure.
/// </summary>
public class WatchNode : NodeModel
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "Watch";

    private string _formattedValue = string.Empty;

    /// <summary>Creates the node with an object input and a pass-through output.</summary>
    public WatchNode()
    {
        Name = "Watch";
        Category = "Display";
        Description = "Displays the incoming value.";
        AddInput("value", typeof(object), "The value to display.");
        AddOutput("value", typeof(object), "The incoming value, passed through.");
    }

    /// <summary>Human-readable rendering of the last observed value (invariant culture).</summary>
    public string FormattedValue
    {
        get => _formattedValue;
        private set => SetField(ref _formattedValue, value);
    }

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        var value = inputs.Length > 0 ? inputs[0] : null;
        FormattedValue = TypeCoercion.FormatValue(value);
        return new object?[] { value };
    }
}
