using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Newtonsoft.Json.Linq;

namespace Dyncamelo.Core.Nodes;

/// <summary>
/// A literal number typed by the user. UI-agnostic: the view binds to <see cref="Value"/>.
/// </summary>
public class NumberInputNode : NodeModel
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "NumberInput";

    private double _value;

    /// <summary>Creates the node with one numeric output.</summary>
    public NumberInputNode()
    {
        Name = "Number";
        Category = "Input";
        Description = "A number literal.";
        AddOutput("value", typeof(double));
    }

    /// <summary>The literal value. Changing it dirties the node.</summary>
    public double Value
    {
        get => _value;
        set
        {
            if (SetField(ref _value, value))
            {
                MarkDirty();
            }
        }
    }

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        return new object?[] { Value };
    }

    /// <inheritdoc />
    public override void SerializeData(JObject data)
    {
        data["Value"] = Value;
    }

    /// <inheritdoc />
    public override void DeserializeData(JObject data)
    {
        Value = data.Value<double?>("Value") ?? 0d;
    }
}
