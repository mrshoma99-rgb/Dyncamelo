using System;
using System.Collections.Generic;
using System.Globalization;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Portable;

/// <summary>
/// Builds a table whose columns are not known in advance: one row per item, a column for every name seen, in the order the names were
/// first seen (Properties.ToTable with no property list: "every property found"). A row that never met a name has an empty cell there.
/// The table is capped at <see cref="MaxCells"/> cells so one very large model cannot exhaust memory. Pure (no Navisworks types), so it
/// is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public sealed class WideTableBuilder
{
    /// <summary>The most cells (rows times columns) the table may hold.</summary>
    public const int MaxCells = 1000000;

    private readonly List<string> _headers = new List<string>();
    private readonly Dictionary<string, int> _columnOf = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly List<Dictionary<int, object?>> _rows = new List<Dictionary<int, object?>>();

    /// <summary>The number of rows started so far.</summary>
    public int RowCount => _rows.Count;

    /// <summary>The number of columns seen so far.</summary>
    public int ColumnCount => _headers.Count;

    /// <summary>Starts the next row.</summary>
    public void StartRow()
    {
        _rows.Add(new Dictionary<int, object?>());
        CheckCap();
    }

    /// <summary>
    /// Sets a cell of the current row. The first value given for a name in a row stays (an item that carries the same category and
    /// property twice reads as its first one).
    /// </summary>
    /// <param name="header">The column name.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="InvalidOperationException">No row was started, or the table would pass <see cref="MaxCells"/> cells.</exception>
    public void Set(string header, object? value)
    {
        if (_rows.Count == 0)
        {
            throw new InvalidOperationException("Start a row first.");
        }

        if (!_columnOf.TryGetValue(header, out var column))
        {
            column = _headers.Count;
            _columnOf[header] = column;
            _headers.Add(header);
            CheckCap();
        }

        var row = _rows[_rows.Count - 1];
        if (!row.ContainsKey(column))
        {
            row[column] = value;
        }
    }

    /// <summary>Builds the table.</summary>
    public CamelGraphTable Build()
    {
        CheckCap();
        var rows = new List<IReadOnlyList<object?>>(_rows.Count);
        foreach (var row in _rows)
        {
            var cells = new object?[_headers.Count];
            foreach (var pair in row)
            {
                cells[pair.Key] = pair.Value;
            }

            rows.Add(cells);
        }

        return new CamelGraphTable(_headers, rows);
    }

    private void CheckCap()
    {
        if ((long)_rows.Count * Math.Max(_headers.Count, 1) > MaxCells)
        {
            throw new InvalidOperationException(
                "Reading every property of " + _rows.Count.ToString(CultureInfo.InvariantCulture) + " item(s) would build a table of more than " +
                MaxCells.ToString("N0", CultureInfo.InvariantCulture) + " cells (rows times columns). Wire fewer items, or name the properties you need in 'properties'.");
        }
    }
}
