using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Files;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Types;
using CamelGraph.Nodes.Internal;

namespace CamelGraph.Nodes;

/// <summary>
/// Excel (.xlsx) nodes built on the in-box <see cref="XlsxLite"/>
/// reader/writer — no Excel installation and no external library required.
/// Legacy .xls (BIFF) files are out of scope: save them as .xlsx first.
/// Excel stores dates as serial numbers (days since 1899-12-30); they arrive
/// as plain numbers when reading, and DateTime values are written as real
/// date cells (a serial number with a date format), so they show as dates.
/// </summary>
[NodeCategory("File")]
public static class ExcelNodes
{
    /// <summary>
    /// Reads an .xlsx worksheet into rows of cells plus the header row and the
    /// workbook's sheet names. Cell values are strings, numbers, booleans or
    /// null (empty cells); shared strings, inline strings, formula results and
    /// rich text are all handled. Dates arrive as Excel serial numbers.
    /// Rows are padded with empty cells to the widest row, and empty rows at the
    /// end of the sheet are dropped.
    /// </summary>
    /// <param name="path">Path to the .xlsx file. A relative path starts in the graph's folder.</param>
    /// <param name="sheet">Worksheet name; empty selects the first sheet.</param>
    /// <param name="hasHeaders">True when the first row is a header row (it is split off into "headers").</param>
    /// <param name="trimEmptyRows">True (default) to drop the empty rows at the end of the sheet (rows that only carry formatting).</param>
    /// <returns>Dictionary with "rows", "headers" and "sheetNames".</returns>
    [NodeName("Excel.ReadFromFile")]
    [MultiReturn("rows", "headers", "sheetNames")]
    [PortKinds("", "text*", "text*")]
    [NodeDescription("Reads an .xlsx worksheet into rows + headers + the sheet names (dates arrive as Excel serial numbers, convert them with DateTime.FromExcelSerial; .xls is not supported). Text cells such as 007 stay text. Every row is padded with empty cells to the widest row, and empty rows at the end of the sheet - the ones that only carry formatting - are dropped (Advanced > trimEmptyRows; blank rows in the middle are kept so row numbers stay true). For a table use Table.FromExcelFile. A relative path starts in the graph's folder.")]
    [NodeSearchTags("xlsx", "excel", "spreadsheet", "workbook", "table", "import", "sheet", "dates")]
    [NodeAliases("CamelGraph.Nodes.ExcelNodes.ReadFromFile@string,string,bool")]
    public static Dictionary<string, object> ReadFromFile(
        [NodePath(NodePathMode.Open, Filter = FileFilters.Excel)] string path,
        string sheet = "",
        [NodePanel("Advanced")] bool hasHeaders = true,
        [NodePanel("Advanced")] bool trimEmptyRows = true)
    {
        const string node = "Excel.ReadFromFile";
        var file = FileNodes.RequireExistingFile(path, node);
        var sheetNames = FileErrors.Run(node, file, false, () => XlsxLite.SheetNames(file));
        var grid = FileErrors.Run(node, file, false, () => XlsxLite.ReadSheet(file, string.IsNullOrEmpty(sheet) ? null : sheet));

        if (trimEmptyRows)
        {
            var last = grid.Count - 1;
            while (last >= 0 && IsEmptyRow(grid[last]))
            {
                last--;
            }

            grid.RemoveRange(last + 1, grid.Count - last - 1);
        }

        var width = 0;
        foreach (var row in grid)
        {
            width = Math.Max(width, row.Count);
        }

        foreach (var row in grid)
        {
            while (row.Count < width)
            {
                row.Add(null);
            }
        }

        var headers = new List<string>();
        var rows = new List<object?>();
        int firstDataRow = 0;
        if (hasHeaders && grid.Count > 0)
        {
            foreach (var cell in grid[0])
            {
                headers.Add(CellText.Format(cell));
            }

            firstDataRow = 1;
        }

        for (int i = firstDataRow; i < grid.Count; i++)
        {
            rows.Add(grid[i]);
        }

        return new Dictionary<string, object>
        {
            ["rows"] = rows,
            ["headers"] = headers,
            ["sheetNames"] = sheetNames,
        };
    }

    /// <summary>
    /// Writes rows (each row a list of cells) to an .xlsx worksheet,
    /// optionally prefixed by a header row. With <paramref name="append"/>
    /// true the sheet is added to an existing workbook (replacing a
    /// same-named sheet); because the package is re-emitted from cell values,
    /// styles and formulas of workbooks produced by other tools are not
    /// preserved (a warning says so). Strings, numbers and booleans round-trip;
    /// DateTime values are written as real date cells; a list in a cell is
    /// written as text like "[1, 2]"; NaN and Infinity are written as text.
    /// The workbook is built beside the target and then put in place, so a
    /// failure never leaves a truncated file.
    /// </summary>
    /// <param name="path">Destination .xlsx path (missing directories are created). A relative path starts in the graph's folder.</param>
    /// <param name="rows">List of rows; each row a list of cell values (scalar rows become single-cell rows).</param>
    /// <param name="headers">Optional header row written before the data rows.</param>
    /// <param name="sheet">Worksheet name (Excel rules: 1-31 chars, no : \ / ? * [ ]).</param>
    /// <param name="append">True to add the sheet to an existing workbook instead of replacing the file.</param>
    /// <returns>The path that was written, for sequencing further file nodes.</returns>
    [NodeName("Excel.WriteToFile")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [return: NodeName("path")]
    [NodeDescription("Writes rows (+ optional headers) to an .xlsx worksheet. Dates become real date cells (shown as dates in Excel), a list in a cell is written as text like [1, 2], and NaN or Infinity as text. Advanced > append adds the sheet to an existing workbook and replaces a sheet of the same name; the workbook is rebuilt from its cell values, so formulas, formatting, charts and pictures of a workbook made in Excel are not kept (a warning says so). The file is built beside the target and then put in place, so a failure never leaves a broken workbook. For a table use Table.ToExcelFile. A relative path starts in the graph's folder.")]
    [NodeSearchTags("xlsx", "excel", "spreadsheet", "workbook", "export", "save", "sheet", "dates")]
    public static string WriteToFile(
        [NodePath(NodePathMode.Save, Filter = FileFilters.Excel)] string path,
        IList<object?> rows,
        IList<object?>? headers = null,
        [NodePanel("Advanced")] string sheet = "Sheet1",
        [NodePanel("Advanced")] bool append = false)
    {
        const string node = "Excel.WriteToFile";
        var file = FileNodes.ResolveForWriting(path, node);
        if (rows == null)
        {
            throw new ArgumentNullException(
                nameof(rows), node + " requires a list of rows (each row a list of cells).");
        }

        var grid = new List<IReadOnlyList<object?>?>(rows.Count + 1);
        if (headers != null && headers.Count > 0)
        {
            grid.Add(new List<object?>(headers));
        }

        foreach (var row in rows)
        {
            grid.Add(ToCells(row));
        }

        var report = FileErrors.Run(node, file, true, () => XlsxLite.WriteSheet(file, grid, sheet, append));
        if (report.NonFiniteCells > 0)
        {
            NodeWarnings.Add(
                report.NonFiniteCells.ToString(CultureInfo.InvariantCulture) +
                (report.NonFiniteCells == 1 ? " cell held NaN or Infinity, which a worksheet cannot store as a number; it was" : " cells held NaN or Infinity, which a worksheet cannot store as a number; they were") +
                " written as text.");
        }

        if (report.LostOnRewrite)
        {
            NodeWarnings.Add(
                "Appending rebuilt the existing workbook from its cell values" +
                (report.FormulaCells > 0 ? ": " + report.FormulaCells.ToString(CultureInfo.InvariantCulture) + " formula cell(s) now hold their last result as a plain value, and" : ": ") +
                " formatting, charts and pictures of its sheets were not kept. Keep a copy of the original if you need them.");
        }

        return file;
    }

    // ------------------------------------------------------------------
    // Helpers (not imported as nodes: non-public).
    // ------------------------------------------------------------------
    private static bool IsEmptyRow(List<object?> row)
    {
        foreach (var cell in row)
        {
            if (cell != null && !(cell is string text && text.Length == 0))
            {
                return false;
            }
        }

        return true;
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

        // Scalar (or null) rows are written as single-cell rows, matching CSV.WriteToFile.
        return new List<object?> { row };
    }
}
