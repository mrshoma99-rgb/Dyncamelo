using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Newtonsoft.Json.Linq;

namespace Dyncamelo.Core.Nodes;

/// <summary>
/// A true/false toggle.
/// </summary>
public class BooleanToggleNode : NodeModel
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "BooleanToggle";

    private bool _value;

    /// <summary>Creates the node with one boolean output.</summary>
    public BooleanToggleNode()
    {
        Name = "Boolean";
        Category = "Input";
        Description = "A true/false toggle.";
        AddOutput("value", typeof(bool));
    }

    /// <summary>The toggle state. Changing it dirties the node.</summary>
    public bool Value
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
    public override NodeFunction Function => NodeFunction.Create;

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
        Value = data.Value<bool?>("Value") ?? false;
    }
}
