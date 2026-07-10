using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Types;
using Dyncamelo.Nodes.Internal;

namespace Dyncamelo.Nodes;

/// <summary>
/// Excel (.xlsx) nodes built on the in-box <see cref="XlsxLite"/>
/// reader/writer — no Excel installation and no external library required.
/// Legacy .xls (BIFF) files are out of scope: save them as .xlsx first.
/// Excel stores dates as serial numbers (days since 1899-12-30); they arrive
/// as plain numbers when reading, and DateTime values are written back as
/// serial numbers (they display as numbers in Excel until formatted as dates).
/// </summary>
[NodeCategory("File")]
public static class ExcelNodes
{
    /// <summary>
    /// Reads an .xlsx worksheet into rows of cells plus the header row and the
    /// workbook's sheet names. Cell values are strings, numbers, booleans or
    /// null (empty cells); shared strings, inline strings, formula results and
    /// rich text are all handled. Dates arrive as Excel serial numbers.
    /// </summary>
    /// <param name="path">Path to the .xlsx file.</param>
    /// <param name="sheet">Worksheet name; empty selects the first sheet.</param>
    /// <param name="hasHeaders">True when the first row is a header row (it is split off into "headers").</param>
    /// <returns>Dictionary with "rows", "headers" and "sheetNames".</returns>
    [NodeName("Excel.ReadFromFile")]
    [MultiReturn("rows", "headers", "sheetNames")]
    [NodeDescription("Reads an .xlsx worksheet into rows + headers (dates arrive as Excel serial numbers; .xls is not supported).")]
    [NodeSearchTags("xlsx", "excel", "spreadsheet", "workbook", "table", "import")]
    public static Dictionary<string, object> ReadFromFile(string path, string sheet = "", bool hasHeaders = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Excel.ReadFromFile requires a file path. Wire a File Path node into the 'path' input.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Excel.ReadFromFile: the file '" + path + "' does not exist.", path);
        }

        var sheetNames = XlsxLite.SheetNames(path);
        var grid = XlsxLite.ReadSheet(path, string.IsNullOrEmpty(sheet) ? null : sheet);

        var headers = new List<string>();
        var rows = new List<object?>();
        int firstDataRow = 0;
        if (hasHeaders && grid.Count > 0)
        {
            foreach (var cell in grid[0])
            {
                headers.Add(HeaderText(cell));
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
    /// preserved. Strings, numbers and booleans round-trip; DateTime values
    /// are written as Excel date serial numbers.
    /// </summary>
    /// <param name="path">Destination .xlsx path (missing directories are created).</param>
    /// <param name="rows">List of rows; each row a list of cell values (scalar rows become single-cell rows).</param>
    /// <param name="headers">Optional header row written before the data rows.</param>
    /// <param name="sheet">Worksheet name (Excel rules: 1-31 chars, no : \ / ? * [ ]).</param>
    /// <param name="append">True to add the sheet to an existing workbook instead of replacing the file.</param>
    /// <returns>The path that was written, for sequencing further file nodes.</returns>
    [NodeName("Excel.WriteToFile")]
    [return: NodeName("path")]
    [NodeDescription("Writes rows (+ optional headers) to an .xlsx worksheet; append adds a sheet to an existing workbook (foreign styles/formulas are not preserved).")]
    [NodeSearchTags("xlsx", "excel", "spreadsheet", "workbook", "export", "save")]
    public static string WriteToFile(
        string path,
        IList<object?> rows,
        IList<object?>? headers = null,
        string sheet = "Sheet1",
        bool append = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Excel.WriteToFile requires a file path. Wire a File Path node into the 'path' input.", nameof(path));
        }

        if (rows == null)
        {
            throw new ArgumentNullException(
                nameof(rows), "Excel.WriteToFile requires a list of rows (each row a list of cells).");
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

        XlsxLite.WriteSheet(path, grid, sheet, append);
        return path;
    }

    // ------------------------------------------------------------------
    // Helpers (not imported as nodes: non-public).
    // ------------------------------------------------------------------

    private static string HeaderText(object? cell)
    {
        if (cell == null)
        {
            return string.Empty;
        }

        if (cell is string text)
        {
            return text;
        }

        return TypeCoercion.FormatValue(cell);
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
