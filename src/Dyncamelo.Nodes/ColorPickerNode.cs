using System;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Newtonsoft.Json.Linq;

namespace Dyncamelo.Nodes;

/// <summary>
/// An interactive color input: the UI binds a color swatch/picker to the
/// channel properties; the node outputs the chosen <see cref="DyncameloColor"/>.
/// </summary>
public class ColorPickerNode : NodeModel
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "ColorPicker";

    private int _a = 255;
    private int _r;
    private int _g;
    private int _b;

    /// <summary>Creates the node with a single color output (default opaque black).</summary>
    public ColorPickerNode()
    {
        Name = "Color Picker";
        Category = "Color";
        Description = "A color chosen with a picker.";
        AddOutput("color", typeof(DyncameloColor), "The chosen color.");
    }

    /// <summary>Alpha channel (0-255, clamped). Changing it dirties the node.</summary>
    public int A
    {
        get => _a;
        set => SetChannel(ref _a, value, nameof(A));
    }

    /// <summary>Red channel (0-255, clamped). Changing it dirties the node.</summary>
    public int R
    {
        get => _r;
        set => SetChannel(ref _r, value, nameof(R));
    }

    /// <summary>Green channel (0-255, clamped). Changing it dirties the node.</summary>
    public int G
    {
        get => _g;
        set => SetChannel(ref _g, value, nameof(G));
    }

    /// <summary>Blue channel (0-255, clamped). Changing it dirties the node.</summary>
    public int B
    {
        get => _b;
        set => SetChannel(ref _b, value, nameof(B));
    }

    /// <summary>The currently chosen color.</summary>
    public DyncameloColor Value => new DyncameloColor(_a, _r, _g, _b);

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
        data["A"] = _a;
        data["R"] = _r;
        data["G"] = _g;
        data["B"] = _b;
    }

    /// <inheritdoc />
    public override void DeserializeData(JObject data)
    {
        A = data.Value<int?>("A") ?? 255;
        R = data.Value<int?>("R") ?? 0;
        G = data.Value<int?>("G") ?? 0;
        B = data.Value<int?>("B") ?? 0;
    }

    private void SetChannel(ref int field, int value, string propertyName)
    {
        // The property name is passed explicitly: relying on [CallerMemberName]
        // here would report "SetChannel" instead of "A"/"R"/"G"/"B", so the UI
        // bindings (color swatch, slider echo) would never refresh.
        var clamped = Math.Max(0, Math.Min(255, value));
        if (SetField(ref field, clamped, propertyName))
        {
            OnPropertyChanged(nameof(Value));
            MarkDirty();
        }
    }
}
