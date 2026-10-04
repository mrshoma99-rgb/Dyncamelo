using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Nodes.Internal;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>The node-audit fixes of the Excel nodes (SYS-13, SYS-14, SYS-20, SYS-31): real date cells, safe appends, trimmed empty rows.</summary>
public class ExcelFixesTests : IDisposable
{
    private readonly string _directory;

    public ExcelFixesTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "CamelGraphExcelFixes", Guid.NewGuid().ToString("N"));
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

    private string PathFor(string name) => Path.Combine(_directory, name);

    private static List<object?> Items(params object?[] items) => new List<object?>(items);

    private static ZeroTouchNodeModel Run(string name, params (string Port, object? Value)[] inputs)
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var node = new ZeroTouchNodeModel(registry.Definitions.Single(d => d.Name == name));
        foreach (var (port, value) in inputs)
        {
            node.InPorts.Single(p => p.Name == port).SetUserValue(value);
        }

        var graph = new GraphModel();
        graph.AddNode(node);
        new GraphEngine().Run(graph);
        return node;
    }

    private static string Part(string workbook, string part)
    {
        using (var zip = ZipFile.OpenRead(workbook))
        using (var reader = new StreamReader(zip.GetEntry(part)!.Open(), Encoding.UTF8))
        {
            return reader.ReadToEnd();
        }
    }

    // ------------------------------------------------------------ dates (SYS-13b)

    [Fact]
    public void DateTimeCells_AreRealDateCells_WithADateStyle()
    {
        var path = PathFor("dates.xlsx");
        var moment = new DateTime(2026, 10, 4, 14, 30, 0);
        ExcelNodes.WriteToFile(path, Items(Items(moment, new DateTime(2026, 1, 2), "text", 5d)));

        var sheet = Part(path, "xl/worksheets/sheet1.xml");
        Assert.Contains("<c r=\"A1\" s=\"1\"><v>" + moment.ToOADate().ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "</v></c>", sheet);
        Assert.Contains("<c r=\"B1\" s=\"2\">", sheet);
        Assert.DoesNotContain("<c r=\"D1\" s=", sheet);

        var styles = Part(path, "xl/styles.xml");
        Assert.Contains("yyyy", styles);
        Assert.Contains("<cellXfs count=\"3\">", styles);
        Assert.Contains("styles", Part(path, "[Content_Types].xml"));
        Assert.Contains("styles.xml", Part(path, "xl/_rels/workbook.xml.rels"));

        // Excel.ReadFromFile keeps handing out serial numbers (documented); the re-emit path sees them as dates.
        var plain = XlsxLite.ReadSheet(path);
        Assert.Equal(moment.ToOADate(), Assert.IsType<double>(plain[0][0]));
        var asDates = XlsxLite.ReadSheet(path, null, datesAsDateTime: true);
        Assert.Equal(moment, Assert.IsType<DateTime>(asDates[0][0]));
        Assert.Equal(new DateTime(2026, 1, 2), Assert.IsType<DateTime>(asDates[0][1]));
        Assert.Equal("text", asDates[0][2]);
        Assert.Equal(5d, asDates[0][3]);
    }

    [Fact]
    public void AppendingASheet_KeepsTheDatesOfTheEarlierSheetsAsDates()
    {
        var path = PathFor("two.xlsx");
        var moment = new DateTime(2026, 5, 6, 7, 8, 9);
        ExcelNodes.WriteToFile(path, Items(Items(moment)), null, "First");
        ExcelNodes.WriteToFile(path, Items(Items(1d)), null, "Second", append: true);

        Assert.Equal(new[] { "First", "Second" }, XlsxLite.SheetNames(path));
        Assert.Contains("s=\"1\"", Part(path, "xl/worksheets/sheet1.xml"));
        Assert.Equal(moment, XlsxLite.ReadSheet(path, "First", datesAsDateTime: true)[0][0]);
    }

    [Fact]
    public void ADateBeforeYear100_IsWrittenAsText_NotAsAnError()
    {
        var path = PathFor("early.xlsx");
        ExcelNodes.WriteToFile(path, Items(Items(new DateTime(50, 1, 1))));
        Assert.Equal("0050-01-01 00:00:00", XlsxLite.ReadSheet(path)[0][0]);
    }

    // ------------------------------------------------- list cells and non-finite numbers (SYS-13c, d)

    [Fact]
    public void AListInACell_IsWrittenLikeInCsv_NotAsTheTypeName()
    {
        var path = PathFor("lists.xlsx");
        ExcelNodes.WriteToFile(path, Items(Items(Items(1d, 2d), new Dictionary<string, object?> { ["a"] = 1d }, Items("x", Items("y")))));
        var row = XlsxLite.ReadSheet(path)[0];
        Assert.Equal("[1, 2]", row[0]);
        Assert.Equal("{a : 1}", row[1]);
        Assert.Equal("[x, [y]]", row[2]);
    }

    [Fact]
    public void NaNAndInfinity_AreWrittenAsText_AndTheNodeSaysSo()
    {
        var path = PathFor("nan.xlsx");
        var node = Run("Excel.WriteToFile", ("path", path), ("rows", Items(Items(double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1.5))));

        var row = XlsxLite.ReadSheet(path)[0];
        Assert.Equal(new object?[] { "NaN", "Infinity", "-Infinity", 1.5 }, row);
        Assert.DoesNotContain("<v>NaN</v>", Part(path, "xl/worksheets/sheet1.xml"));
        Assert.Contains(node.Messages, m => m.Severity == MessageSeverity.Warning && m.Text.StartsWith("3 cells held NaN or Infinity"));
    }

    // ------------------------------------------------------------ safe writing (SYS-14)

    private sealed class Unwritable
    {
        public override string ToString() => throw new InvalidOperationException("cannot be written");
    }

    [Fact]
    public void AFailureWhileWriting_LeavesTheOriginalWorkbookIntact_AndNoTemporaryFile()
    {
        var path = PathFor("safe.xlsx");
        ExcelNodes.WriteToFile(path, Items(Items("original", 1d)));
        var before = File.ReadAllBytes(path);

        Assert.Throws<InvalidOperationException>(() => ExcelNodes.WriteToFile(path, Items(Items("fine", new Unwritable()))));
        Assert.Throws<InvalidOperationException>(() => ExcelNodes.WriteToFile(path, Items(Items(new Unwritable())), null, "Other", append: true));

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(_directory));
        Assert.Equal("original", XlsxLite.ReadSheet(path)[0][0]);
    }

    [Fact]
    public void AppendingToAWorkbookMadeElsewhere_WarnsWhatTheRewriteCannotKeep()
    {
        var foreign = PathFor("foreign.xlsx");
        MakeForeignWorkbook(foreign);

        var node = Run("Excel.WriteToFile", ("path", foreign), ("rows", Items(Items("new"))), ("sheet", "Added"), ("append", true));

        Assert.Equal(NodeState.Warning, node.State);
        var warning = Assert.Single(node.Messages, m => m.Severity == MessageSeverity.Warning).Text;
        Assert.Contains("1 formula cell(s)", warning);
        Assert.Contains("formatting", warning);
        Assert.Equal(new[] { "Data", "Added" }, XlsxLite.SheetNames(foreign));
        Assert.Equal(3d, XlsxLite.ReadSheet(foreign, "Data")[0][0]); // the formula's last result stays as a value
    }

    [Fact]
    public void AppendingToAWorkbookThisNodeWrote_IsNotAWarning()
    {
        var own = PathFor("own.xlsx");
        ExcelNodes.WriteToFile(own, Items(Items(1d)));

        var node = Run("Excel.WriteToFile", ("path", own), ("rows", Items(Items(2d))), ("sheet", "More"), ("append", true));

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Empty(node.Messages.Where(m => m.Severity == MessageSeverity.Warning));
    }

    private static void MakeForeignWorkbook(string path)
    {
        using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            void Add(string name, string content)
            {
                using (var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)))
                {
                    writer.Write(content);
                }
            }

            const string main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            Add("[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/></Types>");
            Add("_rels/.rels", "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add("xl/workbook.xml", "<?xml version=\"1.0\"?><workbook xmlns=\"" + main + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Data\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            Add("xl/worksheets/sheet1.xml", "<?xml version=\"1.0\"?><worksheet xmlns=\"" + main + "\"><sheetData><row r=\"1\"><c r=\"A1\"><f>1+2</f><v>3</v></c></row></sheetData></worksheet>");
            Add("docProps/app.xml", "<Properties/>");
            Add("xl/theme/theme1.xml", "<theme/>");
        }
    }

    // ------------------------------------------------- empty rows and padding (SYS-31)

    private string WorkbookWithAnEmptyTail()
    {
        var path = PathFor("tail.xlsx");
        // Rows 1-2 hold data, row 3 is blank on purpose (a separator), rows 4-6 hold nothing but formatting.
        XlsxLite.WriteSheet(
            path,
            new List<IReadOnlyList<object?>?>
            {
                new List<object?> { "a", "b", "c" },
                new List<object?> { 1d },
                new List<object?>(),
                new List<object?> { 2d, "x" },
                new List<object?> { null },
                new List<object?> { string.Empty, null },
                new List<object?>(),
            });
        return path;
    }

    [Fact]
    public void ReadFromFile_DropsTheEmptyRowsAtTheEnd_ButKeepsABlankRowInTheMiddle()
    {
        var path = WorkbookWithAnEmptyTail();

        var read = ExcelNodes.ReadFromFile(path);

        Assert.Equal(new[] { "a", "b", "c" }, (IList<string>)read["headers"]);
        var rows = (IList<object?>)read["rows"];
        Assert.Equal(3, rows.Count);
        Assert.Equal(new object?[] { 1d, null, null }, (IList<object?>)rows[0]!);
        Assert.Equal(new object?[] { null, null, null }, (IList<object?>)rows[1]!);
        Assert.Equal(new object?[] { 2d, "x", null }, (IList<object?>)rows[2]!);
    }

    [Fact]
    public void ReadFromFile_TrimEmptyRowsOffKeepsEveryRow_StillPaddedToTheSameWidth()
    {
        var path = WorkbookWithAnEmptyTail();

        var read = ExcelNodes.ReadFromFile(path, "", hasHeaders: false, trimEmptyRows: false);

        var rows = (IList<object?>)read["rows"];
        Assert.Equal(7, rows.Count);
        Assert.All(rows, r => Assert.Equal(3, ((IList<object?>)r!).Count));
    }

    [Fact]
    public void ReadFromFile_ASheetOfOnlyEmptyRows_IsEmpty()
    {
        var path = PathFor("blank.xlsx");
        XlsxLite.WriteSheet(path, new List<IReadOnlyList<object?>?> { new List<object?>(), new List<object?> { null } });
        var read = ExcelNodes.ReadFromFile(path, "", hasHeaders: true);
        Assert.Empty((IList<object?>)read["rows"]);
        Assert.Empty((IList<string>)read["headers"]);
    }

    [Fact]
    public void TableFromExcelFile_InheritsTheTrimmedAndPaddedRows()
    {
        var path = WorkbookWithAnEmptyTail();
        var table = TableToolkitNodes.FromExcelFile(path);
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal(3, table.Rows[0].Length);
    }

    // ------------------------------------------------------------- panels (SYS-20) and the old id

    [Fact]
    public void ExcelNodes_KeepTheOptionsInTheAdvancedPanel_AndTheReaderIsStillFoundUnderItsOldId()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var read = registry.Definitions.Single(d => d.Name == "Excel.ReadFromFile");
        var write = registry.Definitions.Single(d => d.Name == "Excel.WriteToFile");

        Assert.Equal(string.Empty, read.Inputs.Single(i => i.Name == "sheet").Panel ?? string.Empty);
        Assert.Equal("Advanced", read.Inputs.Single(i => i.Name == "hasHeaders").Panel);
        Assert.Equal("Advanced", read.Inputs.Single(i => i.Name == "trimEmptyRows").Panel);
        Assert.True((bool)read.Inputs.Single(i => i.Name == "trimEmptyRows").DefaultValue!);
        Assert.Equal("Advanced", write.Inputs.Single(i => i.Name == "sheet").Panel);
        Assert.Equal("Advanced", write.Inputs.Single(i => i.Name == "append").Panel);
        Assert.Equal(NodePathMode.Save, write.Inputs.Single(i => i.Name == "path").PathMode);
        Assert.Equal(NodePathMode.Open, read.Inputs.Single(i => i.Name == "path").PathMode);

        // Cross references (SYS-28).
        Assert.Contains("Table.FromExcelFile", read.Description);
        Assert.Contains("Table.ToExcelFile", write.Description);
    }
}
