using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CamelGraph.Core.Files;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes;

/// <summary>
/// File-system nodes that go beyond reading and writing one file: file facts and hashes, copy / move / delete,
/// folder listing and search, appending writers (text, CSV, log lines) and zip packaging. Reads are Info nodes;
/// everything that writes, copies, moves, deletes or extracts is a Modify node (the Script Player asks before
/// running those) and returns the resulting path so it can sequence further nodes. A relative path starts in the
/// graph's folder (<see cref="PathResolver"/>).
/// </summary>
[NodeCategory("File")]
public static class FileExtraNodes
{
    // ------------------------------------------------------------------ File

    /// <summary>Reads the basic facts of a file without opening it. A missing file is not an error: "exists" is false and the rest is null, so a script can branch on it.</summary>
    /// <param name="path">The file to inspect. A relative path starts in the graph's folder.</param>
    /// <returns>Dictionary with "exists", "name", "extension", "directory", "sizeBytes", "modified" and "created".</returns>
    [NodeName("File.Info")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [MultiReturn("exists", "name", "extension", "directory", "sizeBytes", "modified", "created")]
    [PortKinds("boolean", "text", "text", "file", "number", "datetime", "datetime")]
    [NodeDescription("Reads a file's name, extension, folder, size in bytes and modified / created dates; a missing file gives exists = false and empty values instead of an error. A relative path starts in the graph's folder.")]
    [NodeSearchTags("size", "date", "modified", "created", "properties", "stat", "exists", "attributes")]
    public static Dictionary<string, object?> GetFileInfo([NodePath(NodePathMode.Open, Filter = FileFilters.All)] string path)
    {
        PathNodes.RequireText(path, "File.Info", nameof(path), "a file path");
        var info = new FileInfo(FileNodes.ResolveChecked(path, "File.Info", nameof(path)));
        if (!info.Exists)
        {
            return new Dictionary<string, object?>
            {
                ["exists"] = false,
                ["name"] = null,
                ["extension"] = null,
                ["directory"] = null,
                ["sizeBytes"] = null,
                ["modified"] = null,
                ["created"] = null,
            };
        }

        return new Dictionary<string, object?>
        {
            ["exists"] = true,
            ["name"] = info.Name,
            ["extension"] = info.Extension,
            ["directory"] = info.DirectoryName ?? string.Empty,
            ["sizeBytes"] = (double)info.Length,
            ["modified"] = info.LastWriteTime,
            ["created"] = info.CreationTime,
        };
    }

    /// <summary>Computes the checksum of a file's content, e.g. to detect that a file changed since the last run.</summary>
    /// <param name="path">The file to hash. A relative path starts in the graph's folder.</param>
    /// <param name="algorithm">SHA256 (default), SHA1 or MD5, in any case.</param>
    /// <returns>The hash as lower-case hexadecimal text.</returns>
    [NodeName("File.Hash")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("hash")]
    [NodeDescription("Computes a checksum of a file's content (SHA256, SHA1 or MD5) as lower-case hex text - handy for change detection. A relative path starts in the graph's folder.")]
    [NodeSearchTags("checksum", "sha", "md5", "digest", "change", "compare", "fingerprint")]
    public static string GetFileHash(
        [NodePath(NodePathMode.Open, Filter = FileFilters.All)] string path,
        [NodeChoices("SHA256", "SHA1", "MD5")] string algorithm = "SHA256")
    {
        var file = FileNodes.RequireExistingFile(path, "File.Hash");
        using (var hasher = CreateHasher(algorithm))
        using (var stream = FileErrors.Run("File.Hash", file, false, () => new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
        {
            var bytes = hasher.ComputeHash(stream);
            var hex = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            return hex.ToString();
        }
    }

    /// <summary>Copies a file, creating the destination folder when needed.</summary>
    /// <param name="source">The file to copy. A relative path starts in the graph's folder.</param>
    /// <param name="destination">The full destination file path (not just a folder).</param>
    /// <param name="overwrite">True to replace an existing destination file; false (default) to fail instead.</param>
    /// <returns>The destination path, for sequencing further file nodes.</returns>
    [NodeName("File.Copy")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesFiles | CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("path")]
    [NodeDescription("Copies a file to a new path (creates the destination folder; refuses to replace an existing file unless overwrite is true). A relative path starts in the graph's folder.")]
    [NodeSearchTags("duplicate", "backup", "clone", "file", "copy")]
    public static string CopyFile(
        [NodePath(NodePathMode.Open, Filter = FileFilters.All)] string source,
        [NodePath(NodePathMode.Save, Filter = FileFilters.All)] string destination,
        bool overwrite = false)
    {
        var from = RequireFile(source, "File.Copy", nameof(source));
        var to = FileNodes.ResolveForWriting(destination, "File.Copy", nameof(destination));
        if (SamePath(from, to))
        {
            return to;
        }

        PrepareDestination(to, "File.Copy", overwrite);
        FileErrors.Run("File.Copy", to, true, () => File.Copy(from, to, overwrite));
        return to;
    }

    /// <summary>Moves (or renames) a file, creating the destination folder when needed.</summary>
    /// <param name="source">The file to move. A relative path starts in the graph's folder.</param>
    /// <param name="destination">The full destination file path (not just a folder).</param>
    /// <param name="overwrite">True to replace an existing destination file; false (default) to fail instead.</param>
    /// <returns>The destination path, for sequencing further file nodes.</returns>
    [NodeName("File.Move")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesFiles | CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("path")]
    [NodeDescription("Moves or renames a file (creates the destination folder; refuses to replace an existing file unless overwrite is true). With overwrite, the old destination file is replaced only once the move can succeed: if the source is locked by another program, the destination stays as it was. A relative path starts in the graph's folder.")]
    [NodeSearchTags("rename", "relocate", "archive", "file", "move")]
    public static string MoveFile(
        [NodePath(NodePathMode.Open, Filter = FileFilters.All)] string source,
        [NodePath(NodePathMode.Save, Filter = FileFilters.All)] string destination,
        bool overwrite = false)
    {
        var from = RequireFile(source, "File.Move", nameof(source));
        var to = FileNodes.ResolveForWriting(destination, "File.Move", nameof(destination));
        if (SamePath(from, to))
        {
            return to;
        }

        PrepareDestination(to, "File.Move", overwrite);
        FileErrors.Run("File.Move", to, true, () => MoveOver(from, to));
        return to;
    }

    /// <summary>Deletes a file. A file that is already gone is not an error.</summary>
    /// <param name="path">The file to delete. A relative path starts in the graph's folder.</param>
    /// <returns>True when a file was deleted; false when there was no such file.</returns>
    [NodeName("File.Delete")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesFiles | CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("deleted")]
    [NodeDescription("Deletes a file; returns true when it was deleted and false when it did not exist. A relative path starts in the graph's folder.")]
    [NodeSearchTags("remove", "erase", "clean", "file", "delete")]
    public static bool DeleteFile([NodePath(NodePathMode.Open, Filter = FileFilters.All)] string path)
    {
        PathNodes.RequireText(path, "File.Delete", nameof(path), "a file path");
        var file = FileNodes.ResolveChecked(path, "File.Delete", nameof(path));
        if (!File.Exists(file))
        {
            return false;
        }

        FileErrors.Run("File.Delete", file, true, () => File.Delete(file));
        return true;
    }

    // ------------------------------------------------------------- Directory

    /// <summary>Tests whether a folder exists.</summary>
    /// <param name="path">The folder path to test. A relative path starts in the graph's folder.</param>
    /// <returns>True when a folder exists at the path (false for files, missing paths and a blank path).</returns>
    [NodeName("Directory.Exists")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("exists")]
    [NodeDescription("Tests whether a folder exists at the given path; a file, a missing folder and a blank path all give false. A relative path starts in the graph's folder.")]
    [NodeSearchTags("folder", "check", "found", "present", "directory")]
    public static bool DirectoryExists([NodePath(NodePathMode.Folder)] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return Directory.Exists(PathResolver.Resolve(path));
        }
        catch (Exception ex) when (FileErrors.IsFileProblem(ex))
        {
            return false;
        }
    }

    /// <summary>Creates a folder (and its parents). A folder that already exists is not an error.</summary>
    /// <param name="path">The folder to create. A relative path starts in the graph's folder.</param>
    /// <returns>The folder path, for sequencing further file nodes.</returns>
    [NodeName("Directory.Create")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("path")]
    [NodeDescription("Creates a folder including any missing parent folders (does nothing when it already exists). A relative path starts in the graph's folder.")]
    [NodeSearchTags("mkdir", "make", "folder", "new", "directory")]
    public static string CreateDirectory([NodePath(NodePathMode.Folder)] string path)
    {
        var folder = ResolveFolder(path, "Directory.Create");
        FileErrors.Run("Directory.Create", folder, true, () => Directory.CreateDirectory(folder));
        return folder;
    }

    /// <summary>Finds files or folders under a folder, with optional sorting by name / date / size and a limit - e.g. the newest file of a type.</summary>
    /// <param name="path">The folder to search. A relative path starts in the graph's folder.</param>
    /// <param name="pattern">Wildcard filter on the file or folder name ("*" and "?"); several patterns can be separated by ";" (e.g. "*.nwd;*.nwf").</param>
    /// <param name="kind">files (default) or folders.</param>
    /// <param name="recursive">True (default) to search sub-folders too.</param>
    /// <param name="sortBy">name (full path, alphabetical, case ignored, numbers in order), modified, created or size (files only).</param>
    /// <param name="descending">True to reverse the order (newest / largest first).</param>
    /// <param name="limit">Keep only the first N results after sorting; 0 keeps all.</param>
    /// <returns>The full paths in the requested order.</returns>
    [NodeName("Directory.Find")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("paths")]
    [NodeDescription("Finds files or folders under a folder by wildcard (several allowed, separated by \";\"), in every sub-folder unless recursive is off. kind chooses files or folders. The paths come sorted by name, ignoring case and with numbers in order (c2 before c10); Advanced sorts by date or size instead and can keep only the first few - the newest file is sortBy modified + descending + limit 1. A pattern must match the whole name, so \"*.ifc\" never also finds .ifcxml. A relative path starts in the graph's folder.")]
    [NodeSearchTags("search", "find", "newest", "latest", "recursive", "list", "wildcard", "files", "folders", "subfolders", "directory", "tree", "browse", "batch", "get files", "get directories")]
    public static IList<string> Find(
        [NodePath(NodePathMode.Folder)] string path,
        string pattern = "*",
        [NodeChoices("files", "folders")] string kind = "files",
        bool recursive = true,
        [NodePanel("Advanced")][NodeChoices("name", "modified", "created", "size")] string sortBy = "name",
        [NodePanel("Advanced")] bool descending = false,
        [NodePanel("Advanced")][NodeRange(0, 100000, SoftMax = 100, Step = 1)] int limit = 0)
    {
        return FindCore("Directory.Find", path, pattern, ParseKind(kind), recursive, sortBy, descending, limit);
    }

    /// <summary>Lists the sub-folders of a folder, optionally filtered by a wildcard and optionally at every depth. Retired: Directory.Find with kind = folders does the same.</summary>
    /// <param name="path">The folder to list. A relative path starts in the graph's folder.</param>
    /// <param name="pattern">Wildcard filter on the folder name ("*" and "?"); several patterns can be separated by ";".</param>
    /// <param name="recursive">True to include sub-folders of sub-folders.</param>
    /// <returns>The full folder paths, sorted by name.</returns>
    [NodeName("Directory.GetDirectories")]
    [NodeDeprecated("Directory.Find")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("directories")]
    [NodeDescription("Lists the sub-folders of a folder as full paths (optionally filtered by a wildcard such as \"2026*\" and optionally at every depth).")]
    [NodeSearchTags("folders", "subfolders", "list", "browse", "directory", "tree")]
    public static IList<string> GetDirectories([NodePath(NodePathMode.Folder)] string path, string pattern = "*", bool recursive = false)
    {
        return FindCore("Directory.GetDirectories", path, pattern, wantFiles: false, recursive, "name", false, 0);
    }

    /// <summary>Finds files under a folder, with optional sorting by name / date / size and a limit - e.g. the newest file of a type. Retired: Directory.Find does the same.</summary>
    /// <param name="path">The folder to search. A relative path starts in the graph's folder.</param>
    /// <param name="pattern">Wildcard filter on the file name ("*" and "?"); several patterns can be separated by ";" (e.g. "*.nwd;*.nwf").</param>
    /// <param name="recursive">True (default) to search sub-folders too.</param>
    /// <param name="sortBy">name (full path, alphabetical), modified, created or size.</param>
    /// <param name="descending">True to reverse the order (newest / largest first).</param>
    /// <param name="limit">Keep only the first N files after sorting; 0 keeps all.</param>
    /// <returns>The full file paths in the requested order.</returns>
    [NodeName("Directory.FindFiles")]
    [NodeDeprecated("Directory.Find")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("files")]
    [NodeDescription("Finds files under a folder by wildcard (several allowed, separated by \";\"), sorted by name, date or size, with an optional limit - the newest file is sortBy modified + descending + limit 1.")]
    [NodeSearchTags("search", "find", "newest", "latest", "recursive", "list", "wildcard", "files", "directory")]
    public static IList<string> FindFiles(
        [NodePath(NodePathMode.Folder)] string path,
        string pattern = "*",
        bool recursive = true,
        [NodeChoices("name", "modified", "created", "size")] string sortBy = "name",
        bool descending = false,
        [NodeRange(0, 100000)] int limit = 0)
    {
        return FindCore("Directory.FindFiles", path, pattern, wantFiles: true, recursive, sortBy, descending, limit);
    }

    /// <summary>Copies a folder with everything in it.</summary>
    /// <param name="source">The folder to copy. A relative path starts in the graph's folder.</param>
    /// <param name="destination">The folder to copy it to (created when needed; its content is kept and merged with the copy).</param>
    /// <param name="overwrite">True to replace files that already exist in the destination; false (default) to fail, before anything is copied, if any would be replaced.</param>
    /// <returns>The destination folder, for sequencing further file nodes.</returns>
    [NodeName("Directory.Copy")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesFiles | CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("path")]
    [NodeDescription("Copies a folder with all its files and sub-folders to another place (creates the destination; files already there are kept, and a file with the same name is only replaced when overwrite is true - checked before anything is copied). The destination cannot be inside the source. A relative path starts in the graph's folder.")]
    [NodeSearchTags("copy", "folder", "directory", "backup", "stage", "deliverable", "duplicate", "robocopy", "xcopy")]
    public static string CopyDirectory(
        [NodePath(NodePathMode.Folder)] string source,
        [NodePath(NodePathMode.Folder)] string destination,
        bool overwrite = false)
    {
        var from = RequireExistingFolder(source, "Directory.Copy", nameof(source));
        var to = ResolveFolder(destination, "Directory.Copy", nameof(destination));
        if (SamePath(from, to))
        {
            return to;
        }

        RequireNotInside(to, from, "Directory.Copy");
        FileErrors.Run("Directory.Copy", to, true, () => CopyTree(from, to, overwrite, "Directory.Copy"));
        return to;
    }

    /// <summary>Moves (or renames) a folder with everything in it.</summary>
    /// <param name="source">The folder to move. A relative path starts in the graph's folder.</param>
    /// <param name="destination">The new place of the folder (its parent is created when needed).</param>
    /// <param name="overwrite">True to move the content into a destination that already exists (files with the same name are replaced); false (default) to fail instead.</param>
    /// <returns>The destination folder, for sequencing further file nodes.</returns>
    [NodeName("Directory.Move")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesFiles | CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("path")]
    [NodeDescription("Moves or renames a folder with everything in it. When the destination already exists it is an error unless overwrite is true, which moves the content into it and replaces files with the same name. Moving to another drive copies first and removes the original afterwards. The destination cannot be inside the source. A relative path starts in the graph's folder.")]
    [NodeSearchTags("move", "rename", "folder", "directory", "relocate", "archive", "stage")]
    public static string MoveDirectory(
        [NodePath(NodePathMode.Folder)] string source,
        [NodePath(NodePathMode.Folder)] string destination,
        bool overwrite = false)
    {
        var from = RequireExistingFolder(source, "Directory.Move", nameof(source));
        var to = ResolveFolder(destination, "Directory.Move", nameof(destination));
        if (SamePath(from, to))
        {
            return to;
        }

        if (string.Equals(Path.GetPathRoot(from), from, PathNodes.PathComparison))
        {
            throw new InvalidOperationException("Directory.Move refuses to move the drive root '" + source + "'. Give the folder you really want to move.");
        }

        RequireNotInside(to, from, "Directory.Move");
        FileErrors.Run("Directory.Move", to, true, () => MoveTree(from, to, overwrite));
        return to;
    }

    /// <summary>Deletes a folder. A folder that is already gone is not an error; a non-empty folder needs recursive = true.</summary>
    /// <param name="path">The folder to delete. A relative path starts in the graph's folder.</param>
    /// <param name="recursive">True to delete the folder together with everything inside it.</param>
    /// <returns>True when a folder was deleted; false when there was no such folder.</returns>
    [NodeName("Directory.Delete")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesFiles | CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("deleted")]
    [NodeDescription("Deletes a folder (an empty one, or with its whole content when recursive is true); returns false when it did not exist. A relative path starts in the graph's folder.")]
    [NodeSearchTags("remove", "rmdir", "erase", "clean", "folder", "directory")]
    public static bool DeleteDirectory([NodePath(NodePathMode.Folder)] string path, bool recursive = false)
    {
        var folder = ResolveFolder(path, "Directory.Delete");
        if (!Directory.Exists(folder))
        {
            return false;
        }

        var full = Path.GetFullPath(folder);
        if (string.Equals(Path.GetPathRoot(full), full, PathNodes.PathComparison))
        {
            throw new InvalidOperationException("Directory.Delete refuses to delete the drive root '" + path + "'. Give the folder you really want to delete.");
        }

        if (!recursive)
        {
            using (var entries = Directory.EnumerateFileSystemEntries(folder).GetEnumerator())
            {
                if (entries.MoveNext())
                {
                    throw new IOException("Directory.Delete: the folder '" + folder + "' is not empty. Set 'recursive' to true to delete it together with its content.");
                }
            }
        }

        FileErrors.Run("Directory.Delete", folder, true, () => Directory.Delete(folder, recursive));
        return true;
    }

    // ------------------------------------------------------------- Appending

    /// <summary>Appends text to the end of a file, creating the file and its folder when needed.</summary>
    /// <param name="path">The file to append to. A relative path starts in the graph's folder.</param>
    /// <param name="text">The text to append; null is an error.</param>
    /// <param name="newLine">True (default) to end the appended text with a line break.</param>
    /// <param name="encoding">UTF-8 (default, no byte-order mark), UTF-8 with BOM, Windows-1252 or UTF-16; a mark is written only when the file is new.</param>
    /// <returns>The path that was written, for sequencing further file nodes.</returns>
    [NodeName("Text.AppendToFile")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("path")]
    [NodeDescription("Appends text (plus a line break by default) to the end of a text file, creating the file and folder if needed. A list of texts appends every item as its own line. Nothing wired is an error; empty text with the line break on adds a blank line. In a loop, wire the loop item (or something made from it) into the text: a node with only fixed inputs belongs to no loop and runs once. A relative path starts in the graph's folder.")]
    [NodeSearchTags("append", "add", "log", "write", "txt", "export", "encoding")]
    [NodeAliases("CamelGraph.Nodes.FileExtraNodes.AppendText@string,string,bool")]
    public static string AppendText(
        [NodePath(NodePathMode.Save, Filter = FileFilters.Text)] string path,
        string text,
        bool newLine = true,
        [NodePanel("Advanced")][NodeChoices("UTF-8", "UTF-8 with BOM", "Windows-1252", "UTF-16")] string encoding = "UTF-8")
    {
        const string node = "Text.AppendToFile";
        var file = FileNodes.ResolveForWriting(path, node);
        if (text == null)
        {
            throw new ArgumentNullException(
                nameof(text),
                node + ": the 'text' input is empty (null), so nothing was added to '" + file + "'. Wire text into 'text', or use empty text \"\" with newLine on to add a blank line.");
        }

        var kind = TextFile.ParseForWriting(encoding, node);
        FileErrors.Run(node, file, true, () =>
        {
            FileNodes.EnsureParentFolder(file);
            TextFile.AppendAllText(file, text + (newLine ? Environment.NewLine : string.Empty), kind);
        });
        return file;
    }

    /// <summary>
    /// Appends rows to a CSV file with the same quoting and number formatting as CSV.WriteToFile. Headers (when given)
    /// are written only if the file does not exist yet or is empty, so a loop can keep appending to one report.
    /// </summary>
    /// <param name="path">The CSV file to append to. A relative path starts in the graph's folder.</param>
    /// <param name="rows">List of rows (each row a list of cell values; a scalar becomes a one-cell row).</param>
    /// <param name="delimiter">Single-character cell delimiter: a comma, semicolon, bar, or "tab".</param>
    /// <param name="headers">Optional header cells, written once when the file is new or empty.</param>
    /// <param name="encoding">UTF-8 (default, no byte-order mark), UTF-8 with BOM, Windows-1252 or UTF-16; a mark is written only when the file is new.</param>
    /// <returns>The path that was written, for sequencing further file nodes.</returns>
    [NodeName("CSV.AppendToFile")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("path")]
    [NodeDescription("Appends rows to a CSV file (same quoting and date format as CSV.WriteToFile), writing the optional headers only when the file is new or empty. In a loop, wire the loop item (or something made from it) into the rows: a node with only fixed inputs belongs to no loop and runs once. A relative path starts in the graph's folder.")]
    [NodeSearchTags("csv", "append", "add rows", "table", "report", "export", "tab", "tsv", "encoding")]
    [NodeAliases("CamelGraph.Nodes.FileExtraNodes.AppendCsv@string,System.Collections.Generic.IList<object>,string,System.Collections.Generic.IList<object>")]
    public static string AppendCsv(
        [NodePath(NodePathMode.Save, Filter = FileFilters.Csv)] string path,
        IList<object?> rows,
        [NodeChoices(",", ";", "|", "tab")] string delimiter = ",",
        IList<object?>? headers = null,
        [NodePanel("Advanced")][NodeChoices("UTF-8", "UTF-8 with BOM", "Windows-1252", "UTF-16")] string encoding = "UTF-8")
    {
        const string node = "CSV.AppendToFile";
        if (rows == null)
        {
            throw new ArgumentNullException(nameof(rows), node + " requires a list of rows (each row a list of cells).");
        }

        var separator = FileNodes.RequireSingleCharDelimiter(delimiter, node);
        var kind = TextFile.ParseForWriting(encoding, node);
        var file = FileNodes.ResolveForWriting(path, node);
        var builder = new StringBuilder();
        var info = new FileInfo(file);
        var isNewOrEmpty = !info.Exists || info.Length == 0;
        if (!isNewOrEmpty && !EndsWithLineBreak(file))
        {
            builder.Append('\n');
        }

        if (isNewOrEmpty && headers != null && headers.Count > 0)
        {
            FileNodes.AppendCsvRow(builder, headers, separator);
        }

        foreach (var row in rows)
        {
            FileNodes.AppendCsvRow(builder, row, separator);
        }

        FileErrors.Run(node, file, true, () =>
        {
            FileNodes.EnsureParentFolder(file);
            TextFile.AppendAllText(file, builder.ToString(), kind);
        });
        return file;
    }

    /// <summary>Appends one timestamped line to a log file, creating the file and folder when needed.</summary>
    /// <param name="path">The log file. A relative path starts in the graph's folder.</param>
    /// <param name="message">The text to log; line breaks are replaced by " | " so an entry stays on one line.</param>
    /// <param name="level">INFO (default), WARN, ERROR or DEBUG, in any case.</param>
    /// <param name="encoding">UTF-8 (default, no byte-order mark), UTF-8 with BOM, Windows-1252 or UTF-16; a mark is written only when the file is new.</param>
    /// <returns>The line that was written, e.g. "2026-10-01 10:20:35  INFO  message".</returns>
    [NodeName("Log.Write")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("line")]
    [NodeDescription("Appends one line \"yyyy-MM-dd HH:mm:ss  LEVEL  message\" (local time) to a log file and returns that line. In a loop, wire the loop item (or something made from it) into the message to get one line per pass: a Log.Write with only fixed inputs belongs to no loop and writes once. A relative path starts in the graph's folder.")]
    [NodeSearchTags("log", "logging", "audit trail", "timestamp", "journal", "append", "report")]
    [NodeAliases("CamelGraph.Nodes.FileExtraNodes.WriteLog@string,string,string")]
    public static string WriteLog(
        [NodePath(NodePathMode.Save, Filter = FileFilters.Log)] string path,
        string message,
        [NodeChoices("INFO", "WARN", "ERROR", "DEBUG")] string level = "INFO",
        [NodePanel("Advanced")][NodeChoices("UTF-8", "UTF-8 with BOM", "Windows-1252", "UTF-16")] string encoding = "UTF-8")
    {
        const string node = "Log.Write";
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message), "Log.Write requires a message. Wire text into the 'message' input.");
        }

        var line = FormatLogLine(DateTime.Now, level, message);
        var kind = TextFile.ParseForWriting(encoding, node);
        var file = FileNodes.ResolveForWriting(path, node);
        FileErrors.Run(node, file, true, () =>
        {
            FileNodes.EnsureParentFolder(file);
            var info = new FileInfo(file);
            var prefix = info.Exists && info.Length > 0 && !EndsWithLineBreak(file) ? Environment.NewLine : string.Empty;
            TextFile.AppendAllText(file, prefix + line + Environment.NewLine, kind);
        });
        return line;
    }

    // ------------------------------------------------------------------- Zip

    /// <summary>Packs files and folders into a zip archive. Folders keep their structure under their own name.</summary>
    /// <param name="sources">The files and/or folders to pack (a list of paths; several wires and lists of lists are joined).</param>
    /// <param name="zipPath">The zip file to create (its folder is created when needed). A relative path starts in the graph's folder.</param>
    /// <param name="overwrite">True to replace an existing zip file; false (default) to fail instead.</param>
    /// <returns>The zip path, for sequencing further file nodes.</returns>
    [NodeName("Zip.Create")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("path")]
    [NodeDescription("Packs files and folders into a zip archive (a folder is stored with its structure under its own name); a missing source is an error. Several wires, lists and lists of lists of paths can go into sources (for example a list of files plus one extra folder); they are packed in order. The zip is built beside the target and then put in place, so a failure never leaves a half-written file. A relative path starts in the graph's folder.")]
    [NodeSearchTags("zip", "compress", "archive", "package", "deliverable", "pack", "bundle")]
    public static string CreateZip(
        [MultiInput] IList<object?> sources,
        [NodePath(NodePathMode.Save, Filter = FileFilters.Zip)] string zipPath,
        bool overwrite = false)
    {
        PathNodes.RequireText(zipPath, "Zip.Create", nameof(zipPath), "the path of the zip file to create");
        if (sources == null)
        {
            throw new ArgumentNullException(nameof(sources), "Zip.Create requires a list of files and/or folders to pack. Wire a list of paths into the 'sources' input.");
        }

        var zipFull = Path.GetFullPath(FileNodes.ResolveChecked(zipPath, "Zip.Create", nameof(zipPath)));
        var items = new List<ZipItem>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var source in Flatten(sources))
        {
            index++;
            var text = source as string;
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException(
                    "Zip.Create: source #" + index.ToString(CultureInfo.InvariantCulture) + " is not a path. Every element of 'sources' must be the path of a file or folder.",
                    nameof(sources));
            }

            var full = Path.GetFullPath(PathResolver.Resolve(text));
            if (File.Exists(full))
            {
                AddZipItem(items, names, new ZipItem(full, Path.GetFileName(full), isDirectory: false), zipFull);
            }
            else if (Directory.Exists(full))
            {
                var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var folderName = Path.GetFileName(trimmed);
                if (folderName.Length == 0)
                {
                    throw new ArgumentException("Zip.Create: '" + text + "' is a drive root. Pack a folder inside it instead.", nameof(sources));
                }

                AddZipItem(items, names, new ZipItem(full, folderName + "/", isDirectory: true), zipFull);
                CollectZipItems(full, folderName, items, names, zipFull);
            }
            else
            {
                throw new FileNotFoundException("Zip.Create: the source '" + text + "' does not exist.", text);
            }
        }

        if (items.Count == 0)
        {
            throw new ArgumentException("Zip.Create: 'sources' is empty. Wire at least one file or folder path.", nameof(sources));
        }

        if (Directory.Exists(zipFull))
        {
            throw new ArgumentException("Zip.Create: '" + zipPath + "' is a folder. Give the full path of the zip file, e.g. \"C:\\Out\\package.zip\".", nameof(zipPath));
        }

        if (File.Exists(zipFull) && !overwrite)
        {
            throw new IOException("Zip.Create: the zip file '" + zipPath + "' already exists. Set 'overwrite' to true to replace it.");
        }

        FileNodes.RequireWritablePath(zipFull, "Zip.Create", nameof(zipPath));

        // Write beside the target first, so a failure never leaves a half-written (or destroyed) zip behind.
        var temp = zipFull + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            FileErrors.Run("Zip.Create", zipFull, true, () =>
            {
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
                {
                    foreach (var item in items)
                    {
                        var entry = archive.CreateEntry(item.EntryName, CompressionLevel.Optimal);
                        TrySetEntryTime(entry, item.IsDirectory ? Directory.GetLastWriteTime(item.FullPath) : File.GetLastWriteTime(item.FullPath));
                        if (item.IsDirectory)
                        {
                            continue;
                        }

                        using (var input = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var target = entry.Open())
                        {
                            input.CopyTo(target);
                        }
                    }
                }

                MoveOver(temp, zipFull);
            });
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        return zipFull;
    }

    /// <summary>Unpacks a zip archive into a folder. Entries that would land outside the folder ("zip slip") are refused.</summary>
    /// <param name="zipPath">The zip file to unpack. A relative path starts in the graph's folder.</param>
    /// <param name="directory">The destination folder (created when needed).</param>
    /// <param name="overwrite">True to replace files that already exist; false (default) to fail instead.</param>
    /// <returns>The destination folder, for sequencing further file nodes.</returns>
    [NodeName("Zip.Extract")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesFiles | CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("directory")]
    [NodeDescription("Unpacks a zip archive into a folder; refuses entries that would escape the folder and files that already exist unless overwrite is true. A relative path starts in the graph's folder.")]
    [NodeSearchTags("unzip", "extract", "decompress", "unpack", "archive", "zip")]
    public static string ExtractZip(
        [NodePath(NodePathMode.Open, Filter = FileFilters.Zip)] string zipPath,
        [NodePath(NodePathMode.Folder)] string directory,
        bool overwrite = false)
    {
        var zipFile = RequireFile(zipPath, "Zip.Extract", nameof(zipPath));
        PathNodes.RequireText(directory, "Zip.Extract", nameof(directory), "a destination folder");

        var targetFull = Path.GetFullPath(FileNodes.ResolveChecked(directory, "Zip.Extract", nameof(directory)));
        var root = targetFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (root.Length == 0)
        {
            root = targetFull;
        }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? root
            : root + Path.DirectorySeparatorChar;

        using (var archive = OpenZip(zipFile, "Zip.Extract"))
        {
            // Validate every entry before writing anything, so a malicious or clashing archive extracts nothing.
            var plan = new List<KeyValuePair<ZipArchiveEntry, string>>();
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                string destination;
                try
                {
                    destination = Path.GetFullPath(Path.Combine(root, name));
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                    throw new InvalidDataException("Zip.Extract: the entry '" + entry.FullName + "' has a name that cannot be used as a path on this computer. " + ex.Message, ex);
                }

                var isDirectory = name.EndsWith("/", StringComparison.Ordinal);
                var inside = destination.StartsWith(prefix, PathNodes.PathComparison) ||
                             (isDirectory && string.Equals(destination, root, PathNodes.PathComparison));
                if (!inside)
                {
                    throw new InvalidDataException(
                        "Zip.Extract: the entry '" + entry.FullName + "' would be written outside the target folder (zip slip). Nothing was extracted.");
                }

                if (isDirectory)
                {
                    plan.Add(new KeyValuePair<ZipArchiveEntry, string>(entry, destination));
                    continue;
                }

                if (Directory.Exists(destination))
                {
                    throw new IOException("Zip.Extract: the entry '" + entry.FullName + "' cannot be written because '" + destination + "' is a folder.");
                }

                if (File.Exists(destination) && !overwrite)
                {
                    throw new IOException("Zip.Extract: the file '" + destination + "' already exists. Set 'overwrite' to true to replace it.");
                }

                plan.Add(new KeyValuePair<ZipArchiveEntry, string>(entry, destination));
            }

            FileErrors.Run("Zip.Extract", targetFull, true, () => Directory.CreateDirectory(targetFull));
            foreach (var step in plan)
            {
                var entry = step.Key;
                var destination = step.Value;
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }

                var parent = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                FileErrors.Run("Zip.Extract", destination, true, () =>
                {
                    using (var source = entry.Open())
                    using (var target = new FileStream(destination, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        source.CopyTo(target);
                    }
                });

                try
                {
                    File.SetLastWriteTime(destination, entry.LastWriteTime.LocalDateTime);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    // The date is a nicety; never fail an extraction over it.
                }
            }
        }

        return targetFull;
    }

    /// <summary>Lists the entry names stored in a zip archive without extracting anything.</summary>
    /// <param name="zipPath">The zip file to read. A relative path starts in the graph's folder.</param>
    /// <returns>The entry names as stored (folders end with "/").</returns>
    [NodeName("Zip.List")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("entries")]
    [NodeDescription("Lists the entry names inside a zip archive without extracting it. A relative path starts in the graph's folder.")]
    [NodeSearchTags("zip", "contents", "list", "inspect", "archive", "entries")]
    public static IList<string> ListZip([NodePath(NodePathMode.Open, Filter = FileFilters.Zip)] string zipPath)
    {
        var zipFile = RequireFile(zipPath, "Zip.List", nameof(zipPath));
        using (var archive = OpenZip(zipFile, "Zip.List"))
        {
            var names = new List<string>();
            foreach (var entry in archive.Entries)
            {
                names.Add(entry.FullName);
            }

            return names;
        }
    }

    // ------------------------------------------------------------------
    // Helpers (not imported as nodes: non-public).
    // ------------------------------------------------------------------

    /// <summary>Like FileNodes.RequireExistingFile, but names the input that is actually wired (e.g. 'source', 'zipPath'); returns the full path.</summary>
    internal static string RequireFile(string path, string nodeName, string inputName)
    {
        return FileNodes.RequireExistingFile(path, nodeName, inputName);
    }

    /// <summary>Checks a folder path (blank, characters Windows refuses) and returns it in full, resolved against the graph's folder.</summary>
    internal static string ResolveFolder(string? path, string nodeName, string inputName = "path")
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(nodeName + " requires a folder path. Wire a Directory Path node into the '" + inputName + "' input.", inputName);
        }

        return FileNodes.ResolveChecked(path!, nodeName, inputName);
    }

    private static string RequireExistingFolder(string? path, string nodeName, string inputName = "path")
    {
        var folder = ResolveFolder(path, nodeName, inputName);
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException(nodeName + ": the folder '" + folder + "' does not exist.");
        }

        return Path.GetFullPath(folder);
    }

    private static bool SamePath(string a, string b)
    {
        return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), PathNodes.PathComparison);
    }

    /// <summary>Validates a copy/move destination and creates its folder.</summary>
    private static void PrepareDestination(string destination, string nodeName, bool overwrite)
    {
        if (Directory.Exists(destination))
        {
            throw new ArgumentException(
                nodeName + ": the destination '" + destination + "' is a folder. Give the full destination file path (folder + file name), e.g. with Path.Combine.",
                nameof(destination));
        }

        if (File.Exists(destination) && !overwrite)
        {
            throw new IOException(nodeName + ": the destination file '" + destination + "' already exists. Set 'overwrite' to true to replace it.");
        }

        FileNodes.RequireWritablePath(destination, nodeName, nameof(destination));
    }

    /// <summary>
    /// Moves a file onto a path, replacing a file that is already there. The old file is only given up once the new one can be put in
    /// place: when the source is locked or on another drive and the move fails, the destination is restored and the error is reported.
    /// </summary>
    /// <param name="source">The file to move.</param>
    /// <param name="destination">Where it goes; replaced when it exists.</param>
    internal static void MoveOver(string source, string destination)
    {
        if (!File.Exists(destination))
        {
            File.Move(source, destination);
            return;
        }

        try
        {
            File.Replace(source, destination, null);
            return;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is PlatformNotSupportedException)
        {
            if (!File.Exists(source))
            {
                throw;
            }

            // File.Replace can refuse (another drive, a lock it cannot work around). Park the old file, try the move, put it back if it fails.
        }

        var aside = destination + "." + Guid.NewGuid().ToString("N") + ".old";
        File.Move(destination, aside);
        try
        {
            File.Move(source, destination);
        }
        catch
        {
            try
            {
                if (!File.Exists(destination))
                {
                    File.Move(aside, destination);
                }
            }
            catch (Exception restore) when (restore is IOException || restore is UnauthorizedAccessException)
            {
                throw new IOException("The move failed and the previous file could not be put back; it is kept as '" + aside + "'.", restore);
            }

            throw;
        }

        TryDelete(aside);
    }

    private static HashAlgorithm CreateHasher(string algorithm)
    {
        var key = (algorithm ?? string.Empty).Trim().ToUpperInvariant().Replace("-", string.Empty);
        try
        {
            switch (key)
            {
                case "SHA256":
                    return SHA256.Create();
                case "SHA1":
                    return SHA1.Create();
                case "MD5":
                    return MD5.Create();
            }
        }
        catch (InvalidOperationException ex)
        {
            // Windows in FIPS mode refuses MD5 / SHA1 implementations that are not FIPS validated.
            throw new InvalidOperationException("File.Hash: this computer does not allow the " + key + " algorithm. Choose SHA256. " + ex.Message, ex);
        }

        throw new ArgumentException("File.Hash: unknown algorithm '" + algorithm + "'. Use SHA256, SHA1 or MD5.", nameof(algorithm));
    }

    private static bool EndsWithLineBreak(string path)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            if (stream.Length == 0)
            {
                return true;
            }

            stream.Seek(-1, SeekOrigin.End);
            var last = stream.ReadByte();
            return last == '\n' || last == '\r';
        }
    }

    /// <summary>The log line for a moment, level and message (kept separate so the clock can be fixed in tests).</summary>
    internal static string FormatLogLine(DateTime moment, string level, string message)
    {
        var text = message.Replace("\r\n", " | ").Replace("\n", " | ").Replace("\r", " | ");
        return moment.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + NormalizeLevel(level) + "  " + text;
    }

    private static string NormalizeLevel(string level)
    {
        var key = (level ?? string.Empty).Trim().ToUpperInvariant();
        if (key == "WARNING")
        {
            key = "WARN";
        }

        if (key == "INFO" || key == "WARN" || key == "ERROR" || key == "DEBUG")
        {
            return key;
        }

        throw new ArgumentException("Log.Write: unknown level '" + level + "'. Use INFO, WARN, ERROR or DEBUG.", nameof(level));
    }

    // ----- folder search

    private enum SortKey
    {
        Name,
        Modified,
        Created,
        Size,
    }

    private sealed class SortEntry
    {
        public SortEntry(string path, long value)
        {
            Path = path;
            Value = value;
            Parts = NaturalOrder.Split(path);
        }

        public string Path { get; }

        public long Value { get; }

        public string[] Parts { get; }
    }

    private static bool ParseKind(string? kind)
    {
        switch ((kind ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "":
            case "files":
            case "file":
                return true;
            case "folders":
            case "folder":
            case "directories":
            case "directory":
                return false;
            default:
                throw new ArgumentException("Directory.Find: 'kind' must be files or folders, not '" + kind + "'.", nameof(kind));
        }
    }

    /// <summary>The search behind Directory.Find and the retired Directory.FindFiles / GetFiles / GetDirectories.</summary>
    internal static IList<string> FindCore(
        string nodeName, string path, string pattern, bool wantFiles, bool recursive, string sortBy, bool descending, int limit)
    {
        var root = RequireExistingFolder(path, nodeName);
        if (limit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), nodeName + ": 'limit' must be 0 (all) or a positive number.");
        }

        var key = ParseSortKey(sortBy, nodeName);
        if (key == SortKey.Size && !wantFiles)
        {
            throw new ArgumentException(nodeName + ": folders have no size. Sort folders by name, modified or created.", nameof(sortBy));
        }

        var matcher = new WildcardSet(pattern);
        var found = new List<string>();
        Collect(root, matcher, recursive, wantFiles, isRoot: true, found);

        var entries = new List<SortEntry>(found.Count);
        foreach (var file in found)
        {
            entries.Add(new SortEntry(file, SortValue(file, key)));
        }

        entries.Sort((a, b) =>
        {
            var c = key == SortKey.Name ? 0 : a.Value.CompareTo(b.Value);
            if (c == 0)
            {
                c = NaturalOrder.Compare(a.Parts, b.Parts);
            }

            if (c == 0)
            {
                c = string.CompareOrdinal(a.Path, b.Path);
            }

            return descending ? -c : c;
        });

        var take = limit == 0 ? entries.Count : Math.Min(limit, entries.Count);
        var result = new List<string>(take);
        for (var i = 0; i < take; i++)
        {
            result.Add(entries[i].Path);
        }

        return result;
    }

    private static SortKey ParseSortKey(string sortBy, string nodeName)
    {
        switch ((sortBy ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "name":
                return SortKey.Name;
            case "modified":
                return SortKey.Modified;
            case "created":
                return SortKey.Created;
            case "size":
                return SortKey.Size;
            default:
                throw new ArgumentException(nodeName + ": unknown sortBy '" + sortBy + "'. Use name, modified, created or size.", nameof(sortBy));
        }
    }

    private static long SortValue(string file, SortKey key)
    {
        try
        {
            switch (key)
            {
                case SortKey.Modified:
                    return File.GetLastWriteTimeUtc(file).Ticks;
                case SortKey.Created:
                    return File.GetCreationTimeUtc(file).Ticks;
                case SortKey.Size:
                    return new FileInfo(file).Length;
                default:
                    return 0;
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // The file vanished or is locked between listing and sorting: sort it first rather than fail the search.
            return 0;
        }
    }

    /// <summary>
    /// The order people expect from a file list: case ignored, and a run of digits counts as a number ("c2" before "c10"), compared
    /// folder by folder so "a" comes before "a b". Ties are settled by the caller with an ordinal comparison.
    /// </summary>
    internal static class NaturalOrder
    {
        internal static string[] Split(string path)
        {
            return path.Split(new[] { '\\', '/' });
        }

        internal static int Compare(string[] a, string[] b)
        {
            var count = Math.Min(a.Length, b.Length);
            for (var i = 0; i < count; i++)
            {
                var c = Compare(a[i], b[i]);
                if (c != 0)
                {
                    return c;
                }
            }

            return a.Length.CompareTo(b.Length);
        }

        internal static int Compare(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                var x = a[i];
                var y = b[j];
                if (IsDigit(x) && IsDigit(y))
                {
                    var startA = i;
                    var startB = j;
                    while (i < a.Length && IsDigit(a[i]))
                    {
                        i++;
                    }

                    while (j < b.Length && IsDigit(b[j]))
                    {
                        j++;
                    }

                    var numberA = a.Substring(startA, i - startA).TrimStart('0');
                    var numberB = b.Substring(startB, j - startB).TrimStart('0');
                    if (numberA.Length != numberB.Length)
                    {
                        return numberA.Length < numberB.Length ? -1 : 1;
                    }

                    var c = string.CompareOrdinal(numberA, numberB);
                    if (c != 0)
                    {
                        return c;
                    }

                    continue;
                }

                var upperX = char.ToUpperInvariant(x);
                var upperY = char.ToUpperInvariant(y);
                if (upperX != upperY)
                {
                    return upperX < upperY ? -1 : 1;
                }

                i++;
                j++;
            }

            return (a.Length - i).CompareTo(b.Length - j);
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';
    }

    /// <summary>
    /// Wildcard patterns ("*" any run, "?" one character) matched against a bare file or folder name. Matching is done here
    /// rather than by Directory.GetFiles so that "*.nwd" never also matches ".nwdx" (the old 8.3 rule of .NET Framework)
    /// and so several patterns can be OR-ed in one pass.
    /// </summary>
    private sealed class WildcardSet
    {
        private readonly List<string> _patterns = new List<string>();

        public WildcardSet(string? pattern)
        {
            foreach (var part in (pattern ?? string.Empty).Split(';'))
            {
                var trimmed = part.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                // "*.*" traditionally means "everything"; keep files without an extension in.
                _patterns.Add(trimmed == "*.*" ? "*" : trimmed);
            }

            if (_patterns.Count == 0)
            {
                _patterns.Add("*");
            }
        }

        public bool IsMatch(string name)
        {
            foreach (var pattern in _patterns)
            {
                if (Matches(name, pattern))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Matches(string name, string pattern)
        {
            var ignoreCase = PathNodes.IsWindows;
            int n = 0, p = 0, star = -1, mark = 0;
            while (n < name.Length)
            {
                if (p < pattern.Length && pattern[p] == '*')
                {
                    star = p++;
                    mark = n;
                }
                else if (p < pattern.Length && (pattern[p] == '?' || CharEquals(pattern[p], name[n], ignoreCase)))
                {
                    n++;
                    p++;
                }
                else if (star >= 0)
                {
                    p = star + 1;
                    n = ++mark;
                }
                else
                {
                    return false;
                }
            }

            while (p < pattern.Length && pattern[p] == '*')
            {
                p++;
            }

            return p == pattern.Length;
        }

        private static bool CharEquals(char a, char b, bool ignoreCase)
        {
            return a == b || (ignoreCase && char.ToUpperInvariant(a) == char.ToUpperInvariant(b));
        }
    }

    /// <summary>Collects matching files (or folders) below a directory. Sub-folders that cannot be read are skipped, not fatal.</summary>
    private static void Collect(string directory, WildcardSet matcher, bool recursive, bool wantFiles, bool isRoot, List<string> result)
    {
        string[] entries;
        string[] children;
        try
        {
            entries = wantFiles ? Directory.GetFiles(directory) : Directory.GetDirectories(directory);
            children = recursive ? (wantFiles ? Directory.GetDirectories(directory) : entries) : Array.Empty<string>();
        }
        catch (Exception ex) when (!isRoot && (ex is UnauthorizedAccessException || ex is IOException))
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (matcher.IsMatch(Path.GetFileName(entry)))
            {
                result.Add(entry);
            }
        }

        foreach (var child in children)
        {
            Collect(child, matcher, recursive, wantFiles, isRoot: false, result);
        }
    }

    // ----- folder copy / move

    private static bool IsInside(string candidate, string folder)
    {
        var inner = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var outer = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return inner.StartsWith(outer + Path.DirectorySeparatorChar, PathNodes.PathComparison);
    }

    private static void RequireNotInside(string destination, string source, string nodeName)
    {
        if (IsInside(destination, source))
        {
            throw new ArgumentException(
                nodeName + ": the destination '" + destination + "' is inside the source folder '" + source + "', so the copy would never end. Choose a destination outside the source.",
                "destination");
        }
    }

    /// <summary>Copies a folder tree. Every clash is found before the first file is written.</summary>
    private static void CopyTree(string source, string destination, bool overwrite, string nodeName)
    {
        if (File.Exists(destination))
        {
            throw new IOException(nodeName + ": '" + destination + "' is a file, not a folder. Give the folder to copy into.");
        }

        var sourceRoot = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var plan = new List<KeyValuePair<string, string>>();
        foreach (var file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, file.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (Directory.Exists(target))
            {
                throw new IOException(nodeName + ": '" + target + "' is a folder in the destination, so the file '" + file + "' cannot be copied there.");
            }

            if (File.Exists(target) && !overwrite)
            {
                throw new IOException(nodeName + ": the file '" + target + "' already exists in the destination. Set 'overwrite' to true to replace files that are already there. Nothing was copied.");
            }

            plan.Add(new KeyValuePair<string, string>(file, target));
        }

        Directory.CreateDirectory(destination);
        foreach (var folder in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, folder.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        }

        foreach (var step in plan)
        {
            File.Copy(step.Key, step.Value, overwrite);
        }
    }

    private static void MoveTree(string source, string destination, bool overwrite)
    {
        if (File.Exists(destination))
        {
            throw new IOException("Directory.Move: '" + destination + "' is a file, not a folder.");
        }

        if (Directory.Exists(destination))
        {
            if (!overwrite)
            {
                throw new IOException("Directory.Move: the folder '" + destination + "' already exists. Set 'overwrite' to true to move the content into it (files with the same name are replaced).");
            }

            CopyTree(source, destination, true, "Directory.Move");
            Directory.Delete(source, true);
            return;
        }

        var parent = Path.GetDirectoryName(destination.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var sameDrive = string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination), PathNodes.PathComparison);
        if (sameDrive)
        {
            Directory.Move(source, destination);
            return;
        }

        // A folder cannot be moved to another drive in one step: copy it, then remove the original.
        CopyTree(source, destination, true, "Directory.Move");
        try
        {
            Directory.Delete(source, true);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            throw new IOException("Directory.Move: the folder was copied to '" + destination + "' but the original '" + source + "' could not be removed (" + ex.Message + "). Remove it by hand.", ex);
        }
    }

    // ----- zip

    private sealed class ZipItem
    {
        public ZipItem(string fullPath, string entryName, bool isDirectory)
        {
            FullPath = fullPath;
            EntryName = entryName;
            IsDirectory = isDirectory;
        }

        public string FullPath { get; }

        public string EntryName { get; }

        public bool IsDirectory { get; }
    }

    /// <summary>The leaves of a list of lists, in order (a text is a leaf, a null is a leaf the caller reports).</summary>
    internal static IEnumerable<object?> Flatten(IEnumerable sources)
    {
        foreach (var item in sources)
        {
            if (item is IEnumerable inner && !(item is string) && !(item is IDictionary))
            {
                foreach (var leaf in Flatten(inner))
                {
                    yield return leaf;
                }
            }
            else
            {
                yield return item;
            }
        }
    }

    private static void AddZipItem(List<ZipItem> items, HashSet<string> names, ZipItem item, string zipFull)
    {
        if (!item.IsDirectory && string.Equals(item.FullPath, zipFull, PathNodes.PathComparison))
        {
            // The archive being written sits inside a packed folder: never pack the zip into itself.
            return;
        }

        if (!names.Add(item.EntryName))
        {
            throw new ArgumentException(
                "Zip.Create: two sources would both be stored as '" + item.EntryName + "'. Rename one or pack them separately.",
                "sources");
        }

        items.Add(item);
    }

    private static void CollectZipItems(string directory, string entryPrefix, List<ZipItem> items, HashSet<string> names, string zipFull)
    {
        var files = Directory.GetFiles(directory);
        Array.Sort(files, StringComparer.Ordinal);
        foreach (var file in files)
        {
            AddZipItem(items, names, new ZipItem(file, entryPrefix + "/" + Path.GetFileName(file), isDirectory: false), zipFull);
        }

        var folders = Directory.GetDirectories(directory);
        Array.Sort(folders, StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            var name = entryPrefix + "/" + Path.GetFileName(folder);
            AddZipItem(items, names, new ZipItem(folder, name + "/", isDirectory: true), zipFull);
            CollectZipItems(folder, name, items, names, zipFull);
        }
    }

    private static void TrySetEntryTime(ZipArchiveEntry entry, DateTime local)
    {
        try
        {
            entry.LastWriteTime = new DateTimeOffset(local);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Zip stores dates between 1980 and 2107 only; keep the default stamp for anything else.
        }
    }

    private static ZipArchive OpenZip(string zipPath, string nodeName)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException(nodeName + ": cannot read '" + zipPath + "'. " + ex.Message, ex);
        }

        try
        {
            return new ZipArchive(stream, ZipArchiveMode.Read);
        }
        catch (InvalidDataException ex)
        {
            stream.Dispose();
            throw new InvalidDataException(nodeName + ": '" + zipPath + "' is not a valid zip file. " + ex.Message, ex);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Deletes a file and says nothing when that is not possible (cleanup of a temporary file).</summary>
    internal static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // best-effort cleanup of the temp file
        }
    }
}
