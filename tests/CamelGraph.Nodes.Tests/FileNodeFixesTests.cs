using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Files;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The node-audit fixes of the file nodes (SYS-05 .. SYS-39): relative paths in the graph's folder, browse modes, writers that flag
/// themselves, encodings, delimiters, ISO dates, numbers that stay text, folder search and copy, zip sources, Path.Join.
/// </summary>
[Collection("GraphContext")]
public class FileNodeFixesTests : IDisposable
{
    private readonly string _directory;

    public FileNodeFixesTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "CamelGraphFileFixes", Guid.NewGuid().ToString("N"));
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

    private string PathFor(params string[] parts) => Path.Combine(new[] { _directory }.Concat(parts).ToArray());

    private static List<object?> Items(params object?[] items) => new List<object?>(items);

    private static NodeRegistry Registry()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        return registry;
    }

    /// <summary>Runs one library node under the engine with typed-in values and returns it (state, messages and outputs).</summary>
    private static ZeroTouchNodeModel Run(string name, params (string Port, object? Value)[] inputs)
    {
        var registry = Registry();
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

    // ----------------------------------------------------- relative paths (SYS-17)

    [Fact]
    public void RelativePaths_StartInTheGraphFolder_ForTheTextCsvJsonAndLogNodes()
    {
        using (GraphContext.Use(_directory))
        {
            var written = FileNodes.WriteText("sub/a.txt", "hi");
            Assert.Equal(PathFor("sub", "a.txt"), written);
            Assert.Equal("hi", File.ReadAllText(PathFor("sub", "a.txt")));
            Assert.Equal("hi", FileNodes.ReadText("sub/a.txt"));

            FileExtraNodes.AppendText("sub/a.txt", "more", false);
            Assert.Equal("himore", FileNodes.ReadText("sub/a.txt"));

            FileNodes.WriteCsv("data/r.csv", Items(Items(1, "x")));
            FileExtraNodes.AppendCsv("data/r.csv", Items(Items(2, "y")));
            Assert.Equal(2, FileNodes.ReadCsv("data/r.csv").Count);

            FileNodes.WriteJson("data/j.json", new Dictionary<string, object?> { ["k"] = 1 });
            Assert.IsType<Dictionary<string, object?>>(FileNodes.ReadJson("data/j.json"));

            FileExtraNodes.WriteLog("logs/run.log", "hello");
            Assert.True(File.Exists(PathFor("logs", "run.log")));

            Assert.True(FileNodes.FileExists("sub/a.txt"));
            Assert.False(FileNodes.FileExists("sub/none.txt"));
        }
    }

    [Fact]
    public void RelativePaths_StartInTheGraphFolder_ForTheFileFolderZipAndExcelNodes()
    {
        using (GraphContext.Use(_directory))
        {
            FileNodes.WriteText("a.txt", "one");
            Assert.Equal(PathFor("b.txt"), FileExtraNodes.CopyFile("a.txt", "b.txt"));
            Assert.Equal(PathFor("c.txt"), FileExtraNodes.MoveFile("b.txt", "c.txt"));
            Assert.True((bool)FileExtraNodes.GetFileInfo("c.txt")["exists"]!);
            Assert.Equal(64, FileExtraNodes.GetFileHash("c.txt").Length);

            Assert.Equal(PathFor("f1", "f2"), FileExtraNodes.CreateDirectory("f1/f2"));
            Assert.True(FileExtraNodes.DirectoryExists("f1/f2"));
            Assert.Equal(new[] { PathFor("c.txt") }, FileExtraNodes.Find(".", "c*", recursive: false));

            Assert.Equal(PathFor("pack.zip"), FileExtraNodes.CreateZip(Items("a.txt", "f1"), "pack.zip"));
            Assert.Contains("a.txt", FileExtraNodes.ListZip("pack.zip"));
            FileExtraNodes.ExtractZip("pack.zip", "unzipped");
            Assert.True(File.Exists(PathFor("unzipped", "a.txt")));

            Assert.Equal(PathFor("book.xlsx"), ExcelNodes.WriteToFile("book.xlsx", Items(Items(1, 2))));
            Assert.Single(Assert.IsAssignableFrom<IList<object?>>(ExcelNodes.ReadFromFile("book.xlsx", hasHeaders: false)["rows"]));

            Assert.True(FileExtraNodes.DeleteFile("c.txt"));
            Assert.False(File.Exists(PathFor("c.txt")));
            Assert.True(FileExtraNodes.DeleteDirectory("unzipped", recursive: true));
            Assert.False(Directory.Exists(PathFor("unzipped")));

            Assert.Equal(PathFor("a.txt"), PathNodes.GetFullPath("a.txt"));
        }
    }

    [Fact]
    public void QuotesPastedFromExplorer_AreRemoved_AndNoFolderIsMadeFromThem()
    {
        var path = PathFor("quoted.txt");
        File.WriteAllText(path, "x");
        Assert.Equal("x", FileNodes.ReadText("\"" + path + "\""));
        Assert.True(FileNodes.FileExists("  \"" + path + "\"  "));
        Assert.Equal(PathFor("w.txt"), FileNodes.WriteText("\"" + PathFor("w.txt") + "\"", "y"));
    }

    [Fact]
    public void AMissingFileIsReportedWithItsFullPath_WhereTheNodeLooked()
    {
        using (GraphContext.Use(_directory))
        {
            var ex = Assert.Throws<FileNotFoundException>(() => FileNodes.ReadText("nothing.txt"));
            Assert.Contains(PathFor("nothing.txt"), ex.Message);
        }
    }

    [Fact]
    public void EveryPathInputOfTheFileNodes_HasABrowseMode()
    {
        // SYS-05: the picker used to be guessed from the port name; now each path input says what it is.
        var wanted = new HashSet<string>(StringComparer.Ordinal)
        {
            "path", "source", "destination", "zipPath", "directory", "executable", "workingDirectory",
        };
        var prefixes = new[] { "Text.", "CSV.", "JSON.ReadFromFile", "JSON.WriteToFile", "Excel.", "File.", "Directory.", "Zip.", "Log.", "System.Run", "System.OpenPath", "Web.Download", "XML.ReadFromFile" };
        var missing = new List<string>();
        foreach (var definition in Registry().Definitions)
        {
            if (!prefixes.Any(p => definition.Name.StartsWith(p, StringComparison.Ordinal)))
            {
                continue;
            }

            foreach (var input in definition.Inputs.Where(i => wanted.Contains(i.Name) && i.Name != "directory" || (i.Name == "directory" && definition.Name.StartsWith("Zip.", StringComparison.Ordinal))))
            {
                if (input.PathMode == null)
                {
                    missing.Add(definition.Name + "." + input.Name);
                }
            }
        }

        Assert.True(missing.Count == 0, "No [NodePath] on: " + string.Join(", ", missing));
    }

    [Fact]
    public void WritersOfFilesAreOpenedWithASaveDialog_AndFoldersWithAFolderChooser()
    {
        var registry = Registry();
        NodePathMode? Mode(string node, string input) =>
            registry.Definitions.Single(d => d.Name == node).Inputs.Single(i => i.Name == input).PathMode;

        Assert.Equal(NodePathMode.Save, Mode("Text.WriteToFile", "path"));
        Assert.Equal(NodePathMode.Save, Mode("CSV.WriteToFile", "path"));
        Assert.Equal(NodePathMode.Save, Mode("Excel.WriteToFile", "path"));
        Assert.Equal(NodePathMode.Save, Mode("Zip.Create", "zipPath"));
        Assert.Equal(NodePathMode.Save, Mode("File.Copy", "destination"));
        Assert.Equal(NodePathMode.Open, Mode("File.Copy", "source"));
        Assert.Equal(NodePathMode.Open, Mode("Excel.ReadFromFile", "path"));
        Assert.Equal(NodePathMode.Folder, Mode("Directory.Find", "path"));
        Assert.Equal(NodePathMode.Folder, Mode("Zip.Extract", "directory"));
        Assert.Contains("*.xlsx", registry.Definitions.Single(d => d.Name == "Excel.WriteToFile").Inputs.Single(i => i.Name == "path").PathFilter);
        Assert.Contains("*.csv", registry.Definitions.Single(d => d.Name == "CSV.ReadFromFile").Inputs.Single(i => i.Name == "path").PathFilter);
    }

    // ----------------------------------------------- null payloads and lists (SYS-18, SYS-19)

    [Fact]
    public void WriteText_NullText_IsAnError_AndTheExistingFileIsKept()
    {
        var path = PathFor("keep.txt");
        File.WriteAllText(path, "precious");
        var ex = Assert.Throws<ArgumentNullException>(() => FileNodes.WriteText(path, null));
        Assert.Contains("Text.WriteToFile", ex.Message);
        Assert.Contains("'text'", ex.Message);
        Assert.Equal("precious", File.ReadAllText(path));

        FileNodes.WriteText(path, string.Empty); // empty text still empties the file on purpose
        Assert.Equal(string.Empty, File.ReadAllText(path));
    }

    [Fact]
    public void WriteJson_NullData_IsAnError_AndTheExistingFileIsKept()
    {
        var path = PathFor("keep.json");
        File.WriteAllText(path, "{}");
        var ex = Assert.Throws<ArgumentNullException>(() => FileNodes.WriteJson(path, null));
        Assert.Contains("JSON.WriteToFile", ex.Message);
        Assert.Equal("{}", File.ReadAllText(path));
    }

    [Fact]
    public void WriteText_AListOfTexts_IsWrittenOneItemPerLine_NotAsNOverwrites()
    {
        // SYS-19: with a scalar path, a list of texts used to write the file three times, so only the last item survived.
        var path = PathFor("lines.txt");
        FileNodes.WriteText(path, Items("A", "B", "C"));
        Assert.Equal("A" + Environment.NewLine + "B" + Environment.NewLine + "C", File.ReadAllText(path));

        FileNodes.WriteText(path, Items("n", 2d, true, null));
        Assert.Equal(string.Join(Environment.NewLine, "n", "2", "True", string.Empty), File.ReadAllText(path));
    }

    [Fact]
    public void WriteText_AListWiredIntoTheEngine_ReachesTheNodeWhole()
    {
        var path = PathFor("engine.txt");
        var node = Run("Text.WriteToFile", ("path", path), ("text", Items("one", "two")));
        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal("one" + Environment.NewLine + "two", File.ReadAllText(path));
    }

    // ----------------------------------------------------------- encodings (SYS-11)

    [Fact]
    public void ReadText_ADefaultReadFallsBackToWindows1252_WhenTheBytesAreNotUtf8()
    {
        var path = PathFor("ansi.csv");
        File.WriteAllBytes(path, new byte[] { (byte)'M', 0xFC, (byte)'l', (byte)'l', (byte)'e', (byte)'r', 0x80 });
        Assert.Equal("M\u00FCller\u20AC", FileNodes.ReadText(path));
        Assert.Equal("M\u00FCller\u20AC", Assert.IsType<string>(((IList<object?>)FileNodes.ReadCsv(path)[0]!)[0]));
    }

    [Fact]
    public void ReadText_HonoursAByteOrderMark_AndAnExplicitChoice()
    {
        var bom = PathFor("bom.txt");
        File.WriteAllBytes(bom, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("\u00E9t\u00E9")).ToArray());
        Assert.Equal("\u00E9t\u00E9", FileNodes.ReadText(bom));
        Assert.Equal("\u00E9t\u00E9", FileNodes.ReadText(bom, "UTF-8"));

        var utf16 = PathFor("u16.txt");
        File.WriteAllBytes(utf16, new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("\u00E9\u4E2D")).ToArray());
        Assert.Equal("\u00E9\u4E2D", FileNodes.ReadText(utf16));
        Assert.Equal("\u00E9\u4E2D", FileNodes.ReadText(utf16, "UTF-16"));

        var ansi = PathFor("forced.txt");
        File.WriteAllBytes(ansi, new byte[] { 0xE9 }); // valid Windows-1252, invalid UTF-8
        Assert.Equal("\u00E9", FileNodes.ReadText(ansi, "Windows-1252"));
        Assert.Contains("encoding", Assert.Throws<ArgumentException>(() => FileNodes.ReadText(ansi, "EBCDIC")).Message);
    }

    [Fact]
    public void WriteText_Encodings_ProduceTheExpectedBytes()
    {
        var path = PathFor("enc.txt");
        FileNodes.WriteText(path, "\u00E9", "UTF-8");
        Assert.Equal(new byte[] { 0xC3, 0xA9 }, File.ReadAllBytes(path));

        FileNodes.WriteText(path, "\u00E9", "UTF-8 with BOM");
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, 0xC3, 0xA9 }, File.ReadAllBytes(path));

        FileNodes.WriteText(path, "\u00E9\u20AC", "Windows-1252");
        Assert.Equal(new byte[] { 0xE9, 0x80 }, File.ReadAllBytes(path));

        FileNodes.WriteText(path, "A\u00E9", "UTF-16");
        Assert.Equal(new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0xE9, 0x00 }, File.ReadAllBytes(path));

        // And they read back with the matching choice.
        Assert.Equal("A\u00E9", FileNodes.ReadText(path));
    }

    [Fact]
    public void WriteText_WindowsCp1252_ReplacesWhatItCannotHold_AndSaysSo()
    {
        var path = PathFor("lossy.txt");
        var node = Run("Text.WriteToFile", ("path", path), ("text", "a\u4E2Db"), ("encoding", "Windows-1252"));
        Assert.Equal("a?b", File.ReadAllText(path));
        Assert.Contains(node.Messages, m => m.Severity == MessageSeverity.Warning && m.Text.Contains("Windows-1252") && m.Text.StartsWith("1 character"));
    }

    [Fact]
    public void CsvAndAppendWriters_TakeAnEncoding_AndWriteTheMarkOnlyOnANewFile()
    {
        var path = PathFor("excel.csv");
        FileNodes.WriteCsv(path, Items(Items("\u00E9", 1)), ",", "UTF-8 with BOM");
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(path).Take(3).ToArray());
        Assert.Equal("\u00E9", ((IList<object?>)FileNodes.ReadCsv(path)[0]!)[0]);

        var appended = PathFor("appended.csv");
        FileExtraNodes.AppendCsv(appended, Items(Items("a")), ",", null, "UTF-8 with BOM");
        FileExtraNodes.AppendCsv(appended, Items(Items("b")), ",", null, "UTF-8 with BOM");
        var bytes = File.ReadAllBytes(appended);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        Assert.Equal(1, CountMarks(bytes));

        var text = PathFor("log.txt");
        FileExtraNodes.AppendText(text, "x", true, "UTF-16");
        FileExtraNodes.AppendText(text, "y", true, "UTF-16");
        Assert.Equal(new byte[] { 0xFF, 0xFE }, File.ReadAllBytes(text).Take(2).ToArray());
        Assert.Equal(1, CountMarks(File.ReadAllBytes(text), new byte[] { 0xFF, 0xFE }));

        var log = PathFor("l.log");
        FileExtraNodes.WriteLog(log, "m", "INFO", "Windows-1252");
        FileExtraNodes.WriteLog(log, "\u00E9", "INFO", "Windows-1252");
        Assert.Contains((byte)0xE9, File.ReadAllBytes(log));
    }

    private static int CountMarks(byte[] bytes, byte[]? mark = null)
    {
        mark = mark ?? new byte[] { 0xEF, 0xBB, 0xBF };
        var count = 0;
        for (var i = 0; i + mark.Length <= bytes.Length; i++)
        {
            if (mark.Where((b, k) => bytes[i + k] == b).Count() == mark.Length)
            {
                count++;
            }
        }

        return count;
    }

    [Fact]
    public void JsonRoundTrip_WorksWithAnEncodingChoice()
    {
        var path = PathFor("e.json");
        FileNodes.WriteJson(path, new Dictionary<string, object?> { ["n"] = "\u00E9" }, true, "UTF-16");
        var read = Assert.IsType<Dictionary<string, object?>>(FileNodes.ReadJson(path));
        Assert.Equal("\u00E9", read["n"]);
    }

    // ------------------------------------------------------------ delimiters (SYS-12)

    [Theory]
    [InlineData("tab")]
    [InlineData("TAB")]
    [InlineData("\t")]
    [InlineData("\\t")]
    public void TabIsADelimiter_UnderEveryName(string delimiter)
    {
        var path = PathFor("t.tsv");
        FileNodes.WriteCsv(path, Items(Items("a", "b c", 3)), delimiter);
        Assert.Equal("a\tb c\t3\n", File.ReadAllText(path));
        Assert.Equal(new object?[] { "a", "b c", 3d }, (IList<object?>)FileNodes.ReadCsv(path, delimiter)[0]!);
    }

    [Fact]
    public void TheDelimiterOfTheCsvNodes_IsADropdown_AndAMultiCharacterOneStillFails()
    {
        var registry = Registry();
        foreach (var node in new[] { "CSV.ReadFromFile", "CSV.WriteToFile", "CSV.AppendToFile" })
        {
            var choices = registry.Definitions.Single(d => d.Name == node).Inputs.Single(i => i.Name == "delimiter").Choices;
            Assert.NotNull(choices);
            Assert.Contains("tab", choices!);
            Assert.Contains(";", choices!);
        }

        var ex = Assert.Throws<ArgumentException>(() => FileNodes.WriteCsv(PathFor("x.csv"), Items(Items(1)), "::"));
        Assert.Contains("tab", ex.Message);
        Assert.False(File.Exists(PathFor("x.csv")));
    }

    // ---------------------------------------------------- dates in CSV (SYS-13)

    [Fact]
    public void CsvDates_AreIso_WhateverTheCulture()
    {
        var path = PathFor("dates.csv");
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            FileNodes.WriteCsv(path, Items(Items(new DateTime(2026, 10, 4, 14, 30, 5), new DateTime(2026, 1, 2), new DateTime(2026, 3, 4, 5, 6, 7, 250))));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }

        Assert.Equal("2026-10-04 14:30:05,2026-01-02 00:00:00,2026-03-04 05:06:07.25\n", File.ReadAllText(path));
    }

    [Fact]
    public void CsvAppend_WritesDatesAsIsoToo()
    {
        var path = PathFor("append-dates.csv");
        FileExtraNodes.AppendCsv(path, Items(Items(new DateTime(2026, 10, 4, 1, 2, 3))));
        Assert.Equal("2026-10-04 01:02:03\n", File.ReadAllText(path));
    }

    // ------------------------------------------ numbers that stay text (SYS-10, COL-09)

    [Fact]
    public void CsvRead_CodesThatLookLikeNumbers_StayText()
    {
        var path = PathFor("codes.csv");
        File.WriteAllText(path, "007,00123,-007,12345678901234567890,NaN,Infinity,-Infinity,0,0.5,-0,1.50,1e5,123456789012345,15,+3\n");
        var row = (IList<object?>)FileNodes.ReadCsv(path)[0]!;
        Assert.Equal(
            new object?[] { "007", "00123", "-007", "12345678901234567890", "NaN", "Infinity", "-Infinity", 0d, 0.5d, -0d, 1.5d, 100000d, 123456789012345d, 15d, 3d },
            row);
    }

    [Fact]
    public void CsvRead_NumbersTextKeepsEveryCellAsText()
    {
        var path = PathFor("asText.csv");
        File.WriteAllText(path, "1,2.5,abc\n");
        var row = (IList<object?>)FileNodes.ReadCsv(path, ",", "text")[0]!;
        Assert.Equal(new object?[] { "1", "2.5", "abc" }, row);
        Assert.Contains("numbers", Assert.Throws<ArgumentException>(() => FileNodes.ReadCsv(path, ",", "maybe")).Message);
    }

    [Fact]
    public void TableFromCsvFile_InheritsTheLeadingZeroFix()
    {
        var path = PathFor("marks.csv");
        File.WriteAllText(path, "mark,qty\n007,2\n");
        var table = TableToolkitNodes.FromCsvFile(path);
        Assert.Equal("007", table.Rows[0][0]);
        Assert.Equal(2d, table.Rows[0][1]);
    }

    // ---------------------------------------- the old ids still load (aliases) and run

    [Theory]
    [InlineData("CamelGraph.Nodes.FileNodes.ReadText@string", "Text.ReadFromFile")]
    [InlineData("CamelGraph.Nodes.FileNodes.WriteText@string,string", "Text.WriteToFile")]
    [InlineData("CamelGraph.Nodes.FileNodes.ReadCsv@string,string", "CSV.ReadFromFile")]
    [InlineData("CamelGraph.Nodes.FileNodes.WriteCsv@string,System.Collections.Generic.IList<object>,string", "CSV.WriteToFile")]
    [InlineData("CamelGraph.Nodes.FileNodes.ReadJson@string", "JSON.ReadFromFile")]
    [InlineData("CamelGraph.Nodes.FileNodes.WriteJson@string,object,bool", "JSON.WriteToFile")]
    [InlineData("CamelGraph.Nodes.FileExtraNodes.AppendText@string,string,bool", "Text.AppendToFile")]
    [InlineData("CamelGraph.Nodes.FileExtraNodes.AppendCsv@string,System.Collections.Generic.IList<object>,string,System.Collections.Generic.IList<object>", "CSV.AppendToFile")]
    [InlineData("CamelGraph.Nodes.FileExtraNodes.WriteLog@string,string,string", "Log.Write")]
    [InlineData("CamelGraph.Nodes.ExcelNodes.ReadFromFile@string,string,bool", "Excel.ReadFromFile")]
    public void TheIdsBeforeTheAuditStillResolve_ToTheSameNode(string oldId, string nodeName)
    {
        Assert.True(Registry().TryGetDefinition(oldId, out var definition));
        Assert.Equal(nodeName, definition!.Name);
    }

    [Fact]
    public void AGraphSavedWithTheOldWriterIds_LoadsAndRuns()
    {
        // A graph file stores the definition id and the typed values by port name; the old ids must keep working.
        var registry = Registry();
        var file = PathFor("old.txt");
        Assert.True(registry.TryGetDefinition("CamelGraph.Nodes.FileNodes.WriteText@string,string", out var write));
        var writeNode = new ZeroTouchNodeModel(write!);
        writeNode.InPorts.Single(p => p.Name == "path").SetUserValue(file);
        writeNode.InPorts.Single(p => p.Name == "text").SetUserValue("legacy");
        Assert.True(registry.TryGetDefinition("CamelGraph.Nodes.FileNodes.ReadText@string", out var read));
        var readNode = new ZeroTouchNodeModel(read!);
        var graph = new GraphModel();
        graph.AddNode(writeNode);
        graph.AddNode(readNode);
        Assert.True(graph.Connect(writeNode.OutPorts[0], readNode.InPorts[0]).Success);

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Executed, readNode.State);
        Assert.Equal("legacy", readNode.OutPorts[0].Value);
    }

    // --------------------------------------------------------- File.Move (SYS-15)

    [Fact]
    public void MoveOver_ReplacesTheDestination_WithoutLeavingScraps()
    {
        var source = PathFor("new.txt");
        var destination = PathFor("old.txt");
        File.WriteAllText(source, "new");
        File.WriteAllText(destination, "old");

        FileExtraNodes.MoveOver(source, destination);

        Assert.Equal("new", File.ReadAllText(destination));
        Assert.False(File.Exists(source));
        Assert.Equal(new[] { destination }, Directory.GetFiles(_directory));
    }

    [Fact]
    public void MoveFile_WhenTheMoveCannotHappen_TheDestinationIsNotLost()
    {
        // SYS-15: the old code deleted the destination first and then moved, so a failing move destroyed it.
        var destination = PathFor("keep.txt");
        File.WriteAllText(destination, "keep me");
        Assert.ThrowsAny<IOException>(() => FileExtraNodes.MoveOver(PathFor("vanished.txt"), destination));
        Assert.Equal("keep me", File.ReadAllText(destination));
        Assert.Equal(new[] { destination }, Directory.GetFiles(_directory));
    }

    [Fact]
    public void MoveFile_OverwriteStillReplacesAnExistingFile()
    {
        var source = PathFor("s.txt");
        var destination = PathFor("d.txt");
        File.WriteAllText(source, "S");
        File.WriteAllText(destination, "D");
        FileExtraNodes.MoveFile(source, destination, true);
        Assert.Equal("S", File.ReadAllText(destination));
        Assert.False(File.Exists(source));
    }

    // ----------------------------------------------- Directory.Find and its retired twins (SYS-16, SYS-29)

    [Fact]
    public void Find_SortsByNameIgnoringCase_WithNumbersInOrder()
    {
        foreach (var name in new[] { "B.ifc", "Zed.ifc", "a.ifc", "c10.ifc", "c2.ifc" })
        {
            File.WriteAllText(PathFor(name), string.Empty);
        }

        var found = FileExtraNodes.Find(_directory, "*.ifc").Select(Path.GetFileName).ToArray();

        Assert.Equal(new[] { "a.ifc", "B.ifc", "c2.ifc", "c10.ifc", "Zed.ifc" }, found);
        Assert.Equal(found.Reverse(), FileExtraNodes.Find(_directory, "*.ifc", sortBy: "name", descending: true).Select(Path.GetFileName));
    }

    [Fact]
    public void Find_KindFolders_ListsFolders_AndSizeIsNotAFolderSort()
    {
        Directory.CreateDirectory(PathFor("alpha"));
        Directory.CreateDirectory(PathFor("beta", "inner"));
        File.WriteAllText(PathFor("file.txt"), string.Empty);

        Assert.Equal(new[] { PathFor("alpha"), PathFor("beta"), PathFor("beta", "inner") }, FileExtraNodes.Find(_directory, "*", "folders"));
        Assert.Equal(new[] { PathFor("alpha"), PathFor("beta") }, FileExtraNodes.Find(_directory, "*", "folders", recursive: false));
        Assert.Equal(new[] { PathFor("file.txt") }, FileExtraNodes.Find(_directory, "*", "files", recursive: false));
        Assert.Contains("size", Assert.Throws<ArgumentException>(() => FileExtraNodes.Find(_directory, "*", "folders", sortBy: "size")).Message);
        Assert.Contains("kind", Assert.Throws<ArgumentException>(() => FileExtraNodes.Find(_directory, "*", "both")).Message);
    }

    [Fact]
    public void Find_LimitAndDescending_GiveTheNewestFile()
    {
        File.WriteAllText(PathFor("old.txt"), "1");
        File.SetLastWriteTimeUtc(PathFor("old.txt"), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.WriteAllText(PathFor("new.txt"), "2");
        File.SetLastWriteTimeUtc(PathFor("new.txt"), new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new[] { PathFor("new.txt") }, FileExtraNodes.Find(_directory, "*", "files", true, "modified", true, 1));
    }

    [Fact]
    public void TheThreeRetiredFinders_AreDeprecatedButStillRunUnderTheirOwnIds()
    {
        var registry = Registry();
        File.WriteAllText(PathFor("x.ifc"), string.Empty);
        File.WriteAllText(PathFor("x.ifcxml"), string.Empty); // .NET Framework's "*.ifc" would also return this one
        Directory.CreateDirectory(PathFor("sub"));

        foreach (var id in new[]
                 {
                     "CamelGraph.Nodes.FileNodes.GetFiles@string,string",
                     "CamelGraph.Nodes.FileExtraNodes.FindFiles@string,string,bool,string,bool,int",
                     "CamelGraph.Nodes.FileExtraNodes.GetDirectories@string,string,bool",
                 })
        {
            Assert.True(registry.TryGetDefinition(id, out var definition), id);
            Assert.True(definition!.IsDeprecated, id);
            Assert.Equal("Directory.Find", definition.Replacement);
            Assert.StartsWith("Retired", definition.Description);
        }

        Assert.Equal(new[] { PathFor("x.ifc") }, FileNodes.GetFiles(_directory, "*.ifc"));
        Assert.Equal(new[] { PathFor("x.ifc") }, FileExtraNodes.FindFiles(_directory, "*.ifc"));
        Assert.Equal(new[] { PathFor("sub") }, FileExtraNodes.GetDirectories(_directory));

        var node = Run("Directory.GetFiles", ("path", _directory), ("pattern", "*.ifc"));
        Assert.Equal(NodeState.Executed, node.State);
        Assert.False(registry.Definitions.Single(d => d.Name == "Directory.Find").IsDeprecated);
    }

    [Fact]
    public void DirectoryFind_CarriesTheSearchTagsOfTheNodesItReplaces()
    {
        var tags = Registry().Definitions.Single(d => d.Name == "Directory.Find").SearchTags;
        foreach (var word in new[] { "newest", "subfolders", "browse", "batch" })
        {
            Assert.Contains(word, tags);
        }
    }

    // --------------------------------------------------- Directory.Copy / Move (SYS-38)

    private string MakeTree(string name)
    {
        var root = PathFor(name);
        Directory.CreateDirectory(Path.Combine(root, "inner", "deep"));
        Directory.CreateDirectory(Path.Combine(root, "empty"));
        File.WriteAllText(Path.Combine(root, "a.txt"), "A");
        File.WriteAllText(Path.Combine(root, "inner", "b.txt"), "B");
        File.WriteAllText(Path.Combine(root, "inner", "deep", "c.txt"), "C");
        return root;
    }

    [Fact]
    public void DirectoryCopy_CopiesTheWholeTree_IncludingEmptyFolders()
    {
        var source = MakeTree("src");
        var destination = PathFor("out", "copy");

        Assert.Equal(destination, FileExtraNodes.CopyDirectory(source, destination));

        Assert.Equal("C", File.ReadAllText(Path.Combine(destination, "inner", "deep", "c.txt")));
        Assert.True(Directory.Exists(Path.Combine(destination, "empty")));
        Assert.True(File.Exists(Path.Combine(source, "a.txt")), "the source stays");
    }

    [Fact]
    public void DirectoryCopy_RefusesToReplaceFiles_BeforeCopyingAnything()
    {
        var source = MakeTree("src");
        var destination = PathFor("dest");
        Directory.CreateDirectory(Path.Combine(destination, "inner"));
        File.WriteAllText(Path.Combine(destination, "inner", "b.txt"), "OLD");

        var ex = Assert.Throws<IOException>(() => FileExtraNodes.CopyDirectory(source, destination));
        Assert.Contains("overwrite", ex.Message);
        Assert.Contains("Nothing was copied", ex.Message);
        Assert.False(File.Exists(Path.Combine(destination, "a.txt")));
        Assert.Equal("OLD", File.ReadAllText(Path.Combine(destination, "inner", "b.txt")));

        FileExtraNodes.CopyDirectory(source, destination, overwrite: true);
        Assert.Equal("B", File.ReadAllText(Path.Combine(destination, "inner", "b.txt")));
        Assert.Equal("A", File.ReadAllText(Path.Combine(destination, "a.txt")));
    }

    [Fact]
    public void DirectoryCopyAndMove_RefuseADestinationInsideTheSource()
    {
        var source = MakeTree("src");
        Assert.Contains("inside", Assert.Throws<ArgumentException>(() => FileExtraNodes.CopyDirectory(source, Path.Combine(source, "inner", "again"))).Message);
        Assert.Contains("inside", Assert.Throws<ArgumentException>(() => FileExtraNodes.MoveDirectory(source, Path.Combine(source, "x"))).Message);
        Assert.False(Directory.Exists(Path.Combine(source, "inner", "again")));
    }

    [Fact]
    public void DirectoryMove_RenamesTheFolder_AndNeedsOverwriteForAnExistingDestination()
    {
        var source = MakeTree("src");
        var destination = PathFor("moved", "here");

        Assert.Equal(destination, FileExtraNodes.MoveDirectory(source, destination));
        Assert.False(Directory.Exists(source));
        Assert.Equal("B", File.ReadAllText(Path.Combine(destination, "inner", "b.txt")));

        var second = MakeTree("again");
        var clash = Assert.Throws<IOException>(() => FileExtraNodes.MoveDirectory(second, destination));
        Assert.Contains("overwrite", clash.Message);
        Assert.True(Directory.Exists(second));

        FileExtraNodes.MoveDirectory(second, destination, overwrite: true);
        Assert.False(Directory.Exists(second));
        Assert.Equal("A", File.ReadAllText(Path.Combine(destination, "a.txt")));
    }

    [Fact]
    public void DirectoryCopyAndMove_ReportAMissingSourceAndABlankDestination()
    {
        Assert.Contains("'source'", Assert.Throws<ArgumentException>(() => FileExtraNodes.CopyDirectory(" ", PathFor("d"))).Message);
        Assert.Contains("'destination'", Assert.Throws<ArgumentException>(() => FileExtraNodes.MoveDirectory(_directory, "")).Message);
        Assert.Contains(PathFor("ghost"), Assert.Throws<DirectoryNotFoundException>(() => FileExtraNodes.CopyDirectory(PathFor("ghost"), PathFor("d"))).Message);
    }

    // ------------------------------------------------------------ Zip.Create sources (SYS-22)

    [Fact]
    public void ZipSources_TakeManyWires_AndNestedLists()
    {
        var registry = Registry();
        Assert.True(registry.Definitions.Single(d => d.Name == "Zip.Create").Inputs.Single(i => i.Name == "sources").MultiInput);

        File.WriteAllText(PathFor("a.txt"), "A");
        File.WriteAllText(PathFor("b.txt"), "B");
        Directory.CreateDirectory(PathFor("folder"));
        File.WriteAllText(PathFor("folder", "c.txt"), "C");
        var zip = PathFor("nested.zip");

        FileExtraNodes.CreateZip(Items(Items(PathFor("a.txt"), Items(PathFor("b.txt"))), PathFor("folder")), zip);

        Assert.Equal(new[] { "a.txt", "b.txt", "folder/", "folder/c.txt" }, FileExtraNodes.ListZip(zip).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Contains("#3", Assert.Throws<ArgumentException>(() => FileExtraNodes.CreateZip(Items(PathFor("a.txt"), Items(PathFor("b.txt"), 7)), PathFor("bad.zip"))).Message);
    }

    // ----------------------------------------------------------------- Path.Join (SYS-39)

    [Fact]
    public void PathJoin_JoinsAnyNumberOfParts_SkippingBlanks()
    {
        var sep = Path.DirectorySeparatorChar;
        Assert.Equal(string.Join(sep.ToString(), "root", "project", "hvac", "model.nwd"), PathNodes.Join(Items("root", "project", Items("", null, "hvac"), "model.nwd")));
        Assert.Equal("only", PathNodes.Join(Items("only")));
        Assert.Equal(string.Join(sep.ToString(), "a", "2026"), PathNodes.Join(Items("a", 2026d)));
    }

    [Fact]
    public void PathJoin_ALeadingSeparatorDoesNotThrowAwayWhatCameBefore_UnlikePathCombine()
    {
        var sep = Path.DirectorySeparatorChar;
        Assert.Equal("a" + sep + "b", PathNodes.Join(Items("a", sep + "b")));
        Assert.Equal("a" + sep + "b", PathNodes.Join(Items("a", "/b")));
        Assert.Equal(sep + "b", Path.Combine("a", sep + "b")); // the trap the Path.Combine description now warns about
        Assert.Contains("trap", Registry().Definitions.Single(d => d.Name == "Path.Combine").Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PathJoin_NoPartsIsAnError_AndTheNodeTakesManyWires()
    {
        Assert.Contains("parts", Assert.Throws<ArgumentException>(() => PathNodes.Join(Items("", null))).Message);
        Assert.Contains("parts", Assert.Throws<ArgumentNullException>(() => PathNodes.Join(null!)).Message);
        Assert.True(Registry().Definitions.Single(d => d.Name == "Path.Join").Inputs.Single().MultiInput);
        if (PathNodes.IsWindows)
        {
            Assert.Contains("drive", Assert.Throws<ArgumentException>(() => PathNodes.Join(Items("C:\\a", "D:\\b"))).Message);
        }
    }

    // ------------------------------------------------------------ friendly failures (ENG-15)

    [Fact]
    public void AFileThatCannotBeRead_IsReportedInPlainWords_NotWithFrameworkText()
    {
        // A folder where a file is expected: the framework says "Access to the path is denied" or "is a directory".
        var folder = PathFor("iamafolder");
        Directory.CreateDirectory(folder);
        var ex = Assert.ThrowsAny<Exception>(() => FileNodes.WriteText(folder, "x"));
        Assert.Contains("Text.WriteToFile", ex.Message);
        Assert.Contains("folder", ex.Message);
        Assert.DoesNotContain("System.", ex.Message);
    }

    [Fact]
    public void FriendlyErrors_NameTheNodeAndTheFile_AndSayWhatToDo()
    {
        var path = PathFor("f.txt");
        var denied = FileErrors.Friendly(new UnauthorizedAccessException("Access to the path '" + path + "' is denied."), "CSV.WriteToFile", path, true);
        Assert.StartsWith("CSV.WriteToFile: access to '" + path + "' was denied", denied.Message);
        Assert.Contains("read-only", denied.Message);

        var locked = FileErrors.Friendly(new IOException("sharing", unchecked((int)0x80070020)), "Text.ReadFromFile", path, false);
        Assert.Contains("in use by another program", locked.Message);

        var full = FileErrors.Friendly(new IOException("disk", unchecked((int)0x80070070)), "Text.WriteToFile", path, true);
        Assert.Contains("no room", full.Message);

        var tooLong = FileErrors.Friendly(new PathTooLongException(), "Text.WriteToFile", path, true);
        Assert.Contains("too long", tooLong.Message);
    }
}
