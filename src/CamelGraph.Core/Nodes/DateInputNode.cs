using System;
using System.Globalization;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Player;
using Newtonsoft.Json.Linq;

namespace CamelGraph.Core.Nodes;

/// <summary>
/// A date (optionally with a time) typed as text: "2026-10-01" or "2026-10-01 14:30". The text is kept as typed so a half-typed date
/// is not lost; an input that is not a date makes the node report it when the graph runs.
/// </summary>
public class DateInputNode : NodeModel, IPlayerInputNode
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "DateInput";

    private string _text;

    /// <summary>Creates the node with one date output, set to today.</summary>
    public DateInputNode()
    {
        Name = "Date";
        Category = "Input";
        Description = "A date (and optional time), typed as 2026-10-01 or 2026-10-01 14:30.";
        _text = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        AddOutput("value", typeof(DateTime));
    }

    /// <summary>The date as typed. Changing it dirties the node.</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (SetField(ref _text, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(IsInvalid));
                MarkDirty();
            }
        }
    }

    /// <summary>True when <see cref="Text"/> reads as a date.</summary>
    public bool IsValid => TryGetValue(out _);

    /// <summary>The opposite of <see cref="IsValid"/> (for visibility bindings).</summary>
    public bool IsInvalid => !IsValid;

    /// <summary>Reads <see cref="Text"/> as a date and time (invariant culture).</summary>
    /// <param name="value">The date, when it reads as one.</param>
    public bool TryGetValue(out DateTime value) =>
        DateTime.TryParse((_text ?? string.Empty).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    /// <inheritdoc />
    public PortModel CreatePlayerPort() =>
        PlayerPorts.Create(this, Name, typeof(string), Text, v => Text = v?.ToString() ?? string.Empty, null, "text");

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override NodeFunction Function => NodeFunction.Create;

    /// <inheritdoc />
    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        if (!TryGetValue(out var date))
        {
            throw new FormatException(
                "'" + _text + "' is not a date. Type it like 2026-10-01 or 2026-10-01 14:30.");
        }

        return new object?[] { date };
    }

    /// <inheritdoc />
    public override void SerializeData(JObject data)
    {
        data["Text"] = Text;
    }

    /// <inheritdoc />
    public override void DeserializeData(JObject data)
    {
        Text = data.Value<string>("Text") ?? string.Empty;
    }
}
