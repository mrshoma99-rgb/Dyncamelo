using System;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Player;
using Newtonsoft.Json.Linq;

namespace CamelGraph.Core.Nodes;

/// <summary>
/// A floating-point slider with min/max/step. UI-agnostic: the view binds to the properties.
/// </summary>
public class NumberSliderNode : NodeModel, IPlayerInputNode
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "NumberSlider";

    private double _value;
    private double _min;
    private double _max = 100d;
    private double _step = 0.1d;

    /// <summary>Creates the node with one numeric output.</summary>
    public NumberSliderNode()
    {
        Name = "Number Slider";
        Category = "Input";
        Description = "A number selected with a slider.";
        AddOutput("value", typeof(double));
    }

    /// <summary>Current value, clamped to [<see cref="Min"/>, <see cref="Max"/>]. Changing it dirties the node.</summary>
    public double Value
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
    public double Min
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
    public double Max
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
    public double Step
    {
        get => _step;
        set => SetField(ref _step, value);
    }

    /// <inheritdoc />
    public PortModel CreatePlayerPort() =>
        PlayerPorts.Create(
            this, Name, typeof(double), Value, v => Value = PlayerPorts.ToDouble(v),
            new CamelGraph.Core.Loader.NodeRangeAttribute(Min, Max) { Step = Step > 0d ? Step : double.NaN });

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override System.Collections.Generic.IReadOnlyList<string> SearchTags { get; } = new[] { "range", "scrub", "drag", "decimal", "float" };

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
        var min = data.Value<double?>("Min") ?? 0d;
        var max = data.Value<double?>("Max") ?? 100d;
        if (min > max)
        {
            // A file with the bounds the wrong way round: read them as the author meant, lowest first.
            (min, max) = (max, min);
        }

        Min = min;
        Max = max;
        Step = data.Value<double?>("Step") ?? 0.1d;
        Value = data.Value<double?>("Value") ?? 0d;
    }
}
