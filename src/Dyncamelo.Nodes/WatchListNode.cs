using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Types;
using Newtonsoft.Json.Linq;

namespace Dyncamelo.Nodes;

/// <summary>
/// Displays the elements of an incoming list one per line ("index : value")
/// and passes the value through unchanged. Non-list values display as a
/// single line. The input is untyped, so lists arrive whole.
/// </summary>
public class WatchListNode : NodeModel
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "WatchList";

    private IReadOnlyList<string> _lines = new List<string>();
    private double _viewWidth;
    private double _viewHeight;

    /// <summary>Creates the node with an untyped input and a pass-through output.</summary>
    public WatchListNode()
    {
        Name = "Watch List";
        Category = "Display";
        Description = "Displays the elements of a list, one per line.";
        AddInput("list", typeof(object), "The list (or value) to display.");
        AddOutput("list", typeof(object), "The incoming value, passed through.");
    }

    /// <summary>One display line per list element ("index : value", invariant culture).</summary>
    public IReadOnlyList<string> Lines
    {
        get => _lines;
        private set
        {
            _lines = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FormattedValue));
        }
    }

    /// <summary>All display lines joined with newlines.</summary>
    public string FormattedValue => string.Join("\n", _lines);

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
        var lines = new List<string>();
        if (value is IList list && !(value is string))
        {
            for (int i = 0; i < list.Count; i++)
            {
                lines.Add(i.ToString(CultureInfo.InvariantCulture) + " : " + TypeCoercion.FormatValue(list[i]));
            }
        }
        else
        {
            lines.Add(TypeCoercion.FormatValue(value));
        }

        Lines = lines;
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
