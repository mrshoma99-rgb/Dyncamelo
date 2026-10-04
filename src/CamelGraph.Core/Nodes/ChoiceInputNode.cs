using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Player;
using Newtonsoft.Json.Linq;

namespace CamelGraph.Core.Nodes;

/// <summary>
/// A pick-list with the script author's own options (one per line): "Discipline: Architecture / MEP / Structure". Gives the chosen
/// text and its position. In the Script Player it is a dropdown (or buttons, for two or three options).
/// </summary>
public class ChoiceInputNode : NodeModel, IPlayerInputNode
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "ChoiceInput";

    private string _optionsText = "Option A\nOption B\nOption C";
    private string _value = "Option A";

    /// <summary>Creates the node with the chosen text and its position as outputs.</summary>
    public ChoiceInputNode()
    {
        Name = "Choice";
        Category = "Input";
        Description = "A pick-list with your own options (one per line); gives the chosen text and its position.";
        AddOutput("value", typeof(string));
        AddOutput("index", typeof(long));
    }

    /// <summary>The options as typed, one per line. Changing it dirties the node.</summary>
    public string OptionsText
    {
        get => _optionsText;
        set
        {
            if (SetField(ref _optionsText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(Options));

                // A choice that is no longer on offer falls back to the first option.
                var options = Options;
                if (!options.Contains(_value))
                {
                    Value = options.Count > 0 ? options[0] : string.Empty;
                }

                MarkDirty();
            }
        }
    }

    /// <summary>The options: the non-blank lines of <see cref="OptionsText"/>, trimmed, without repeats.</summary>
    public IReadOnlyList<string> Options =>
        (_optionsText ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>The chosen option. Changing it dirties the node.</summary>
    public string Value
    {
        get => _value;
        set
        {
            if (SetField(ref _value, value ?? string.Empty))
            {
                MarkDirty();
            }
        }
    }

    /// <inheritdoc />
    public PortModel CreatePlayerPort()
    {
        var port = PlayerPorts.Create(this, Name, typeof(string), Value, v => Value = v?.ToString() ?? string.Empty, null, "text");
        port.Choices = Options;
        return port;
    }

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override System.Collections.Generic.IReadOnlyList<string> SearchTags { get; } = new[] { "dropdown", "select", "option", "pick", "choose", "menu", "combo" };

    /// <inheritdoc />
    public override NodeFunction Function => NodeFunction.Create;

    /// <inheritdoc />
    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        var options = Options;
        if (options.Count == 0)
        {
            throw new InvalidOperationException("The choice has no options. Type one option per line.");
        }

        var index = -1;
        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i], _value, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            index = 0;
        }

        return new object?[] { options[index], (long)index };
    }

    /// <inheritdoc />
    public override void SerializeData(JObject data)
    {
        data["Options"] = OptionsText;
        data["Value"] = Value;
    }

    /// <inheritdoc />
    public override void DeserializeData(JObject data)
    {
        // Options first, then the value: a choice that is not on offer (a hand-edited file, options changed since) falls back to
        // the first option, as it does when the options are edited in the editor, so the property agrees with the output.
        _optionsText = data.Value<string>("Options") ?? _optionsText;
        _value = data.Value<string>("Value") ?? string.Empty;
        var options = Options;
        if (!options.Contains(_value))
        {
            _value = options.Count > 0 ? options[0] : string.Empty;
        }

        OnPropertyChanged(nameof(OptionsText));
        OnPropertyChanged(nameof(Options));
        OnPropertyChanged(nameof(Value));
        MarkDirty();
    }
}
