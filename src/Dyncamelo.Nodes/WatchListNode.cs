using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Types;

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
}
