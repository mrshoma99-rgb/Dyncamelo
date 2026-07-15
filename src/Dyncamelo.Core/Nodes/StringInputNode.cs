using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Newtonsoft.Json.Linq;

namespace Dyncamelo.Core.Nodes;

/// <summary>
/// A literal string typed by the user.
/// </summary>
public class StringInputNode : NodeModel
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "StringInput";

    private string _value = string.Empty;

    /// <summary>Creates the node with one string output.</summary>
    public StringInputNode()
    {
        Name = "String";
        Category = "Input";
        Description = "A string literal.";
        AddOutput("value", typeof(string));
    }

    /// <summary>The literal value. Changing it dirties the node.</summary>
    public string Value
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
        Value = data.Value<string>("Value") ?? string.Empty;
    }
}
