using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dyncamelo.Core.Types;

namespace Dyncamelo.Nodes;

/// <summary>
/// A table: named columns and rows of cells. Cells are whatever the source held — numbers, text, booleans, dates, null —
/// and the table never changes after it is built: the Table nodes always return a new one. Two columns never share a name
/// (a blank or repeated header is renamed when the table is made), so every column can be addressed by name.
/// </summary>
public sealed class DyncameloTable
{
    private readonly string[] _headers;
    private readonly object?[][] _rows;

    /// <summary>Creates a table. Rows shorter than the headers are padded with nulls; longer rows are an error unless the extra cells are empty.</summary>
    /// <param name="headers">The column names (blank or repeated names are made unique).</param>
    /// <param name="rows">The rows.</param>
    public DyncameloTable(IEnumerable<string?> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        if (headers == null)
        {
            throw new ArgumentNullException(nameof(headers));
        }

        if (rows == null)
        {
            throw new ArgumentNullException(nameof(rows));
        }

        _headers = UniqueNames(headers);
        var list = new List<object?[]>();
        foreach (var row in rows)
        {
            var cells = new object?[_headers.Length];
            var source = row ?? Array.Empty<object?>();
            for (int i = 0; i < source.Count; i++)
            {
                if (i < cells.Length)
                {
                    cells[i] = source[i];
                }
                else if (source[i] != null && !(source[i] is string text && text.Length == 0))
                {
                    throw new ArgumentException(
                        "Row " + (list.Count + 1).ToString(CultureInfo.InvariantCulture) + " has " + source.Count.ToString(CultureInfo.InvariantCulture) +
                        " cell(s) but the table has " + _headers.Length.ToString(CultureInfo.InvariantCulture) + " column(s).");
                }
            }

            list.Add(cells);
        }

        _rows = list.ToArray();
    }

    /// <summary>The column names, in order.</summary>
    public IReadOnlyList<string> Headers => _headers;

    /// <summary>The rows; each has exactly one cell per column.</summary>
    public IReadOnlyList<object?[]> Rows => _rows;

    /// <summary>Number of rows.</summary>
    public int RowCount => _rows.Length;

    /// <summary>Number of columns.</summary>
    public int ColumnCount => _headers.Length;

    /// <summary>
    /// Finds a column by name: exact match first, then ignoring case and surrounding spaces. Throws an error that lists the
    /// available columns when there is none.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="nodeName">The node asking, for the error message.</param>
    public int IndexOf(string? column, string nodeName)
    {
        var index = TryIndexOf(column);
        if (index >= 0)
        {
            return index;
        }

        throw new ArgumentException(
            nodeName + ": the table has no column named '" + column + "'. Columns: " +
            (_headers.Length == 0 ? "(none)" : string.Join(", ", _headers)) + ".", "column");
    }

    /// <summary>Like <see cref="IndexOf"/> but returns -1 when there is no such column.</summary>
    /// <param name="column">The column name.</param>
    public int TryIndexOf(string? column)
    {
        if (column == null)
        {
            return -1;
        }

        for (int i = 0; i < _headers.Length; i++)
        {
            if (string.Equals(_headers[i], column, StringComparison.Ordinal))
            {
                return i;
            }
        }

        var trimmed = column.Trim();
        for (int i = 0; i < _headers.Length; i++)
        {
            if (string.Equals(_headers[i].Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>One row as a header to cell dictionary.</summary>
    /// <param name="row">The row index.</param>
    public Dictionary<string, object?> RowAsDictionary(int row)
    {
        var dictionary = new Dictionary<string, object?>(_headers.Length, StringComparer.Ordinal);
        for (int i = 0; i < _headers.Length; i++)
        {
            dictionary[_headers[i]] = _rows[row][i];
        }

        return dictionary;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var shown = string.Join(", ", _headers.Take(6)) + (_headers.Length > 6 ? ", …" : string.Empty);
        return "Table " + RowCount.ToString(CultureInfo.InvariantCulture) + " row(s) × " +
               ColumnCount.ToString(CultureInfo.InvariantCulture) + " column(s): " + shown;
    }

    /// <summary>Makes a list of names unique and non-blank: blank becomes "Column N", a repeat gets " (2)", " (3)".</summary>
    /// <param name="names">The requested names.</param>
    internal static string[] UniqueNames(IEnumerable<string?> names)
    {
        var result = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var requested in names)
        {
            var name = string.IsNullOrWhiteSpace(requested)
                ? "Column " + (result.Count + 1).ToString(CultureInfo.InvariantCulture)
                : requested!.Trim();
            var candidate = name;
            var suffix = 2;
            while (!used.Add(candidate))
            {
                candidate = name + " (" + suffix.ToString(CultureInfo.InvariantCulture) + ")";
                suffix++;
            }

            result.Add(candidate);
        }

        return result.ToArray();
    }

    /// <summary>A cell as display text (numbers and dates in invariant form, null as empty).</summary>
    /// <param name="cell">The cell value.</param>
    internal static string CellText(object? cell)
    {
        if (cell == null)
        {
            return string.Empty;
        }

        if (cell is double number)
        {
            return number.ToString("0.############", CultureInfo.InvariantCulture);
        }

        if (cell is DateTime date)
        {
            return date.TimeOfDay == TimeSpan.Zero
                ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return TypeCoercion.FormatValue(cell);
    }
}
