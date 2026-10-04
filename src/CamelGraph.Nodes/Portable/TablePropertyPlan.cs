using System;
using System.Collections.Generic;
using System.Globalization;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Portable;

/// <summary>
/// The pure half of Properties.SetCustomFromTable: which columns of a table become properties, how a row finds its item (row i is item
/// i, or the GUID in a key column), and the checks that run BEFORE any item is written to, so a table that does not fit stops the node
/// with a message instead of stamping the wrong values on items. Pure (no Navisworks types), so it is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public sealed class TablePropertyPlan
{
    private readonly CamelGraphTable _table;
    private readonly int[] _columnIndexes;
    private readonly string _nodeName;

    private TablePropertyPlan(CamelGraphTable table, int[] columnIndexes, int keyColumn, IReadOnlyList<GuidRequest>? keys, string nodeName)
    {
        _table = table;
        _columnIndexes = columnIndexes;
        KeyColumn = keyColumn;
        Keys = keys;
        _nodeName = nodeName;
    }

    /// <summary>The index of the key column, or -1 when row i belongs to item i.</summary>
    public int KeyColumn { get; }

    /// <summary>True when rows find their items by the GUID in the key column.</summary>
    public bool IsKeyed => KeyColumn >= 0;

    /// <summary>The GUID of every row (keyed plans only; null otherwise), with the text it was written as.</summary>
    public IReadOnlyList<GuidRequest>? Keys { get; }

    /// <summary>The number of rows.</summary>
    public int RowCount => _table.RowCount;

    /// <summary>The headers of the columns that become properties, in order.</summary>
    public IReadOnlyList<string> Columns
    {
        get
        {
            var names = new List<string>(_columnIndexes.Length);
            foreach (var index in _columnIndexes)
            {
                names.Add(_table.Headers[index]);
            }

            return names;
        }
    }

    /// <summary>
    /// Works out the plan: the columns to write (the named ones, or every column except the key column and the virtual "@" columns of
    /// Properties.ToTable), the key column and the GUID of every row.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="columns">The headers of the columns to write; null or empty means every column that is not the key and does not start with "@".</param>
    /// <param name="keyColumn">The header of the column that holds each row's item GUID (text, or a 22-character IFC id); null or empty means row i is item i.</param>
    /// <param name="nodeName">The node asking, for the messages.</param>
    /// <exception cref="ArgumentException">A column does not exist, no column is left to write, a key is empty or not a GUID, or a GUID is in two rows.</exception>
    public static TablePropertyPlan Create(CamelGraphTable table, IList<object?>? columns, string? keyColumn, string nodeName)
    {
        if (table == null)
        {
            throw new ArgumentNullException(nameof(table), nodeName + " needs a table. Wire one into 'table'.");
        }

        var key = -1;
        if (!string.IsNullOrWhiteSpace(keyColumn))
        {
            key = table.IndexOf(keyColumn, nodeName);
        }

        var chosen = new List<int>();
        if (columns != null && columns.Count > 0)
        {
            for (var i = 0; i < columns.Count; i++)
            {
                var name = Convert.ToString(columns[i], CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new ArgumentException(nodeName + ": the column name at index " + i.ToString(CultureInfo.InvariantCulture) + " is empty.", nameof(columns));
                }

                var index = table.IndexOf(name, nodeName);
                if (!chosen.Contains(index))
                {
                    chosen.Add(index);
                }
            }
        }
        else
        {
            for (var index = 0; index < table.ColumnCount; index++)
            {
                if (index != key && !table.Headers[index].TrimStart().StartsWith("@", StringComparison.Ordinal))
                {
                    chosen.Add(index);
                }
            }
        }

        if (chosen.Count == 0)
        {
            throw new ArgumentException(
                nodeName + ": no column is left to write as a property. Name the columns in 'columns' (the key column and the @ columns are not written unless you name them).",
                nameof(columns));
        }

        IReadOnlyList<GuidRequest>? keys = null;
        if (key >= 0)
        {
            var cells = new List<object?>(table.RowCount);
            foreach (var row in table.Rows)
            {
                cells.Add(row[key]);
            }

            keys = GuidLookup.ParseRequests(cells, nodeName);
            var firstRow = new Dictionary<Guid, int>();
            for (var row = 0; row < keys.Count; row++)
            {
                if (firstRow.TryGetValue(keys[row].Guid, out var earlier))
                {
                    throw new ArgumentException(
                        nodeName + ": the key '" + keys[row].Text + "' is in row " + (earlier + 1).ToString(CultureInfo.InvariantCulture) + " and again in row " +
                        (row + 1).ToString(CultureInfo.InvariantCulture) + ". Each item can take only one row: remove the repeat (for example with Table.Distinct) first.",
                        nameof(keyColumn));
                }

                firstRow[keys[row].Guid] = row;
            }
        }

        return new TablePropertyPlan(table, chosen.ToArray(), key, keys, nodeName);
    }

    /// <summary>
    /// The property names and values of one row, checked: every value must be something a property can hold (text, a number, true/false
    /// or a date; an empty cell is written as empty text).
    /// </summary>
    /// <param name="row">The row, counting from 0.</param>
    public List<KeyValuePair<string, object?>> PairsOf(int row)
    {
        var pairs = new List<KeyValuePair<string, object?>>(_columnIndexes.Length);
        foreach (var index in _columnIndexes)
        {
            var name = _table.Headers[index].Trim();
            var value = _table.Rows[row][index];
            CustomPropertyValues.Require(_nodeName, name, value);
            pairs.Add(new KeyValuePair<string, object?>(name, value));
        }

        return pairs;
    }

    /// <summary>For a plan without a key column: the table must have exactly one row per item, or the values would land on the wrong items.</summary>
    /// <param name="itemCount">How many items were wired.</param>
    /// <exception cref="ArgumentException">The counts differ.</exception>
    public void RequireItemCount(int itemCount)
    {
        if (RowCount != itemCount)
        {
            throw new ArgumentException(
                _nodeName + ": the table has " + RowCount.ToString(CultureInfo.InvariantCulture) + " row(s) but " + itemCount.ToString(CultureInfo.InvariantCulture) +
                " item(s) were wired. With no 'keyColumn', row 1 goes to item 1, row 2 to item 2 and so on, so both need the same length. " +
                "Give 'keyColumn' the column that holds each row's GUID to match rows to items by GUID instead.",
                "table");
        }
    }
}
