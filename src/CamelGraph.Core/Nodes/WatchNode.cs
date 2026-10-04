using System;
using System.Collections;
using System.Collections.Generic;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Player;
using CamelGraph.Core.Types;
using Newtonsoft.Json.Linq;

namespace CamelGraph.Core.Nodes;

/// <summary>
/// Displays whatever flows into it and passes the value through unchanged.
/// The input is declared as <see cref="object"/>, so lists arrive whole
/// (no replication) and <see cref="FormattedValue"/> shows the full structure.
/// </summary>
public class WatchNode : NodeModel, CamelGraph.Core.Player.IPlayerOutputNode
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "Watch";

    /// <summary>The most items written into the display of a long list; the rest is counted ("… 99,000 more").</summary>
    public const int MaxItems = 1000;

    private string _formattedValue = string.Empty;
    private string _playerText = string.Empty;
    private double _viewWidth;
    private double _viewHeight;

    /// <summary>Creates the node with an object input and a pass-through output.</summary>
    public WatchNode()
    {
        Name = "Watch";
        Category = "Display";
        Description = "Displays the incoming value. A long list shows its first 1,000 items and a count of the rest; the value itself passes through whole. When the node does not run (an input failed, was switched off or is not connected) the display is cleared.";
        AddInput("value", typeof(object), "The value to display.");
        AddOutput("value", typeof(object), "The incoming value, passed through.");
    }

    /// <summary>Human-readable rendering of the last observed value (invariant culture), at most <see cref="MaxItems"/> items; empty when the node did not run.</summary>
    public string FormattedValue
    {
        get => _formattedValue;
        private set => SetField(ref _formattedValue, value);
    }

    /// <summary>
    /// What the Script Player shows: a list as a count and one item per line (as many as the Player keeps), any other value as
    /// <see cref="FormattedValue"/>. Empty when the node did not run.
    /// </summary>
    public string PlayerText => _playerText;

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
    public override System.Collections.Generic.IReadOnlyList<string> SearchTags { get; } = new[] { "preview", "inspect", "debug", "print", "show", "output", "result", "view" };

    /// <inheritdoc />
    public override NodeFunction Function => NodeFunction.Info;

    /// <inheritdoc />
    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        var value = inputs.Length > 0 ? inputs[0] : null;
        FormattedValue = ValueText.Format(value, MaxItems);
        _playerText = value is IList list && !(value is string) ? ListText(list) : FormattedValue;
        OnPropertyChanged(nameof(PlayerText));
        return new object?[] { value };
    }

    /// <summary>The display is cleared: an old value is never shown as the current result.</summary>
    public override void OnNotRun()
    {
        FormattedValue = string.Empty;
        _playerText = string.Empty;
        OnPropertyChanged(nameof(PlayerText));
    }

    // "500 items", then one item per line; the Player keeps ScriptSession.MaxOutputLines lines, so stop one short of it.
    private static string ListText(IList list)
    {
        var shown = ScriptSession.MaxOutputLines - 2;
        var lines = new List<string>(Math.Min(list.Count, shown) + 2)
        {
            ValueText.Count(list.Count) + (list.Count == 1 ? " item" : " items"),
        };
        for (var i = 0; i < list.Count && i < shown; i++)
        {
            lines.Add(ValueText.Format(list[i], 50));
        }

        if (list.Count > shown)
        {
            lines.Add("… " + ValueText.Count(list.Count - shown) + " more items");
        }

        return string.Join("\n", lines);
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
