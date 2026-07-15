using System;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Newtonsoft.Json.Linq;

namespace Dyncamelo.Core.Nodes;

/// <summary>
/// An integer slider with min/max/step. UI-agnostic: the view binds to the properties.
/// </summary>
public class IntegerSliderNode : NodeModel
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "IntegerSlider";

    private long _value;
    private long _min;
    private long _max = 100;
    private long _step = 1;

    /// <summary>Creates the node with one integer output.</summary>
    public IntegerSliderNode()
    {
        Name = "Integer Slider";
        Category = "Input";
        Description = "An integer selected with a slider.";
        AddOutput("value", typeof(long));
    }

    /// <summary>Current value, clamped to [<see cref="Min"/>, <see cref="Max"/>]. Changing it dirties the node.</summary>
    public long Value
    {
        get => _value;
        set
        {
            var clamped = Math.Min(Math.Max(value, _min), _max);
            if (SetField(ref _value, clamped))
            {
                MarkDirty();
            }
        }
    }

    /// <summary>Lower bound of the slider.</summary>
    public long Min
    {
        get => _min;
        set
        {
            if (SetField(ref _min, value))
            {
                Value = _value; // re-clamp
            }
        }
    }

    /// <summary>Upper bound of the slider.</summary>
    public long Max
    {
        get => _max;
        set
        {
            if (SetField(ref _max, value))
            {
                Value = _value; // re-clamp
            }
        }
    }

    /// <summary>Slider increment.</summary>
    public long Step
    {
        get => _step;
        set => SetField(ref _step, value);
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
        data["Min"] = Min;
        data["Max"] = Max;
        data["Step"] = Step;
    }

    /// <inheritdoc />
    public override void DeserializeData(JObject data)
    {
        Min = data.Value<long?>("Min") ?? 0;
        Max = data.Value<long?>("Max") ?? 100;
        Step = data.Value<long?>("Step") ?? 1;
        Value = data.Value<long?>("Value") ?? 0;
    }
}
