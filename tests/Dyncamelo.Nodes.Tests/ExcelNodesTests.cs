using System;
using System.Collections.Generic;
using System.IO;
using Dyncamelo.Nodes.Internal;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

public class ExcelNodesTests : IDisposable
{
    private readonly string _directory;

    public ExcelNodesTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "DyncameloExcelNodesTests", Guid.NewGuid().ToString("N"));
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

    private static IList<object?> RowList(params object?[][] rows)
    {
        var list = new List<object?>();
        foreach (var row in rows)
        {
            list.Add(new List<object?>(row));
        }

        return list;
    }

    private static IList<object?> Cells(params object?[] cells) => new List<object?>(cells);

    // ------------------------------------------------------------ Round-trip

    [Fact]
    public void WriteThenRead_WithHeaders_SplitsHeadersFromRows()
    {
        var path = PathFor("headers.xlsx");
        ExcelNodes.WriteToFile(
            path,
            RowList(
                new object?[] { "Wall", 42.5, true },
                new object?[] { "Door", -3.0, false }),
            headers: Cells("Name", "Count", "Active"));

        var result = ExcelNodes.ReadFromFile(path);

        var headers = Assert.IsType<List<string>>(result["headers"]);
        Assert.Equal(new[] { "Name", "Count", "Active" }, headers);

        var rows = Assert.IsType<List<object?>>(result["rows"]);
        Assert.Equal(2, rows.Count);
        var row0 = Assert.IsType<List<object?>>(rows[0]);
        Assert.Equal("Wall", row0[0]);
        Assert.Equal(42.5, (double)row0[1]!, 12);
        Assert.Equal(true, row0[2]);
        var row1 = Assert.IsType<List<object?>>(rows[1]);
        Assert.Equal(false, row1[2]);

        var sheetNames = Assert.IsType<List<string>>(result["sheetNames"]);
        Assert.Equal(new[] { "Sheet1" }, sheetNames);
    }

    [Fact]
    public void Read_WithoutHeaders_ReturnsAllRowsAndEmptyHeaders()
    {
        var path = PathFor("noheaders.xlsx");
        ExcelNodes.WriteToFile(path, RowList(
            new object?[] { "a", 1.0 },
            new object?[] { "b", 2.0 }));

        var result = ExcelNodes.ReadFromFile(path, hasHeaders: false);

        Assert.Empty(Assert.IsType<List<string>>(result["headers"]));
        Assert.Equal(2, Assert.IsType<List<object?>>(result["rows"]).Count);
    }

    [Fact]
    public void Read_NumericHeaderCells_AreFormattedInvariantly()
    {
        var path = PathFor("numheader.xlsx");
        ExcelNodes.WriteToFile(path, RowList(new object?[] { "x" }), headers: Cells("Name", 1.5, null));

        var result = ExcelNodes.ReadFromFile(path);

        var headers = Assert.IsType<List<string>>(result["headers"]);
        Assert.Equal("Name", headers[0]);
        Assert.Equal("1.5", headers[1]);
    }

    [Fact]
    public void Read_EmptyWorkbook_YieldsEmptyRowsAndHeaders()
    {
        var path = PathFor("empty.xlsx");
        ExcelNodes.WriteToFile(path, new List<object?>());

        var result = ExcelNodes.ReadFromFile(path);

        Assert.Empty(Assert.IsType<List<object?>>(result["rows"]));
        Assert.Empty(Assert.IsType<List<string>>(result["headers"]));
    }

    [Fact]
    public void WriteThenRead_DateTimeRoundTripsAsSerialNumber()
    {
        var path = PathFor("dates.xlsx");
        var date = new DateTime(2026, 7, 10, 12, 30, 0);
        ExcelNodes.WriteToFile(path, RowList(new object?[] { date }));

        var result = ExcelNodes.ReadFromFile(path, hasHeaders: false);

        var rows = Assert.IsType<List<object?>>(result["rows"]);
        var row = Assert.IsType<List<object?>>(rows[0]);
        var serial = Assert.IsType<double>(row[0]);
        Assert.Equal(date, XlsxLite.DateFromSerial(serial), TimeSpan.FromSeconds(1));
    }

    // -------------------------------------------------------------- Sheets

    [Fact]
    public void Append_AddsSecondSheet_AndReadSelectsByName()
    {
        var path = PathFor("multi.xlsx");
        ExcelNodes.WriteToFile(path, RowList(new object?[] { "first" }), sheet: "Alpha");
        ExcelNodes.WriteToFile(path, RowList(new object?[] { "second" }), sheet: "Beta", append: true);

        var alpha = ExcelNodes.ReadFromFile(path, "Alpha", hasHeaders: false);
        var beta = ExcelNodes.ReadFromFile(path, "Beta", hasHeaders: false);

        Assert.Equal(new[] { "Alpha", "Beta" }, Assert.IsType<List<string>>(alpha["sheetNames"]));
        Assert.Equal("first", ((List<object?>)((List<object?>)alpha["rows"])[0]!)[0]);
        Assert.Equal("second", ((List<object?>)((List<object?>)beta["rows"])[0]!)[0]);
    }

    [Fact]
    public void Append_ReplacesSameNamedSheet()
    {
        var path = PathFor("replace.xlsx");
        ExcelNodes.WriteToFile(path, RowList(new object?[] { "old" }), sheet: "Data");
        ExcelNodes.WriteToFile(path, RowList(new object?[] { "new" }), sheet: "Data", append: true);

        var result = ExcelNodes.ReadFromFile(path, "Data", hasHeaders: false);

        Assert.Equal(new[] { "Data" }, Assert.IsType<List<string>>(result["sheetNames"]));
        var rows = Assert.IsType<List<object?>>(result["rows"]);
        Assert.Single(rows);
        Assert.Equal("new", ((List<object?>)rows[0]!)[0]);
    }

    [Fact]
    public void Read_UnknownSheet_ThrowsListingAvailableSheets()
    {
        var path = PathFor("sheets.xlsx");
        ExcelNodes.WriteToFile(path, RowList(new object?[] { 1.0 }), sheet: "Takeoff");

        var ex = Assert.Throws<ArgumentException>(() => ExcelNodes.ReadFromFile(path, "Nope"));
        Assert.Contains("Nope", ex.Message);
        Assert.Contains("Takeoff", ex.Message);
    }

    [Fact]
    public void Write_InvalidSheetName_Throws()
    {
        var path = PathFor("badsheet.xlsx");
        var ex = Assert.Throws<ArgumentException>(
            () => ExcelNodes.WriteToFile(path, RowList(new object?[] { 1.0 }), sheet: "bad[name]"));
        Assert.Contains("bad[name]", ex.Message);
    }

    // -------------------------------------------------------------- Errors

    [Fact]
    public void Read_MissingFile_ThrowsWithPathInMessage()
    {
        var path = PathFor("missing.xlsx");
        var ex = Assert.Throws<FileNotFoundException>(() => ExcelNodes.ReadFromFile(path));
        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void Read_EmptyPath_ThrowsHelpfulMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => ExcelNodes.ReadFromFile(" "));
        Assert.Contains("File Path", ex.Message);
    }

    [Fact]
    public void Read_NonZipFile_ThrowsNotAnXlsxMessage()
    {
        var path = PathFor("fake.xlsx");
        File.WriteAllText(path, "this is not a zip");
        var ex = Assert.Throws<InvalidDataException>(() => ExcelNodes.ReadFromFile(path));
        Assert.Contains("not an .xlsx", ex.Message);
    }

    [Fact]
    public void Write_NullRows_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => ExcelNodes.WriteToFile(PathFor("null.xlsx"), null!));
        Assert.Contains("Excel.WriteToFile", ex.Message);
    }

    [Fact]
    public void Write_EmptyPath_ThrowsHelpfulMessage()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => ExcelNodes.WriteToFile("", RowList(new object?[] { 1.0 })));
        Assert.Contains("File Path", ex.Message);
    }

    // ------------------------------------------------------------- Shaping

    [Fact]
    public void Write_ScalarRows_BecomeSingleCellRows()
    {
        var path = PathFor("scalars.xlsx");
        ExcelNodes.WriteToFile(path, new List<object?> { "loose", 7.0 });

        var result = ExcelNodes.ReadFromFile(path, hasHeaders: false);

        var rows = Assert.IsType<List<object?>>(result["rows"]);
        Assert.Equal(2, rows.Count);
        Assert.Equal("loose", ((List<object?>)rows[0]!)[0]);
        Assert.Equal(7.0, ((List<object?>)rows[1]!)[0]);
    }

    [Fact]
    public void Write_ReturnsPathAndCreatesMissingDirectories()
    {
        var path = PathFor(Path.Combine("sub", "dir", "report.xlsx"));
        var written = ExcelNodes.WriteToFile(path, RowList(new object?[] { "x" }));

        Assert.Equal(path, written);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Write_NullHeaders_WritesRowsOnly()
    {
        var path = PathFor("nullheaders.xlsx");
        ExcelNodes.WriteToFile(path, RowList(new object?[] { "only" }), headers: null);

        var result = ExcelNodes.ReadFromFile(path, hasHeaders: false);

        Assert.Single(Assert.IsType<List<object?>>(result["rows"]));
    }
}
