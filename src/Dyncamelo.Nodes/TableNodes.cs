using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Types;

namespace Dyncamelo.Nodes;

/// <summary>
/// Table nodes: operations over spreadsheet-shaped data (a list of rows plus a
/// list of column headers), the shape produced by Excel.ReadFromFile and
/// CSV.ReadFromFile.
/// </summary>
[NodeCategory("List")]
public static class TableNodes
{
    /// <summary>
    /// Joins table rows to a list of keys (element GUIDs, mark values, ...):
    /// for each key, returns the first row whose cell in the key column
    /// matches. Cells and keys are compared as invariant text, so the number
    /// 42 and the string "42" match; the comparison is case-sensitive. When
    /// the same key occurs in several rows the first row wins.
    /// </summary>
    /// <param name="rows">Table rows; each row a list of cells (e.g. from Excel.ReadFromFile).</param>
    /// <param name="headers">Column headers, one per cell column.</param>
    /// <param name="keys">Keys to look up, e.g. element GUIDs or mark values.</param>
    /// <param name="keyColumn">Name of the header column holding the keys.</param>
    /// <returns>Dictionary with "matchedRows" (parallel to keys; null when unmatched) and "unmatchedKeys".</returns>
    [NodeName("Table.JoinByKey")]
    [MultiReturn("matchedRows", "unmatchedKeys")]
    [NodeDescription("Joins spreadsheet rows to a key list: one matched row per key (null when unmatched), plus the keys that matched nothing.")]
    [NodeSearchTags("join", "lookup", "vlookup", "merge", "link", "table", "excel", "csv")]
    public static Dictionary<string, object> JoinByKey(
        IList<object?> rows,
        IList<object?> headers,
        IList<object?> keys,
        string keyColumn)
    {
        if (rows == null)
        {
            throw new ArgumentNullException(nameof(rows), "Table.JoinByKey requires a list of rows (each row a list of cells).");
        }

        if (headers == null)
        {
            throw new ArgumentNullException(nameof(headers), "Table.JoinByKey requires the table's column headers.");
        }

        if (keys == null)
        {
            throw new ArgumentNullException(nameof(keys), "Table.JoinByKey requires a list of keys to look up.");
        }

        if (string.IsNullOrWhiteSpace(keyColumn))
        {
            throw new ArgumentException(
                "Table.JoinByKey requires the name of the key column (one of the header values).", nameof(keyColumn));
        }

        var columnIndex = FindColumnIndex(headers, keyColumn);

        // First occurrence wins when the same key value appears in several rows.
        var rowByKey = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var cells = ToCells(row);
            if (cells == null || columnIndex >= cells.Count)
            {
                continue; // row too short to have a key cell
            }

            var cellKey = NormalizeKey(cells[columnIndex]);
            if (!rowByKey.ContainsKey(cellKey))
            {
                rowByKey.Add(cellKey, row);
            }
        }

        var matchedRows = new List<object?>(keys.Count);
        var unmatchedKeys = new List<object?>();
        foreach (var key in keys)
        {
            if (rowByKey.TryGetValue(NormalizeKey(key), out var row))
            {
                matchedRows.Add(row);
            }
            else
            {
                matchedRows.Add(null);
                unmatchedKeys.Add(key);
            }
        }

        return new Dictionary<string, object>
        {
            ["matchedRows"] = matchedRows,
            ["unmatchedKeys"] = unmatchedKeys,
        };
    }

    // ------------------------------------------------------------------
    // Helpers (not imported as nodes: non-public).
    // ------------------------------------------------------------------

    private static int FindColumnIndex(IList<object?> headers, string keyColumn)
    {
        var names = new List<string>(headers.Count);
        foreach (var header in headers)
        {
            names.Add(header is string text ? text : header == null ? string.Empty : TypeCoercion.FormatValue(header));
        }

        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], keyColumn, StringComparison.Ordinal))
            {
                return i;
            }
        }

        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], keyColumn, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new ArgumentException(
            "Table.JoinByKey: the headers contain no column named '" + keyColumn + "'. Available columns: " +
            (names.Count == 0 ? "(none)" : string.Join(", ", names)) + ".", nameof(keyColumn));
    }

    private static IList? ToCells(object? row)
    {
        if (row is IList cells && !(row is string))
        {
            return cells;
        }

        // Scalar rows behave as single-cell rows (consistent with CSV/Excel writers).
        return row == null ? null : new object?[] { row };
    }

    /// <summary>
    /// Invariant text form of a key or cell so numeric and textual keys
    /// compare naturally (42 and "42" match, independent of locale).
    /// </summary>
    private static string NormalizeKey(object? value)
    {
        if (value is string text)
        {
            return text;
        }

        if (value is double number)
        {
            return number.ToString("R", CultureInfo.InvariantCulture);
        }

        return TypeCoercion.FormatValue(value);
    }
}
