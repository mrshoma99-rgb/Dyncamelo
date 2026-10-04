using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Types;
using CamelGraph.Nodes.Internal;
using Newtonsoft.Json.Linq;

namespace CamelGraph.Nodes;

/// <summary>
/// Shows a table as a grid — column names on top, one line per row — and passes the value through unchanged. Takes a table,
/// a list of rows (each a list of cells) or a list of dictionaries; any other value is shown as a single column.
/// </summary>
public class WatchTableNode : NodeModel, CamelGraph.Core.Player.IPlayerOutputNode
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "WatchTable";

    /// <summary>The most rows drawn; the summary says how many there are in all.</summary>
    public const int MaxRows = 2000;

    private IReadOnlyList<string> _headers = new List<string>();
    private IReadOnlyList<WatchTableRow> _rows = new List<WatchTableRow>();
    private string _summary = string.Empty;
    private string _playerText = string.Empty;
    private double _viewWidth;
    private double _viewHeight;

    /// <summary>Creates the node with an untyped input and a pass-through output.</summary>
    public WatchTableNode()
    {
        Name = "Watch Table";
        Category = "Display";
        Description = "Displays a table as a grid: column names on top, one line per row.";
        AddInput("table", typeof(object), "The table (or rows, or dictionaries) to display.");
        AddOutput("table", typeof(object), "The incoming value, passed through.");
    }

    /// <summary>The column names shown on top.</summary>
    public IReadOnlyList<string> Headers
    {
        get => _headers;
        private set
        {
            _headers = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The rows drawn (at most <see cref="MaxRows"/>).</summary>
    public IReadOnlyList<WatchTableRow> Rows
    {
        get => _rows;
        private set
        {
            _rows = value;
            OnPropertyChanged();
        }
    }

    /// <summary>"12 rows × 4 columns", plus a note when only the first rows are drawn.</summary>
    public string Summary
    {
        get => _summary;
        private set
        {
            _summary = value;
            OnPropertyChanged();
        }
    }

    /// <inheritdoc />
    public string PlayerText => _playerText;

    /// <summary>User-chosen width of the display area (0 = automatic). View state only: never dirties the node.</summary>
    public double ViewWidth
    {
        get => _viewWidth;
        set => SetField(ref _viewWidth, value);
    }

    /// <summary>User-chosen height of the display area (0 = automatic). View state only: never dirties the node.</summary>
    public double ViewHeight
    {
        get => _viewHeight;
        set => SetField(ref _viewHeight, value);
    }

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override NodeFunction Function => NodeFunction.Info;

    /// <summary>Turns whatever arrives into a table to draw.</summary>
    /// <param name="value">A table, a list of rows, a list of dictionaries, or any other value.</param>
    [IsVisibleInLibrary(false)]
    public static CamelGraphTable ToTable(object? value)
    {
        switch (value)
        {
            case CamelGraphTable table:
                return table;
            case IDictionary dictionary:
                return TableToolkitNodes.FromDictionaries(new List<object?> { dictionary });
            case IList list when !(value is string):
                var items = list.Cast<object?>().ToList();
                if (items.Count > 0 && items.All(item => item is IDictionary))
                {
                    return TableToolkitNodes.FromDictionaries(items);
                }

                if (items.Count > 0 && items.All(item => item is IList && !(item is string)))
                {
                    return TableToolkitNodes.FromRows(items);
                }

                return TableToolkitNodes.FromRows(items.Select(item => (object?)new List<object?> { item }).ToList(), new List<object?> { "value" });
            default:
                return TableToolkitNodes.FromRows(new List<object?> { new List<object?> { value } }, new List<object?> { "value" });
        }
    }

    /// <inheritdoc />
    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        var value = inputs.Length > 0 ? inputs[0] : null;
        var table = ToTable(value);
        var shown = Math.Min(table.RowCount, MaxRows);

        var rows = new List<WatchTableRow>(shown);
        for (var i = 0; i < shown; i++)
        {
            rows.Add(new WatchTableRow(table.Rows[i].Select(CamelGraphTable.CellText).ToList()));
        }

        var summary = table.RowCount.ToString(CultureInfo.InvariantCulture) + (table.RowCount == 1 ? " row" : " rows") + " × " +
                      table.ColumnCount.ToString(CultureInfo.InvariantCulture) + (table.ColumnCount == 1 ? " column" : " columns");
        if (shown < table.RowCount)
        {
            summary += " — first " + shown.ToString(CultureInfo.InvariantCulture) + " shown";
        }

        _playerText = TableText.Markdown(table);
        Headers = table.Headers.ToList();
        Rows = rows;
        Summary = summary;
        return new object?[] { value };
    }

    /// <inheritdoc />
    public override void SerializeData(JObject data)
    {
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

/// <summary>One drawn row of a <see cref="WatchTableNode"/>: the text of each cell.</summary>
public class WatchTableRow
{
    /// <summary>Creates a row.</summary>
    /// <param name="cells">The text of each cell.</param>
    public WatchTableRow(IReadOnlyList<string> cells)
    {
        Cells = cells;
    }

    /// <summary>The text of each cell, in column order.</summary>
    public IReadOnlyList<string> Cells { get; }
}
