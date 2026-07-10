using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Types;
using Newtonsoft.Json.Linq;

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
    private double _viewWidth;
    private double _viewHeight;

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

    /// <summary>
    /// User-chosen width of the display area (0 = automatic). Pure view state:
    /// changing it never dirties the node. Persisted in the .dyc payload.
    /// </summary>
    public double ViewWidth
    {
        get => _viewWidth;
        set => SetField(ref _viewWidth, value);
    }

    /// <summary>
    /// User-chosen height of the display area (0 = automatic). Pure view state:
    /// changing it never dirties the node. Persisted in the .dyc payload.
    /// </summary>
    public double ViewHeight
    {
        get => _viewHeight;
        set => SetField(ref _viewHeight, value);
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

    /// <inheritdoc />
    public override void SerializeData(JObject data)
    {
        // Additive, optional fields: absent (or zero) means "size automatically",
        // so files written by older versions keep loading unchanged.
        data["ViewWidth"] = ViewWidth;
        data["ViewHeight"] = ViewHeight;
    }

    /// <inheritdoc />
    public override void DeserializeData(JObject data)
    {
        ViewWidth = data.Value<double?>("ViewWidth") ?? 0d;
        ViewHeight = data.Value<double?>("ViewHeight") ?? 0d;
    }
}
