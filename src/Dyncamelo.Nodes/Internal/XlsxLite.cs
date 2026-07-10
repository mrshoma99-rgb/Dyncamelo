using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

namespace Dyncamelo.Nodes.Internal;

/// <summary>
/// Minimal .xlsx (SpreadsheetML) reader/writer with zero external dependencies
/// (ZipArchive + XmlReader/XmlWriter, both in-box on netstandard2.0 and net48).
/// Internal — surfaced through the Excel.* nodes, never as a node itself.
///
/// Reader: resolves worksheets by name through xl/workbook.xml(.rels), handles
/// shared strings (<c>t="s"</c>, including rich-text runs), inline strings
/// (<c>t="inlineStr"</c>), formula strings (<c>t="str"</c>), booleans
/// (<c>t="b"</c>), error cells (<c>t="e"</c>, returned as their raw text) and
/// numbers. Excel stores dates as serial numbers (days since 1899-12-30) — they
/// arrive as doubles; use <see cref="DateFromSerial"/> to convert.
///
/// Writer: emits the minimal 5-part package ([Content_Types].xml, _rels/.rels,
/// xl/workbook.xml, xl/_rels/workbook.xml.rels, xl/worksheets/sheetN.xml) using
/// inline strings (no sharedStrings bookkeeping — Excel opens these fine).
/// Multi-sheet output via <c>append: true</c>. DateTime values are written as
/// Excel date serial numbers (they display as numbers without a styles part —
/// documented behavior). Legacy .xls (BIFF) is out of scope.
/// </summary>
internal static class XlsxLite
{
    private const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string NsPkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    // ------------------------------------------------------------- Reading

    /// <summary>The workbook's sheet names, in workbook order.</summary>
    internal static List<string> SheetNames(string path)
    {
        using (var zip = OpenWorkbook(path))
        {
            return ReadSheetEntries(zip).Select(s => s.Name).ToList();
        }
    }

    /// <summary>
    /// Reads one worksheet into rows of cells. Cell values are null (empty),
    /// string, double or bool. Skipped rows/cells (sparse sheets) are padded
    /// with nulls so row/column indices stay faithful to the grid.
    /// </summary>
    /// <param name="path">Path to the .xlsx file.</param>
    /// <param name="sheetName">Sheet to read; null/empty selects the first sheet.</param>
    internal static List<List<object?>> ReadSheet(string path, string? sheetName = null)
    {
        using (var zip = OpenWorkbook(path))
        {
            var sheets = ReadSheetEntries(zip);
            if (sheets.Count == 0)
            {
                throw new InvalidDataException("'" + path + "' contains no worksheets.");
            }

            SheetEntry sheet;
            if (string.IsNullOrEmpty(sheetName))
            {
                sheet = sheets[0];
            }
            else
            {
                sheet = sheets.FirstOrDefault(s => string.Equals(s.Name, sheetName, StringComparison.Ordinal))
                    ?? sheets.FirstOrDefault(s => string.Equals(s.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException(
                        "The workbook has no sheet named '" + sheetName + "'. Available sheets: " +
                        string.Join(", ", sheets.Select(s => s.Name)) + ".", nameof(sheetName));
            }

            var partName = ResolveSheetPart(zip, sheet.RelId);
            var part = zip.GetEntry(partName)
                ?? throw new InvalidDataException(
                    "'" + path + "' is missing the worksheet part '" + partName + "' for sheet '" + sheet.Name + "'.");

            var shared = ReadSharedStrings(zip);
            using (var stream = part.Open())
            {
                return ReadSheetData(stream, shared);
            }
        }
    }

    // ------------------------------------------------------------- Writing

    /// <summary>
    /// Writes rows to a worksheet. With <paramref name="append"/> false the file
    /// is (re)created with the single sheet; with true, the sheet is added to an
    /// existing workbook (replacing a same-named sheet). Appending re-emits the
    /// minimal package from cell values, so styles/formulas of files produced by
    /// other tools are not preserved — documented limitation.
    /// </summary>
    /// <param name="path">Destination .xlsx path (parent directory is created).</param>
    /// <param name="rows">Rows of cells; null rows/cells become empty cells.</param>
    /// <param name="sheetName">Worksheet name (Excel rules: 1-31 chars, no : \ / ? * [ ]).</param>
    /// <param name="append">Add to an existing workbook instead of replacing the file.</param>
    internal static void WriteSheet(
        string path,
        IReadOnlyList<IReadOnlyList<object?>?>? rows,
        string sheetName = "Sheet1",
        bool append = false)
    {
        ValidateSheetName(sheetName);

        var sheets = new List<KeyValuePair<string, IReadOnlyList<IReadOnlyList<object?>?>>>();
        if (append && File.Exists(path))
        {
            foreach (var existingName in SheetNames(path))
            {
                if (string.Equals(existingName, sheetName, StringComparison.OrdinalIgnoreCase))
                {
                    continue; // replaced by the new data below
                }

                IReadOnlyList<IReadOnlyList<object?>?> existingRows = ReadSheet(path, existingName)
                    .Select(r => (IReadOnlyList<object?>?)r)
                    .ToList();
                sheets.Add(new KeyValuePair<string, IReadOnlyList<IReadOnlyList<object?>?>>(existingName, existingRows));
            }
        }

        sheets.Add(new KeyValuePair<string, IReadOnlyList<IReadOnlyList<object?>?>>(
            sheetName, rows ?? new List<IReadOnlyList<object?>?>()));

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using (var fs = File.Create(path))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            WritePackage(zip, sheets);
        }
    }

    // -------------------------------------------------------- Date serials

    /// <summary>Converts an Excel date serial number (days since 1899-12-30) to a DateTime.</summary>
    internal static DateTime DateFromSerial(double serial)
    {
        return DateTime.FromOADate(serial);
    }

    /// <summary>Converts a DateTime to an Excel date serial number.</summary>
    internal static double SerialFromDate(DateTime dateTime)
    {
        return dateTime.ToOADate();
    }

    // ------------------------------------------------------ Reader internals

    private sealed class SheetEntry
    {
        internal SheetEntry(string name, string relId)
        {
            Name = name;
            RelId = relId;
        }

        internal string Name { get; }
        internal string RelId { get; }
    }

    private static ZipArchive OpenWorkbook(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("No file path provided.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The Excel file '" + path + "' does not exist.", path);
        }

        try
        {
            return new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException(
                "'" + path + "' is not an .xlsx workbook (it is not a zip package). " +
                "Legacy .xls files are not supported — save as .xlsx.");
        }
    }

    private static List<SheetEntry> ReadSheetEntries(ZipArchive zip)
    {
        var workbook = zip.GetEntry("xl/workbook.xml")
            ?? throw new InvalidDataException("The workbook is missing xl/workbook.xml — not a valid .xlsx file.");

        var sheets = new List<SheetEntry>();
        using (var stream = workbook.Open())
        using (var reader = XmlReader.Create(stream))
        {
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sheet")
                {
                    var name = reader.GetAttribute("name") ?? string.Empty;
                    var relId = reader.GetAttribute("id", NsRel) ?? string.Empty;
                    sheets.Add(new SheetEntry(name, relId));
                }
            }
        }

        return sheets;
    }

    private static string ResolveSheetPart(ZipArchive zip, string relId)
    {
        var rels = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (rels != null && !string.IsNullOrEmpty(relId))
        {
            using (var stream = rels.Open())
            using (var reader = XmlReader.Create(stream))
            {
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element &&
                        reader.LocalName == "Relationship" &&
                        reader.GetAttribute("Id") == relId)
                    {
                        var target = reader.GetAttribute("Target") ?? string.Empty;
                        if (target.StartsWith("/", StringComparison.Ordinal))
                        {
                            return target.TrimStart('/');
                        }

                        // Targets are relative to xl/; normalize "../" segments.
                        var combined = "xl/" + target;
                        while (combined.Contains("/../"))
                        {
                            var index = combined.IndexOf("/../", StringComparison.Ordinal);
                            var prior = combined.LastIndexOf('/', Math.Max(0, index - 1));
                            combined = prior < 0
                                ? combined.Substring(index + 4)
                                : combined.Substring(0, prior) + combined.Substring(index + 3);
                        }

                        return combined;
                    }
                }
            }
        }

        return "xl/worksheets/sheet1.xml";
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var shared = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry == null)
        {
            return shared;
        }

        using (var stream = entry.Open())
        using (var reader = XmlReader.Create(stream))
        {
            var builder = new StringBuilder();
            bool inItem = false;
            bool skipRead = false;
            while (skipRead || reader.Read())
            {
                skipRead = false;
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "si")
                {
                    inItem = true;
                    builder.Clear();
                    if (reader.IsEmptyElement)
                    {
                        inItem = false;
                        shared.Add(string.Empty);
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "si")
                {
                    inItem = false;
                    shared.Add(builder.ToString());
                }
                else if (inItem && reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
                {
                    // ReadElementContentAsString leaves the reader ON the next
                    // node — process it without another Read() or rich-text
                    // runs / adjacent elements would be skipped.
                    builder.Append(reader.ReadElementContentAsString());
                    skipRead = true;
                }
            }
        }

        return shared;
    }

    private static List<List<object?>> ReadSheetData(Stream sheetStream, List<string> shared)
    {
        var rows = new List<List<object?>>();
        using (var reader = XmlReader.Create(sheetStream))
        {
            List<object?>? row = null;
            string? cellType = null;
            int cellColumn = 0; // 1-based target column of the current cell; 0 = sequential
            bool inInlineString = false;
            var inlineText = new StringBuilder();
            bool hasInlineText = false;
            bool skipRead = false;

            while (skipRead || reader.Read())
            {
                skipRead = false;
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "row")
                {
                    // Pad skipped (entirely empty) rows so indices stay faithful.
                    var declared = reader.GetAttribute("r");
                    if (declared != null &&
                        int.TryParse(declared, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNumber))
                    {
                        while (rows.Count < rowNumber - 1)
                        {
                            rows.Add(new List<object?>());
                        }
                    }

                    row = new List<object?>();
                    if (reader.IsEmptyElement)
                    {
                        rows.Add(row);
                        row = null;
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "row")
                {
                    if (row != null)
                    {
                        rows.Add(row);
                        row = null;
                    }
                }
                else if (row != null && reader.NodeType == XmlNodeType.Element && reader.LocalName == "c")
                {
                    cellType = reader.GetAttribute("t");
                    cellColumn = ColumnOfReference(reader.GetAttribute("r"));
                    inInlineString = false;
                    inlineText.Clear();
                    hasInlineText = false;
                }
                else if (row != null && reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "c")
                {
                    // Commit an inline-string cell (its text can span several <t> runs).
                    if (hasInlineText)
                    {
                        PadRowTo(row, cellColumn);
                        row.Add(inlineText.ToString());
                        inlineText.Clear();
                        hasInlineText = false;
                    }
                }
                else if (row != null && reader.NodeType == XmlNodeType.Element && reader.LocalName == "is")
                {
                    inInlineString = true;
                    hasInlineText = true; // an empty <is/> still yields an empty-string cell
                }
                else if (row != null && reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "is")
                {
                    inInlineString = false;
                }
                else if (row != null && inInlineString &&
                         reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
                {
                    // ReadElementContentAsString leaves the reader ON the next
                    // node — process it without another Read() or adjacent
                    // rich-text runs would be skipped.
                    inlineText.Append(reader.ReadElementContentAsString());
                    skipRead = true;
                }
                else if (row != null && reader.NodeType == XmlNodeType.Element && reader.LocalName == "v")
                {
                    var raw = reader.ReadElementContentAsString();
                    skipRead = true;
                    PadRowTo(row, cellColumn);
                    row.Add(ParseCellValue(raw, cellType, shared));
                }
            }
        }

        return rows;
    }

    private static object? ParseCellValue(string raw, string? cellType, List<string> shared)
    {
        switch (cellType)
        {
            case "s":
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ||
                    index < 0 || index >= shared.Count)
                {
                    throw new InvalidDataException(
                        "Shared-string index '" + raw + "' is out of range — the workbook's sharedStrings part is inconsistent.");
                }

                return shared[index];
            case "str":
            case "inlineStr":
            case "e":
                return raw;
            case "b":
                return raw == "1";
            default:
                return double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Column number (1-based) of an "A1"-style reference; 0 when absent/unparsable.</summary>
    private static int ColumnOfReference(string? cellReference)
    {
        if (string.IsNullOrEmpty(cellReference))
        {
            return 0;
        }

        int column = 0;
        foreach (var ch in cellReference!)
        {
            if (ch >= 'A' && ch <= 'Z')
            {
                column = column * 26 + (ch - 'A' + 1);
            }
            else if (ch >= 'a' && ch <= 'z')
            {
                column = column * 26 + (ch - 'a' + 1);
            }
            else
            {
                break;
            }
        }

        return column;
    }

    private static void PadRowTo(List<object?> row, int column)
    {
        if (column <= 0)
        {
            return;
        }

        while (row.Count < column - 1)
        {
            row.Add(null);
        }
    }

    // ------------------------------------------------------ Writer internals

    private static void ValidateSheetName(string sheetName)
    {
        if (string.IsNullOrWhiteSpace(sheetName))
        {
            throw new ArgumentException("Sheet name must not be empty.", nameof(sheetName));
        }

        if (sheetName.Length > 31 || sheetName.IndexOfAny(new[] { ':', '\\', '/', '?', '*', '[', ']' }) >= 0)
        {
            throw new ArgumentException(
                "'" + sheetName + "' is not a valid Excel sheet name (max 31 characters; " +
                @"must not contain : \ / ? * [ ]).", nameof(sheetName));
        }
    }

    private static void WritePackage(
        ZipArchive zip,
        List<KeyValuePair<string, IReadOnlyList<IReadOnlyList<object?>?>>> sheets)
    {
        var contentTypes = new StringBuilder()
            .Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
            .Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">")
            .Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>")
            .Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>")
            .Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
        for (int i = 1; i <= sheets.Count; i++)
        {
            contentTypes
                .Append("<Override PartName=\"/xl/worksheets/sheet").Append(i)
                .Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        }

        contentTypes.Append("</Types>");
        WriteEntry(zip, "[Content_Types].xml", contentTypes.ToString());

        WriteEntry(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"" + NsPkgRel + "\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");

        var workbook = new StringBuilder()
            .Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
            .Append("<workbook xmlns=\"").Append(NsMain).Append("\" xmlns:r=\"").Append(NsRel).Append("\"><sheets>");
        var workbookRels = new StringBuilder()
            .Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
            .Append("<Relationships xmlns=\"").Append(NsPkgRel).Append("\">");
        for (int i = 1; i <= sheets.Count; i++)
        {
            workbook
                .Append("<sheet name=\"").Append(EscapeXmlAttribute(sheets[i - 1].Key))
                .Append("\" sheetId=\"").Append(i).Append("\" r:id=\"rId").Append(i).Append("\"/>");
            workbookRels
                .Append("<Relationship Id=\"rId").Append(i)
                .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\"")
                .Append(" Target=\"worksheets/sheet").Append(i).Append(".xml\"/>");
        }

        workbook.Append("</sheets></workbook>");
        workbookRels.Append("</Relationships>");
        WriteEntry(zip, "xl/workbook.xml", workbook.ToString());
        WriteEntry(zip, "xl/_rels/workbook.xml.rels", workbookRels.ToString());

        for (int i = 1; i <= sheets.Count; i++)
        {
            WriteWorksheetPart(zip, "xl/worksheets/sheet" + i + ".xml", sheets[i - 1].Value);
        }
    }

    private static void WriteWorksheetPart(
        ZipArchive zip, string partName, IReadOnlyList<IReadOnlyList<object?>?> rows)
    {
        var entry = zip.CreateEntry(partName);
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = true };
        using (var writer = XmlWriter.Create(entry.Open(), settings))
        {
            writer.WriteStartElement("worksheet", NsMain);
            writer.WriteStartElement("sheetData", NsMain);
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                writer.WriteStartElement("row", NsMain);
                writer.WriteAttributeString("r", (rowIndex + 1).ToString(CultureInfo.InvariantCulture));
                var row = rows[rowIndex];
                for (int columnIndex = 0; row != null && columnIndex < row.Count; columnIndex++)
                {
                    var value = row[columnIndex];
                    if (value == null)
                    {
                        continue; // empty cell: skipped, cell references keep the grid aligned
                    }

                    writer.WriteStartElement("c", NsMain);
                    writer.WriteAttributeString("r", ColumnName(columnIndex + 1) + (rowIndex + 1));
                    WriteCellValue(writer, value);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        }
    }

    private static void WriteCellValue(XmlWriter writer, object value)
    {
        if (value is double || value is float || value is int || value is long ||
            value is short || value is byte || value is decimal || value is uint || value is ushort)
        {
            writer.WriteStartElement("v", NsMain);
            writer.WriteString(Convert.ToDouble(value, CultureInfo.InvariantCulture)
                .ToString("R", CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }
        else if (value is bool flag)
        {
            writer.WriteAttributeString("t", "b");
            writer.WriteStartElement("v", NsMain);
            writer.WriteString(flag ? "1" : "0");
            writer.WriteEndElement();
        }
        else if (value is DateTime dateTime)
        {
            // Excel date serial; displays as a number without a styles part.
            writer.WriteStartElement("v", NsMain);
            writer.WriteString(dateTime.ToOADate().ToString("R", CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }
        else
        {
            writer.WriteAttributeString("t", "inlineStr");
            writer.WriteStartElement("is", NsMain);
            writer.WriteStartElement("t", NsMain);
            writer.WriteString(SanitizeXmlText(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty));
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
    }

    /// <summary>Column name for a 1-based column number: 1 → "A", 27 → "AA".</summary>
    private static string ColumnName(int column)
    {
        var name = string.Empty;
        while (column > 0)
        {
            int remainder = (column - 1) % 26;
            name = (char)('A' + remainder) + name;
            column = (column - 1) / 26;
        }

        return name;
    }

    /// <summary>Drops characters that are illegal in XML 1.0 (e.g. control chars from binary-ish data).</summary>
    private static string SanitizeXmlText(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (!XmlConvert.IsXmlChar(text[i]) && !char.IsHighSurrogate(text[i]) && !char.IsLowSurrogate(text[i]))
            {
                var builder = new StringBuilder(text.Length);
                foreach (var ch in text)
                {
                    if (XmlConvert.IsXmlChar(ch) || char.IsHighSurrogate(ch) || char.IsLowSurrogate(ch))
                    {
                        builder.Append(ch);
                    }
                }

                return builder.ToString();
            }
        }

        return text;
    }

    private static string EscapeXmlAttribute(string text)
    {
        return SanitizeXmlText(text)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
        {
            writer.Write(content);
        }
    }
}
