using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CamelGraph.Core.Execution;
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

    /// <summary>The column names of a table. Retired: Table.Info gives the same list as its <c>headers</c> output.</summary>
    /// <param name="table">The table.</param>
    /// <returns>The names, in column order.</returns>
    [NodeName("Table.Headers")]
    [NodeDeprecated("Table.Info")]
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
    [NodeDescription("How many rows and columns a table has, and its column names (the headers output is the list of names, in column order).")]
    [NodeSearchTags("size", "count", "shape", "dimensions", "length", "columns", "names", "header", "headers", "fields", "column names")]
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
    public static Dictionary<string, object?> Row(CamelGraphTable table, [NodeRange(-10000000, 10000000, SoftMin = -10, SoftMax = 1000)] int index)
    {
        Require(table, "Table.Row");
        var position = index < 0 ? table.RowCount + index : index;
        if (position < 0 || position >= table.RowCount)
        {
            throw new ArgumentOutOfRangeException(
                null,
                "Table.Row: row " + index.ToString(CultureInfo.InvariantCulture) + " does not exist; the table has " +
                table.RowCount.ToString(CultureInfo.InvariantCulture) + " row(s) (0 is the first row, -1 the last).");
        }

        return table.RowAsDictionary(position);
    }

    // ------------------------------------------------------------------ Columns

    /// <summary>Keeps only some columns, in the order given.</summary>
    /// <param name="table">The table.</param>
    /// <param name="columns">The names to keep: a list, or one text with names separated by commas. A column whose own name contains a comma ("Area, gross") works when you type or wire that name whole.</param>
    /// <returns>The narrower table.</returns>
    [NodeName("Table.SelectColumns")]
    [return: NodeName("table")]
    [NodeDescription("Keeps only the listed columns, in that order. Names come as a list or as one text separated by commas; a column whose name has a comma in it (\"Area, gross\") is found by its whole name. At least one name is needed.")]
    [NodeSearchTags("keep", "columns", "pick", "reorder", "project", "narrow")]
    public static CamelGraphTable SelectColumns(CamelGraphTable table, [PortKinds("text")] IList<object?> columns)
    {
        Require(table, "Table.SelectColumns");
        var indexes = ResolveColumns(table, columns, "Table.SelectColumns", "columns", false).Select(c => c.Index).ToList();
        if (indexes.Count == 0)
        {
            throw new ArgumentException(
                "Table.SelectColumns: no column names were given in 'columns', so there would be nothing left to keep. Type or wire the names to keep (" +
                string.Join(", ", table.Headers.Take(3)) + (table.ColumnCount > 3 ? ", ..." : string.Empty) + ").");
        }

        return new CamelGraphTable(
            indexes.Select(i => table.Headers[i]),
            table.Rows.Select(r => (IReadOnlyList<object?>)indexes.Select(i => r[i]).ToList()).ToList());
    }

    /// <summary>Drops some columns.</summary>
    /// <param name="table">The table.</param>
    /// <param name="columns">The names to remove: a list, or one text with names separated by commas. A column whose own name contains a comma works when you type or wire that name whole.</param>
    /// <returns>The table without them.</returns>
    [NodeName("Table.RemoveColumns")]
    [return: NodeName("table")]
    [NodeDescription("Drops the listed columns and keeps the rest. Names come as a list or as one text separated by commas; a column whose name has a comma in it (\"Area, gross\") is found by its whole name. An empty list removes nothing and says so.")]
    [NodeSearchTags("drop", "delete", "columns", "remove", "hide")]
    public static CamelGraphTable RemoveColumns(CamelGraphTable table, [PortKinds("text")] IList<object?> columns)
    {
        Require(table, "Table.RemoveColumns");
        var resolved = ResolveColumns(table, columns, "Table.RemoveColumns", "columns", false);
        if (resolved.Count == 0)
        {
            NodeWarnings.Add("Table.RemoveColumns: no column names were given in 'columns', so no column was removed.");
        }

        var drop = new HashSet<int>(resolved.Select(c => c.Index));
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

        var perRow = ValuesPerRow(table, values, "Table.AddColumn");
        var rows = new List<IReadOnlyList<object?>>(table.RowCount);
        for (int i = 0; i < table.RowCount; i++)
        {
            var cells = new List<object?>(table.Rows[i]) { perRow[i] };
            rows.Add(cells);
        }

        return new CamelGraphTable(table.Headers.Concat(new[] { name }), rows);
    }

    /// <summary>Replaces the cells of a column, or adds the column when there is none of that name.</summary>
    /// <param name="table">The table.</param>
    /// <param name="name">The column to replace (found like every other column name: exactly, then ignoring case and spaces). A name the table does not have adds a new column at the end.</param>
    /// <param name="values">A list with one value per row, or a single value repeated on every row.</param>
    /// <returns>The table with that column's cells replaced, in the same position (the column keeps its name).</returns>
    [NodeName("Table.SetColumn")]
    [return: NodeName("table")]
    [NodeDescription("Replaces the cells of a column in place, keeping its position and name, or adds the column at the end when there is none of that name. Give a list with one value per row, or a single value repeated on every row — take a column out with Table.Column, clean it with String or List nodes, and put it back here.")]
    [NodeSearchTags("replace column", "overwrite", "update column", "set", "clean", "modify column", "column", "fill")]
    public static CamelGraphTable SetColumn(CamelGraphTable table, string name, object? values)
    {
        Require(table, "Table.SetColumn");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Table.SetColumn needs the name of the column to replace (or to add).");
        }

        var perRow = ValuesPerRow(table, values, "Table.SetColumn");
        var index = table.TryIndexOf(name);
        var rows = new List<IReadOnlyList<object?>>(table.RowCount);
        for (int i = 0; i < table.RowCount; i++)
        {
            var cells = new List<object?>(table.Rows[i]);
            if (index >= 0)
            {
                cells[index] = perRow[i];
            }
            else
            {
                cells.Add(perRow[i]);
            }

            rows.Add(cells);
        }

        return new CamelGraphTable(index >= 0 ? table.Headers : table.Headers.Concat(new[] { name }), rows);
    }

    /// <summary>Adds a column calculated from the other columns.</summary>
    /// <param name="table">The table.</param>
    /// <param name="name">The new column's name.</param>
    /// <param name="formula">A formula over the column names, e.g. <c>Width * Height</c> or <c>[Fire Rating] / 60</c> (names with spaces in square brackets).</param>
    /// <returns>The table with the calculated column added at the end; a row where a column the formula uses is blank or not a number, and a result that is not a finite number, get an empty cell.</returns>
    [NodeName("Table.AddFormulaColumn")]
    [return: NodeName("table")]
    [NodeDescription("Adds a column calculated per row from the others, e.g. \"Width * Height * Length / 1000000000\" (same formula language as Math.Formula; [Names with spaces] in brackets). A blank or text cell is never counted as 0: a row where a column the formula uses is blank or not a number gets an empty cell, and so does a result that is not a finite number (a division by zero). The node turns amber and says how many rows were left empty.")]
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

        // A cell that is blank or not a number is NaN, never 0. NaN spreads through the arithmetic, so a row whose formula uses such a
        // cell comes out as NaN and gets an empty cell; the columns the formula names tell the warning which cell was the cause.
        var used = ReferencedColumns(formula, table.Headers);
        var rows = new List<IReadOnlyList<object?>>(table.RowCount);
        var emptied = 0;
        var nonFinite = 0;
        string? firstCause = null;
        for (int r = 0; r < table.RowCount; r++)
        {
            var values = new double[table.ColumnCount];
            var cause = -1;
            for (int c = 0; c < values.Length; c++)
            {
                if (!TryCellNumber(table.Rows[r][c], out values[c]))
                {
                    values[c] = double.NaN;
                    if (cause < 0 && used.Contains(c))
                    {
                        cause = c;
                    }
                }
            }

            object? result = null;
            if (cause >= 0)
            {
                emptied++;
                firstCause ??= "'" + table.Headers[cause] + "' in row " + (r + 1).ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                var number = compiled(values);
                if (double.IsNaN(number) || double.IsInfinity(number))
                {
                    nonFinite++;
                }
                else
                {
                    result = number;
                }
            }

            rows.Add(new List<object?>(table.Rows[r]) { result });
        }

        if (emptied > 0)
        {
            NodeWarnings.Add(
                "Table.AddFormulaColumn: " + emptied.ToString(CultureInfo.InvariantCulture) + " row(s) got an empty cell because a column the formula uses is blank or not a number there (first: " +
                firstCause + "). A blank is never counted as 0.");
        }

        if (nonFinite > 0)
        {
            NodeWarnings.Add(
                "Table.AddFormulaColumn: " + nonFinite.ToString(CultureInfo.InvariantCulture) + " result(s) were not a finite number (a division by zero, for example) and were left empty.");
        }

        return new CamelGraphTable(table.Headers.Concat(new[] { name }), rows);
    }

    // ------------------------------------------------------------------ Rows

    /// <summary>Splits the rows into those that pass a test and those that do not.</summary>
    /// <param name="table">The table.</param>
    /// <param name="column">The column to test.</param>
    /// <param name="test">The test: ==, !=, &gt;, &gt;=, &lt;, &lt;=, contains, !contains, startsWith, endsWith, matches (wildcards * and ?), regex, in, notIn, isNull, notNull, isEmpty, notEmpty.</param>
    /// <param name="value">What to test against (unused by the null / empty tests). For in and notIn: the values to look for, as a list or as one text with the values separated by commas.</param>
    /// <param name="ignoreCase">True (default) ignores upper/lower case in text tests.</param>
    /// <returns>The rows that passed and the rows that did not, both as tables.</returns>
    [NodeName("Table.Filter")]
    [MultiReturn("matched", "rejected")]
    [PortKinds("data", "data")]
    [NodeDescription("Splits a table by a test on one column — Level == \"L02\", Length > 3000, Name matches \"W-*\", Level in \"L01, L02\" — into the rows that pass and the rows that do not. A blank cell never passes > >= < <=, and neither does text that is not a number when you compare with a number (the node turns amber and counts them); they land in the rejected table. in and notIn take a list of values, or one text with the values separated by commas; an empty cell is in no list. Regex tests are case sensitive unless ignoreCase is ticked.")]
    [NodeSearchTags("filter", "where", "query", "select rows", "search", "keep", "in list", "is one of", "member of", "not in")]
    public static Dictionary<string, object> Filter(
        CamelGraphTable table,
        string column,
        [NodeChoices("==", "!=", ">", ">=", "<", "<=", "contains", "!contains", "startsWith", "endsWith", "matches", "regex", "in", "notIn", "isNull", "notNull", "isEmpty", "notEmpty")]
        string test = "==",
        object? value = null,
        bool ignoreCase = true)
    {
        Require(table, "Table.Filter");
        var index = table.IndexOf(column, "Table.Filter");
        var membership = FilterMembership(test);
        var op = membership == null ? ValueTests.Normalize(test, "Table.Filter") : null;
        var members = membership == null ? null : FilterMembers(value);
        var ordering = op == ">" || op == ">=" || op == "<" || op == "<=";
        if (ordering && (value == null || (value is string blankValue && blankValue.Trim().Length == 0)))
        {
            NodeWarnings.Add("Table.Filter: the test '" + op + "' needs a value to compare with in 'value', and none was given, so no row passes.");
        }

        var matched = new List<IReadOnlyList<object?>>();
        var rejected = new List<IReadOnlyList<object?>>();
        var incomparable = 0;
        string? firstIncomparable = null;
        for (int r = 0; r < table.RowCount; r++)
        {
            var row = table.Rows[r];
            var cell = row[index];
            bool passes;
            if (membership != null)
            {
                var found = cell != null && members!.Any(member => ValueTests.AreEqual(cell, member, ignoreCase));
                passes = membership == "in" ? found : !found;
            }
            else if (ordering)
            {
                if (cell == null || (cell is string blankCell && blankCell.Trim().Length == 0) || value == null || (value is string blankTarget && blankTarget.Trim().Length == 0))
                {
                    // No value is not smaller than anything, and not larger either: it is left out of every ordering test.
                    passes = false;
                }
                else if (ValueTests.CannotBeOrdered(cell, value))
                {
                    passes = false;
                    incomparable++;
                    firstIncomparable ??= "'" + CamelGraphTable.CellText(cell) + "' in row " + (r + 1).ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    try
                    {
                        passes = ValueTests.Test(op!, cell, value, ignoreCase, "Table.Filter");
                    }
                    catch (InvalidOperationException)
                    {
                        passes = false;
                        incomparable++;
                        firstIncomparable ??= "'" + CamelGraphTable.CellText(cell) + "' in row " + (r + 1).ToString(CultureInfo.InvariantCulture);
                    }
                }
            }
            else
            {
                try
                {
                    passes = ValueTests.Test(op!, cell, value, ignoreCase, "Table.Filter");
                }
                catch (PatternTimedOutException)
                {
                    throw new InvalidOperationException(
                        "Table.Filter: the regular expression took longer than 2 seconds on row " + (r + 1).ToString(CultureInfo.InvariantCulture) +
                        " and was stopped. Simplify the pattern (nested repeats such as (a+)+ are the usual cause).");
                }
            }

            (passes ? matched : rejected).Add(row);
        }

        if (incomparable > 0)
        {
            NodeWarnings.Add(
                "Table.Filter: " + incomparable.ToString(CultureInfo.InvariantCulture) + " row(s) hold something in '" + table.Headers[index] +
                "' that cannot be compared with " + CamelGraphTable.CellText(value) + " (first: " + firstIncomparable + "); they did not pass the test.");
        }

        return new Dictionary<string, object>
        {
            ["matched"] = new CamelGraphTable(table.Headers, matched),
            ["rejected"] = new CamelGraphTable(table.Headers, rejected),
        };
    }

    /// <summary>Sorts the rows by one or more columns.</summary>
    /// <param name="table">The table.</param>
    /// <param name="columns">Columns to sort by: a list of names, or one text with names separated by commas; add " desc" or a leading "-" for largest first: <c>Level, -Length</c>. A list sorts by its first name, then the next.</param>
    /// <param name="descending">True sorts every column largest first (a column can still opt out with " asc").</param>
    /// <returns>The sorted table; empty cells go last and equal rows keep their order.</returns>
    [NodeName("Table.Sort")]
    [NodeAliases("CamelGraph.Nodes.TableToolkitNodes.Sort@CamelGraph.Nodes.CamelGraphTable,string,bool")]
    [return: NodeName("table")]
    [NodeDescription("Sorts the rows by one or more columns, as a list of names or one text (\"Level, -Length\" sorts by level, then longest first); numbers numerically, text alphabetically, empty cells last. A list of names sorts once by all of them, in that order.")]
    [NodeSearchTags("sort", "order", "rank", "arrange", "ascending", "descending")]
    public static CamelGraphTable Sort(CamelGraphTable table, [PortKinds("text")] IList<object?> columns, bool descending = false)
    {
        Require(table, "Table.Sort");
        var keys = ResolveColumns(table, columns, "Table.Sort", "columns", true)
            .Select(c => (Index: c.Index, Descending: c.Descending ?? descending))
            .ToList();

        if (keys.Count == 0)
        {
            throw new ArgumentException("Table.Sort needs at least one column to sort by. Type or wire the column names into 'columns'.");
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
    /// <param name="columns">Columns that must differ: a list of names, or one text with names separated by commas; leave empty to compare whole rows.</param>
    /// <returns>The table with only the first row of each kind.</returns>
    [NodeName("Table.Distinct")]
    [NodeAliases("CamelGraph.Nodes.TableToolkitNodes.Distinct@CamelGraph.Nodes.CamelGraphTable,string")]
    [return: NodeName("table")]
    [NodeDescription("Keeps only the first row for each distinct value of the given columns (a list of names or one text with commas), or of the whole row when none are given.")]
    [NodeSearchTags("unique", "duplicates", "dedupe", "remove duplicates", "distinct")]
    public static CamelGraphTable Distinct(CamelGraphTable table, [PortKinds("text")] IList<object?>? columns = null)
    {
        Require(table, "Table.Distinct");
        var named = columns == null ? new List<ColumnRef>() : ResolveColumns(table, columns, "Table.Distinct", "columns", false);
        var indexes = named.Count == 0
            ? Enumerable.Range(0, table.ColumnCount).ToList()
            : named.Select(c => c.Index).ToList();
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
    /// <param name="start">The first row to take, counting from 0 (not below 0).</param>
    /// <param name="count">How many rows (-1 takes all the rest; not below -1).</param>
    /// <returns>The rows from start on, at most count of them.</returns>
    [NodeName("Table.Slice")]
    [return: NodeName("table")]
    [NodeDescription("Takes count rows from a starting row (count -1 takes all the rest) — the first 10 rows: start 0, count 10.")]
    [NodeSearchTags("take", "skip", "head", "top", "limit", "page", "subset")]
    public static CamelGraphTable Slice(
        CamelGraphTable table,
        [NodeRange(0, 10000000, SoftMax = 1000)] int start = 0,
        [NodeRange(-1, 10000000, SoftMin = -1, SoftMax = 1000)] int count = -1)
    {
        Require(table, "Table.Slice");
        if (start < 0)
        {
            throw new ArgumentOutOfRangeException(null, "Table.Slice: the start row cannot be negative; 0 is the first row.");
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
    /// <param name="by">The column(s) to group by: a list of names, or one text with names separated by commas; leave empty for one total row.</param>
    /// <param name="aggregations">What to work out per group, one text each: <c>count</c>, <c>sum:Volume</c>, <c>average:Length as AvgLength</c>. Functions: count, sum, average, min, max, median, first, last, list, distinct.</param>
    /// <returns>One row per group: the grouping columns followed by one column per aggregation.</returns>
    [NodeName("Table.GroupBy")]
    [NodeAliases("CamelGraph.Nodes.TableToolkitNodes.GroupBy@CamelGraph.Nodes.CamelGraphTable,string,System.Collections.Generic.IList<object>")]
    [return: NodeName("table")]
    [NodeDescription("Groups rows by one or more columns (a list of names or one text with commas) and works out count, sum, average, min, max, median, first, last, list or distinct per group — \"sum:Volume\", \"count\" — the pivot-table core for quantities. Blank cells are skipped by sum, average and median; text in a number column is an error that names it.")]
    [NodeSearchTags("group", "aggregate", "summary", "rollup", "total", "sum", "count", "pivot", "takeoff", "qto")]
    public static CamelGraphTable GroupBy(CamelGraphTable table, [PortKinds("text")] IList<object?> by, IList<object?> aggregations)
    {
        Require(table, "Table.GroupBy");
        var groupIndexes = ResolveColumns(table, by, "Table.GroupBy", "by", false).Select(c => c.Index).ToList();
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
    /// <param name="aggregation">How to combine: sum (default), count, average, min, max, median, first, last, list or distinct.</param>
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
        [NodeChoices("sum", "count", "average", "min", "max", "median", "first", "last", "list", "distinct")] string aggregation = "sum")
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

    /// <summary>Joins two tables on one or more key columns.</summary>
    /// <param name="left">The main table.</param>
    /// <param name="right">The table to add columns from.</param>
    /// <param name="leftKey">The key column(s) of the left table: one name, several names separated by commas, or a list of names.</param>
    /// <param name="rightKey">The key column(s) of the right table, as many as in leftKey and in the same order (leave empty to use the same names as leftKey).</param>
    /// <param name="kind">inner keeps rows with a match on both sides; left keeps every left row; outer keeps every row of both.</param>
    /// <returns>The joined table: the left columns, then the right columns (a repeated name gets " (right)").</returns>
    [NodeName("Table.Join")]
    [NodeAliases("CamelGraph.Nodes.TableToolkitNodes.Join@CamelGraph.Nodes.CamelGraphTable,CamelGraph.Nodes.CamelGraphTable,string,string,string")]
    [return: NodeName("table")]
    [NodeDescription("Joins two tables on one or more key columns (inner, left or outer) — add the Excel columns to the model data by GUID or mark. Keys match as text, so 42 and \"42\" are the same, and a key that looks like a GUID matches whatever its capitals and small letters (other text keys must match exactly). An empty key never matches anything, not even another empty key. leftKey and rightKey take one column name, several separated by commas (or a list) to join on Level and Mark together; leave rightKey empty when both tables use the same names. Table.Unmatched lists the left rows that found no partner (it replaces Table.JoinByKey's unmatchedKeys).")]
    [NodeSearchTags("join", "merge", "lookup", "vlookup", "combine", "match", "relate", "joinbykey", "by key", "guid", "mark")]
    public static CamelGraphTable Join(
        CamelGraphTable left,
        CamelGraphTable right,
        [PortKinds("text")] IList<object?> leftKey,
        [PortKinds("text")] IList<object?>? rightKey = null,
        [NodeChoices("inner", "left", "outer")] string kind = "inner")
    {
        return JoinTables(left, right, leftKey, rightKey, kind, "Table.Join", out _);
    }

    /// <summary>The rows of the left table that find no partner in the right table.</summary>
    /// <param name="left">The main table.</param>
    /// <param name="right">The table to look the keys up in.</param>
    /// <param name="leftKey">The key column(s) of the left table: one name, several names separated by commas, or a list of names.</param>
    /// <param name="rightKey">The key column(s) of the right table, as many as in leftKey and in the same order (leave empty to use the same names as leftKey).</param>
    /// <returns>The left rows with no partner, with the left columns only (rows with a blank key are always among them).</returns>
    [NodeName("Table.Unmatched")]
    [return: NodeName("table")]
    [NodeDescription("The rows of the left table that find no partner in the right table, using the same keys and the same matching as Table.Join (GUIDs match whatever their case, a blank key never matches) — which model elements have no row in the Excel list, which GUIDs were not found.")]
    [NodeSearchTags("unmatched", "anti join", "missing", "not found", "no match", "difference", "left only", "joinbykey", "unmatchedkeys", "guid", "mark")]
    public static CamelGraphTable Unmatched(
        CamelGraphTable left,
        CamelGraphTable right,
        [PortKinds("text")] IList<object?> leftKey,
        [PortKinds("text")] IList<object?>? rightKey = null)
    {
        JoinTables(left, right, leftKey, rightKey, "left", "Table.Unmatched", out var unmatched);
        return unmatched;
    }

    // The join itself, shared by Table.Join and Table.Unmatched: the joined table, and the left rows that found no partner.
    private static CamelGraphTable JoinTables(
        CamelGraphTable left,
        CamelGraphTable right,
        IList<object?> leftKey,
        IList<object?>? rightKey,
        string kind,
        string nodeName,
        out CamelGraphTable unmatchedRows)
    {
        Require(left, nodeName);
        if (right == null)
        {
            throw new ArgumentNullException(null, nodeName + " requires a second table. Wire a table into the 'right' input.");
        }

        var leftKeys = ResolveColumns(left, leftKey, nodeName, "leftKey", false, "the left table").Select(c => c.Index).ToList();
        if (leftKeys.Count == 0)
        {
            throw new ArgumentException(nodeName + " needs the key column of the left table. Type or wire its name into 'leftKey' (for example " + (left.ColumnCount > 0 ? left.Headers[0] : "GUID") + ").");
        }

        var rightNamed = rightKey == null ? new List<ColumnRef>() : ResolveColumns(right, rightKey, nodeName, "rightKey", false, "the right table");
        List<int> rightKeys;
        if (rightNamed.Count == 0)
        {
            rightKeys = leftKeys.Select(i => FindColumn(right, left.Headers[i], nodeName, "the right table")).ToList();
        }
        else if (rightNamed.Count != leftKeys.Count)
        {
            throw new ArgumentException(
                nodeName + ": 'leftKey' names " + leftKeys.Count.ToString(CultureInfo.InvariantCulture) + " column(s) but 'rightKey' names " +
                rightNamed.Count.ToString(CultureInfo.InvariantCulture) + ". Give the same number on both sides, in the same order, or leave 'rightKey' empty to use the left names.");
        }
        else
        {
            rightKeys = rightNamed.Select(c => c.Index).ToList();
        }

        var joinKind = (kind ?? string.Empty).Trim().ToLowerInvariant();
        if (joinKind != "inner" && joinKind != "left" && joinKind != "outer")
        {
            throw new ArgumentException(nodeName + ": kind must be inner, left or outer; got '" + kind + "'.");
        }

        // A right key column is dropped when it carries the same name as its left partner (it would only repeat it).
        var dropped = new HashSet<int>();
        for (int k = 0; k < leftKeys.Count; k++)
        {
            if (string.Equals(left.Headers[leftKeys[k]].Trim(), right.Headers[rightKeys[k]].Trim(), StringComparison.OrdinalIgnoreCase))
            {
                dropped.Add(rightKeys[k]);
            }
        }

        var rightColumns = Enumerable.Range(0, right.ColumnCount).Where(i => !dropped.Contains(i)).ToList();
        var leftNames = new HashSet<string>(left.Headers, StringComparer.OrdinalIgnoreCase);
        var headers = left.Headers.Concat(rightColumns.Select(i => leftNames.Contains(right.Headers[i]) ? right.Headers[i] + " (right)" : right.Headers[i])).ToList();

        // A row with a blank key cell is not in the lookup at all: blanks never match each other.
        var rightByKey = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int i = 0; i < right.RowCount; i++)
        {
            var key = JoinKey(right.Rows[i], rightKeys);
            if (key == null)
            {
                continue;
            }

            if (!rightByKey.TryGetValue(key, out var bucket))
            {
                bucket = new List<int>();
                rightByKey[key] = bucket;
            }

            bucket.Add(i);
        }

        var usedRight = new HashSet<int>();
        var rows = new List<IReadOnlyList<object?>>();
        var unmatched = new List<IReadOnlyList<object?>>();
        foreach (var row in left.Rows)
        {
            var leftValue = JoinKey(row, leftKeys);
            if (leftValue != null && rightByKey.TryGetValue(leftValue, out var matches))
            {
                foreach (var m in matches)
                {
                    usedRight.Add(m);
                    rows.Add(row.Concat(rightColumns.Select(i => right.Rows[m][i])).ToList());
                }
            }
            else
            {
                unmatched.Add(row);
                if (joinKind != "inner")
                {
                    rows.Add(row.Concat(rightColumns.Select(_ => (object?)null)).ToList());
                }
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
                for (int k = 0; k < leftKeys.Count; k++)
                {
                    cells[leftKeys[k]] = right.Rows[m][rightKeys[k]];
                }

                rows.Add(cells.Concat(rightColumns.Select(i => right.Rows[m][i])).ToList());
            }
        }

        unmatchedRows = new CamelGraphTable(left.Headers, unmatched);
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
    /// <param name="delimiter">The cell separator: a comma (default), a semicolon, a bar or tab.</param>
    /// <param name="firstRowIsHeader">True (default) takes the first row as the column names.</param>
    /// <returns>The table.</returns>
    [NodeName("Table.FromCsvFile")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("table")]
    [NodeDescription("Reads a CSV file straight into a table (numbers become numbers, everything else stays text). The delimiter is a comma, a semicolon (European Excel), a bar or a tab.")]
    [NodeSearchTags("csv", "read", "import", "load", "file", "tsv", "tab", "semicolon")]
    public static CamelGraphTable FromCsvFile(
        [NodePath(NodePathMode.Open, Filter = "CSV files (*.csv)|*.csv|Text files (*.txt;*.tsv)|*.txt;*.tsv|All files (*.*)|*.*")] string path,
        [NodeChoices(",", ";", "|", "tab")] string delimiter = ",",
        bool firstRowIsHeader = true)
    {
        return FromRows(FileNodes.ReadCsv(path, CsvDelimiter(delimiter)), null, firstRowIsHeader);
    }

    /// <summary>Writes a table to a CSV file.</summary>
    /// <param name="table">The table to write: one table, not a list of them.</param>
    /// <param name="path">The file to write (replaced when it exists; folders are created).</param>
    /// <param name="delimiter">The cell separator: a comma (default), a semicolon, a bar or tab.</param>
    /// <returns>The path that was written.</returns>
    [NodeName("Table.ToCsvFile")]
    [NodeAliases("CamelGraph.Nodes.TableToolkitNodes.ToCsvFile@CamelGraph.Nodes.CamelGraphTable,string,string")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [return: NodeName("path")]
    [NodeDescription("Writes one table, with its column names, to a CSV file; a file that is already there is replaced. The delimiter is a comma, a semicolon, a bar or a tab. A list of tables is an error (they would overwrite each other): stack them with Table.Concat first.")]
    [NodeSearchTags("csv", "write", "export", "save", "file", "tsv", "tab", "semicolon")]
    public static string ToCsvFile(
        object? table,
        [NodePath(NodePathMode.Save, Filter = "CSV files (*.csv)|*.csv|Text files (*.txt;*.tsv)|*.txt;*.tsv|All files (*.*)|*.*")] string path,
        [NodeChoices(",", ";", "|", "tab")] string delimiter = ",")
    {
        var single = RequireOneTable(table, "Table.ToCsvFile");
        var data = new List<object?> { new List<object?>(single.Headers.Cast<object?>()) };
        data.AddRange(single.Rows.Select(r => (object?)new List<object?>(r)));
        return FileNodes.WriteCsv(path, data, CsvDelimiter(delimiter));
    }

    /// <summary>Reads an Excel worksheet into a table.</summary>
    /// <param name="path">The .xlsx file.</param>
    /// <param name="sheet">The worksheet name (empty takes the first sheet).</param>
    /// <param name="firstRowIsHeader">True (default) takes the first row as the column names.</param>
    /// <returns>The table.</returns>
    [NodeName("Table.FromExcelFile")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("table")]
    [NodeDescription("Reads an Excel worksheet straight into a table. Dates arrive as Excel serial numbers; DateTime.FromExcelSerial turns one into a date.")]
    [NodeSearchTags("excel", "xlsx", "read", "import", "load", "spreadsheet")]
    public static CamelGraphTable FromExcelFile(
        [NodePath(NodePathMode.Open, Filter = "Excel workbooks (*.xlsx)|*.xlsx|All files (*.*)|*.*")] string path,
        string sheet = "",
        bool firstRowIsHeader = true)
    {
        var read = ExcelNodes.ReadFromFile(path, sheet, firstRowIsHeader);
        var headers = (IList<string>)read["headers"];
        return FromRows((IList<object?>)read["rows"], headers.Cast<object?>().ToList(), false);
    }

    /// <summary>Writes a table to an Excel worksheet.</summary>
    /// <param name="table">The table to write: one table, not a list of them.</param>
    /// <param name="path">The .xlsx file to write.</param>
    /// <param name="sheet">The worksheet name.</param>
    /// <param name="append">True adds the sheet to an existing workbook instead of replacing the file.</param>
    /// <returns>The path that was written.</returns>
    [NodeName("Table.ToExcelFile")]
    [NodeAliases("CamelGraph.Nodes.TableToolkitNodes.ToExcelFile@CamelGraph.Nodes.CamelGraphTable,string,string,bool")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [return: NodeName("path")]
    [NodeDescription("Writes one table, with its column names, to an Excel worksheet. With append ticked the sheet is added to the workbook that is already there; without it the whole file is replaced, other sheets included. For several sheets in one workbook use one node per table, each with its own sheet name and append ticked, and wire the path of one into the next so they run in order. A list of tables is an error (they would overwrite each other).")]
    [NodeSearchTags("excel", "xlsx", "write", "export", "save", "spreadsheet", "sheet")]
    public static string ToExcelFile(
        object? table,
        [NodePath(NodePathMode.Save, Filter = "Excel workbooks (*.xlsx)|*.xlsx|All files (*.*)|*.*")] string path,
        string sheet = "Sheet1",
        bool append = false)
    {
        var single = RequireOneTable(table, "Table.ToExcelFile");
        return ExcelNodes.WriteToFile(
            path,
            single.Rows.Select(r => (object?)new List<object?>(r)).ToList(),
            single.Headers.Cast<object?>().ToList(),
            sheet,
            append);
    }

    // ------------------------------------------------------------------ Helpers

    private static void Require(CamelGraphTable? table, string nodeName)
    {
        if (table == null)
        {
            throw new ArgumentNullException(null, nodeName + " requires a table. Wire one into the 'table' input (Table.FromRows makes one).");
        }
    }

    // The writers take exactly one table. A list would make the engine call the node once per table, all of them writing the same
    // file, so the input is read whole and a list is refused with a sentence that says what to do.
    private static CamelGraphTable RequireOneTable(object? table, string nodeName)
    {
        if (table is CamelGraphTable single)
        {
            return single;
        }

        if (table == null)
        {
            throw new ArgumentNullException(null, nodeName + " requires a table. Wire one into the 'table' input (Table.FromRows makes one).");
        }

        if (table is IList list && !(table is string))
        {
            throw new ArgumentException(
                nodeName + " writes one table to one file, but 'table' holds a list of " + list.Count.ToString(CultureInfo.InvariantCulture) +
                " item(s), which would overwrite each other. Stack the tables into one with Table.Concat, or write each table with its own node (or a loop) to its own file.");
        }

        throw new ArgumentException(nodeName + ": the 'table' input holds something that is not a table. Table.FromRows makes one.");
    }

    // The delimiter choices of the CSV nodes: "tab" (or \t) is a tab character, anything else is passed on as typed.
    private static string CsvDelimiter(string? delimiter)
    {
        var text = delimiter ?? string.Empty;
        return string.Equals(text.Trim(), "tab", StringComparison.OrdinalIgnoreCase) || text == "\\t" || text == "\t" ? "\t" : text;
    }

    private static void RequireList(IList<object?>? list, string port, string nodeName)
    {
        if (list == null)
        {
            throw new ArgumentNullException(null, nodeName + " requires a list. Wire one into the '" + port + "' input.");
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

    // One column named on a 'columns' / 'by' / 'leftKey' input, with the sort direction when the text carried one.
    private readonly struct ColumnRef
    {
        public ColumnRef(int index, bool? descending)
        {
            Index = index;
            Descending = descending;
        }

        public int Index { get; }

        public bool? Descending { get; }
    }

    // Column names given as a list, or as one text with names separated by commas. Each item is first tried as a whole name, so a
    // column called "Area, gross (m2)" can be addressed; only when there is no such column is the item split at its commas.
    private static List<ColumnRef> ResolveColumns(
        CamelGraphTable table, IList<object?>? items, string nodeName, string port, bool allowDirection, string label = "the table")
    {
        if (items == null)
        {
            throw new ArgumentNullException(null, nodeName + " requires column names. Wire a list of names (or one text with commas) into the '" + port + "' input.");
        }

        var result = new List<ColumnRef>();
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item == null)
            {
                continue;
            }

            if (item is IDictionary || (item is IList && !(item is string)))
            {
                throw new ArgumentException(
                    nodeName + ": item " + i.ToString(CultureInfo.InvariantCulture) + " of '" + port +
                    "' is a list, not a column name. Give a flat list of names (List.Flatten flattens one level).");
            }

            var text = TypeCoercion.FormatValue(item).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (TryResolveColumn(table, text, allowDirection, out var whole))
            {
                result.Add(whole);
                continue;
            }

            if (text.IndexOf(',') < 0)
            {
                result.Add(ResolveColumn(table, text, allowDirection, nodeName, label));
                continue;
            }

            // Names separated by commas. A column whose own name holds a comma ("Area, gross (m2)") is still found: from each
            // position the longest run of pieces that together make a column name wins.
            var pieces = text.Split(',');
            var at = 0;
            while (at < pieces.Length)
            {
                var found = false;
                for (int length = pieces.Length - at; length >= 1 && !found; length--)
                {
                    var candidate = string.Join(",", pieces, at, length).Trim();
                    if (candidate.Length > 0 && TryResolveColumn(table, candidate, allowDirection, out var column))
                    {
                        result.Add(column);
                        at += length;
                        found = true;
                    }
                }

                if (!found)
                {
                    var name = pieces[at].Trim();
                    if (name.Length > 0)
                    {
                        result.Add(ResolveColumn(table, name, allowDirection, nodeName, label));
                    }

                    at++;
                }
            }
        }

        return result;
    }

    private static bool TryResolveColumn(CamelGraphTable table, string text, bool allowDirection, out ColumnRef column)
    {
        var index = table.TryIndexOf(text);
        if (index >= 0)
        {
            column = new ColumnRef(index, null);
            return true;
        }

        if (allowDirection)
        {
            var (name, descending) = SplitDirection(text);
            if (descending != null)
            {
                index = table.TryIndexOf(name);
                if (index >= 0)
                {
                    column = new ColumnRef(index, descending);
                    return true;
                }
            }
        }

        column = default;
        return false;
    }

    private static ColumnRef ResolveColumn(CamelGraphTable table, string text, bool allowDirection, string nodeName, string label)
    {
        if (TryResolveColumn(table, text, allowDirection, out var column))
        {
            return column;
        }

        FindColumn(table, allowDirection ? SplitDirection(text).Name : text, nodeName, label);
        throw new InvalidOperationException(nodeName + ": no column named '" + text + "'.");
    }

    // "Length desc", "-Length" and "Length asc" for the sort; anything else has no direction.
    private static (string Name, bool? Descending) SplitDirection(string spec)
    {
        if (spec.StartsWith("-", StringComparison.Ordinal))
        {
            return (spec.Substring(1).Trim(), true);
        }

        if (spec.EndsWith(" desc", StringComparison.OrdinalIgnoreCase))
        {
            return (spec.Substring(0, spec.Length - 5).Trim(), true);
        }

        if (spec.EndsWith(" asc", StringComparison.OrdinalIgnoreCase))
        {
            return (spec.Substring(0, spec.Length - 4).Trim(), false);
        }

        return (spec, null);
    }

    // The column of that name, or an error that lists the columns there are and says which table was meant.
    private static int FindColumn(CamelGraphTable table, string name, string nodeName, string label)
    {
        var index = table.TryIndexOf(name);
        if (index >= 0)
        {
            return index;
        }

        throw new ArgumentException(
            nodeName + ": " + label + " has no column named '" + name + "'. Columns: " +
            (table.ColumnCount == 0 ? "(none)" : string.Join(", ", table.Headers)) + ".");
    }

    // A list with one value per row, or one value for every row.
    private static object?[] ValuesPerRow(CamelGraphTable table, object? values, string nodeName)
    {
        IList? list = values is IList l && !(values is string) ? l : null;
        if (list != null && list.Count != table.RowCount)
        {
            throw new ArgumentException(
                nodeName + ": the table has " + table.RowCount.ToString(CultureInfo.InvariantCulture) + " row(s) but " +
                list.Count.ToString(CultureInfo.InvariantCulture) + " value(s) were given. Give one per row, or a single value.");
        }

        var cells = new object?[table.RowCount];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = list != null ? list[i] : values;
        }

        return cells;
    }

    // The tests that look a cell up in a set of values; the other tests belong to ValueTests.
    private static string? FilterMembership(string? test)
    {
        switch ((test ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "in":
            case "isin":
            case "oneof":
                return "in";
            case "notin":
            case "!in":
            case "isnotin":
                return "notIn";
            default:
                return null;
        }
    }

    // The values of an in / notIn test: a list as it is, one text split at its commas, anything else as a single value.
    private static List<object?> FilterMembers(object? value)
    {
        var members = new List<object?>();
        if (value == null)
        {
            return members;
        }

        if (value is string text)
        {
            foreach (var part in text.Split(','))
            {
                if (part.Trim().Length > 0)
                {
                    members.Add(part.Trim());
                }
            }

            return members;
        }

        if (value is IList list)
        {
            foreach (var item in list)
            {
                if (item != null)
                {
                    members.Add(item);
                }
            }

            return members;
        }

        members.Add(value);
        return members;
    }

    // The columns a formula names (the same reading of the text as FormulaParser: [bracketed names] and plain names that are not
    // followed by a bracket), so a blank or text cell only matters where the formula really uses it.
    private static HashSet<int> ReferencedColumns(string formula, IReadOnlyList<string> headers)
    {
        var used = new HashSet<int>();
        void Add(string name)
        {
            for (int c = 0; c < headers.Count; c++)
            {
                if (string.Equals(headers[c], name, StringComparison.OrdinalIgnoreCase))
                {
                    used.Add(c);
                    return;
                }
            }
        }

        var i = 0;
        while (i < formula.Length)
        {
            var ch = formula[i];
            if (char.IsDigit(ch) || (ch == '.' && i + 1 < formula.Length && char.IsDigit(formula[i + 1])))
            {
                while (i < formula.Length && (char.IsDigit(formula[i]) || formula[i] == '.'))
                {
                    i++;
                }

                if (i < formula.Length && (formula[i] == 'e' || formula[i] == 'E'))
                {
                    var j = i + 1;
                    if (j < formula.Length && (formula[j] == '+' || formula[j] == '-'))
                    {
                        j++;
                    }

                    if (j < formula.Length && char.IsDigit(formula[j]))
                    {
                        while (j < formula.Length && char.IsDigit(formula[j]))
                        {
                            j++;
                        }

                        i = j;
                    }
                }

                continue;
            }

            if (ch == '[')
            {
                var close = formula.IndexOf(']', i + 1);
                if (close < 0)
                {
                    break;
                }

                Add(formula.Substring(i + 1, close - i - 1).Trim());
                i = close + 1;
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                var start = i;
                while (i < formula.Length && (char.IsLetterOrDigit(formula[i]) || formula[i] == '_'))
                {
                    i++;
                }

                var next = i;
                while (next < formula.Length && char.IsWhiteSpace(formula[next]))
                {
                    next++;
                }

                if (!(next < formula.Length && formula[next] == '('))
                {
                    Add(formula.Substring(start, i - start));
                }

                continue;
            }

            i++;
        }

        return used;
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

    // A cell read as a number: a number, a true / false (1 / 0) or text that reads as a finite number. Blank, other text and
    // NaN / Infinity are not numbers.
    private static bool TryCellNumber(object? cell, out double number)
    {
        number = 0d;
        if (cell == null)
        {
            return false;
        }

        if (ValueComparison.IsNumeric(cell))
        {
            number = ValueComparison.ToDouble(cell);
        }
        else if (cell is bool flag)
        {
            number = flag ? 1d : 0d;
        }
        else if (!(cell is string text && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)))
        {
            return false;
        }

        return !double.IsNaN(number) && !double.IsInfinity(number);
    }

    /// <summary>A key cell with nothing in it: empty, or only spaces. A blank key never matches anything.</summary>
    /// <param name="value">The cell.</param>
    internal static bool IsBlankKey(object? value) => value == null || (value is string text && text.Trim().Length == 0);

    // A GUID as Navisworks, Revit and Excel write it: 32 hex digits, usually in 8-4-4-4-12 groups, optionally in { } or ( ).
    private static readonly Regex GuidLike = new Regex(
        @"^[{(]?[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}[})]?$", RegexOptions.CultureInvariant);

    // The text of one key cell for a join: numbers and text as KeyText, and a GUID in small letters, because the same GUID is
    // written in capitals by one tool and in small letters by another. Any other text must match exactly.
    private static string JoinCellText(object? cell)
    {
        var text = KeyText(cell);
        return text.Length >= 32 && text.Length <= 38 && GuidLike.IsMatch(text.Trim()) ? text.Trim().Trim('{', '}', '(', ')').ToLowerInvariant() : text;
    }

    // The text a join compares for one row, or null when any key cell is blank (such a row is never matched).
    private static string? JoinKey(object?[] row, IReadOnlyList<int> columns)
    {
        if (columns.Count == 1)
        {
            var only = row[columns[0]];
            return IsBlankKey(only) ? null : JoinCellText(only);
        }

        var parts = new string[columns.Count];
        for (int k = 0; k < parts.Length; k++)
        {
            var cell = row[columns[k]];
            if (IsBlankKey(cell))
            {
                return null;
            }

            parts[k] = JoinCellText(cell);
        }

        return string.Join("\u0001", parts);
    }

    /// <summary>The text of a join key: a number as its shortest exact form (so 42 and "42" agree), anything else as its display text.</summary>
    /// <param name="value">The key cell.</param>
    internal static string JoinKeyText(object? value) => KeyText(value);

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
