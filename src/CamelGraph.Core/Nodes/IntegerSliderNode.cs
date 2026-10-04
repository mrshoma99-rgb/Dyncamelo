using System;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Player;
using Newtonsoft.Json.Linq;

namespace CamelGraph.Core.Nodes;

/// <summary>
/// An integer slider with min/max/step. UI-agnostic: the view binds to the properties.
/// </summary>
public class IntegerSliderNode : NodeModel, IPlayerInputNode
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

    /// <summary>Lower bound of the slider. Raising it above <see cref="Max"/> pushes <see cref="Max"/> up, so Min never exceeds Max.</summary>
    public long Min
    {
        get => _min;
        set
        {
            if (SetField(ref _min, value))
            {
                if (_max < _min)
                {
                    Max = _min;
                }

                Value = _value; // re-clamp
            }
        }
    }

    /// <summary>Upper bound of the slider. Lowering it below <see cref="Min"/> pushes <see cref="Min"/> down, so Min never exceeds Max.</summary>
    public long Max
    {
        get => _max;
        set
        {
            if (SetField(ref _max, value))
            {
                if (_min > _max)
                {
                    Min = _max;
                }

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
    public PortModel CreatePlayerPort() =>
        PlayerPorts.Create(
            this, Name, typeof(long), Value, v => Value = (long)System.Math.Round(PlayerPorts.ToDouble(v)),
            new CamelGraph.Core.Loader.NodeRangeAttribute(Min, Max) { Step = Step > 0 ? Step : double.NaN });

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override System.Collections.Generic.IReadOnlyList<string> SearchTags { get; } = new[] { "range", "scrub", "drag", "int", "whole" };

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
        var min = data.Value<long?>("Min") ?? 0;
        var max = data.Value<long?>("Max") ?? 100;
        if (min > max)
        {
            // A file with the bounds the wrong way round: read them as the author meant, lowest first.
            (min, max) = (max, min);
        }

        Min = min;
        Max = max;
        Step = data.Value<long?>("Step") ?? 1;
        Value = data.Value<long?>("Value") ?? 0;
    }
}
