using System;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Player;
using Newtonsoft.Json.Linq;

namespace CamelGraph.Nodes;

/// <summary>
/// An interactive color input: the UI binds a color swatch/picker to the
/// channel properties; the node outputs the chosen <see cref="CamelGraphColor"/>.
/// </summary>
public class ColorPickerNode : NodeModel, IPlayerInputNode
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
        Category = "Input";
        Description = "A color chosen with a picker.";
        AddOutput("color", typeof(CamelGraphColor), "The chosen color.");
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
    public CamelGraphColor Value => new CamelGraphColor(_a, _r, _g, _b);

    /// <inheritdoc />
    public PortModel CreatePlayerPort() =>
        PlayerPorts.Create(
            this,
            Name,
            typeof(string),
            PortEditors.ToHex((byte)_a, (byte)_r, (byte)_g, (byte)_b),
            value =>
            {
                if (PortEditors.TryParseHex(value as string, out var a, out var r, out var g, out var b))
                {
                    A = a;
                    R = r;
                    G = g;
                    B = b;
                }
            },
            null,
            "colour");

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override System.Collections.Generic.IReadOnlyList<string> SearchTags { get; } = new[] { "colour", "swatch", "palette", "hue", "paint" };

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
