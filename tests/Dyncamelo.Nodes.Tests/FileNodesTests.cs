using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

public class FileNodesTests : IDisposable
{
    private readonly string _directory;

    public FileNodesTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "DyncameloNodesTests", Guid.NewGuid().ToString("N"));
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

    // ------------------------------------------------------------------ Text

    [Fact]
    public void Text_WriteThenRead_RoundTrips()
    {
        var path = PathFor("note.txt");
        var written = FileNodes.WriteText(path, "Hello\nDyncamelo");
        Assert.Equal(path, written);
        Assert.Equal("Hello\nDyncamelo", FileNodes.ReadText(path));
    }

    [Fact]
    public void Text_Write_CreatesMissingDirectories()
    {
        var path = PathFor(Path.Combine("sub", "dir", "note.txt"));
        FileNodes.WriteText(path, "x");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Text_Read_MissingFile_ThrowsWithPathInMessage()
    {
        var path = PathFor("missing.txt");
        var ex = Assert.Throws<FileNotFoundException>(() => FileNodes.ReadText(path));
        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void Text_Read_EmptyPath_ThrowsHelpfulMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => FileNodes.ReadText(" "));
        Assert.Contains("File Path", ex.Message);
    }

    // ------------------------------------------------------------------- CSV

    [Fact]
    public void Csv_WriteThenRead_RoundTripsNumbersAndStrings()
    {
        var path = PathFor("data.csv");
        var data = new List<object?>
        {
            new List<object?> { 1.5, "plain", "with, comma" },
            new List<object?> { -2, "with \"quotes\"", "multi\nline" },
        };

        FileNodes.WriteCsv(path, data);
        var read = FileNodes.ReadCsv(path);

        Assert.Equal(2, read.Count);
        var row0 = Assert.IsAssignableFrom<IList<object?>>(read[0]);
        Assert.Equal(1.5, row0[0]);
        Assert.Equal("plain", row0[1]);
        Assert.Equal("with, comma", row0[2]);

        var row1 = Assert.IsAssignableFrom<IList<object?>>(read[1]);
        Assert.Equal(-2d, row1[0]);
        Assert.Equal("with \"quotes\"", row1[1]);
        Assert.Equal("multi\nline", row1[2]);
    }

    [Fact]
    public void Csv_Read_ParsesUnquotedNumbersOnly()
    {
        var path = PathFor("mixed.csv");
        File.WriteAllText(path, "1,\"2\",three\n");
        var read = FileNodes.ReadCsv(path);
        var row = Assert.IsAssignableFrom<IList<object?>>(read[0]);
        Assert.Equal(1d, row[0]);
        Assert.Equal("2", row[1]); // quoted cells stay text
        Assert.Equal("three", row[2]);
    }

    [Fact]
    public void Csv_Read_HandlesMissingTrailingNewlineAndEmptyCells()
    {
        var path = PathFor("edge.csv");
        File.WriteAllText(path, "a,,b\r\nc,");
        var read = FileNodes.ReadCsv(path);

        Assert.Equal(2, read.Count);
        Assert.Equal(new object?[] { "a", "", "b" }, (IList<object?>)read[0]!);
        Assert.Equal(new object?[] { "c", "" }, (IList<object?>)read[1]!);
    }

    [Fact]
    public void Csv_SupportsCustomDelimiter()
    {
        var path = PathFor("semi.csv");
        FileNodes.WriteCsv(path, new List<object?> { new List<object?> { "a;x", 1 } }, ";");
        var read = FileNodes.ReadCsv(path, ";");
        Assert.Equal(new object?[] { "a;x", 1d }, (IList<object?>)read[0]!);
    }

    [Fact]
    public void Csv_ScalarRow_BecomesSingleCellRow()
    {
        var path = PathFor("scalar.csv");
        FileNodes.WriteCsv(path, new List<object?> { "only", 7 });
        var read = FileNodes.ReadCsv(path);
        Assert.Equal(new object?[] { "only" }, (IList<object?>)read[0]!);
        Assert.Equal(new object?[] { 7d }, (IList<object?>)read[1]!);
    }

    [Fact]
    public void Csv_MultiCharacterDelimiter_Throws()
    {
        Assert.Throws<ArgumentException>(() => FileNodes.ReadCsv(PathForExisting("bad.csv"), "::"));
    }

    [Fact]
    public void Csv_Read_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => FileNodes.ReadCsv(PathFor("nope.csv")));
    }

    private string PathForExisting(string fileName)
    {
        var path = PathFor(fileName);
        File.WriteAllText(path, "a,b\n");
        return path;
    }

    // ------------------------------------------------------------------ JSON

    [Fact]
    public void Json_WriteThenRead_RoundTripsDictionariesListsAndValues()
    {
        var path = PathFor("data.json");
        var data = new Dictionary<string, object?>
        {
            ["name"] = "Dyncamelo",
            ["version"] = 0.1,
            ["tags"] = new List<object?> { "bim", "navisworks" },
            ["active"] = true,
            ["nothing"] = null,
        };

        FileNodes.WriteJson(path, data);
        var read = FileNodes.ReadJson(path);

        var dictionary = Assert.IsType<Dictionary<string, object?>>(read);
        Assert.Equal("Dyncamelo", dictionary["name"]);
        Assert.Equal(0.1, dictionary["version"]);
        Assert.True((bool)dictionary["active"]!);
        Assert.Null(dictionary["nothing"]);
        var tags = Assert.IsType<List<object?>>(dictionary["tags"]);
        Assert.Equal(new object?[] { "bim", "navisworks" }, tags);
    }

    [Fact]
    public void Json_IntegersBecomeDoubles()
    {
        var path = PathFor("num.json");
        File.WriteAllText(path, "[1, 2, 3]");
        var read = Assert.IsType<List<object?>>(FileNodes.ReadJson(path));
        Assert.All(read, item => Assert.IsType<double>(item));
    }

    [Fact]
    public void Json_InvalidContent_ThrowsFormatExceptionWithPath()
    {
        var path = PathFor("broken.json");
        File.WriteAllText(path, "{ not json");
        var ex = Assert.Throws<FormatException>(() => FileNodes.ReadJson(path));
        Assert.Contains(path, ex.Message);
    }

    // ---------------------------------------------------------- JSON (string)

    [Fact]
    public void JsonParse_BuildsDictionariesListsAndValues()
    {
        var value = FileNodes.ParseJson("{\"name\": \"Wall\", \"ids\": [1, 2], \"ok\": true, \"gone\": null}");
        var dictionary = Assert.IsType<Dictionary<string, object?>>(value);
        Assert.Equal("Wall", dictionary["name"]);
        Assert.Equal(new object?[] { 1d, 2d }, Assert.IsType<List<object?>>(dictionary["ids"]));
        Assert.True((bool)dictionary["ok"]!);
        Assert.Null(dictionary["gone"]);
    }

    [Fact]
    public void JsonParse_InvalidOrEmptyInput_ThrowsClearErrors()
    {
        Assert.Contains("not valid JSON", Assert.Throws<FormatException>(() => FileNodes.ParseJson("{ nope")).Message);
        Assert.Contains("JSON.Parse", Assert.Throws<ArgumentException>(() => FileNodes.ParseJson(" ")).Message);
    }

    [Fact]
    public void JsonStringify_RoundTripsThroughParse()
    {
        var data = new Dictionary<string, object?>
        {
            ["name"] = "Dyncamelo",
            ["values"] = new List<object?> { 1d, 2.5, null },
        };

        var compact = FileNodes.StringifyJson(data, indented: false);
        Assert.DoesNotContain("\n", compact);

        var roundTripped = Assert.IsType<Dictionary<string, object?>>(FileNodes.ParseJson(compact));
        Assert.Equal("Dyncamelo", roundTripped["name"]);
        Assert.Equal(new object?[] { 1d, 2.5, null }, Assert.IsType<List<object?>>(roundTripped["values"]));
    }

    [Fact]
    public void JsonStringify_IndentedByDefault_AndHandlesNull()
    {
        Assert.Contains("\n", FileNodes.StringifyJson(new Dictionary<string, object?> { ["a"] = 1 }));
        Assert.Equal("null", FileNodes.StringifyJson(null));
    }

    // ----------------------------------------------------- File system helpers

    [Fact]
    public void FileExists_DistinguishesFilesFoldersAndMissingPaths()
    {
        var path = PathFor("present.txt");
        File.WriteAllText(path, "x");

        Assert.True(FileNodes.FileExists(path));
        Assert.False(FileNodes.FileExists(PathFor("absent.txt")));
        Assert.False(FileNodes.FileExists(_directory)); // a folder is not a file
    }

    [Fact]
    public void FileExists_EmptyPath_ThrowsHelpfulMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => FileNodes.FileExists(" "));
        Assert.Contains("File Path", ex.Message);
    }

    [Fact]
    public void GetFiles_ListsFilesSorted_IncludingExtensionless()
    {
        File.WriteAllText(PathFor("b.txt"), "");
        File.WriteAllText(PathFor("a.txt"), "");
        File.WriteAllText(PathFor("README"), "");
        Directory.CreateDirectory(PathFor("subdir"));

        var files = FileNodes.GetFiles(_directory);

        Assert.Equal(
            new[] { PathFor("README"), PathFor("a.txt"), PathFor("b.txt") },
            files);
    }

    [Fact]
    public void GetFiles_AppliesWildcardPattern()
    {
        File.WriteAllText(PathFor("model.nwd"), "");
        File.WriteAllText(PathFor("notes.txt"), "");

        var files = FileNodes.GetFiles(_directory, "*.nwd");

        Assert.Equal(new[] { PathFor("model.nwd") }, files);
    }

    [Fact]
    public void GetFiles_MissingFolder_ThrowsWithPathInMessage()
    {
        var missing = PathFor("no-such-dir");
        var ex = Assert.Throws<DirectoryNotFoundException>(() => FileNodes.GetFiles(missing));
        Assert.Contains(missing, ex.Message);
    }

    [Fact]
    public void CombinePath_JoinsSegments()
    {
        Assert.Equal(Path.Combine("folder", "file.txt"), FileNodes.CombinePath("folder", "file.txt"));
        Assert.Equal("file.txt", FileNodes.CombinePath("", "file.txt"));
    }

    [Fact]
    public void CombinePath_NullInputs_ThrowHelpfulMessages()
    {
        Assert.Contains("directory", Assert.Throws<ArgumentNullException>(() => FileNodes.CombinePath(null!, "f")).Message);
        Assert.Contains("fileName", Assert.Throws<ArgumentNullException>(() => FileNodes.CombinePath("d", null!)).Message);
    }
}
