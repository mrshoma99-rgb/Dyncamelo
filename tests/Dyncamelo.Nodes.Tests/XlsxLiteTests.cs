using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Dyncamelo.Nodes.Internal;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

public class XlsxLiteTests : IDisposable
{
    private readonly string _directory;

    public XlsxLiteTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "DyncameloXlsxTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    private string PathFor(string fileName) => Path.Combine(_directory, fileName);

    private static List<IReadOnlyList<object>> Rows(params object[][] rows)
    {
        var list = new List<IReadOnlyList<object>>();
        foreach (var row in rows)
        {
            list.Add(row);
        }

        return list;
    }

    // ------------------------------------------------------------ Round-trip

    [Fact]
    public void WriteThenRead_RoundTripsStringsNumbersAndBooleans()
    {
        var path = PathFor("roundtrip.xlsx");
        XlsxLite.WriteSheet(path, Rows(
            new object[] { "Name", "Count", "Active" },
            new object[] { "Wall", 42.5, true },
            new object[] { "Door", -3.0, false }));

        var rows = XlsxLite.ReadSheet(path);

        Assert.Equal(3, rows.Count);
        Assert.Equal(new object[] { "Name", "Count", "Active" }, rows[0]);
        Assert.Equal("Wall", rows[1][0]);
        Assert.Equal(42.5, (double)rows[1][1]!, 12);
        Assert.Equal(true, rows[1][2]);
        Assert.Equal(false, rows[2][2]);
    }

    [Fact]
    public void WriteThenRead_RoundTripsXmlSpecialCharactersAndUnicode()
    {
        var path = PathFor("escaping.xlsx");
        var nasty = "a<b>&\"quoted\"'x' Ω漢字 – ✓";
        XlsxLite.WriteSheet(path, Rows(new object[] { nasty }));

        var rows = XlsxLite.ReadSheet(path);

        Assert.Equal(nasty, rows[0][0]);
    }

    [Fact]
    public void Write_DropsCharactersIllegalInXml()
    {
        var path = PathFor("illegal.xlsx");
        XlsxLite.WriteSheet(path, Rows(new object[] { "ok\u0000\u0001still ok\ttab" }));

        var rows = XlsxLite.ReadSheet(path);

        Assert.Equal("okstill ok\ttab", rows[0][0]);
    }

    [Fact]
    public void WriteThenRead_EmptyCellsBecomeNullsAndGridStaysAligned()
    {
        var path = PathFor("gaps.xlsx");
        XlsxLite.WriteSheet(path, Rows(
            new object[] { "A1", null, "C1" },
            new object[] { null, "B2" }));

        var rows = XlsxLite.ReadSheet(path);

        Assert.Equal(new object[] { "A1", null, "C1" }, rows[0]);
        Assert.Equal(new object[] { null, "B2" }, rows[1]);
    }

    [Fact]
    public void WriteThenRead_ManyColumns_RoundTripsBeyondColumnZ()
    {
        var path = PathFor("wide.xlsx");
        var row = new object[30];
        for (int i = 0; i < row.Length; i++)
        {
            row[i] = "col" + i;
        }

        XlsxLite.WriteSheet(path, Rows(row));

        var rows = XlsxLite.ReadSheet(path);

        Assert.Equal(30, rows[0].Count);
        Assert.Equal("col0", rows[0][0]);
        Assert.Equal("col26", rows[0][26]); // column "AA"
        Assert.Equal("col29", rows[0][29]);
    }

    [Fact]
    public void WriteThenRead_EmptyRowsList_ProducesReadableEmptySheet()
    {
        var path = PathFor("empty.xlsx");
        XlsxLite.WriteSheet(path, new List<IReadOnlyList<object>>());

        var rows = XlsxLite.ReadSheet(path);

        Assert.Empty(rows);
        Assert.Equal(new[] { "Sheet1" }, XlsxLite.SheetNames(path));
    }

    [Fact]
    public void Write_NullRowInList_BecomesEmptyRow()
    {
        var path = PathFor("nullrow.xlsx");
        XlsxLite.WriteSheet(path, new List<IReadOnlyList<object>> { null, new object[] { "x" } });

        var rows = XlsxLite.ReadSheet(path);

        Assert.Equal(2, rows.Count);
        Assert.Empty(rows[0]);
        Assert.Equal("x", rows[1][0]);
    }

    // ------------------------------------------------------- Dates as serials

    [Fact]
    public void DateTimes_AreWrittenAsSerialNumbers_AndConvertBack()
    {
        var path = PathFor("dates.xlsx");
        var stamp = new DateTime(2026, 7, 10, 12, 30, 0);
        XlsxLite.WriteSheet(path, Rows(new object[] { stamp }));

        var rows = XlsxLite.ReadSheet(path);

        // Dates arrive as Excel serial numbers (documented behavior).
        var serial = Assert.IsType<double>(rows[0][0]);
        Assert.True(Math.Abs((stamp - XlsxLite.DateFromSerial(serial)).TotalMilliseconds) < 1.0);
        Assert.Equal(serial, XlsxLite.SerialFromDate(stamp), 9);
    }

    // ------------------------------------------------------------ Multi-sheet

    [Fact]
    public void Append_AddsSecondSheet_KeepingTheFirst()
    {
        var path = PathFor("multi.xlsx");
        XlsxLite.WriteSheet(path, Rows(new object[] { "first" }), "Alpha");
        XlsxLite.WriteSheet(path, Rows(new object[] { "second", 2.0 }), "Beta", append: true);

        Assert.Equal(new[] { "Alpha", "Beta" }, XlsxLite.SheetNames(path));
        Assert.Equal("first", XlsxLite.ReadSheet(path, "Alpha")[0][0]);
        var beta = XlsxLite.ReadSheet(path, "Beta");
        Assert.Equal("second", beta[0][0]);
        Assert.Equal(2.0, (double)beta[0][1]!, 12);
    }

    [Fact]
    public void Append_SameSheetName_ReplacesThatSheetOnly()
    {
        var path = PathFor("replace.xlsx");
        XlsxLite.WriteSheet(path, Rows(new object[] { "old" }), "Data");
        XlsxLite.WriteSheet(path, Rows(new object[] { "other" }), "Other", append: true);
        XlsxLite.WriteSheet(path, Rows(new object[] { "new" }), "Data", append: true);

        Assert.Equal("new", XlsxLite.ReadSheet(path, "Data")[0][0]);
        Assert.Equal("other", XlsxLite.ReadSheet(path, "Other")[0][0]);
        Assert.Equal(2, XlsxLite.SheetNames(path).Count);
    }

    [Fact]
    public void Append_ToMissingFile_JustCreatesIt()
    {
        var path = PathFor("appendnew.xlsx");
        XlsxLite.WriteSheet(path, Rows(new object[] { 1.0 }), "OnlySheet", append: true);

        Assert.Equal(new[] { "OnlySheet" }, XlsxLite.SheetNames(path));
    }

    [Fact]
    public void WriteWithoutAppend_ReplacesTheWholeFile()
    {
        var path = PathFor("overwrite.xlsx");
        XlsxLite.WriteSheet(path, Rows(new object[] { "a" }), "A");
        XlsxLite.WriteSheet(path, Rows(new object[] { "b" }), "B", append: false);

        Assert.Equal(new[] { "B" }, XlsxLite.SheetNames(path));
    }

    [Fact]
    public void ReadSheet_ByName_IsCaseInsensitiveFallback()
    {
        var path = PathFor("casename.xlsx");
        XlsxLite.WriteSheet(path, Rows(new object[] { "x" }), "Takeoff");

        Assert.Equal("x", XlsxLite.ReadSheet(path, "takeoff")[0][0]);
    }

    // ---------------------------------------------------------------- Errors

    [Fact]
    public void ReadSheet_MissingFile_ThrowsWithPath()
    {
        var path = PathFor("missing.xlsx");
        var ex = Assert.Throws<FileNotFoundException>(() => XlsxLite.ReadSheet(path));
        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void ReadSheet_UnknownSheet_ListsAvailableSheets()
    {
        var path = PathFor("unknown.xlsx");
        XlsxLite.WriteSheet(path, Rows(new object[] { 1.0 }), "Alpha");
        XlsxLite.WriteSheet(path, Rows(new object[] { 2.0 }), "Beta", append: true);

        var ex = Assert.Throws<ArgumentException>(() => XlsxLite.ReadSheet(path, "Gamma"));
        Assert.Contains("Gamma", ex.Message);
        Assert.Contains("Alpha", ex.Message);
        Assert.Contains("Beta", ex.Message);
    }

    [Fact]
    public void ReadSheet_NotAZip_SaysNotAnXlsx()
    {
        var path = PathFor("fake.xlsx");
        File.WriteAllText(path, "this is not a zip");

        var ex = Assert.Throws<InvalidDataException>(() => XlsxLite.ReadSheet(path));
        Assert.Contains(".xlsx", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("way:too/bad?name")]
    [InlineData("far far far far far too long a sheet name")]
    public void Write_InvalidSheetName_Throws(string sheetName)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => XlsxLite.WriteSheet(PathFor("bad.xlsx"), Rows(new object[] { 1.0 }), sheetName));
        Assert.Contains("name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------- Excel-shaped fixtures (reading)

    [Fact]
    public void Read_SharedStrings_IncludingRichTextRuns_AndConsecutiveCells()
    {
        // Hand-built package the way real Excel writes it: sharedStrings with a
        // plain <si><t> and a rich-text <si> split over two runs.
        var path = PathFor("shared.xlsx");
        WriteFixtureXlsx(path,
            sharedStringsXml:
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"3\" uniqueCount=\"3\">" +
                "<si><t>Hello</t></si>" +
                "<si><r><t>Rich </t></r><r><rPr><b/></rPr><t>Text</t></r></si>" +
                "<si><t xml:space=\"preserve\"> spaced </t></si>" +
                "</sst>",
            sheetXml:
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
                "<row r=\"1\">" +
                "<c r=\"A1\" t=\"s\"><v>0</v></c>" +
                "<c r=\"B1\" t=\"s\"><v>1</v></c>" +
                "<c r=\"C1\" t=\"s\"><v>2</v></c>" +
                "<c r=\"D1\"><v>7</v></c>" +
                "</row>" +
                "</sheetData></worksheet>");

        var rows = XlsxLite.ReadSheet(path);

        Assert.Equal("Hello", rows[0][0]);
        Assert.Equal("Rich Text", rows[0][1]);
        Assert.Equal(" spaced ", rows[0][2]);
        Assert.Equal(7.0, (double)rows[0][3]!, 12);
    }

    [Fact]
    public void Read_SparseSheet_PadsSkippedRowsAndCells()
    {
        // Row 2 entirely absent; row 3 has only C3 — Excel omits empty rows/cells.
        var path = PathFor("sparse.xlsx");
        WriteFixtureXlsx(path,
            sharedStringsXml: null,
            sheetXml:
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
                "<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>" +
                "<row r=\"3\"><c r=\"C3\"><v>3</v></c></row>" +
                "</sheetData></worksheet>");

        var rows = XlsxLite.ReadSheet(path);

        Assert.Equal(3, rows.Count);
        Assert.Equal(1.0, (double)rows[0][0]!, 12);
        Assert.Empty(rows[1]);
        Assert.Equal(new object[] { null, null, 3.0 }, rows[2]);
    }

    [Fact]
    public void Read_InlineStringsFormulaStringsAndErrorCells()
    {
        var path = PathFor("mixed.xlsx");
        WriteFixtureXlsx(path,
            sharedStringsXml: null,
            sheetXml:
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
                "<row r=\"1\">" +
                "<c r=\"A1\" t=\"inlineStr\"><is><t>inline</t></is></c>" +
                "<c r=\"B1\" t=\"inlineStr\"><is><r><t>two</t></r><r><t> runs</t></r></is></c>" +
                "<c r=\"C1\" t=\"str\"><f>CONCAT(A1)</f><v>formula result</v></c>" +
                "<c r=\"D1\" t=\"b\"><v>1</v></c>" +
                "<c r=\"E1\" t=\"e\"><v>#DIV/0!</v></c>" +
                "</row>" +
                "</sheetData></worksheet>");

        var rows = XlsxLite.ReadSheet(path);

        Assert.Equal("inline", rows[0][0]);
        Assert.Equal("two runs", rows[0][1]);
        Assert.Equal("formula result", rows[0][2]);
        Assert.Equal(true, rows[0][3]);
        Assert.Equal("#DIV/0!", rows[0][4]);
    }

    [Fact]
    public void Read_ScientificNotationNumbers()
    {
        var path = PathFor("sci.xlsx");
        WriteFixtureXlsx(path,
            sharedStringsXml: null,
            sheetXml:
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
                "<row r=\"1\"><c r=\"A1\"><v>1.5E-3</v></c></row>" +
                "</sheetData></worksheet>");

        var rows = XlsxLite.ReadSheet(path);

        Assert.Equal(0.0015, (double)rows[0][0]!, 12);
    }

    /// <summary>
    /// Writes a minimal but Excel-faithful package with one sheet named
    /// "Fixture" (and optionally a sharedStrings part) for reader tests.
    /// </summary>
    private static void WriteFixtureXlsx(string path, string sharedStringsXml, string sheetXml)
    {
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        AddEntry(zip, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            (sharedStringsXml != null
                ? "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>"
                : string.Empty) +
            "</Types>");
        AddEntry(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");
        AddEntry(zip, "xl/workbook.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"" +
            " xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"Fixture\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        AddEntry(zip, "xl/_rels/workbook.xml.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            (sharedStringsXml != null
                ? "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/>"
                : string.Empty) +
            "</Relationships>");
        if (sharedStringsXml != null)
        {
            AddEntry(zip, "xl/sharedStrings.xml", sharedStringsXml);
        }

        AddEntry(zip, "xl/worksheets/sheet1.xml", sheetXml);
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
