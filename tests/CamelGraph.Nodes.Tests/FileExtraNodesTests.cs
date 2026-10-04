using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Nodes;
using Xunit;
using GraphFunction = CamelGraph.Core.Graph.NodeFunction;

namespace CamelGraph.Nodes.Tests;

/// <summary>File.*, Directory.*, appending writers, Log.Write and Zip.* nodes.</summary>
public class FileExtraNodesTests : IDisposable
{
    private readonly string _directory;

    public FileExtraNodesTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "camelgraph-" + Guid.NewGuid().ToString("N"));
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
        catch (UnauthorizedAccessException)
        {
            // best-effort cleanup
        }
    }

    private string PathFor(params string[] parts) => Path.Combine(new[] { _directory }.Concat(parts).ToArray());

    private string Write(string name, string content = "x")
    {
        var path = PathFor(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static List<object?> Items(params object?[] items) => new List<object?>(items);

    // -------------------------------------------------------------- File.Info

    [Fact]
    public void FileInfo_ExistingFile_ReportsItsFacts()
    {
        var path = Write("report.final.txt", "hello");
        var modified = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Local);
        File.SetLastWriteTime(path, modified);

        var info = FileExtraNodes.GetFileInfo(path);

        Assert.Equal(true, info["exists"]);
        Assert.Equal("report.final.txt", info["name"]);
        Assert.Equal(".txt", info["extension"]);
        Assert.Equal(_directory, info["directory"]);
        Assert.Equal(5d, info["sizeBytes"]);
        Assert.Equal(modified, Assert.IsType<DateTime>(info["modified"]));
        Assert.IsType<DateTime>(info["created"]);
    }

    [Fact]
    public void FileInfo_ExtensionlessFile_HasEmptyExtension()
    {
        var info = FileExtraNodes.GetFileInfo(Write("README"));
        Assert.Equal(string.Empty, info["extension"]);
        Assert.Equal("README", info["name"]);
    }

    [Fact]
    public void FileInfo_MissingFile_IsNotAnError_ButAllNulls()
    {
        var info = FileExtraNodes.GetFileInfo(PathFor("ghost.txt"));

        Assert.Equal(false, info["exists"]);
        foreach (var key in new[] { "name", "extension", "directory", "sizeBytes", "modified", "created" })
        {
            Assert.True(info.ContainsKey(key), key);
            Assert.Null(info[key]);
        }
    }

    [Fact]
    public void FileInfo_AFolder_IsNotAFile()
    {
        Assert.Equal(false, FileExtraNodes.GetFileInfo(_directory)["exists"]);
    }

    [Fact]
    public void FileInfo_BlankPath_ThrowsNamingTheNode()
    {
        var ex = Assert.Throws<ArgumentException>(() => FileExtraNodes.GetFileInfo(" "));
        Assert.Contains("File.Info", ex.Message);
    }

    // -------------------------------------------------------------- File.Hash

    [Theory]
    [InlineData("SHA256", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("SHA1", "a9993e364706816aba3e25717850c26c9cd0d89d")]
    [InlineData("MD5", "900150983cd24fb0d6963f7d28e17f72")]
    public void FileHash_MatchesTheKnownDigestOfAbc(string algorithm, string expected)
    {
        var path = PathFor("abc.bin");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("abc"));
        Assert.Equal(expected, FileExtraNodes.GetFileHash(path, algorithm));
    }

    [Theory]
    [InlineData("sha256")]
    [InlineData("SHA-256")]
    [InlineData(" Sha256 ")]
    public void FileHash_AcceptsAnyCase_AndTheDashedName(string algorithm)
    {
        var path = PathFor("abc.bin");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("abc"));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", FileExtraNodes.GetFileHash(path, algorithm));
    }

    [Fact]
    public void FileHash_DefaultsToSha256_AndIsLowerCaseHex()
    {
        var path = Write("empty.txt", string.Empty);
        var hash = FileExtraNodes.GetFileHash(path);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void FileHash_ChangesWhenTheContentChanges()
    {
        var path = Write("data.txt", "one");
        var before = FileExtraNodes.GetFileHash(path);
        File.WriteAllText(path, "two");
        Assert.NotEqual(before, FileExtraNodes.GetFileHash(path));
        File.WriteAllText(path, "one");
        Assert.Equal(before, FileExtraNodes.GetFileHash(path));
    }

    [Fact]
    public void FileHash_UnknownAlgorithm_ListsTheChoices()
    {
        var path = Write("data.txt");
        var ex = Assert.Throws<ArgumentException>(() => FileExtraNodes.GetFileHash(path, "CRC32"));
        Assert.Contains("SHA256", ex.Message);
        Assert.Contains("CRC32", ex.Message);
    }

    [Fact]
    public void FileHash_MissingFile_ThrowsWithThePath()
    {
        var missing = PathFor("ghost.txt");
        var ex = Assert.Throws<FileNotFoundException>(() => FileExtraNodes.GetFileHash(missing));
        Assert.Contains(missing, ex.Message);
        Assert.Contains("File.Hash", ex.Message);
    }

    // -------------------------------------------------------------- File.Copy

    [Fact]
    public void Copy_CopiesTheFile_CreatesTheFolder_AndReturnsTheDestination()
    {
        var source = Write("a.txt", "payload");
        var destination = PathFor("backup", "2026", "a-copy.txt");

        var result = FileExtraNodes.CopyFile(source, destination);

        Assert.Equal(destination, result);
        Assert.Equal("payload", File.ReadAllText(destination));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Copy_ExistingDestination_NeedsOverwrite()
    {
        var source = Write("a.txt", "new");
        var destination = Write("b.txt", "old");

        var ex = Assert.Throws<IOException>(() => FileExtraNodes.CopyFile(source, destination));
        Assert.Contains("overwrite", ex.Message);
        Assert.Contains(destination, ex.Message);
        Assert.Equal("old", File.ReadAllText(destination));

        FileExtraNodes.CopyFile(source, destination, overwrite: true);
        Assert.Equal("new", File.ReadAllText(destination));
    }

    [Fact]
    public void Copy_MissingSource_ThrowsWithThePath()
    {
        var missing = PathFor("ghost.txt");
        var ex = Assert.Throws<FileNotFoundException>(() => FileExtraNodes.CopyFile(missing, PathFor("x.txt")));
        Assert.Contains(missing, ex.Message);
        Assert.False(File.Exists(PathFor("x.txt")));
    }

    [Fact]
    public void Copy_BlankInputs_NameTheInput()
    {
        var source = Write("a.txt");
        Assert.Contains("'source'", Assert.Throws<ArgumentException>(() => FileExtraNodes.CopyFile(" ", PathFor("x"))).Message);
        Assert.Contains("'destination'", Assert.Throws<ArgumentException>(() => FileExtraNodes.CopyFile(source, null!)).Message);
    }

    [Fact]
    public void Copy_DestinationThatIsAFolder_ExplainsToGiveAFileName()
    {
        var source = Write("a.txt");
        var folder = PathFor("existing-folder");
        Directory.CreateDirectory(folder);

        var ex = Assert.Throws<ArgumentException>(() => FileExtraNodes.CopyFile(source, folder));
        Assert.Contains("folder", ex.Message);
    }

    [Fact]
    public void Copy_ToItself_IsANoOpThatKeepsTheFile()
    {
        var source = Write("a.txt", "keep");
        Assert.Equal(source, FileExtraNodes.CopyFile(source, source, overwrite: true));
        Assert.Equal("keep", File.ReadAllText(source));
    }

    // -------------------------------------------------------------- File.Move

    [Fact]
    public void Move_MovesTheFile_CreatesTheFolder_AndReturnsTheDestination()
    {
        var source = Write("a.txt", "payload");
        var destination = PathFor("archive", "a.txt");

        Assert.Equal(destination, FileExtraNodes.MoveFile(source, destination));

        Assert.False(File.Exists(source));
        Assert.Equal("payload", File.ReadAllText(destination));
    }

    [Fact]
    public void Move_Renames()
    {
        var source = Write("old.txt", "x");
        var destination = PathFor("new.txt");
        FileExtraNodes.MoveFile(source, destination);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(destination));
    }

    [Fact]
    public void Move_ExistingDestination_NeedsOverwrite_AndKeepsBothFilesOtherwise()
    {
        var source = Write("a.txt", "new");
        var destination = Write("b.txt", "old");

        var ex = Assert.Throws<IOException>(() => FileExtraNodes.MoveFile(source, destination));
        Assert.Contains("overwrite", ex.Message);
        Assert.True(File.Exists(source));
        Assert.Equal("old", File.ReadAllText(destination));

        FileExtraNodes.MoveFile(source, destination, overwrite: true);
        Assert.False(File.Exists(source));
        Assert.Equal("new", File.ReadAllText(destination));
    }

    [Fact]
    public void Move_MissingSource_Throws_AndToItselfIsANoOp()
    {
        Assert.Throws<FileNotFoundException>(() => FileExtraNodes.MoveFile(PathFor("ghost.txt"), PathFor("x.txt")));

        var source = Write("a.txt", "keep");
        Assert.Equal(source, FileExtraNodes.MoveFile(source, source, overwrite: true));
        Assert.Equal("keep", File.ReadAllText(source));
    }

    // ------------------------------------------------------------ File.Delete

    [Fact]
    public void Delete_ReturnsTrueOnce_ThenFalse()
    {
        var path = Write("a.txt");
        Assert.True(FileExtraNodes.DeleteFile(path));
        Assert.False(File.Exists(path));
        Assert.False(FileExtraNodes.DeleteFile(path));
    }

    [Fact]
    public void Delete_AFolderPath_IsNotAFile_SoNothingHappens()
    {
        Assert.False(FileExtraNodes.DeleteFile(_directory));
        Assert.True(Directory.Exists(_directory));
    }

    [Fact]
    public void Delete_BlankPath_ThrowsNamingTheNode()
    {
        Assert.Contains("File.Delete", Assert.Throws<ArgumentException>(() => FileExtraNodes.DeleteFile("")).Message);
    }

    // ------------------------------------------------------------- Directory

    [Fact]
    public void DirectoryExists_DistinguishesFoldersFilesAndMissingPaths()
    {
        var file = Write("a.txt");
        Assert.True(FileExtraNodes.DirectoryExists(_directory));
        Assert.False(FileExtraNodes.DirectoryExists(file));
        Assert.False(FileExtraNodes.DirectoryExists(PathFor("nope")));

        // Wave D (SYS-32): a blank path answers false instead of failing.
        Assert.False(FileExtraNodes.DirectoryExists(" "));
        Assert.False(FileExtraNodes.DirectoryExists(null!));
    }

    [Fact]
    public void DirectoryCreate_MakesNestedFolders_IsIdempotent_AndReturnsThePath()
    {
        var nested = PathFor("a", "b", "c");
        Assert.Equal(nested, FileExtraNodes.CreateDirectory(nested));
        Assert.True(Directory.Exists(nested));
        Assert.Equal(nested, FileExtraNodes.CreateDirectory(nested));
    }

    [Fact]
    public void GetDirectories_ListsSubFoldersSorted_NotFiles()
    {
        Directory.CreateDirectory(PathFor("beta"));
        Directory.CreateDirectory(PathFor("alpha", "inner"));
        Write("file.txt");

        Assert.Equal(new[] { PathFor("alpha"), PathFor("beta") }, FileExtraNodes.GetDirectories(_directory));
    }

    [Fact]
    public void GetDirectories_Recursive_AndPattern()
    {
        Directory.CreateDirectory(PathFor("2026-01", "raw"));
        Directory.CreateDirectory(PathFor("2026-02"));
        Directory.CreateDirectory(PathFor("archive", "2026-03"));

        var all = FileExtraNodes.GetDirectories(_directory, "*", recursive: true);
        Assert.Equal(
            new[] { PathFor("2026-01"), PathFor("2026-01", "raw"), PathFor("2026-02"), PathFor("archive"), PathFor("archive", "2026-03") }
                .OrderBy(p => p, StringComparer.Ordinal),
            all);

        Assert.Equal(new[] { PathFor("2026-01"), PathFor("2026-02") }, FileExtraNodes.GetDirectories(_directory, "2026*"));
        Assert.Equal(
            new[] { PathFor("2026-01"), PathFor("2026-02"), PathFor("archive", "2026-03") }.OrderBy(p => p, StringComparer.Ordinal),
            FileExtraNodes.GetDirectories(_directory, "2026*", recursive: true));
    }

    [Fact]
    public void GetDirectories_MissingFolder_Throws()
    {
        var missing = PathFor("nope");
        var ex = Assert.Throws<DirectoryNotFoundException>(() => FileExtraNodes.GetDirectories(missing));
        Assert.Contains(missing, ex.Message);
    }

    // -------------------------------------------------------- Directory.FindFiles

    [Fact]
    public void FindFiles_IsRecursiveByDefault_AndReturnsSortedFullPaths()
    {
        Write("b.nwd");
        Write("a.nwd");
        Write(Path.Combine("sub", "c.nwd"));

        var found = FileExtraNodes.FindFiles(_directory);

        Assert.Equal(new[] { PathFor("a.nwd"), PathFor("b.nwd"), PathFor("sub", "c.nwd") }, found);
        Assert.All(found, f => Assert.True(Path.IsPathRooted(f)));
    }

    [Fact]
    public void FindFiles_NotRecursive_StaysInTheFolder()
    {
        Write("a.nwd");
        Write(Path.Combine("sub", "c.nwd"));
        Assert.Equal(new[] { PathFor("a.nwd") }, FileExtraNodes.FindFiles(_directory, "*", recursive: false));
    }

    [Fact]
    public void FindFiles_SeveralPatterns_AreCombined_AndNothingIsListedTwice()
    {
        Write("model.nwd");
        Write("model.nwf");
        Write("notes.txt");
        Write(Path.Combine("sub", "other.nwd"));

        var found = FileExtraNodes.FindFiles(_directory, " *.nwd ; *.nwf;*.nw*;; ");

        Assert.Equal(
            new[] { PathFor("model.nwd"), PathFor("model.nwf"), PathFor("sub", "other.nwd") },
            found);
    }

    [Fact]
    public void FindFiles_ThreeLetterExtensionPatternDoesNotMatchLongerExtensions()
    {
        // .NET Framework's Directory.GetFiles("*.nwd") also returns "x.nwdx" (8.3 matching); this node must not.
        Write("a.nwd");
        Write("b.nwdx");
        Write("c.xnwd");
        Assert.Equal(new[] { PathFor("a.nwd") }, FileExtraNodes.FindFiles(_directory, "*.nwd"));
    }

    [Fact]
    public void FindFiles_WildcardsStarAndQuestionMark()
    {
        Write("file1.txt");
        Write("fileA.txt");
        Write("file10.txt");
        Write("file.txt");
        Write("xxaxxbxx");
        Write("xxbxxaxx");

        Assert.Equal(new[] { PathFor("file1.txt"), PathFor("fileA.txt") }, FileExtraNodes.FindFiles(_directory, "file?.txt"));
        Assert.Equal(new[] { PathFor("xxaxxbxx") }, FileExtraNodes.FindFiles(_directory, "*a*b*"));
        Assert.Equal(
            new[] { PathFor("file.txt"), PathFor("file1.txt"), PathFor("file10.txt"), PathFor("fileA.txt") },
            FileExtraNodes.FindFiles(_directory, "file*.txt"));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("*.*")]
    [InlineData("")]
    [InlineData("  ")]
    public void FindFiles_AllFilesPatterns_IncludeExtensionlessFiles(string pattern)
    {
        Write("README");
        Write("a.txt");

        // Names sort ignoring case (SYS-16), so README follows a.txt as in Explorer.
        Assert.Equal(new[] { PathFor("a.txt"), PathFor("README") }, FileExtraNodes.FindFiles(_directory, pattern));
    }

    [Fact]
    public void FindFiles_PatternCaseFollowsTheFileSystem()
    {
        Write("Upper.NWD");
        var found = FileExtraNodes.FindFiles(_directory, "*.nwd");
        if (PathNodes.IsWindows)
        {
            Assert.Equal(new[] { PathFor("Upper.NWD") }, found); // Windows paths ignore case
        }
        else
        {
            Assert.Empty(found); // elsewhere "*.nwd" does not match ".NWD"
            Assert.Equal(new[] { PathFor("Upper.NWD") }, FileExtraNodes.FindFiles(_directory, "*.NWD"));
        }
    }

    [Fact]
    public void FindFiles_SortByModified_NewestFirstWithLimitOne()
    {
        var oldest = Write("f1.txt");
        var newest = Write("f2.txt");
        var middle = Write("f3.txt");
        File.SetLastWriteTimeUtc(oldest, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newest, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(middle, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new[] { oldest, middle, newest }, FileExtraNodes.FindFiles(_directory, "*", true, "modified"));
        Assert.Equal(new[] { newest, middle, oldest }, FileExtraNodes.FindFiles(_directory, "*", true, "MODIFIED", descending: true));
        Assert.Equal(new[] { newest }, FileExtraNodes.FindFiles(_directory, "*", true, "modified", descending: true, limit: 1));
        Assert.Equal(new[] { newest, middle }, FileExtraNodes.FindFiles(_directory, "*", true, "modified", descending: true, limit: 2));
    }

    [Fact]
    public void FindFiles_SortBySize_WithPathAsTheTieBreak()
    {
        Write("big.txt", "123456");
        Write("small.txt", "1");
        Write("same-a.txt", "123");
        Write("same-b.txt", "123");

        Assert.Equal(
            new[] { PathFor("small.txt"), PathFor("same-a.txt"), PathFor("same-b.txt"), PathFor("big.txt") },
            FileExtraNodes.FindFiles(_directory, "*", true, "size"));
        Assert.Equal(
            new[] { PathFor("big.txt"), PathFor("same-b.txt"), PathFor("same-a.txt"), PathFor("small.txt") },
            FileExtraNodes.FindFiles(_directory, "*", true, "size", descending: true));
    }

    [Fact]
    public void FindFiles_SortByName_Descending()
    {
        Write("a.txt");
        Write("b.txt");
        Assert.Equal(new[] { PathFor("b.txt"), PathFor("a.txt") }, FileExtraNodes.FindFiles(_directory, "*", true, "name", descending: true));
    }

    [Fact]
    public void FindFiles_SortByCreated_Works()
    {
        var first = Write("first.txt");
        var second = Write("second.txt");
        if (PathNodes.IsWindows)
        {
            // Creation time can be set on Windows; on Linux most file systems have no settable creation time.
            File.SetCreationTimeUtc(first, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetCreationTimeUtc(second, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
            Assert.Equal(new[] { second }, FileExtraNodes.FindFiles(_directory, "*", true, "created", descending: true, limit: 1));
        }
        else
        {
            Assert.Equal(2, FileExtraNodes.FindFiles(_directory, "*", true, "created").Count);
        }
    }

    [Fact]
    public void FindFiles_LimitLargerThanTheResult_ReturnsEverything()
    {
        Write("a.txt");
        Assert.Single(FileExtraNodes.FindFiles(_directory, "*", true, "name", false, 50));
    }

    [Fact]
    public void FindFiles_NoMatch_IsAnEmptyList()
    {
        Write("a.txt");
        Assert.Empty(FileExtraNodes.FindFiles(_directory, "*.nwd"));
    }

    [Fact]
    public void FindFiles_Errors_NameTheProblem()
    {
        var missing = PathFor("nope");
        Assert.Contains(missing, Assert.Throws<DirectoryNotFoundException>(() => FileExtraNodes.FindFiles(missing)).Message);
        Assert.Contains("Directory.FindFiles", Assert.Throws<ArgumentException>(() => FileExtraNodes.FindFiles(" ")).Message);

        var sort = Assert.Throws<ArgumentException>(() => FileExtraNodes.FindFiles(_directory, "*", true, "colour"));
        Assert.Contains("colour", sort.Message);
        Assert.Contains("modified", sort.Message);

        var limit = Assert.Throws<ArgumentOutOfRangeException>(() => FileExtraNodes.FindFiles(_directory, "*", true, "name", false, -1));
        Assert.Contains("limit", limit.Message);
    }

    // -------------------------------------------------------- Directory.Delete

    [Fact]
    public void DirectoryDelete_EmptyFolder_IsDeleted()
    {
        var folder = PathFor("empty");
        Directory.CreateDirectory(folder);
        Assert.True(FileExtraNodes.DeleteDirectory(folder));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void DirectoryDelete_NonEmptyFolder_NeedsRecursive_AndIsLeftUntouchedOtherwise()
    {
        var folder = PathFor("full");
        Write(Path.Combine("full", "sub", "a.txt"));

        var ex = Assert.Throws<IOException>(() => FileExtraNodes.DeleteDirectory(folder));
        Assert.Contains("recursive", ex.Message);
        Assert.True(File.Exists(Path.Combine(folder, "sub", "a.txt")));

        Assert.True(FileExtraNodes.DeleteDirectory(folder, recursive: true));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void DirectoryDelete_MissingFolder_IsFalse_AndAFileIsNotAFolder()
    {
        Assert.False(FileExtraNodes.DeleteDirectory(PathFor("ghost")));
        var file = Write("a.txt");
        Assert.False(FileExtraNodes.DeleteDirectory(file, recursive: true));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void DirectoryDelete_RefusesADriveRoot()
    {
        // recursive: false on purpose - if the guard ever broke, this test must not be able to delete anything.
        var root = Path.GetPathRoot(_directory)!;
        var ex = Assert.Throws<InvalidOperationException>(() => FileExtraNodes.DeleteDirectory(root, recursive: false));
        Assert.Contains("root", ex.Message);
    }

    [Fact]
    public void DirectoryDelete_BlankPath_ThrowsNamingTheNode()
    {
        Assert.Contains("Directory.Delete", Assert.Throws<ArgumentException>(() => FileExtraNodes.DeleteDirectory(null!)).Message);
    }

    // ------------------------------------------------------------ Text.AppendToFile

    [Fact]
    public void AppendText_CreatesFileAndFolder_AppendsLines_AndReturnsThePath()
    {
        var path = PathFor("logs", "notes.txt");

        Assert.Equal(path, FileExtraNodes.AppendText(path, "first"));
        FileExtraNodes.AppendText(path, "second");

        Assert.Equal("first" + Environment.NewLine + "second" + Environment.NewLine, File.ReadAllText(path));
    }

    [Fact]
    public void AppendText_NewLineFalse_ConcatenatesOnTheSameLine()
    {
        var path = PathFor("notes.txt");
        FileExtraNodes.AppendText(path, "a", newLine: false);
        FileExtraNodes.AppendText(path, "b", newLine: false);
        Assert.Equal("ab", File.ReadAllText(path));
    }

    [Fact]
    public void AppendText_AddsToExistingContent_AndWritesUtf8WithoutByteOrderMark()
    {
        var path = Write("notes.txt", "kept\n");
        FileExtraNodes.AppendText(path, "Größe ✓");

        Assert.Equal("kept\nGröße ✓" + Environment.NewLine, File.ReadAllText(path));
        var bytes = File.ReadAllBytes(path);
        Assert.Equal((byte)'k', bytes[0]);

        var fresh = PathFor("fresh.txt");
        FileExtraNodes.AppendText(fresh, "ä");
        var freshBytes = File.ReadAllBytes(fresh);
        Assert.False(freshBytes.Length >= 3 && freshBytes[0] == 0xEF && freshBytes[1] == 0xBB && freshBytes[2] == 0xBF, "new files must not get a BOM");
    }

    [Fact]
    public void AppendText_NullText_IsAnError_AndLeavesTheFileAlone()
    {
        // Wave D (SYS-18): null used to append a bare line break; a missing value must not look like an intended blank line.
        var path = PathFor("notes.txt");
        File.WriteAllText(path, "kept");
        var ex = Assert.Throws<ArgumentNullException>(() => FileExtraNodes.AppendText(path, null!));
        Assert.Contains("Text.AppendToFile", ex.Message);
        Assert.Contains("'text'", ex.Message);
        Assert.Equal("kept", File.ReadAllText(path));

        // Empty text is still a deliberate blank line.
        FileExtraNodes.AppendText(path, string.Empty);
        Assert.Equal("kept" + Environment.NewLine, File.ReadAllText(path));
    }

    [Fact]
    public void AppendText_BlankPath_ThrowsNamingTheNode()
    {
        Assert.Contains("Text.AppendToFile", Assert.Throws<ArgumentException>(() => FileExtraNodes.AppendText(" ", "x")).Message);
    }

    // ------------------------------------------------------------ CSV.AppendToFile

    private static List<object?> SampleRows() => Items(
        Items(1.5, "plain", "with, comma"),
        Items(-2, "with \"quotes\"", "multi\nline"),
        "scalar",
        null);

    [Fact]
    public void AppendCsv_OnANewFile_IsByteIdenticalToCsvWriteToFile()
    {
        var written = PathFor("written.csv");
        var appended = PathFor("appended.csv");
        FileNodes.WriteCsv(written, SampleRows());

        Assert.Equal(appended, FileExtraNodes.AppendCsv(appended, SampleRows()));

        Assert.Equal(File.ReadAllBytes(written), File.ReadAllBytes(appended));
    }

    [Fact]
    public void AppendCsv_WithHeaders_WritesThemOnlyOnce()
    {
        var path = PathFor("report.csv");
        var headers = Items("name", "area, m2");

        FileExtraNodes.AppendCsv(path, Items(Items("wall", 12.5)), ",", headers);
        FileExtraNodes.AppendCsv(path, Items(Items("slab", 30)), ",", headers);

        Assert.Equal("name,\"area, m2\"\nwall,12.5\nslab,30\n", File.ReadAllText(path));
    }

    [Fact]
    public void AppendCsv_HeadersAreAlsoWrittenWhenTheExistingFileIsEmpty()
    {
        var path = Write("empty.csv", string.Empty);
        FileExtraNodes.AppendCsv(path, Items(Items(1, 2)), ",", Items("a", "b"));
        Assert.Equal("a,b\n1,2\n", File.ReadAllText(path));
    }

    [Fact]
    public void AppendCsv_HeadersAreNotWrittenAgain_WhenTheFileHasContent()
    {
        var path = Write("report.csv", "a,b\n1,2\n");
        FileExtraNodes.AppendCsv(path, Items(Items(3, 4)), ",", Items("a", "b"));
        Assert.Equal("a,b\n1,2\n3,4\n", File.ReadAllText(path));
    }

    [Fact]
    public void AppendCsv_StartsANewLine_WhenTheExistingFileDoesNotEndWithOne()
    {
        var path = Write("report.csv", "a,b\n1,2");
        FileExtraNodes.AppendCsv(path, Items(Items(3, 4)));
        Assert.Equal("a,b\n1,2\n3,4\n", File.ReadAllText(path));
    }

    [Fact]
    public void AppendCsv_NullOrEmptyHeaders_AddNothing()
    {
        var a = PathFor("a.csv");
        var b = PathFor("b.csv");
        FileExtraNodes.AppendCsv(a, Items(Items(1)), ",", null);
        FileExtraNodes.AppendCsv(b, Items(Items(1)), ",", new List<object?>());
        Assert.Equal("1\n", File.ReadAllText(a));
        Assert.Equal("1\n", File.ReadAllText(b));
    }

    [Fact]
    public void AppendCsv_NoRows_StillCreatesTheFile_WithJustTheHeaders()
    {
        var path = PathFor("sub", "report.csv");
        FileExtraNodes.AppendCsv(path, new List<object?>(), ",", Items("a", "b"));
        Assert.Equal("a,b\n", File.ReadAllText(path));
    }

    [Fact]
    public void AppendCsv_CustomDelimiter_QuotesCellsThatContainIt()
    {
        var path = PathFor("semi.csv");
        FileExtraNodes.AppendCsv(path, Items(Items("a;x", 1)), ";");
        Assert.Equal("\"a;x\";1\n", File.ReadAllText(path));
        Assert.Equal(new object?[] { "a;x", 1d }, (IList<object?>)FileNodes.ReadCsv(path, ";")[0]!);
    }

    [Fact]
    public void AppendCsv_RoundTripsThroughCsvReadFromFile()
    {
        var path = PathFor("round.csv");
        FileExtraNodes.AppendCsv(path, Items(Items(1.5, "x, y")));
        FileExtraNodes.AppendCsv(path, Items(Items(-2, "multi\nline")));

        var read = FileNodes.ReadCsv(path);
        Assert.Equal(new object?[] { 1.5, "x, y" }, (IList<object?>)read[0]!);
        Assert.Equal(new object?[] { -2d, "multi\nline" }, (IList<object?>)read[1]!);
    }

    [Fact]
    public void AppendCsv_NumbersAreInvariant_WhateverTheCurrentCulture()
    {
        var path = PathFor("culture.csv");
        var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = comma;
            FileExtraNodes.AppendCsv(path, Items(Items(1.5, 1234567.25)), ";");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.Equal("1.5;1234567.25\n", File.ReadAllText(path));
    }

    [Fact]
    public void AppendCsv_Errors_NameTheProblem_AndCreateNothing()
    {
        var path = PathFor("never", "x.csv");
        Assert.Contains("rows", Assert.Throws<ArgumentNullException>(() => FileExtraNodes.AppendCsv(path, null!)).Message);
        Assert.Contains("delimiter", Assert.Throws<ArgumentException>(() => FileExtraNodes.AppendCsv(path, Items(), "::")).Message);
        Assert.Contains("CSV.AppendToFile", Assert.Throws<ArgumentException>(() => FileExtraNodes.AppendCsv(" ", Items())).Message);
        Assert.False(Directory.Exists(PathFor("never")));
    }

    // ----------------------------------------------------------------- Log.Write

    [Fact]
    public void FormatLogLine_UsesTheDocumentedLayout()
    {
        var moment = new DateTime(2026, 10, 1, 10, 20, 35);
        Assert.Equal("2026-10-01 10:20:35  INFO  message", FileExtraNodes.FormatLogLine(moment, "INFO", "message"));
        Assert.Equal("2026-10-01 10:20:35  ERROR  boom", FileExtraNodes.FormatLogLine(moment, "error", "boom"));
    }

    [Fact]
    public void FormatLogLine_IsInvariant_WhateverTheCurrentCulture()
    {
        var odd = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        odd.DateTimeFormat.TimeSeparator = ".";
        odd.DateTimeFormat.DateSeparator = "/";
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = odd;
            Assert.Equal("2026-10-01 09:05:03  WARN  x", FileExtraNodes.FormatLogLine(new DateTime(2026, 10, 1, 9, 5, 3), "WARN", "x"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("info", "INFO")]
    [InlineData("Warn", "WARN")]
    [InlineData("WARNING", "WARN")]
    [InlineData(" error ", "ERROR")]
    [InlineData("debug", "DEBUG")]
    public void FormatLogLine_AcceptsAnyCaseOfTheLevel(string level, string expected)
    {
        Assert.Contains("  " + expected + "  m", FileExtraNodes.FormatLogLine(DateTime.Now, level, "m"));
    }

    [Theory]
    [InlineData("a\nb", "a | b")]
    [InlineData("a\r\nb", "a | b")]
    [InlineData("a\rb", "a | b")]
    [InlineData("a\n\nb", "a |  | b")]
    public void FormatLogLine_KeepsOneEntryOnOneLine(string message, string expected)
    {
        Assert.EndsWith("  INFO  " + expected, FileExtraNodes.FormatLogLine(DateTime.Now, "INFO", message));
    }

    [Fact]
    public void WriteLog_AppendsTimestampedLines_AndReturnsTheLine()
    {
        var path = PathFor("logs", "run.log");

        var first = FileExtraNodes.WriteLog(path, "started");
        var second = FileExtraNodes.WriteLog(path, "disk almost full", "warn");
        FileExtraNodes.WriteLog(path, "failed\nbadly", "ERROR");

        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}  INFO  started$"), first);
        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}  WARN  disk almost full$"), second);

        var lines = File.ReadAllLines(path);
        Assert.Equal(3, lines.Length);
        Assert.Equal(first, lines[0]);
        Assert.Equal(second, lines[1]);
        Assert.EndsWith("  ERROR  failed | badly", lines[2]);
    }

    [Fact]
    public void WriteLog_TimestampIsLocalTimeAtTheMomentOfTheCall()
    {
        var path = PathFor("run.log");
        var before = DateTime.Now.AddSeconds(-1);
        var line = FileExtraNodes.WriteLog(path, "now");
        var after = DateTime.Now.AddSeconds(1);

        var stamp = DateTime.ParseExact(line.Substring(0, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        Assert.InRange(stamp, before, after);
    }

    [Fact]
    public void WriteLog_StartsANewLine_WhenTheFileDoesNotEndWithOne()
    {
        var path = Write("run.log", "old entry");
        FileExtraNodes.WriteLog(path, "new entry");

        var lines = File.ReadAllLines(path);
        Assert.Equal("old entry", lines[0]);
        Assert.EndsWith("  INFO  new entry", lines[1]);
    }

    [Fact]
    public void WriteLog_EmptyMessage_IsAllowed()
    {
        var line = FileExtraNodes.WriteLog(PathFor("run.log"), string.Empty);
        Assert.EndsWith("  INFO  ", line);
    }

    [Fact]
    public void WriteLog_Errors_NameTheProblem_AndWriteNothing()
    {
        var path = PathFor("never", "run.log");
        Assert.Contains("message", Assert.Throws<ArgumentNullException>(() => FileExtraNodes.WriteLog(path, null!)).Message);

        var level = Assert.Throws<ArgumentException>(() => FileExtraNodes.WriteLog(path, "x", "FATAL"));
        Assert.Contains("FATAL", level.Message);
        Assert.Contains("INFO", level.Message);

        Assert.Contains("Log.Write", Assert.Throws<ArgumentException>(() => FileExtraNodes.WriteLog(" ", "x")).Message);
        Assert.False(Directory.Exists(PathFor("never")));
    }

    // -------------------------------------------------------------------- Zip

    private string MakeSampleTree()
    {
        var docs = PathFor("docs");
        Write(Path.Combine("docs", "a.txt"), "alpha");
        Write(Path.Combine("docs", "sub", "b.txt"), "beta");
        Directory.CreateDirectory(Path.Combine(docs, "empty"));
        return docs;
    }

    [Fact]
    public void ZipCreate_PacksFilesAndFolders_WithFolderStructureUnderItsOwnName()
    {
        var docs = MakeSampleTree();
        var loose = Write("loose.txt", "gamma");
        var zip = PathFor("out", "package.zip");

        var result = FileExtraNodes.CreateZip(Items(docs, loose), zip);

        Assert.Equal(zip, result);
        Assert.Equal(
            new[] { "docs/", "docs/a.txt", "docs/empty/", "docs/sub/", "docs/sub/b.txt", "loose.txt" },
            FileExtraNodes.ListZip(zip).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void ZipCreate_ThenExtract_RoundTripsContentStructureAndEmptyFolders()
    {
        var docs = MakeSampleTree();
        File.SetLastWriteTime(Path.Combine(docs, "a.txt"), new DateTime(2026, 5, 6, 7, 8, 10, DateTimeKind.Local));
        var zip = PathFor("package.zip");
        var target = PathFor("unpacked");

        FileExtraNodes.CreateZip(Items(docs), zip);
        Assert.Equal(target, FileExtraNodes.ExtractZip(zip, target));

        Assert.Equal("alpha", File.ReadAllText(Path.Combine(target, "docs", "a.txt")));
        Assert.Equal("beta", File.ReadAllText(Path.Combine(target, "docs", "sub", "b.txt")));
        Assert.True(Directory.Exists(Path.Combine(target, "docs", "empty")));
        var restored = File.GetLastWriteTime(Path.Combine(target, "docs", "a.txt"));
        Assert.True(Math.Abs((restored - new DateTime(2026, 5, 6, 7, 8, 10)).TotalSeconds) <= 3, "last write time should survive the round trip, was " + restored);
    }

    [Fact]
    public void ZipCreate_UnicodeNames_RoundTrip()
    {
        var file = Write("Übersicht Ø 日本.txt", "ünï");
        var zip = PathFor("u.zip");
        var target = PathFor("u");

        FileExtraNodes.CreateZip(Items(file), zip);
        FileExtraNodes.ExtractZip(zip, target);

        Assert.Equal("ünï", File.ReadAllText(Path.Combine(target, "Übersicht Ø 日本.txt")));
    }

    [Fact]
    public void ZipCreate_ExistingZip_NeedsOverwrite_AndLeavesNoTempFileBehind()
    {
        var file = Write("a.txt");
        var zip = PathFor("pack", "out.zip");
        FileExtraNodes.CreateZip(Items(file), zip);

        var ex = Assert.Throws<IOException>(() => FileExtraNodes.CreateZip(Items(file), zip));
        Assert.Contains("overwrite", ex.Message);

        var other = Write("b.txt");
        FileExtraNodes.CreateZip(Items(other), zip, overwrite: true);
        Assert.Equal(new[] { "b.txt" }, FileExtraNodes.ListZip(zip));
        Assert.Equal(new[] { zip }, Directory.GetFiles(PathFor("pack")));
    }

    [Fact]
    public void ZipCreate_MissingSource_IsAnErrorNamingIt_AndCreatesNoZip()
    {
        var present = Write("a.txt");
        var missing = PathFor("ghost.txt");
        var zip = PathFor("out", "package.zip");

        var ex = Assert.Throws<FileNotFoundException>(() => FileExtraNodes.CreateZip(Items(present, missing), zip));

        Assert.Contains(missing, ex.Message);
        Assert.Contains("Zip.Create", ex.Message);
        Assert.False(File.Exists(zip));
        Assert.False(Directory.Exists(PathFor("out")));
    }

    [Fact]
    public void ZipCreate_NeverPacksTheZipIntoItself()
    {
        var folder = PathFor("pack");
        Write(Path.Combine("pack", "a.txt"));
        var zip = Path.Combine(folder, "out.zip");

        FileExtraNodes.CreateZip(Items(folder), zip);
        FileExtraNodes.CreateZip(Items(folder), zip, overwrite: true);

        Assert.Equal(new[] { "pack/", "pack/a.txt" }, FileExtraNodes.ListZip(zip).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void ZipCreate_TwoSourcesWithTheSameStoredName_AreRefused()
    {
        var a = Write(Path.Combine("one", "x.txt"));
        var b = Write(Path.Combine("two", "x.txt"));
        var ex = Assert.Throws<ArgumentException>(() => FileExtraNodes.CreateZip(Items(a, b), PathFor("out.zip")));
        Assert.Contains("x.txt", ex.Message);
        Assert.False(File.Exists(PathFor("out.zip")));
    }

    [Fact]
    public void ZipCreate_InvalidArguments_NameTheProblem()
    {
        var file = Write("a.txt");
        Assert.Contains("sources", Assert.Throws<ArgumentNullException>(() => FileExtraNodes.CreateZip(null!, PathFor("o.zip"))).Message);
        Assert.Contains("'zipPath'", Assert.Throws<ArgumentException>(() => FileExtraNodes.CreateZip(Items(file), " ")).Message);
        Assert.Contains("empty", Assert.Throws<ArgumentException>(() => FileExtraNodes.CreateZip(Items(), PathFor("o.zip"))).Message);
        Assert.Contains("#2", Assert.Throws<ArgumentException>(() => FileExtraNodes.CreateZip(Items(file, 42), PathFor("o.zip"))).Message);
        Assert.Contains("#1", Assert.Throws<ArgumentException>(() => FileExtraNodes.CreateZip(Items((object?)null), PathFor("o.zip"))).Message);
        Assert.Contains("folder", Assert.Throws<ArgumentException>(() => FileExtraNodes.CreateZip(Items(file), _directory)).Message);
    }

    [Fact]
    public void ZipExtract_ExistingFile_NeedsOverwrite_AndExtractsNothingOtherwise()
    {
        var docs = MakeSampleTree();
        var zip = PathFor("package.zip");
        FileExtraNodes.CreateZip(Items(docs), zip);

        var target = PathFor("target");
        Write(Path.Combine("target", "docs", "sub", "b.txt"), "precious");

        var ex = Assert.Throws<IOException>(() => FileExtraNodes.ExtractZip(zip, target));
        Assert.Contains("overwrite", ex.Message);
        Assert.Contains("b.txt", ex.Message);
        Assert.Equal("precious", File.ReadAllText(Path.Combine(target, "docs", "sub", "b.txt")));
        Assert.False(File.Exists(Path.Combine(target, "docs", "a.txt")), "nothing may be extracted when the plan is refused");

        FileExtraNodes.ExtractZip(zip, target, overwrite: true);
        Assert.Equal("beta", File.ReadAllText(Path.Combine(target, "docs", "sub", "b.txt")));
        Assert.Equal("alpha", File.ReadAllText(Path.Combine(target, "docs", "a.txt")));
    }

    private string MakeZipWithEntries(string zipName, params (string Name, string Content)[] entries)
    {
        var zip = PathFor(zipName);
        using (var stream = new FileStream(zip, FileMode.Create))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using (var writer = new StreamWriter(entry.Open()))
                {
                    writer.Write(content);
                }
            }
        }

        return zip;
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("..\\evil.txt")]
    [InlineData("docs/../../evil.txt")]
    [InlineData("ok/../../../evil.txt")]
    public void ZipExtract_ZipSlip_IsRefused_AndNothingIsExtracted(string maliciousName)
    {
        var zip = MakeZipWithEntries("slip.zip", ("fine.txt", "fine"), (maliciousName, "pwned"));
        var target = PathFor("jail", "target");

        var ex = Assert.Throws<InvalidDataException>(() => FileExtraNodes.ExtractZip(zip, target));

        Assert.Contains("zip slip", ex.Message);
        Assert.Contains(maliciousName, ex.Message);
        Assert.False(File.Exists(PathFor("jail", "evil.txt")));
        Assert.False(File.Exists(PathFor("evil.txt")));
        Assert.False(File.Exists(Path.Combine(target, "fine.txt")));
    }

    [Fact]
    public void ZipExtract_AbsoluteEntryNames_AreRefused()
    {
        var outside = PathFor("elsewhere", "abs.txt");
        Directory.CreateDirectory(PathFor("elsewhere"));
        var zip = MakeZipWithEntries("abs.zip", (outside, "pwned"));
        var target = PathFor("target");

        var ex = Assert.Throws<InvalidDataException>(() => FileExtraNodes.ExtractZip(zip, target));

        Assert.Contains("zip slip", ex.Message);
        Assert.False(File.Exists(outside));
    }

    [Fact]
    public void ZipExtract_SiblingFolderWithTheSamePrefix_IsStillOutside()
    {
        // "target-evil" starts with "target": a naive StartsWith check would let it through.
        var zip = MakeZipWithEntries("sibling.zip", ("../target-evil/x.txt", "pwned"));
        var target = PathFor("target");
        Assert.Throws<InvalidDataException>(() => FileExtraNodes.ExtractZip(zip, target));
        Assert.False(Directory.Exists(PathFor("target-evil")));
    }

    [Fact]
    public void ZipExtract_HarmlessDotDotInsideTheArchive_IsAllowed()
    {
        var zip = MakeZipWithEntries("dots.zip", ("a/../b.txt", "ok"), ("./c.txt", "ok"));
        var target = PathFor("target");
        FileExtraNodes.ExtractZip(zip, target);
        Assert.True(File.Exists(Path.Combine(target, "b.txt")));
        Assert.True(File.Exists(Path.Combine(target, "c.txt")));
    }

    [Fact]
    public void ZipExtract_BackslashEntryNames_BecomeFolders()
    {
        var zip = MakeZipWithEntries("win.zip", ("docs\\inner\\a.txt", "ok"));
        var target = PathFor("target");
        FileExtraNodes.ExtractZip(zip, target);
        Assert.Equal("ok", File.ReadAllText(Path.Combine(target, "docs", "inner", "a.txt")));
    }

    [Fact]
    public void ZipExtract_NotAZip_ThrowsNamingTheFile()
    {
        var notZip = Write("fake.zip", "this is not a zip");
        var ex = Assert.Throws<InvalidDataException>(() => FileExtraNodes.ExtractZip(notZip, PathFor("t")));
        Assert.Contains("not a valid zip", ex.Message);
        Assert.Contains(notZip, ex.Message);
        Assert.Throws<InvalidDataException>(() => FileExtraNodes.ListZip(notZip));
    }

    [Fact]
    public void ZipExtract_InvalidArguments_NameTheInput()
    {
        var missing = PathFor("ghost.zip");
        Assert.Contains(missing, Assert.Throws<FileNotFoundException>(() => FileExtraNodes.ExtractZip(missing, PathFor("t"))).Message);
        Assert.Contains("'zipPath'", Assert.Throws<ArgumentException>(() => FileExtraNodes.ExtractZip(" ", PathFor("t"))).Message);

        var zip = MakeZipWithEntries("ok.zip", ("a.txt", "x"));
        Assert.Contains("'directory'", Assert.Throws<ArgumentException>(() => FileExtraNodes.ExtractZip(zip, " ")).Message);
    }

    [Fact]
    public void ZipList_ReturnsTheEntryNames_WithoutExtracting()
    {
        var zip = MakeZipWithEntries("list.zip", ("a.txt", "1"), ("dir/b.txt", "2"));
        Assert.Equal(new[] { "a.txt", "dir/b.txt" }, FileExtraNodes.ListZip(zip));
        Assert.False(Directory.Exists(PathFor("dir")));
    }

    [Fact]
    public void ZipList_MissingFile_ThrowsWithTheInputName()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => FileExtraNodes.ListZip(PathFor("ghost.zip")));
        Assert.Contains("Zip.List", ex.Message);
        Assert.Contains("'zipPath'", Assert.Throws<ArgumentException>(() => FileExtraNodes.ListZip("")).Message);
    }

    // --------------------------------------------------- registration & engine

    private static readonly (string Name, GraphFunction Role)[] ExpectedRoles =
    {
        ("File.Info", GraphFunction.Info),
        ("File.Hash", GraphFunction.Info),
        ("File.Copy", GraphFunction.Modify),
        ("File.Move", GraphFunction.Modify),
        ("File.Delete", GraphFunction.Modify),
        ("Directory.Exists", GraphFunction.Info),
        ("Directory.Create", GraphFunction.Modify),
        ("Directory.GetDirectories", GraphFunction.Info),
        ("Directory.FindFiles", GraphFunction.Info),
        ("Directory.Find", GraphFunction.Info),
        ("Directory.Copy", GraphFunction.Modify),
        ("Directory.Move", GraphFunction.Modify),
        ("Directory.Delete", GraphFunction.Modify),
        ("Text.AppendToFile", GraphFunction.Modify),
        ("CSV.AppendToFile", GraphFunction.Modify),
        ("Log.Write", GraphFunction.Modify),
        ("Zip.Create", GraphFunction.Modify),
        ("Zip.Extract", GraphFunction.Modify),
        ("Zip.List", GraphFunction.Info),
    };

    private static NodeRegistry CreateRegistry()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        return registry;
    }

    private static ZeroTouchNodeModel CreateNode(NodeRegistry registry, string name)
    {
        return new ZeroTouchNodeModel(registry.Definitions.Single(d => d.Name == name));
    }

    [Fact]
    public void EveryNodeOfTheClass_IsRegistered_InTheFileCategory_WithAnExplicitRole()
    {
        var registry = CreateRegistry();

        var ownDefinitions = registry.Definitions.Where(d => d.Id.StartsWith("CamelGraph.Nodes.FileExtraNodes.", StringComparison.Ordinal)).ToList();
        Assert.Equal(ExpectedRoles.Length, ownDefinitions.Count);

        foreach (var (name, role) in ExpectedRoles)
        {
            var definition = Assert.Single(ownDefinitions, d => d.Name == name);
            Assert.Equal("File", definition.Category);
            Assert.Equal(role, definition.Function);
            Assert.False(string.IsNullOrWhiteSpace(definition.Description), name + " needs a description");
        }
    }

    [Fact]
    public void NoNodeNameIsUsedTwice_AcrossTheWholeLibrary()
    {
        var names = CreateRegistry().Definitions.Select(d => d.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void FileInfo_RunsUnderTheEngine_WithItsSevenNamedOutputs()
    {
        var registry = CreateRegistry();
        var file = Write("model.nwd", "12345");
        var graph = new GraphModel();
        var path = new StringInputNode { Value = file };
        var info = CreateNode(registry, "File.Info");
        graph.AddNode(path);
        graph.AddNode(info);
        Assert.True(graph.Connect(path.OutPorts[0], info.InPorts[0]).Success);

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(NodeState.Executed, info.State);
        Assert.Equal(new[] { "exists", "name", "extension", "directory", "sizeBytes", "modified", "created" }, info.OutPorts.Select(p => p.Name));
        Assert.Equal(true, info.OutPorts[0].Value);
        Assert.Equal("model.nwd", info.OutPorts[1].Value);
        Assert.Equal(".nwd", info.OutPorts[2].Value);
        Assert.Equal(5d, info.OutPorts[4].Value);
    }

    [Fact]
    public void FindFiles_RunsUnderTheEngine_WithItsDefaults()
    {
        var registry = CreateRegistry();
        Write("a.nwd");
        Write("b.nwf");
        var graph = new GraphModel();
        var folder = new StringInputNode { Value = _directory };
        var find = CreateNode(registry, "Directory.FindFiles");
        graph.AddNode(folder);
        graph.AddNode(find);
        Assert.True(graph.Connect(folder.OutPorts[0], find.InPorts[0]).Success);

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(NodeState.Executed, find.State);
        var files = Assert.IsAssignableFrom<IEnumerable<object?>>(find.OutPorts[0].Value).Cast<string>().ToArray();
        Assert.Equal(new[] { PathFor("a.nwd"), PathFor("b.nwf") }, files);
    }

    [Fact]
    public void AppendingWriters_ChainThroughTheirReturnedPath_UnderTheEngine()
    {
        var registry = CreateRegistry();
        var log = PathFor("logs", "run.log");
        var graph = new GraphModel();
        var path = new StringInputNode { Value = log };
        var message = new StringInputNode { Value = "hello" };
        var write = CreateNode(registry, "Log.Write");
        foreach (var node in new NodeModel[] { path, message, write })
        {
            graph.AddNode(node);
        }

        Assert.True(graph.Connect(path.OutPorts[0], write.InPorts[0]).Success);
        Assert.True(graph.Connect(message.OutPorts[0], write.InPorts[1]).Success);

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(NodeState.Executed, write.State);
        Assert.EndsWith("  INFO  hello", Assert.IsType<string>(write.OutPorts[0].Value));
        Assert.Single(File.ReadAllLines(log));
    }
}
