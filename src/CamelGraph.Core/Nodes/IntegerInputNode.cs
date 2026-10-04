using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Player;
using Newtonsoft.Json.Linq;

namespace CamelGraph.Core.Nodes;

/// <summary>
/// A whole number typed or scrubbed by the user, with no slider range: a count, an index, a level number.
/// </summary>
public class IntegerInputNode : NodeModel, IPlayerInputNode
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "IntegerInput";

    private long _value;

    /// <summary>Creates the node with one integer output.</summary>
    public IntegerInputNode()
    {
        Name = "Integer";
        Category = "Input";
        Description = "A whole number (no slider range): a count, an index, a level.";
        AddOutput("value", typeof(long));
    }

    /// <summary>The value. Changing it dirties the node.</summary>
    public long Value
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
    public PortModel CreatePlayerPort() =>
        PlayerPorts.Create(this, Name, typeof(long), Value, v => Value = (long)System.Math.Round(PlayerPorts.ToDouble(v)));

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override System.Collections.Generic.IReadOnlyList<string> SearchTags { get; } = new[] { "int", "whole", "count", "index", "literal" };

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
        Value = data.Value<long?>("Value") ?? 0;
    }
}
