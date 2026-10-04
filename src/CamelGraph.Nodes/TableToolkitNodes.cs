using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Types;
using CamelGraph.Nodes.Internal;

namespace CamelGraph.Nodes;

/// <summary>
/// The table toolkit: build a table from rows, dictionaries or columns (or a CSV / Excel file), pick and rename columns, add
/// computed columns, filter, sort, group and total, pivot, join two tables, and turn the result back into rows, text or a file.
/// A table is one value, so one wire carries all of it; every node returns a new table and leaves its input alone.
/// </summary>
[NodeCategory("Table")]
public static class TableToolkitNodes
{
    // One boxed 0 shared by every empty count/sum cell of a pivot (boxed numbers are never changed in place).
    private static readonly object BoxedZero = 0d;

    // ------------------------------------------------------------------ Build

    /// <summary>Makes a table from rows of cells and column names.</summary>
    /// <param name="rows">The rows; each row a list of cells (as read by Excel.ReadFromFile or CSV.ReadFromFile).</param>
    /// <param name="headers">The column names. Leave unwired to name the columns Column 1, Column 2 …, or tick firstRowIsHeader.</param>
    /// <param name="firstRowIsHeader">True takes the first row as the column names (when no headers are wired).</param>
    /// <returns>The table.</returns>
    [NodeName("Table.FromRows")]
    [return: NodeName("table")]
    [NodeDescription("Makes a table from rows of cells and column names (or from the first row) — the doorway from Excel, CSV and list data.")]
    [NodeSearchTags("create", "table", "dataframe", "rows", "headers", "sheet", "grid")]
    public static CamelGraphTable FromRows(IList<object?> rows, IList<object?>? headers = null, bool firstRowIsHeader = false)
    {
        RequireList(rows, "rows", "Table.FromRows");
        var cells = rows.Select(ToCells).ToList();
        IEnumerable<string?> names;
        if (headers != null && headers.Count > 0)
        {
            names = headers.Select(h => h == null ? null : TypeCoercion.FormatValue(h));
        }
        else if (firstRowIsHeader && cells.Count > 0)
        {
            names = cells[0].Select(c => c == null ? null : TypeCoercion.FormatValue(c));
            cells.RemoveAt(0);
        }
        else
        {
            var width = cells.Count == 0 ? 0 : cells.Max(r => r.Count);
            names = Enumerable.Range(1, width).Select(i => (string?)("Column " + i.ToString(CultureInfo.InvariantCulture)));
        }

        return new CamelGraphTable(names, cells);
    }

    /// <summary>Makes a table from dictionaries, one row per dictionary.</summary>
    /// <param name="dictionaries">Dictionaries such as the ones Properties.AsDictionary returns; the columns are all their keys, in order of first appearance.</param>
    /// <returns>The table.</returns>
    [NodeName("Table.FromDictionaries")]
    [return: NodeName("table")]
    [NodeDescription("Makes a table from a list of dictionaries (one row each); the columns are all their keys — property bags become rows.")]
    [NodeSearchTags("properties", "records", "json", "dictionary", "table", "dataframe")]
    public static CamelGraphTable FromDictionaries(IList<object?> dictionaries)
    {
        RequireList(dictionaries, "dictionaries", "Table.FromDictionaries");
        var columns = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var maps = new List<IDictionary>();
        for (int i = 0; i < dictionaries.Count; i++)
        {
            if (!(dictionaries[i] is IDictionary map))
            {
                throw new ArgumentException(
                    "Table.FromDictionaries: item " + i.ToString(CultureInfo.InvariantCulture) + " is not a dictionary.");
            }

            maps.Add(map);
            foreach (var key in map.Keys)
            {
                var name = TypeCoercion.FormatValue(key);
                if (seen.Add(name))
                {
                    columns.Add(name);
                }
            }
        }

        var rows = maps.Select(map => (IReadOnlyList<object?>)columns.Select(c => map.Contains(c) ? map[c] : null).ToList()).ToList();
        return new CamelGraphTable(columns, rows);
    }

    /// <summary>Makes a table from columns of values.</summary>
    /// <param name="columns">One list per column (they may differ in length; short ones are padded with nulls).</param>
    /// <param name="headers">The column names (leave unwired for Column 1, Column 2 …).</param>
    /// <returns>The table.</returns>
    [NodeName("Table.FromColumns")]
    [return: NodeName("table")]
    [NodeDescription("Makes a table from columns: a list of lists, one per column, with optional names — for results that come as parallel lists.")]
    [NodeSearchTags("columns", "parallel lists", "zip", "table", "transpose")]
    public static CamelGraphTable FromColumns(IList<object?> columns, IList<object?>? headers = null)
    {
        RequireList(columns, "columns", "Table.FromColumns");
        var lists = new List<IList>();
        for (int i = 0; i < columns.Count; i++)
        {
            lists.Add(columns[i] is IList list && !(columns[i] is string)
                ? list
                : throw new ArgumentException("Table.FromColumns: column " + (i + 1).ToString(CultureInfo.InvariantCulture) + " is not a list."));
        }

        var height = lists.Count == 0 ? 0 : lists.Max(l => l.Count);
        var rows = new List<IReadOnlyList<object?>>(height);
        for (int r = 0; r < height; r++)
        {
            rows.Add(lists.Select(l => r < l.Count ? l[r] : null).ToList());
        }

        IEnumerable<string?> names = headers != null && headers.Count > 0
            ? headers.Select(h => h == null ? null : TypeCoercion.FormatValue(h))
            : Enumerable.Range(1, lists.Count).Select(i => (string?)("Column " + i.ToString(CultureInfo.InvariantCulture)));
        return new CamelGraphTable(names, rows);
    }

    // ------------------------------------------------------------------ Read back

    /// <summary>The rows of a table as lists of cells.</summary>
    /// <param name="table">The table.</param>
    /// <returns>A list of rows, each a list of cells in column order (feeds Excel.WriteToFile and CSV.WriteToFile).</returns>
    [NodeName("Table.Rows")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("rows")]
    [NodeDescription("The rows of a table as a list of lists of cells (for Excel.WriteToFile, CSV.WriteToFile and the List nodes).")]
    [NodeSearchTags("rows", "cells", "list", "export", "values")]
    public static IList<object?> Rows(CamelGraphTable table)
    {
        Require(table, "Table.Rows");
        return table.Rows.Select(r => (object?)new List<object?>(r)).ToList();
    }

    /// <summary>The column names of a table.</summary>
    /// <param name="table">The table.</param>
    /// <returns>The names, in column order.</returns>
    [NodeName("Table.Headers")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("headers")]
    [NodeDescription("The column names of a table, in order.")]
    [NodeSearchTags("columns", "names", "header", "fields")]
    public static IList<string> Headers(CamelGraphTable table)
    {
        Require(table, "Table.Headers");
        return table.Headers.ToList();
    }

    /// <summary>The size and columns of a table.</summary>
    /// <param name="table">The table.</param>
    /// <returns>rowCount, columnCount and headers.</returns>
    [NodeName("Table.Info")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [MultiReturn("rowCount", "columnCount", "headers")]
    [PortKinds("integer", "integer", "text*")]
    [NodeDescription("How many rows and columns a table has, and its column names.")]
    [NodeSearchTags("size", "count", "shape", "dimensions", "length")]
    public static Dictionary<string, object> Info(CamelGraphTable table)
    {
        Require(table, "Table.Info");
        return new Dictionary<string, object>
        {
            ["rowCount"] = table.RowCount,
            ["columnCount"] = table.ColumnCount,
            ["headers"] = table.Headers.ToList(),
        };
    }

    /// <summary>The rows of a table as dictionaries.</summary>
    /// <param name="table">The table.</param>
    /// <returns>One dictionary per row, from column name to cell.</returns>
    [NodeName("Table.ToDictionaries")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("dictionaries")]
    [NodeDescription("The rows of a table as dictionaries (column name to cell) — for JSON.WriteToFile or per-row lookups.")]
    [NodeSearchTags("records", "json", "dictionary", "objects")]
    public static IList<object?> ToDictionaries(CamelGraphTable table)
    {
        Require(table, "Table.ToDictionaries");
        return Enumerable.Range(0, table.RowCount).Select(i => (object?)table.RowAsDictionary(i)).ToList();
    }

    /// <summary>One column of a table.</summary>
    /// <param name="table">The table.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The cells of that column, top to bottom.</returns>
    [NodeName("Table.Column")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("values")]
    [NodeDescription("The cells of one column, top to bottom — wire it into List.Sum, List.CountBy, Search or a property writer.")]
    [NodeSearchTags("column", "field", "values", "extract", "pick")]
    public static IList<object?> Column(CamelGraphTable table, string column)
    {
        Require(table, "Table.Column");
        var index = table.IndexOf(column, "Table.Column");
        return table.Rows.Select(r => r[index]).ToList();
    }

    /// <summary>One row of a table.</summary>
    /// <param name="table">The table.</param>
    /// <param name="index">The row number, counting from 0 (negative counts from the end).</param>
    /// <returns>The row as a dictionary from column name to cell.</returns>
    [NodeName("Table.Row")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("row")]
    [NodeDescription("One row of a table as a dictionary from column name to cell (0 is the first row; -1 the last).")]
    [NodeSearchTags("row", "record", "line", "get")]
    public static Dictionary<string, object?> Row(CamelGraphTable table, int index)
    {
        Require(table, "Table.Row");
        var position = index < 0 ? table.RowCount + index : index;
        if (position < 0 || position >= table.RowCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                "Table.Row: row " + index.ToString(CultureInfo.InvariantCulture) + " does not exist; the table has " +
                table.RowCount.ToString(CultureInfo.InvariantCulture) + " row(s).");
        }

        return table.RowAsDictionary(position);
    }

    // ------------------------------------------------------------------ Columns

    /// <summary>Keeps only some columns, in the order given.</summary>
    /// <param name="table">The table.</param>
    /// <param name="columns">The names to keep (a list, or one text with names separated by commas).</param>
    /// <returns>The narrower table.</returns>
    [NodeName("Table.SelectColumns")]
    [return: NodeName("table")]
    [NodeDescription("Keeps only the listed columns, in that order (names as a list or one comma-separated text).")]
    [NodeSearchTags("keep", "columns", "pick", "reorder", "project", "narrow")]
    public static CamelGraphTable SelectColumns(CamelGraphTable table, IList<object?> columns)
    {
        Require(table, "Table.SelectColumns");
        var indexes = ColumnNames(columns, "Table.SelectColumns").Select(c => table.IndexOf(c, "Table.SelectColumns")).ToList();
        return new CamelGraphTable(
            indexes.Select(i => table.Headers[i]),
            table.Rows.Select(r => (IReadOnlyList<object?>)indexes.Select(i => r[i]).ToList()).ToList());
    }

    /// <summary>Drops some columns.</summary>
    /// <param name="table">The table.</param>
    /// <param name="columns">The names to remove (a list, or one text with names separated by commas).</param>
    /// <returns>The table without them.</returns>
    [NodeName("Table.RemoveColumns")]
    [return: NodeName("table")]
    [NodeDescription("Drops the listed columns and keeps the rest.")]
    [NodeSearchTags("drop", "delete", "columns", "remove", "hide")]
    public static CamelGraphTable RemoveColumns(CamelGraphTable table, IList<object?> columns)
    {
        Require(table, "Table.RemoveColumns");
        var drop = new HashSet<int>(ColumnNames(columns, "Table.RemoveColumns").Select(c => table.IndexOf(c, "Table.RemoveColumns")));
        var keep = Enumerable.Range(0, table.ColumnCount).Where(i => !drop.Contains(i)).ToList();
        return new CamelGraphTable(
            keep.Select(i => table.Headers[i]),
            table.Rows.Select(r => (IReadOnlyList<object?>)keep.Select(i => r[i]).ToList()).ToList());
    }

    /// <summary>Renames a column.</summary>
    /// <param name="table">The table.</param>
    /// <param name="column">The current name.</param>
    /// <param name="newName">The new name.</param>
    /// <returns>The table with the column renamed.</returns>
    [NodeName("Table.RenameColumn")]
    [return: NodeName("table")]
    [NodeDescription("Renames one column.")]
    [NodeSearchTags("rename", "header", "column name")]
    public static CamelGraphTable RenameColumn(CamelGraphTable table, string column, string newName)
    {
        Require(table, "Table.RenameColumn");
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("Table.RenameColumn needs a new name.", nameof(newName));
        }

        var index = table.IndexOf(column, "Table.RenameColumn");
        var names = table.Headers.ToArray();
        names[index] = newName;
        return new CamelGraphTable(names, table.Rows.Select(r => (IReadOnlyList<object?>)r).ToList());
    }

    /// <summary>Adds a column with the given cells.</summary>
    /// <param name="table">The table.</param>
    /// <param name="name">The new column's name.</param>
    /// <param name="values">A list with one value per row, or a single value repeated on every row.</param>
    /// <returns>The table with the column added at the end.</returns>
    [NodeName("Table.AddColumn")]
    [return: NodeName("table")]
    [NodeDescription("Adds a column at the end: a list with one value per row, or a single value repeated on every row.")]
    [NodeSearchTags("add", "column", "append", "new field", "constant")]
    public static CamelGraphTable AddColumn(CamelGraphTable table, string name, object? values)
    {
        Require(table, "Table.AddColumn");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Table.AddColumn needs a name for the new column.", nameof(name));
        }

        IList? list = values is IList l && !(values is string) ? l : null;
        if (list != null && list.Count != table.RowCount)
        {
            throw new ArgumentException(
                "Table.AddColumn: the table has " + table.RowCount.ToString(CultureInfo.InvariantCulture) + " row(s) but " +
                list.Count.ToString(CultureInfo.InvariantCulture) + " value(s) were given. Give one per row, or a single value.");
        }

        var rows = new List<IReadOnlyList<object?>>(table.RowCount);
        for (int i = 0; i < table.RowCount; i++)
        {
            var cells = new List<object?>(table.Rows[i]) { list != null ? list[i] : values };
            rows.Add(cells);
        }

        return new CamelGraphTable(table.Headers.Concat(new[] { name }), rows);
    }

    /// <summary>Adds a column calculated from the other columns.</summary>
    /// <param name="table">The table.</param>
    /// <param name="name">The new column's name.</param>
    /// <param name="formula">A formula over the column names, e.g. <c>Width * Height</c> or <c>[Fire Rating] / 60</c> (names with spaces in square brackets).</param>
    /// <returns>The table with the calculated column added at the end.</returns>
    [NodeName("Table.AddFormulaColumn")]
    [return: NodeName("table")]
    [NodeDescription("Adds a column calculated per row from the others, e.g. \"Width * Height * Length / 1000000000\" (same formula language as Math.Formula; [Names with spaces] in brackets).")]
    [NodeSearchTags("formula", "calculated", "computed", "column", "expression", "volume", "area")]
    public static CamelGraphTable AddFormulaColumn(CamelGraphTable table, string name, string formula)
    {
        Require(table, "Table.AddFormulaColumn");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Table.AddFormulaColumn needs a name for the new column.", nameof(name));
        }

        Func<double[], double> compiled;
        try
        {
            compiled = FormulaParser.Compile(formula, table.Headers);
        }
        catch (FormatException ex)
        {
            throw new FormatException("Table.AddFormulaColumn: " + ex.Message, ex);
        }

        var rows = new List<IReadOnlyList<object?>>(table.RowCount);
        for (int r = 0; r < table.RowCount; r++)
        {
            var values = new double[table.ColumnCount];
            for (int c = 0; c < values.Length; c++)
            {
                values[c] = CellNumber(table.Rows[r][c], 0d);
            }

            var cells = new List<object?>(table.Rows[r]) { compiled(values) };
            rows.Add(cells);
        }

        return new CamelGraphTable(table.Headers.Concat(new[] { name }), rows);
    }

    // ------------------------------------------------------------------ Rows

    /// <summary>Splits the rows into those that pass a test and those that do not.</summary>
    /// <param name="table">The table.</param>
    /// <param name="column">The column to test.</param>
    /// <param name="test">The test: ==, !=, &gt;, &gt;=, &lt;, &lt;=, contains, !contains, startsWith, endsWith, matches (wildcards * and ?), regex, isNull, notNull, isEmpty, notEmpty.</param>
    /// <param name="value">What to test against (unused by the null / empty tests).</param>
    /// <param name="ignoreCase">True (default) ignores upper/lower case in text tests.</param>
    /// <returns>The rows that passed and the rows that did not, both as tables.</returns>
    [NodeName("Table.Filter")]
    [MultiReturn("matched", "rejected")]
    [PortKinds("data", "data")]
    [NodeDescription("Splits a table by a test on one column — Level == \"L02\", Length > 3000, Name matches \"W-*\" — into the rows that pass and the rows that do not.")]
    [NodeSearchTags("filter", "where", "query", "select rows", "search", "keep")]
    public static Dictionary<string, object> Filter(
        CamelGraphTable table,
        string column,
        [NodeChoices("==", "!=", ">", ">=", "<", "<=", "contains", "!contains", "startsWith", "endsWith", "matches", "regex", "isNull", "notNull", "isEmpty", "notEmpty")]
        string test = "==",
        object? value = null,
        bool ignoreCase = true)
    {
        Require(table, "Table.Filter");
        var index = table.IndexOf(column, "Table.Filter");
        var op = ValueTests.Normalize(test, "Table.Filter");
        var matched = new List<IReadOnlyList<object?>>();
        var rejected = new List<IReadOnlyList<object?>>();
        foreach (var row in table.Rows)
        {
            (ValueTests.Test(op, row[index], value, ignoreCase, "Table.Filter") ? matched : rejected).Add(row);
        }

        return new Dictionary<string, object>
        {
            ["matched"] = new CamelGraphTable(table.Headers, matched),
            ["rejected"] = new CamelGraphTable(table.Headers, rejected),
        };
    }

    /// <summary>Sorts the rows by one or more columns.</summary>
    /// <param name="table">The table.</param>
    /// <param name="columns">Columns to sort by, separated by commas; add " desc" or a leading "-" for largest first: <c>Level, -Length</c>.</param>
    /// <param name="descending">True sorts every column largest first (a column can still opt out with " asc").</param>
    /// <returns>The sorted table; empty cells go last and equal rows keep their order.</returns>
    [NodeName("Table.Sort")]
    [return: NodeName("table")]
    [NodeDescription("Sorts the rows by one or more columns (\"Level, -Length\" sorts by level, then longest first); numbers numerically, text alphabetically, empty cells last.")]
    [NodeSearchTags("sort", "order", "rank", "arrange", "ascending", "descending")]
    public static CamelGraphTable Sort(CamelGraphTable table, string columns, bool descending = false)
    {
        Require(table, "Table.Sort");
        var keys = ColumnNames(new List<object?> { columns }, "Table.Sort").Select(spec =>
        {
            var desc = descending;
            var name = spec;
            if (name.StartsWith("-", StringComparison.Ordinal))
            {
                desc = true;
                name = name.Substring(1).Trim();
            }
            else if (name.EndsWith(" desc", StringComparison.OrdinalIgnoreCase))
            {
                desc = true;
                name = name.Substring(0, name.Length - 5).Trim();
            }
            else if (name.EndsWith(" asc", StringComparison.OrdinalIgnoreCase))
            {
                desc = false;
                name = name.Substring(0, name.Length - 4).Trim();
            }

            return (Index: table.IndexOf(name, "Table.Sort"), Descending: desc);
        }).ToList();

        if (keys.Count == 0)
        {
            throw new ArgumentException("Table.Sort needs at least one column to sort by.", nameof(columns));
        }

        // Every cell of a sort column is read once into a sort key (empty, number, text or other, with its number and text worked out
        // only when a comparison needs them), so a comparison of two cells is plain code: no parsing per pair and, for a column that
        // mixes numbers and text, none of the two exceptions per pair the old comparer threw and caught. OrderBy/ThenBy still do the
        // sorting (stable, same sequence of comparisons), so the order is exactly what it was.
        var rows = table.Rows;
        var keysOfColumn = new Dictionary<int, CellSortKey[]>();
        IOrderedEnumerable<int>? ordered = null;
        foreach (var key in keys)
        {
            if (!keysOfColumn.TryGetValue(key.Index, out var cells))
            {
                cells = new CellSortKey[rows.Count];
                for (var i = 0; i < cells.Length; i++)
                {
                    cells[i] = CellSortKey.Of(rows[i][key.Index]);
                }

                keysOfColumn[key.Index] = cells;
            }

            var comparer = CellSortKey.Comparer(key.Descending);
            ordered = ordered == null
                ? Enumerable.Range(0, cells.Length).OrderBy(i => cells[i], comparer)
                : ordered.ThenBy(i => cells[i], comparer);
        }

        return new CamelGraphTable(table.Headers, ordered!.Select(i => (IReadOnlyList<object?>)rows[i]).ToList());
    }

    /// <summary>Removes duplicate rows.</summary>
    /// <param name="table">The table.</param>
    /// <param name="columns">Columns that must differ (separated by commas); leave empty to compare whole rows.</param>
    /// <returns>The table with only the first row of each kind.</returns>
    [NodeName("Table.Distinct")]
    [return: NodeName("table")]
    [NodeDescription("Keeps only the first row for each distinct value (of the given columns, or of the whole row).")]
    [NodeSearchTags("unique", "duplicates", "dedupe", "remove duplicates", "distinct")]
    public static CamelGraphTable Distinct(CamelGraphTable table, string columns = "")
    {
        Require(table, "Table.Distinct");
        var indexes = string.IsNullOrWhiteSpace(columns)
            ? Enumerable.Range(0, table.ColumnCount).ToList()
            : ColumnNames(new List<object?> { columns }, "Table.Distinct").Select(c => table.IndexOf(c, "Table.Distinct")).ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<IReadOnlyList<object?>>();
        foreach (var row in table.Rows)
        {
            if (seen.Add(string.Join("\u0001", indexes.Select(i => KeyText(row[i])))))
            {
                rows.Add(row);
            }
        }

        return new CamelGraphTable(table.Headers, rows);
    }

    /// <summary>Stacks tables one below the other.</summary>
    /// <param name="tables">The tables, top first (wire several into the one socket).</param>
    /// <returns>One table with every column of every table (matched by name; missing cells are empty).</returns>
    [NodeName("Table.Concat")]
    [return: NodeName("table")]
    [NodeDescription("Stacks tables one below the other, matching columns by name; a column one table lacks is left empty — weekly snapshots, one table per model.")]
    [NodeSearchTags("append", "union", "stack", "merge", "combine", "rbind")]
    public static CamelGraphTable Concat([MultiInput] IList<object?> tables)
    {
        RequireList(tables, "tables", "Table.Concat");
        var list = new List<CamelGraphTable>();
        for (int i = 0; i < tables.Count; i++)
        {
            if (!(tables[i] is CamelGraphTable t))
            {
                throw new ArgumentException("Table.Concat: item " + i.ToString(CultureInfo.InvariantCulture) + " is not a table.");
            }

            list.Add(t);
        }

        var headers = new List<string>();
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in list)
        {
            foreach (var header in table.Headers)
            {
                if (!lookup.ContainsKey(header.Trim()))
                {
                    lookup[header.Trim()] = headers.Count;
                    headers.Add(header);
                }
            }
        }

        var rows = new List<IReadOnlyList<object?>>();
        foreach (var table in list)
        {
            var map = table.Headers.Select(h => lookup[h.Trim()]).ToArray();
            foreach (var row in table.Rows)
            {
                var cells = new object?[headers.Count];
                for (int c = 0; c < map.Length; c++)
                {
                    cells[map[c]] = row[c];
                }

                rows.Add(cells);
            }
        }

        return new CamelGraphTable(headers, rows);
    }

    /// <summary>Takes a block of rows.</summary>
    /// <param name="table">The table.</param>
    /// <param name="start">The first row to take, counting from 0.</param>
    /// <param name="count">How many rows (-1 takes all the rest).</param>
    /// <returns>The rows from start on, at most count of them.</returns>
    [NodeName("Table.Slice")]
    [return: NodeName("table")]
    [NodeDescription("Takes count rows from a starting row (count -1 takes all the rest) — the first 10 rows: start 0, count 10.")]
    [NodeSearchTags("take", "skip", "head", "top", "limit", "page", "subset")]
    public static CamelGraphTable Slice(CamelGraphTable table, int start = 0, int count = -1)
    {
        Require(table, "Table.Slice");
        if (start < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "Table.Slice: the start row cannot be negative.");
        }

        var rows = table.Rows.Skip(start);
        if (count >= 0)
        {
            rows = rows.Take(count);
        }

        return new CamelGraphTable(table.Headers, rows.Select(r => (IReadOnlyList<object?>)r).ToList());
    }

    // ------------------------------------------------------------------ Analysis

    /// <summary>Groups rows and totals them.</summary>
    /// <param name="table">The table.</param>
    /// <param name="by">The column(s) to group by, separated by commas; leave empty for one total row.</param>
    /// <param name="aggregations">What to work out per group, one text each: <c>count</c>, <c>sum:Volume</c>, <c>average:Length as AvgLength</c>. Functions: count, sum, average, min, max, median, first, last, list, distinct.</param>
    /// <returns>One row per group: the grouping columns followed by one column per aggregation.</returns>
    [NodeName("Table.GroupBy")]
    [return: NodeName("table")]
    [NodeDescription("Groups rows by one or more columns and works out count, sum, average, min, max, median, first, last, list or distinct per group — \"sum:Volume\", \"count\" — the pivot-table core for quantities.")]
    [NodeSearchTags("group", "aggregate", "summary", "rollup", "total", "sum", "count", "pivot", "takeoff", "qto")]
    public static CamelGraphTable GroupBy(CamelGraphTable table, string by, IList<object?> aggregations)
    {
        Require(table, "Table.GroupBy");
        var groupIndexes = string.IsNullOrWhiteSpace(by)
            ? new List<int>()
            : ColumnNames(new List<object?> { by }, "Table.GroupBy").Select(c => table.IndexOf(c, "Table.GroupBy")).ToList();
        var specs = ParseAggregations(aggregations, table, "Table.GroupBy");
        if (specs.Count == 0 && groupIndexes.Count == 0)
        {
            throw new ArgumentException("Table.GroupBy needs something to group by or something to work out.");
        }

        var groups = new List<List<object?[]>>();
        var firstRows = new List<object?[]>();
        var lookup = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in table.Rows)
        {
            var key = string.Join("\u0001", groupIndexes.Select(i => KeyText(row[i])));
            if (!lookup.TryGetValue(key, out var position))
            {
                position = groups.Count;
                lookup[key] = position;
                groups.Add(new List<object?[]>());
                firstRows.Add(row);
            }

            groups[position].Add(row);
        }

        if (groups.Count == 0 && groupIndexes.Count == 0)
        {
            // Totals of an empty table: one row of zeros / nothing rather than no row at all.
            groups.Add(new List<object?[]>());
            firstRows.Add(new object?[table.ColumnCount]);
        }

        var headers = groupIndexes.Select(i => table.Headers[i]).Concat(specs.Select(s => s.Alias)).ToList();
        var rows = new List<IReadOnlyList<object?>>(groups.Count);
        for (int g = 0; g < groups.Count; g++)
        {
            var cells = groupIndexes.Select(i => firstRows[g][i]).ToList();
            foreach (var spec in specs)
            {
                cells.Add(Aggregate(spec, groups[g], "Table.GroupBy"));
            }

            rows.Add(cells);
        }

        return new CamelGraphTable(headers, rows);
    }

    /// <summary>Turns the values of one column into columns of their own.</summary>
    /// <param name="table">The table.</param>
    /// <param name="rowColumn">The column whose values become the rows.</param>
    /// <param name="columnColumn">The column whose values become the new columns.</param>
    /// <param name="valueColumn">The column to total in each cell (not needed for count).</param>
    /// <param name="aggregation">How to combine: sum (default), count, average, min, max, first, last.</param>
    /// <returns>One row per row value, one column per column value.</returns>
    [NodeName("Table.Pivot")]
    [return: NodeName("table")]
    [NodeDescription("Cross-tabulates: rows from one column, columns from another, each cell the sum (or count, average …) of a third — clashes by level and status.")]
    [NodeSearchTags("pivot", "cross-tab", "matrix", "crosstab", "summary", "levels by status")]
    public static CamelGraphTable Pivot(
        CamelGraphTable table,
        string rowColumn,
        string columnColumn,
        string valueColumn = "",
        [NodeChoices("sum", "count", "average", "min", "max", "first", "last")] string aggregation = "sum")
    {
        Require(table, "Table.Pivot");
        var rowIndex = table.IndexOf(rowColumn, "Table.Pivot");
        var columnIndex = table.IndexOf(columnColumn, "Table.Pivot");
        var function = AggregationFunction(aggregation, "Table.Pivot");
        var valueIndex = -1;
        if (function != "count")
        {
            valueIndex = table.IndexOf(valueColumn, "Table.Pivot");
        }

        var rowKeys = new List<object?>();
        var columnKeys = new List<object?>();
        var rowLookup = new Dictionary<string, int>(StringComparer.Ordinal);
        var columnLookup = new Dictionary<string, int>(StringComparer.Ordinal);
        var cellRows = new Dictionary<(int, int), List<object?[]>>();
        foreach (var row in table.Rows)
        {
            var rk = KeyText(row[rowIndex]);
            if (!rowLookup.TryGetValue(rk, out var r))
            {
                r = rowKeys.Count;
                rowLookup[rk] = r;
                rowKeys.Add(row[rowIndex]);
            }

            var ck = KeyText(row[columnIndex]);
            if (!columnLookup.TryGetValue(ck, out var c))
            {
                c = columnKeys.Count;
                columnLookup[ck] = c;
                columnKeys.Add(row[columnIndex]);
            }

            if (!cellRows.TryGetValue((r, c), out var bucket))
            {
                bucket = new List<object?[]>();
                cellRows[(r, c)] = bucket;
            }

            bucket.Add(row);
        }

        var spec = new AggregationSpec(function, valueIndex, function);
        var headers = new List<string?> { table.Headers[rowIndex] };
        headers.AddRange(columnKeys.Select(k => (string?)CellOrBlank(k)));
        // A cell nothing fell into is 0 for count and sum, empty otherwise. The pivot is rows x columns cells, mostly empty for
        // high-cardinality keys, so box that zero once instead of once per cell, and size each row for its cells up front.
        object? emptyCell = function == "count" || function == "sum" ? BoxedZero : null;
        var rows = new List<IReadOnlyList<object?>>(rowKeys.Count);
        for (int r = 0; r < rowKeys.Count; r++)
        {
            var cells = new List<object?>(columnKeys.Count + 1) { rowKeys[r] };
            for (int c = 0; c < columnKeys.Count; c++)
            {
                if (cellRows.TryGetValue((r, c), out var bucket))
                {
                    cells.Add(Aggregate(spec, bucket, "Table.Pivot"));
                }
                else
                {
                    cells.Add(emptyCell);
                }
            }

            rows.Add(cells);
        }

        return new CamelGraphTable(headers, rows);
    }

    /// <summary>Joins two tables on a key column.</summary>
    /// <param name="left">The main table.</param>
    /// <param name="right">The table to add columns from.</param>
    /// <param name="leftKey">The key column of the left table.</param>
    /// <param name="rightKey">The key column of the right table (leave empty to use the same name as leftKey).</param>
    /// <param name="kind">inner keeps rows with a match on both sides; left keeps every left row; outer keeps every row of both.</param>
    /// <returns>The joined table: the left columns, then the right columns (a repeated name gets " (right)").</returns>
    [NodeName("Table.Join")]
    [return: NodeName("table")]
    [NodeDescription("Joins two tables on a key column (inner, left or outer) — add the Excel columns to the model data by GUID or mark. Keys match as text, so 42 and \"42\" are the same.")]
    [NodeSearchTags("join", "merge", "lookup", "vlookup", "combine", "match", "relate")]
    public static CamelGraphTable Join(
        CamelGraphTable left,
        CamelGraphTable right,
        string leftKey,
        string rightKey = "",
        [NodeChoices("inner", "left", "outer")] string kind = "inner")
    {
        Require(left, "Table.Join");
        if (right == null)
        {
            throw new ArgumentNullException(nameof(right), "Table.Join requires a second table. Wire a table into the 'right' input.");
        }

        var li = left.IndexOf(leftKey, "Table.Join");
        var ri = right.IndexOf(string.IsNullOrWhiteSpace(rightKey) ? leftKey : rightKey, "Table.Join");
        var joinKind = (kind ?? string.Empty).Trim().ToLowerInvariant();
        if (joinKind != "inner" && joinKind != "left" && joinKind != "outer")
        {
            throw new ArgumentException("Table.Join: kind must be inner, left or outer; got '" + kind + "'.", nameof(kind));
        }

        // The right key column is dropped when it carries the same name as the left one (it would only repeat it).
        var sameName = string.Equals(left.Headers[li].Trim(), right.Headers[ri].Trim(), StringComparison.OrdinalIgnoreCase);
        var rightColumns = Enumerable.Range(0, right.ColumnCount).Where(i => !(sameName && i == ri)).ToList();
        var leftNames = new HashSet<string>(left.Headers, StringComparer.OrdinalIgnoreCase);
        var headers = left.Headers.Concat(rightColumns.Select(i => leftNames.Contains(right.Headers[i]) ? right.Headers[i] + " (right)" : right.Headers[i])).ToList();

        var rightByKey = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int i = 0; i < right.RowCount; i++)
        {
            var key = KeyText(right.Rows[i][ri]);
            if (!rightByKey.TryGetValue(key, out var bucket))
            {
                bucket = new List<int>();
                rightByKey[key] = bucket;
            }

            bucket.Add(i);
        }

        var usedRight = new HashSet<int>();
        var rows = new List<IReadOnlyList<object?>>();
        foreach (var row in left.Rows)
        {
            if (rightByKey.TryGetValue(KeyText(row[li]), out var matches))
            {
                foreach (var m in matches)
                {
                    usedRight.Add(m);
                    rows.Add(row.Concat(rightColumns.Select(i => right.Rows[m][i])).ToList());
                }
            }
            else if (joinKind != "inner")
            {
                rows.Add(row.Concat(rightColumns.Select(_ => (object?)null)).ToList());
            }
        }

        if (joinKind == "outer")
        {
            for (int m = 0; m < right.RowCount; m++)
            {
                if (usedRight.Contains(m))
                {
                    continue;
                }

                var cells = new object?[left.ColumnCount];
                cells[li] = right.Rows[m][ri];
                rows.Add(cells.Concat(rightColumns.Select(i => right.Rows[m][i])).ToList());
            }
        }

        return new CamelGraphTable(headers, rows);
    }

    // ------------------------------------------------------------------ Output

    /// <summary>The table as text.</summary>
    /// <param name="table">The table.</param>
    /// <param name="format">markdown (default), csv, tsv, or html.</param>
    /// <returns>The table rendered in that format, ready for Text.WriteToFile, an e-mail or Log.Write.</returns>
    [NodeName("Table.ToText")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("text")]
    [NodeDescription("Renders a table as Markdown, CSV, tab-separated or an HTML table — for a report, an e-mail or a log.")]
    [NodeSearchTags("markdown", "csv", "html", "render", "print", "string", "report")]
    public static string ToText(CamelGraphTable table, [NodeChoices("markdown", "csv", "tsv", "html")] string format = "markdown")
    {
        Require(table, "Table.ToText");
        switch ((format ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "":
            case "markdown":
            case "md":
                return TableText.Markdown(table);
            case "csv":
                return TableText.Delimited(table, ',');
            case "tsv":
            case "tab":
                return TableText.Delimited(table, '\t');
            case "html":
                return TableText.Html(table);
            default:
                throw new ArgumentException("Table.ToText: format must be markdown, csv, tsv or html; got '" + format + "'.", nameof(format));
        }
    }

    /// <summary>Reads a CSV file into a table.</summary>
    /// <param name="path">The CSV file.</param>
    /// <param name="delimiter">The cell separator (default a comma).</param>
    /// <param name="firstRowIsHeader">True (default) takes the first row as the column names.</param>
    /// <returns>The table.</returns>
    [NodeName("Table.FromCsvFile")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("table")]
    [NodeDescription("Reads a CSV file straight into a table (numbers become numbers, everything else stays text).")]
    [NodeSearchTags("csv", "read", "import", "load", "file")]
    public static CamelGraphTable FromCsvFile(string path, string delimiter = ",", bool firstRowIsHeader = true)
    {
        return FromRows(FileNodes.ReadCsv(path, delimiter), null, firstRowIsHeader);
    }

    /// <summary>Writes a table to a CSV file.</summary>
    /// <param name="table">The table.</param>
    /// <param name="path">The file to write (overwritten; folders are created).</param>
    /// <param name="delimiter">The cell separator (default a comma).</param>
    /// <returns>The path that was written.</returns>
    [NodeName("Table.ToCsvFile")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("path")]
    [NodeDescription("Writes a table, with its column names, to a CSV file.")]
    [NodeSearchTags("csv", "write", "export", "save", "file")]
    public static string ToCsvFile(CamelGraphTable table, string path, string delimiter = ",")
    {
        Require(table, "Table.ToCsvFile");
        var data = new List<object?> { new List<object?>(table.Headers.Cast<object?>()) };
        data.AddRange(table.Rows.Select(r => (object?)new List<object?>(r)));
        return FileNodes.WriteCsv(path, data, delimiter);
    }

    /// <summary>Reads an Excel worksheet into a table.</summary>
    /// <param name="path">The .xlsx file.</param>
    /// <param name="sheet">The worksheet name (empty takes the first sheet).</param>
    /// <param name="firstRowIsHeader">True (default) takes the first row as the column names.</param>
    /// <returns>The table.</returns>
    [NodeName("Table.FromExcelFile")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("table")]
    [NodeDescription("Reads an Excel worksheet straight into a table.")]
    [NodeSearchTags("excel", "xlsx", "read", "import", "load", "spreadsheet")]
    public static CamelGraphTable FromExcelFile(string path, string sheet = "", bool firstRowIsHeader = true)
    {
        var read = ExcelNodes.ReadFromFile(path, sheet, firstRowIsHeader);
        var headers = (IList<string>)read["headers"];
        return FromRows((IList<object?>)read["rows"], headers.Cast<object?>().ToList(), false);
    }

    /// <summary>Writes a table to an Excel worksheet.</summary>
    /// <param name="table">The table.</param>
    /// <param name="path">The .xlsx file to write.</param>
    /// <param name="sheet">The worksheet name.</param>
    /// <param name="append">True adds the sheet to an existing workbook instead of replacing the file.</param>
    /// <returns>The path that was written.</returns>
    [NodeName("Table.ToExcelFile")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("path")]
    [NodeDescription("Writes a table, with its column names, to an Excel worksheet (append adds a sheet to an existing workbook).")]
    [NodeSearchTags("excel", "xlsx", "write", "export", "save", "spreadsheet", "sheet")]
    public static string ToExcelFile(CamelGraphTable table, string path, string sheet = "Sheet1", bool append = false)
    {
        Require(table, "Table.ToExcelFile");
        return ExcelNodes.WriteToFile(
            path,
            table.Rows.Select(r => (object?)new List<object?>(r)).ToList(),
            table.Headers.Cast<object?>().ToList(),
            sheet,
            append);
    }

    // ------------------------------------------------------------------ Helpers

    private static void Require(CamelGraphTable? table, string nodeName)
    {
        if (table == null)
        {
            throw new ArgumentNullException(nameof(table), nodeName + " requires a table. Wire one into the 'table' input (Table.FromRows makes one).");
        }
    }

    private static void RequireList(IList<object?>? list, string port, string nodeName)
    {
        if (list == null)
        {
            throw new ArgumentNullException(port, nodeName + " requires a list. Wire one into the '" + port + "' input.");
        }
    }

    private static IReadOnlyList<object?> ToCells(object? row)
    {
        if (row is IList cells && !(row is string))
        {
            var copy = new List<object?>(cells.Count);
            foreach (var cell in cells)
            {
                copy.Add(cell);
            }

            return copy;
        }

        return new List<object?> { row };
    }

    // Column names given as a list, or as one text with names separated by commas.
    private static List<string> ColumnNames(IList<object?>? columns, string nodeName)
    {
        if (columns == null)
        {
            throw new ArgumentNullException(nameof(columns), nodeName + " requires column names. Wire a list of names (or one text with commas) into the 'columns' input.");
        }

        var names = new List<string>();
        foreach (var item in columns)
        {
            if (item == null)
            {
                continue;
            }

            foreach (var part in TypeCoercion.FormatValue(item).Split(','))
            {
                var name = part.Trim();
                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    private static string KeyText(object? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (value is string text)
        {
            return text;
        }

        return value is double number ? number.ToString("R", CultureInfo.InvariantCulture) : TypeCoercion.FormatValue(value);
    }

    private static string CellOrBlank(object? value) => value == null ? "(blank)" : CamelGraphTable.CellText(value);

    private static double CellNumber(object? cell, double fallback)
    {
        if (cell == null)
        {
            return fallback;
        }

        if (ValueComparison.IsNumeric(cell))
        {
            return ValueComparison.ToDouble(cell);
        }

        if (cell is bool flag)
        {
            return flag ? 1d : 0d;
        }

        return cell is string text && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private sealed class AggregationSpec
    {
        public AggregationSpec(string function, int column, string alias)
        {
            Function = function;
            Column = column;
            Alias = alias;
        }

        public string Function { get; }

        public int Column { get; }

        public string Alias { get; }
    }

    private static readonly string[] AggregationNames = { "count", "sum", "average", "min", "max", "median", "first", "last", "list", "distinct" };

    private static string AggregationFunction(string? name, string nodeName)
    {
        var key = (name ?? string.Empty).Trim().ToLowerInvariant();
        if (key == "avg" || key == "mean")
        {
            key = "average";
        }

        if (Array.IndexOf(AggregationNames, key) < 0)
        {
            throw new ArgumentException(
                nodeName + ": '" + name + "' is not an aggregation. Use one of: " + string.Join(", ", AggregationNames) + ".");
        }

        return key;
    }

    private static List<AggregationSpec> ParseAggregations(IList<object?>? aggregations, CamelGraphTable table, string nodeName)
    {
        var specs = new List<AggregationSpec>();
        if (aggregations == null)
        {
            return specs;
        }

        foreach (var item in aggregations)
        {
            if (item == null)
            {
                continue;
            }

            var text = TypeCoercion.FormatValue(item).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            string? alias = null;
            var asIndex = text.LastIndexOf(" as ", StringComparison.OrdinalIgnoreCase);
            if (asIndex > 0)
            {
                alias = text.Substring(asIndex + 4).Trim();
                text = text.Substring(0, asIndex).Trim();
            }

            var colon = text.IndexOf(':');
            var function = AggregationFunction(colon < 0 ? text : text.Substring(0, colon), nodeName);
            var column = -1;
            string label = function;
            if (function != "count" || colon >= 0)
            {
                if (colon < 0)
                {
                    throw new ArgumentException(nodeName + ": '" + text + "' needs a column, e.g. \"" + function + ":Volume\".");
                }

                var columnName = text.Substring(colon + 1).Trim();
                column = table.IndexOf(columnName, nodeName);
                label = function + "(" + table.Headers[column] + ")";
            }

            specs.Add(new AggregationSpec(function, column, string.IsNullOrWhiteSpace(alias) ? label : alias!));
        }

        return specs;
    }

    private static object? Aggregate(AggregationSpec spec, List<object?[]> rows, string nodeName)
    {
        if (spec.Function == "count" && spec.Column < 0)
        {
            return (double)rows.Count;
        }

        var cells = rows.Select(r => r[spec.Column]).ToList();
        switch (spec.Function)
        {
            case "count":
                return (double)cells.Count(c => c != null && !(c is string s && s.Length == 0));
            case "first":
                return cells.FirstOrDefault();
            case "last":
                return cells.LastOrDefault();
            case "list":
                return cells;
            case "distinct":
                return (double)cells.Select(KeyText).Distinct(StringComparer.Ordinal).Count();
            case "min":
            case "max":
            {
                var present = cells.Where(c => c != null && !(c is string blankText && blankText.Trim().Length == 0)).ToList();
                if (present.Count == 0)
                {
                    return null;
                }

                // Numbers that arrived as text (a column read from pasted rows) compare, and come back, as numbers.
                var numbersOnly = new List<double>();
                foreach (var cell in present)
                {
                    if (ValueComparison.IsNumeric(cell!))
                    {
                        numbersOnly.Add(ValueComparison.ToDouble(cell!));
                    }
                    else if (cell is string numericText && double.TryParse(numericText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedNumber))
                    {
                        numbersOnly.Add(parsedNumber);
                    }
                    else
                    {
                        break;
                    }
                }

                if (numbersOnly.Count == present.Count)
                {
                    return spec.Function == "min" ? numbersOnly.Min() : numbersOnly.Max();
                }

                var best = present[0];
                foreach (var cell in present.Skip(1))
                {
                    var order = ValueTests.Order(cell, best, nodeName);
                    if (spec.Function == "min" ? order < 0 : order > 0)
                    {
                        best = cell;
                    }
                }

                return best;
            }
        }

        // The rest are numeric: blanks are skipped, anything else must read as a number.
        var numbers = new List<double>();
        foreach (var cell in cells)
        {
            if (cell == null || (cell is string blank && blank.Trim().Length == 0))
            {
                continue;
            }

            if (ValueComparison.IsNumeric(cell))
            {
                numbers.Add(ValueComparison.ToDouble(cell));
            }
            else if (cell is string text && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                numbers.Add(parsed);
            }
            else
            {
                throw new ArgumentException(
                    nodeName + ": '" + TypeCoercion.FormatValue(cell) + "' in the " + spec.Function + " of '" + spec.Alias + "' is not a number.");
            }
        }

        switch (spec.Function)
        {
            case "sum":
                return numbers.Sum();
            case "average":
                return numbers.Count == 0 ? null : (object)(numbers.Sum() / numbers.Count);
            case "median":
            {
                if (numbers.Count == 0)
                {
                    return null;
                }

                var sorted = numbers.OrderBy(n => n).ToList();
                var mid = sorted.Count / 2;
                return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2d;
            }
        }

        throw new InvalidOperationException(nodeName + ": unhandled aggregation " + spec.Function + ".");
    }
}
