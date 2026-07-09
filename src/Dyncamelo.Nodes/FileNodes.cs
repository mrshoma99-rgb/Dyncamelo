using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Types;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Dyncamelo.Nodes;

/// <summary>
/// File input/output nodes: plain text, CSV (RFC 4180 style quoting) and JSON
/// (via Newtonsoft.Json). Read nodes fail with a clear message when the file
/// is missing; write nodes create the target directory when needed and return
/// the written path so writes can be sequenced.
/// </summary>
[NodeCategory("File")]
public static class FileNodes
{
    /// <summary>Reads an entire text file (UTF-8, honoring a byte-order mark).</summary>
    /// <param name="path">Path to the file, e.g. from a File Path node.</param>
    /// <returns>The file content as one string.</returns>
    [NodeName("Text.ReadFromFile")]
    [return: NodeName("text")]
    [NodeDescription("Reads the entire content of a text file.")]
    [NodeSearchTags("read", "load", "txt", "import")]
    public static string ReadText(string path)
    {
        RequireExistingFile(path, "Text.ReadFromFile");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Writes text to a file (UTF-8), overwriting any existing content and
    /// creating the parent directory when it does not exist.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="text">The text to write.</param>
    /// <returns>The path that was written, for sequencing further file nodes.</returns>
    [NodeName("Text.WriteToFile")]
    [return: NodeName("path")]
    [NodeDescription("Writes text to a file (overwrites; creates missing directories).")]
    [NodeSearchTags("write", "save", "txt", "export")]
    public static string WriteText(string path, string text)
    {
        RequireWritablePath(path, "Text.WriteToFile");
        File.WriteAllText(path, text ?? string.Empty, new UTF8Encoding(false));
        return path;
    }

    /// <summary>
    /// Reads a CSV file into a list of rows, each row a list of cells.
    /// Quoted fields (including embedded delimiters, quotes and newlines) are
    /// handled; numeric cells become numbers, everything else stays text.
    /// </summary>
    /// <param name="path">Path to the CSV file.</param>
    /// <param name="delimiter">Single-character cell delimiter.</param>
    /// <returns>List of rows; each row is a list of numbers/strings.</returns>
    [NodeName("CSV.ReadFromFile")]
    [return: NodeName("data")]
    [NodeDescription("Reads a CSV file into a list of rows (numeric cells become numbers).")]
    [NodeSearchTags("csv", "table", "spreadsheet", "import")]
    public static IList<object?> ReadCsv(string path, string delimiter = ",")
    {
        RequireExistingFile(path, "CSV.ReadFromFile");
        var separator = RequireSingleCharDelimiter(delimiter, "CSV.ReadFromFile");
        return ParseCsv(File.ReadAllText(path), separator);
    }

    /// <summary>
    /// Writes a list of rows to a CSV file. Each element of
    /// <paramref name="data"/> should itself be a list of cells; scalar rows
    /// are written as single-cell rows. Cells containing the delimiter,
    /// quotes or newlines are quoted per RFC 4180.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="data">List of rows (each row a list of cell values).</param>
    /// <param name="delimiter">Single-character cell delimiter.</param>
    /// <returns>The path that was written, for sequencing further file nodes.</returns>
    [NodeName("CSV.WriteToFile")]
    [return: NodeName("path")]
    [NodeDescription("Writes a list of rows to a CSV file (overwrites; creates missing directories).")]
    [NodeSearchTags("csv", "table", "spreadsheet", "export")]
    public static string WriteCsv(string path, IList<object?> data, string delimiter = ",")
    {
        RequireWritablePath(path, "CSV.WriteToFile");
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data), "CSV.WriteToFile requires a list of rows (each row a list of cells).");
        }

        var separator = RequireSingleCharDelimiter(delimiter, "CSV.WriteToFile");
        var builder = new StringBuilder();
        foreach (var row in data)
        {
            var cells = row is IList rowList && !(row is string)
                ? rowList
                : new object?[] { row };

            bool first = true;
            foreach (var cell in cells)
            {
                if (!first)
                {
                    builder.Append(separator);
                }

                builder.Append(EscapeCsvCell(FormatCell(cell), separator));
                first = false;
            }

            builder.Append('\n');
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        return path;
    }

    /// <summary>
    /// Reads a JSON file into graph-friendly values: objects become
    /// dictionaries, arrays become lists, numbers become doubles.
    /// </summary>
    /// <param name="path">Path to the JSON file.</param>
    /// <returns>The parsed value (dictionary, list, number, string, boolean or null).</returns>
    [NodeName("JSON.ReadFromFile")]
    [return: NodeName("data")]
    [NodeDescription("Reads a JSON file into dictionaries, lists and values.")]
    [NodeSearchTags("json", "parse", "import")]
    public static object? ReadJson(string path)
    {
        RequireExistingFile(path, "JSON.ReadFromFile");
        JToken token;
        try
        {
            token = JToken.Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw new FormatException("JSON.ReadFromFile: the file '" + path + "' is not valid JSON. " + ex.Message, ex);
        }

        return ToGraphValue(token);
    }

    /// <summary>
    /// Serializes any value (dictionaries, lists, numbers, strings, ...) to a
    /// JSON file, overwriting existing content and creating the parent
    /// directory when needed.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="data">The value to serialize.</param>
    /// <param name="indented">True for pretty-printed output.</param>
    /// <returns>The path that was written, for sequencing further file nodes.</returns>
    [NodeName("JSON.WriteToFile")]
    [return: NodeName("path")]
    [NodeDescription("Writes any value to a JSON file (overwrites; creates missing directories).")]
    [NodeSearchTags("json", "serialize", "export")]
    public static string WriteJson(string path, object? data, bool indented = true)
    {
        RequireWritablePath(path, "JSON.WriteToFile");
        var json = JsonConvert.SerializeObject(data, indented ? Formatting.Indented : Formatting.None);
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return path;
    }

    // ------------------------------------------------------------------
    // Helpers (not imported as nodes: non-public).
    // ------------------------------------------------------------------

    private static void RequireExistingFile(string path, string nodeName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(nodeName + " requires a file path. Wire a File Path node into the 'path' input.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(nodeName + ": the file '" + path + "' does not exist.", path);
        }
    }

    private static void RequireWritablePath(string path, string nodeName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(nodeName + " requires a file path. Wire a File Path node into the 'path' input.", nameof(path));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static char RequireSingleCharDelimiter(string delimiter, string nodeName)
    {
        if (string.IsNullOrEmpty(delimiter) || delimiter.Length != 1)
        {
            throw new ArgumentException(nodeName + " requires a single-character delimiter (e.g. \",\" or \";\").", nameof(delimiter));
        }

        return delimiter[0];
    }

    /// <summary>Parses CSV text (RFC 4180 style: quoted fields may contain delimiters, quotes and newlines).</summary>
    internal static IList<object?> ParseCsv(string content, char separator)
    {
        var rows = new List<object?>();
        var row = new List<object?>();
        var cell = new StringBuilder();
        bool inQuotes = false;
        bool cellWasQuoted = false;

        void EndCell()
        {
            row.Add(ParseCell(cell.ToString(), cellWasQuoted));
            cell.Length = 0;
            cellWasQuoted = false;
        }

        void EndRow()
        {
            EndCell();
            rows.Add(row);
            row = new List<object?>();
        }

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    cell.Append(c);
                }
            }
            else if (c == '"' && cell.Length == 0 && !cellWasQuoted)
            {
                inQuotes = true;
                cellWasQuoted = true;
            }
            else if (c == separator)
            {
                EndCell();
            }
            else if (c == '\r')
            {
                if (i + 1 < content.Length && content[i + 1] == '\n')
                {
                    i++;
                }

                EndRow();
            }
            else if (c == '\n')
            {
                EndRow();
            }
            else
            {
                cell.Append(c);
            }
        }

        // Trailing cell/row without a final newline.
        if (cell.Length > 0 || cellWasQuoted || row.Count > 0)
        {
            EndRow();
        }

        return rows;
    }

    private static object? ParseCell(string text, bool wasQuoted)
    {
        if (!wasQuoted &&
            text.Length > 0 &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return text;
    }

    private static string FormatCell(object? cell)
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

    private static string EscapeCsvCell(string cell, char separator)
    {
        bool needsQuoting = cell.IndexOf(separator) >= 0 ||
                            cell.IndexOf('"') >= 0 ||
                            cell.IndexOf('\n') >= 0 ||
                            cell.IndexOf('\r') >= 0;
        if (!needsQuoting)
        {
            return cell;
        }

        return "\"" + cell.Replace("\"", "\"\"") + "\"";
    }

    private static object? ToGraphValue(JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Object:
                var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var property in ((JObject)token).Properties())
                {
                    dictionary[property.Name] = ToGraphValue(property.Value);
                }

                return dictionary;

            case JTokenType.Array:
                var list = new List<object?>();
                foreach (var item in (JArray)token)
                {
                    list.Add(ToGraphValue(item));
                }

                return list;

            case JTokenType.Integer:
            case JTokenType.Float:
                return ((JValue)token).ToObject<double>();

            case JTokenType.Boolean:
                return ((JValue)token).ToObject<bool>();

            case JTokenType.Date:
                return ((JValue)token).ToObject<DateTime>();

            case JTokenType.Null:
            case JTokenType.Undefined:
                return null;

            default:
                return ((JValue)token).Value?.ToString();
        }
    }
}
