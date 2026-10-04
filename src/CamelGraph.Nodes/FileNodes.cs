using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CamelGraph.Core.Files;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Types;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CamelGraph.Nodes;

/// <summary>
/// File input/output nodes: plain text, CSV (RFC 4180 style quoting) and JSON
/// (via Newtonsoft.Json). Read nodes fail with a clear message when the file
/// is missing; write nodes create the target directory when needed and return
/// the written path so writes can be sequenced. A relative path starts in the
/// graph's folder (<see cref="PathResolver"/>), and quotes pasted from
/// Explorer's "Copy as path" are removed.
/// </summary>
[NodeCategory("File")]
public static class FileNodes
{
    /// <summary>
    /// Reads an entire text file. The encoding is detected (UTF-8 or UTF-16 as the file marks it; a file that is not valid UTF-8 is
    /// read as Windows-1252) unless one is chosen.
    /// </summary>
    /// <param name="path">Path to the file, e.g. from a File Path node. A relative path starts in the graph's folder.</param>
    /// <param name="encoding">auto (default), UTF-8, Windows-1252 or UTF-16.</param>
    /// <returns>The file content as one string.</returns>
    [NodeName("Text.ReadFromFile")]
    [return: NodeName("text")]
    [NodeDescription("Reads the entire content of a text file as one text. The encoding is detected: UTF-8 or UTF-16 as the file marks it, and a file that is not valid UTF-8 is read as Windows-1252 (what a European Excel \"CSV (Comma delimited)\" export uses); Advanced > encoding forces one. A relative path starts in the graph's folder. To split the text into lines use String.Split.")]
    [NodeSearchTags("read", "load", "txt", "import", "encoding", "utf-8", "ansi", "lines")]
    [NodeAliases("CamelGraph.Nodes.FileNodes.ReadText@string")]
    public static string ReadText(
        [NodePath(NodePathMode.Open, Filter = FileFilters.Text)] string path,
        [NodePanel("Advanced")][NodeChoices("auto", "UTF-8", "Windows-1252", "UTF-16")] string encoding = "auto")
    {
        const string node = "Text.ReadFromFile";
        var file = RequireExistingFile(path, node);
        var kind = TextFile.ParseForReading(encoding, node);
        return FileErrors.Run(node, file, false, () => TextFile.ReadAllText(file, kind));
    }

    /// <summary>
    /// Writes text to a file, overwriting any existing content and creating the parent directory when it does not exist. A list is
    /// written one item per line.
    /// </summary>
    /// <param name="path">Destination file path. A relative path starts in the graph's folder.</param>
    /// <param name="text">The text to write. A list is written one item per line (joined with line breaks, no final line break); null is an error.</param>
    /// <param name="encoding">UTF-8 (default, no byte-order mark), UTF-8 with BOM, Windows-1252 or UTF-16.</param>
    /// <returns>The path that was written, for sequencing further file nodes.</returns>
    [NodeName("Text.WriteToFile")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [return: NodeName("path")]
    [NodeDescription("Writes text to a file, replacing what was there, and creates missing folders; returns the path. A list of texts is written one item per line (joined with line breaks, no final line break). Nothing wired is an error that leaves the file alone; empty text \"\" empties it. A list of paths writes every file with the same text - set the text input to @L1 to write one text per path. Advanced > encoding: \"UTF-8 with BOM\" makes Excel show accents and symbols correctly. A relative path starts in the graph's folder. To add to a file use Text.AppendToFile.")]
    [NodeSearchTags("write", "save", "txt", "export", "encoding", "utf-8", "bom", "lines")]
    [NodeAliases("CamelGraph.Nodes.FileNodes.WriteText@string,string")]
    public static string WriteText(
        [NodePath(NodePathMode.Save, Filter = FileFilters.Text)] string path,
        [PortKinds("text")] object? text,
        [NodePanel("Advanced")][NodeChoices("UTF-8", "UTF-8 with BOM", "Windows-1252", "UTF-16")] string encoding = "UTF-8")
    {
        const string node = "Text.WriteToFile";
        var file = ResolveForWriting(path, node);
        if (text == null)
        {
            throw new ArgumentNullException(
                nameof(text),
                node + ": the 'text' input is empty (null), so there is nothing to write and '" + file + "' was left as it was. Wire text into 'text', or use empty text \"\" to empty the file.");
        }

        var kind = TextFile.ParseForWriting(encoding, node);
        var content = TextFromValue(text);
        FileErrors.Run(node, file, true, () =>
        {
            EnsureParentFolder(file);
            TextFile.WriteAllText(file, content, kind);
        });
        return file;
    }

    /// <summary>
    /// Reads a CSV file into a list of rows, each row a list of cells.
    /// Quoted fields (including embedded delimiters, quotes and newlines) are
    /// handled; plain numeric cells become numbers, everything else stays text.
    /// </summary>
    /// <param name="path">Path to the CSV file. A relative path starts in the graph's folder.</param>
    /// <param name="delimiter">Single-character cell delimiter: a comma, semicolon, bar, or "tab".</param>
    /// <param name="numbers">auto (default) turns plain numbers into numbers; text keeps every cell as text.</param>
    /// <param name="encoding">auto (default), UTF-8, Windows-1252 or UTF-16.</param>
    /// <returns>List of rows; each row is a list of numbers/strings.</returns>
    [NodeName("CSV.ReadFromFile")]
    [return: NodeName("data")]
    [NodeDescription("Reads a CSV file into a list of rows. Plain numbers become numbers; codes with leading zeros (007), whole numbers of more than 15 digits and the words NaN and Infinity stay text, and Advanced > numbers = text keeps every cell as text. Delimiter: comma, semicolon, bar or tab. The encoding is detected (a file that is not valid UTF-8 is read as Windows-1252). For a table with column names use Table.FromCsvFile. A relative path starts in the graph's folder.")]
    [NodeSearchTags("csv", "table", "spreadsheet", "import", "tab", "tsv", "delimiter", "encoding", "leading zeros")]
    [NodeAliases("CamelGraph.Nodes.FileNodes.ReadCsv@string,string")]
    public static IList<object?> ReadCsv(
        [NodePath(NodePathMode.Open, Filter = FileFilters.Csv)] string path,
        [NodeChoices(",", ";", "|", "tab")] string delimiter = ",",
        [NodePanel("Advanced")][NodeChoices("auto", "text")] string numbers = "auto",
        [NodePanel("Advanced")][NodeChoices("auto", "UTF-8", "Windows-1252", "UTF-16")] string encoding = "auto")
    {
        return ReadCsvAs("CSV.ReadFromFile", path, delimiter, numbers, encoding);
    }

    /// <summary>
    /// Writes a list of rows to a CSV file. Each element of
    /// <paramref name="data"/> should itself be a list of cells; scalar rows
    /// are written as single-cell rows. Cells containing the delimiter,
    /// quotes or newlines are quoted per RFC 4180. Dates are written as
    /// <c>yyyy-MM-dd HH:mm:ss</c>.
    /// </summary>
    /// <param name="path">Destination file path. A relative path starts in the graph's folder.</param>
    /// <param name="data">List of rows (each row a list of cell values).</param>
    /// <param name="delimiter">Single-character cell delimiter: a comma, semicolon, bar, or "tab".</param>
    /// <param name="encoding">UTF-8 (default, no byte-order mark), UTF-8 with BOM, Windows-1252 or UTF-16.</param>
    /// <returns>The path that was written, for sequencing further file nodes.</returns>
    [NodeName("CSV.WriteToFile")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [return: NodeName("path")]
    [NodeDescription("Writes a list of rows to a CSV file, replacing what was there, and creates missing folders. Cells with the delimiter, quotes or line breaks are quoted; dates are written as 2026-10-04 14:30:00 (the same in every country); empty cells stay empty. Delimiter: comma, semicolon, bar or tab. Advanced > encoding: \"UTF-8 with BOM\" makes Excel show accents and symbols correctly. For a table use Table.ToCsvFile; to add rows to a file use CSV.AppendToFile. A relative path starts in the graph's folder.")]
    [NodeSearchTags("csv", "table", "spreadsheet", "export", "tab", "tsv", "delimiter", "encoding", "bom")]
    [NodeAliases("CamelGraph.Nodes.FileNodes.WriteCsv@string,System.Collections.Generic.IList<object>,string")]
    public static string WriteCsv(
        [NodePath(NodePathMode.Save, Filter = FileFilters.Csv)] string path,
        IList<object?> data,
        [NodeChoices(",", ";", "|", "tab")] string delimiter = ",",
        [NodePanel("Advanced")][NodeChoices("UTF-8", "UTF-8 with BOM", "Windows-1252", "UTF-16")] string encoding = "UTF-8")
    {
        return WriteCsvAs("CSV.WriteToFile", path, data, delimiter, encoding);
    }

    /// <summary>
    /// Reads a JSON file into graph-friendly values: objects become
    /// dictionaries, arrays become lists, numbers become doubles.
    /// </summary>
    /// <param name="path">Path to the JSON file. A relative path starts in the graph's folder.</param>
    /// <param name="encoding">auto (default), UTF-8, Windows-1252 or UTF-16.</param>
    /// <returns>The parsed value (dictionary, list, number, string, boolean or null).</returns>
    [NodeName("JSON.ReadFromFile")]
    [return: NodeName("data")]
    [NodeDescription("Reads a JSON file into dictionaries, lists and values (every number becomes a double). The encoding is detected. A relative path starts in the graph's folder.")]
    [NodeSearchTags("json", "parse", "import", "encoding")]
    [NodeAliases("CamelGraph.Nodes.FileNodes.ReadJson@string")]
    public static object? ReadJson(
        [NodePath(NodePathMode.Open, Filter = FileFilters.Json)] string path,
        [NodePanel("Advanced")][NodeChoices("auto", "UTF-8", "Windows-1252", "UTF-16")] string encoding = "auto")
    {
        const string node = "JSON.ReadFromFile";
        var file = RequireExistingFile(path, node);
        var kind = TextFile.ParseForReading(encoding, node);
        var text = FileErrors.Run(node, file, false, () => TextFile.ReadAllText(file, kind));
        JToken token;
        try
        {
            token = JToken.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new FormatException(node + ": the file '" + file + "' is not valid JSON. " + ex.Message, ex);
        }

        return ToGraphValue(token);
    }

    /// <summary>
    /// Serializes any value (dictionaries, lists, numbers, strings, ...) to a
    /// JSON file, overwriting existing content and creating the parent
    /// directory when needed.
    /// </summary>
    /// <param name="path">Destination file path. A relative path starts in the graph's folder.</param>
    /// <param name="data">The value to serialize; null is an error.</param>
    /// <param name="indented">True for pretty-printed output.</param>
    /// <param name="encoding">UTF-8 (default, no byte-order mark), UTF-8 with BOM, Windows-1252 or UTF-16.</param>
    /// <returns>The path that was written, for sequencing further file nodes.</returns>
    [NodeName("JSON.WriteToFile")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [return: NodeName("path")]
    [NodeDescription("Writes any value to a JSON file, replacing what was there, and creates missing folders. Nothing wired (null) is an error that leaves the file alone. A relative path starts in the graph's folder.")]
    [NodeSearchTags("json", "serialize", "export", "encoding")]
    [NodeAliases("CamelGraph.Nodes.FileNodes.WriteJson@string,object,bool")]
    public static string WriteJson(
        [NodePath(NodePathMode.Save, Filter = FileFilters.Json)] string path,
        object? data,
        bool indented = true,
        [NodePanel("Advanced")][NodeChoices("UTF-8", "UTF-8 with BOM", "Windows-1252", "UTF-16")] string encoding = "UTF-8")
    {
        const string node = "JSON.WriteToFile";
        var file = ResolveForWriting(path, node);
        if (data == null)
        {
            throw new ArgumentNullException(
                nameof(data),
                node + ": the 'data' input is empty (null), so there is nothing to write and '" + file + "' was left as it was. Wire the value to save into 'data'.");
        }

        var kind = TextFile.ParseForWriting(encoding, node);
        var json = JsonConvert.SerializeObject(data, indented ? Formatting.Indented : Formatting.None);
        FileErrors.Run(node, file, true, () =>
        {
            EnsureParentFolder(file);
            TextFile.WriteAllText(file, json, kind);
        });
        return file;
    }

    /// <summary>
    /// Parses a JSON string into graph-friendly values: objects become
    /// dictionaries, arrays become lists, numbers become doubles. The
    /// string-based twin of JSON.ReadFromFile (e.g. for JSON held in a
    /// property value or built by other nodes).
    /// </summary>
    /// <param name="json">The JSON text to parse.</param>
    /// <returns>The parsed value (dictionary, list, number, string, boolean or null).</returns>
    [NodeName("JSON.Parse")]
    [NodeCategory("Data")]
    [return: NodeName("value")]
    [NodeDescription("Parses a JSON string into dictionaries, lists and values.")]
    [NodeSearchTags("json", "deserialize", "decode")]
    public static object? ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("JSON.Parse requires a JSON string, e.g. \"{\\\"a\\\": 1}\".", nameof(json));
        }

        JToken token;
        try
        {
            token = JToken.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException("JSON.Parse: the input is not valid JSON. " + ex.Message, ex);
        }

        return ToGraphValue(token);
    }

    /// <summary>
    /// Serializes any value (dictionaries, lists, numbers, strings, ...) to a
    /// JSON string. The string-based twin of JSON.WriteToFile.
    /// </summary>
    /// <param name="value">The value to serialize.</param>
    /// <param name="indented">True (default) for pretty-printed output.</param>
    /// <returns>The JSON text.</returns>
    [NodeName("JSON.Stringify")]
    [NodeCategory("Data")]
    [return: NodeName("json")]
    [NodeDescription("Serializes any value to a JSON string.")]
    [NodeSearchTags("json", "serialize", "encode", "tostring")]
    public static string StringifyJson(object? value, bool indented = true)
    {
        return JsonConvert.SerializeObject(value, indented ? Formatting.Indented : Formatting.None);
    }

    /// <summary>Tests whether a file exists at a path (false for folders, missing paths and a blank path).</summary>
    /// <param name="path">The file path to test. A relative path starts in the graph's folder.</param>
    /// <returns>True when a file exists at the path.</returns>
    [NodeName("File.Exists")]
    [return: NodeName("exists")]
    [NodeDescription("Tests whether a file exists at the given path; a folder, a missing file and a blank path all give false. A relative path starts in the graph's folder.")]
    [NodeSearchTags("file", "check", "found", "present")]
    public static bool FileExists([NodePath(NodePathMode.Open, Filter = FileFilters.All)] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return File.Exists(PathResolver.Resolve(path));
        }
        catch (Exception ex) when (FileErrors.IsFileProblem(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// Lists the files in a folder, optionally filtered by a wildcard pattern
    /// (e.g. "*.nwd"). Returns full paths sorted by name; subfolders are not
    /// searched. Retired: Directory.Find does the same and more.
    /// </summary>
    /// <param name="path">The folder to list. A relative path starts in the graph's folder.</param>
    /// <param name="pattern">Wildcard filter; the default matches every file.</param>
    /// <returns>The full file paths, sorted.</returns>
    [NodeName("Directory.GetFiles")]
    [NodeDeprecated("Directory.Find")]
    [return: NodeName("files")]
    [NodeDescription("Lists the files in a folder (optionally filtered by a wildcard such as \"*.nwd\").")]
    [NodeSearchTags("folder", "list", "batch", "directory", "browse")]
    public static IList<string> GetFiles([NodePath(NodePathMode.Folder)] string path, string pattern = "*.*")
    {
        return FileExtraNodes.FindCore("Directory.GetFiles", path, pattern, wantFiles: true, recursive: false, "name", false, 0);
    }

    /// <summary>Joins a folder path and a file name with the correct separator.</summary>
    /// <param name="directory">The folder part.</param>
    /// <param name="fileName">The file (or subpath) part.</param>
    /// <returns>The combined path.</returns>
    [NodeName("Path.Combine")]
    [return: NodeName("path")]
    [NodeDescription("Joins a folder path and a file name with the correct separator. Trap: if the file name is itself a full path (it starts with a drive such as C:\\ or with a backslash), the result is that part alone and the folder is dropped; Path.Join does not do that and takes any number of parts.")]
    [NodeSearchTags("join", "folder", "filename", "concat")]
    public static string CombinePath(string directory, string fileName)
    {
        if (directory == null)
        {
            throw new ArgumentNullException(nameof(directory), "Path.Combine requires a folder path in the 'directory' input.");
        }

        if (fileName == null)
        {
            throw new ArgumentNullException(nameof(fileName), "Path.Combine requires a file name in the 'fileName' input.");
        }

        return Path.Combine(directory, fileName);
    }

    // ------------------------------------------------------------------
    // Helpers (not imported as nodes: non-public).
    // ------------------------------------------------------------------

    /// <summary>Reads a CSV file for the node called <paramref name="nodeName"/> (so the Table wrappers can report their own name).</summary>
    internal static IList<object?> ReadCsvAs(string nodeName, string path, string delimiter, string numbers, string encoding)
    {
        var file = RequireExistingFile(path, nodeName);
        var separator = RequireSingleCharDelimiter(delimiter, nodeName);
        var asText = ParseNumbersChoice(numbers, nodeName);
        var kind = TextFile.ParseForReading(encoding, nodeName);
        var content = FileErrors.Run(nodeName, file, false, () => TextFile.ReadAllText(file, kind));
        return ParseCsv(content, separator, asText);
    }

    /// <summary>Writes a CSV file for the node called <paramref name="nodeName"/> (so the Table wrappers can report their own name).</summary>
    internal static string WriteCsvAs(string nodeName, string path, IList<object?> data, string delimiter, string encoding)
    {
        var file = ResolveForWriting(path, nodeName);
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data), nodeName + " requires a list of rows (each row a list of cells).");
        }

        var separator = RequireSingleCharDelimiter(delimiter, nodeName);
        var kind = TextFile.ParseForWriting(encoding, nodeName);
        var builder = new StringBuilder();
        foreach (var row in data)
        {
            AppendCsvRow(builder, row, separator);
        }

        FileErrors.Run(nodeName, file, true, () =>
        {
            EnsureParentFolder(file);
            TextFile.WriteAllText(file, builder.ToString(), kind);
        });
        return file;
    }

    /// <summary>One row of cells as a CSV line (with the line break), quoted per RFC 4180; a scalar is a one-cell row.</summary>
    internal static void AppendCsvRow(StringBuilder builder, object? row, char separator)
    {
        var cells = row is IList rowList && !(row is string)
            ? rowList
            : new object?[] { row };

        var first = true;
        foreach (var cell in cells)
        {
            if (!first)
            {
                builder.Append(separator);
            }

            builder.Append(EscapeCsvCell(CellText.FormatForFile(cell), separator));
            first = false;
        }

        builder.Append('\n');
    }

    /// <summary>
    /// A text for a text file from whatever was wired to a "text" input: a string as it is, a list as one item per line (the items
    /// as they read everywhere in CamelGraph), any other value as its text.
    /// </summary>
    internal static string TextFromValue(object? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (value is string text)
        {
            return text;
        }

        if (value is IEnumerable items && !(value is IDictionary))
        {
            var builder = new StringBuilder();
            var first = true;
            foreach (var item in items)
            {
                if (!first)
                {
                    builder.Append(Environment.NewLine);
                }

                builder.Append(CellText.Format(item));
                first = false;
            }

            return builder.ToString();
        }

        return CellText.Format(value);
    }

    /// <summary>
    /// Checks the path of a file that must exist and makes it usable: quotes and spaces trimmed, a relative path placed in the graph's
    /// folder. Returns the full path.
    /// </summary>
    internal static string RequireExistingFile(string path, string nodeName, string inputName = "path")
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(nodeName + " requires a file path. Wire a File Path node into the '" + inputName + "' input.", inputName);
        }

        var file = ResolveChecked(path, nodeName, inputName);
        if (!File.Exists(file))
        {
            throw new FileNotFoundException(nodeName + ": the file '" + file + "' does not exist.", file);
        }

        return file;
    }

    /// <summary>
    /// Checks the path of a file that is about to be written and makes it usable (see <see cref="RequireExistingFile"/>); the folder is
    /// not created yet, so a node can first check its other inputs. Follow with <see cref="EnsureParentFolder"/>.
    /// </summary>
    internal static string ResolveForWriting(string path, string nodeName, string inputName = "path")
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(nodeName + " requires a file path. Wire a File Path node into the '" + inputName + "' input.", inputName);
        }

        var file = ResolveChecked(path, nodeName, inputName);
        if (Directory.Exists(file))
        {
            throw new ArgumentException(
                nodeName + ": '" + file + "' is a folder. Give the full path of the file, e.g. with Path.Combine.",
                inputName);
        }

        return file;
    }

    /// <summary>Like <see cref="ResolveForWriting"/>, then creates the folder the file goes in.</summary>
    internal static string RequireWritablePath(string path, string nodeName, string inputName = "path")
    {
        var file = ResolveForWriting(path, nodeName, inputName);
        FileErrors.Run(nodeName, file, true, () => EnsureParentFolder(file));
        return file;
    }

    /// <summary>Creates the folder a file will be written in (no error when it exists).</summary>
    internal static void EnsureParentFolder(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>Resolves a path against the graph's folder and refuses one the operating system cannot use, naming the node.</summary>
    internal static string ResolveChecked(string path, string nodeName, string inputName)
    {
        var resolved = PathResolver.Resolve(path);
        try
        {
            Path.GetFullPath(resolved);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
        {
            throw new ArgumentException(FileErrors.NotAPath(nodeName, PathResolver.Clean(path)), inputName, ex);
        }

        return resolved;
    }

    internal static char RequireSingleCharDelimiter(string delimiter, string nodeName)
    {
        if (delimiter != null)
        {
            var key = delimiter.Trim();
            if (delimiter == "\t" || key.Equals("tab", StringComparison.OrdinalIgnoreCase) || key == "\\t")
            {
                return '\t';
            }
        }

        if (delimiter == null || delimiter.Length != 1)
        {
            throw new ArgumentException(
                nodeName + " requires a single-character delimiter: a comma, semicolon or bar, or the word tab (not \"" + delimiter + "\").",
                nameof(delimiter));
        }

        return delimiter[0];
    }

    /// <summary>True for "text" (every cell stays text); false for "auto".</summary>
    internal static bool ParseNumbersChoice(string? numbers, string nodeName)
    {
        var key = (numbers ?? string.Empty).Trim().ToLowerInvariant();
        if (key == string.Empty || key == "auto")
        {
            return false;
        }

        if (key == "text")
        {
            return true;
        }

        throw new ArgumentException(
            nodeName + ": 'numbers' must be auto (plain numbers become numbers) or text (every cell stays text), not '" + numbers + "'.",
            nameof(numbers));
    }

    /// <summary>Parses CSV text (RFC 4180 style: quoted fields may contain delimiters, quotes and newlines).</summary>
    internal static IList<object?> ParseCsv(string content, char separator, bool numbersAsText = false)
    {
        var rows = new List<object?>();
        var row = new List<object?>();
        var cell = new StringBuilder();
        bool inQuotes = false;
        bool cellWasQuoted = false;

        void EndCell()
        {
            row.Add(ParseCell(cell.ToString(), cellWasQuoted, numbersAsText));
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

    private static object? ParseCell(string text, bool wasQuoted, bool numbersAsText)
    {
        if (!wasQuoted && !numbersAsText && text.Length > 0 && TryParsePlainNumber(text, out var number))
        {
            return number;
        }

        return text;
    }

    /// <summary>
    /// True when an unquoted cell is a number to compute with. A code that only looks like one stays text: a leading zero (007,
    /// 00123), a whole number of more than 15 digits (an id that a double cannot hold exactly) and the words NaN and Infinity.
    /// </summary>
    internal static bool TryParsePlainNumber(string text, out double number)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ||
            double.IsNaN(number) || double.IsInfinity(number))
        {
            return false;
        }

        var core = text.Trim();
        if (core.Length > 0 && (core[0] == '-' || core[0] == '+'))
        {
            core = core.Substring(1);
        }

        var digits = 0;
        while (digits < core.Length && core[digits] >= '0' && core[digits] <= '9')
        {
            digits++;
        }

        if (digits > 1 && core[0] == '0')
        {
            return false;
        }

        return !(digits == core.Length && digits > 15);
    }

    internal static string EscapeCsvCell(string cell, char separator)
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
