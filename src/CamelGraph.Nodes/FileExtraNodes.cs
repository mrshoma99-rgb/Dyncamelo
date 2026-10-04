using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes;

/// <summary>
/// File-system nodes that go beyond reading and writing one file: file facts and hashes, copy / move / delete,
/// folder listing and search, appending writers (text, CSV, log lines) and zip packaging. Reads are Info nodes;
/// everything that writes, copies, moves, deletes or extracts is a Modify node (the Script Player asks before
/// running those) and returns the resulting path so it can sequence further nodes.
/// </summary>
[NodeCategory("File")]
public static class FileExtraNodes
{
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

    // ------------------------------------------------------------------ File

    /// <summary>Reads the basic facts of a file without opening it. A missing file is not an error: "exists" is false and the rest is null, so a script can branch on it.</summary>
    /// <param name="path">The file to inspect.</param>
    /// <returns>Dictionary with "exists", "name", "extension", "directory", "sizeBytes", "modified" and "created".</returns>
    [NodeName("File.Info")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [MultiReturn("exists", "name", "extension", "directory", "sizeBytes", "modified", "created")]
    [PortKinds("boolean", "text", "text", "file", "number", "datetime", "datetime")]
    [NodeDescription("Reads a file's name, extension, folder, size in bytes and modified / created dates; a missing file gives exists = false and empty values instead of an error.")]
    [NodeSearchTags("size", "date", "modified", "created", "properties", "stat", "exists", "attributes")]
    public static Dictionary<string, object?> GetFileInfo(string path)
    {
        PathNodes.RequireText(path, "File.Info", nameof(path), "a file path");
        var info = new FileInfo(path);
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
    /// <param name="path">The file to hash.</param>
    /// <param name="algorithm">SHA256 (default), SHA1 or MD5, in any case.</param>
    /// <returns>The hash as lower-case hexadecimal text.</returns>
    [NodeName("File.Hash")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("hash")]
    [NodeDescription("Computes a checksum of a file's content (SHA256, SHA1 or MD5) as lower-case hex text - handy for change detection.")]
    [NodeSearchTags("checksum", "sha", "md5", "digest", "change", "compare", "fingerprint")]
    public static string GetFileHash(string path, [NodeChoices("SHA256", "SHA1", "MD5")] string algorithm = "SHA256")
    {
        FileNodes.RequireExistingFile(path, "File.Hash");
        using (var hasher = CreateHasher(algorithm))
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
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
    /// <param name="source">The file to copy.</param>
    /// <param name="destination">The full destination file path (not just a folder).</param>
    /// <param name="overwrite">True to replace an existing destination file; false (default) to fail instead.</param>
    /// <returns>The destination path, for sequencing further file nodes.</returns>
    [NodeName("File.Copy")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesFiles)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("path")]
    [NodeDescription("Copies a file to a new path (creates the destination folder; refuses to replace an existing file unless overwrite is true).")]
    [NodeSearchTags("duplicate", "backup", "clone", "file", "copy")]
    public static string CopyFile(string source, string destination, bool overwrite = false)
    {
        RequireFile(source, "File.Copy", nameof(source));
        PathNodes.RequireText(destination, "File.Copy", nameof(destination), "a destination file path");
        if (SamePath(source, destination))
        {
            return destination;
        }

        PrepareDestination(destination, "File.Copy", overwrite);
        File.Copy(source, destination, overwrite);
        return destination;
    }

    /// <summary>Moves (or renames) a file, creating the destination folder when needed.</summary>
    /// <param name="source">The file to move.</param>
    /// <param name="destination">The full destination file path (not just a folder).</param>
    /// <param name="overwrite">True to replace an existing destination file; false (default) to fail instead.</param>
    /// <returns>The destination path, for sequencing further file nodes.</returns>
    [NodeName("File.Move")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesFiles)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("path")]
    [NodeDescription("Moves or renames a file (creates the destination folder; refuses to replace an existing file unless overwrite is true).")]
    [NodeSearchTags("rename", "relocate", "archive", "file", "move")]
    public static string MoveFile(string source, string destination, bool overwrite = false)
    {
        RequireFile(source, "File.Move", nameof(source));
        PathNodes.RequireText(destination, "File.Move", nameof(destination), "a destination file path");
        if (SamePath(source, destination))
        {
            return destination;
        }

        PrepareDestination(destination, "File.Move", overwrite);
        if (File.Exists(destination))
        {
            // netstandard2.0 has no File.Move(source, destination, overwrite).
            File.Delete(destination);
        }

        File.Move(source, destination);
        return destination;
    }

    /// <summary>Deletes a file. A file that is already gone is not an error.</summary>
    /// <param name="path">The file to delete.</param>
    /// <returns>True when a file was deleted; false when there was no such file.</returns>
    [NodeName("File.Delete")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesFiles)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("deleted")]
    [NodeDescription("Deletes a file; returns true when it was deleted and false when it did not exist.")]
    [NodeSearchTags("remove", "erase", "clean", "file", "delete")]
    public static bool DeleteFile(string path)
    {
        PathNodes.RequireText(path, "File.Delete", nameof(path), "a file path");
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    // ------------------------------------------------------------- Directory

    /// <summary>Tests whether a folder exists.</summary>
    /// <param name="path">The folder path to test.</param>
    /// <returns>True when a folder exists at the path (false for files and missing paths).</returns>
    [NodeName("Directory.Exists")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("exists")]
    [NodeDescription("Tests whether a folder exists at the given path.")]
    [NodeSearchTags("folder", "check", "found", "present", "directory")]
    public static bool DirectoryExists(string path)
    {
        RequireFolderText(path, "Directory.Exists");
        return Directory.Exists(path);
    }

    /// <summary>Creates a folder (and its parents). A folder that already exists is not an error.</summary>
    /// <param name="path">The folder to create.</param>
    /// <returns>The folder path, for sequencing further file nodes.</returns>
    [NodeName("Directory.Create")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("path")]
    [NodeDescription("Creates a folder including any missing parent folders (does nothing when it already exists).")]
    [NodeSearchTags("mkdir", "make", "folder", "new", "directory")]
    public static string CreateDirectory(string path)
    {
        RequireFolderText(path, "Directory.Create");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Lists the sub-folders of a folder, optionally filtered by a wildcard and optionally at every depth.</summary>
    /// <param name="path">The folder to list.</param>
    /// <param name="pattern">Wildcard filter on the folder name ("*" and "?"); several patterns can be separated by ";".</param>
    /// <param name="recursive">True to include sub-folders of sub-folders.</param>
    /// <returns>The full folder paths, sorted alphabetically.</returns>
    [NodeName("Directory.GetDirectories")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("directories")]
    [NodeDescription("Lists the sub-folders of a folder as full paths (optionally filtered by a wildcard such as \"2026*\" and optionally at every depth).")]
    [NodeSearchTags("folders", "subfolders", "list", "browse", "directory", "tree")]
    public static IList<string> GetDirectories(string path, string pattern = "*", bool recursive = false)
    {
        var root = RequireExistingFolder(path, "Directory.GetDirectories");
        var matcher = new WildcardSet(pattern);
        var found = new List<string>();
        Collect(root, matcher, recursive, wantFiles: false, isRoot: true, found);
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <summary>Finds files under a folder, with optional sorting by name / date / size and a limit - e.g. the newest file of a type.</summary>
    /// <param name="path">The folder to search.</param>
    /// <param name="pattern">Wildcard filter on the file name ("*" and "?"); several patterns can be separated by ";" (e.g. "*.nwd;*.nwf").</param>
    /// <param name="recursive">True (default) to search sub-folders too.</param>
    /// <param name="sortBy">name (full path, alphabetical), modified, created or size.</param>
    /// <param name="descending">True to reverse the order (newest / largest first).</param>
    /// <param name="limit">Keep only the first N files after sorting; 0 keeps all.</param>
    /// <returns>The full file paths in the requested order.</returns>
    [NodeName("Directory.FindFiles")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("files")]
    [NodeDescription("Finds files under a folder by wildcard (several allowed, separated by \";\"), sorted by name, date or size, with an optional limit - the newest file is sortBy modified + descending + limit 1.")]
    [NodeSearchTags("search", "find", "newest", "latest", "recursive", "list", "wildcard", "files", "directory")]
    public static IList<string> FindFiles(
        string path,
        string pattern = "*",
        bool recursive = true,
        [NodeChoices("name", "modified", "created", "size")] string sortBy = "name",
        bool descending = false,
        [NodeRange(0, 100000)] int limit = 0)
    {
        var root = RequireExistingFolder(path, "Directory.FindFiles");
        if (limit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Directory.FindFiles: 'limit' must be 0 (all files) or a positive number.");
        }

        var key = ParseSortKey(sortBy);
        var matcher = new WildcardSet(pattern);
        var found = new List<string>();
        Collect(root, matcher, recursive, wantFiles: true, isRoot: true, found);

        var entries = new List<SortEntry>(found.Count);
        foreach (var file in found)
        {
            entries.Add(new SortEntry(file, SortValue(file, key)));
        }

        entries.Sort((a, b) =>
        {
            var c = a.Value.CompareTo(b.Value);
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

    /// <summary>Deletes a folder. A folder that is already gone is not an error; a non-empty folder needs recursive = true.</summary>
    /// <param name="path">The folder to delete.</param>
    /// <param name="recursive">True to delete the folder together with everything inside it.</param>
    /// <returns>True when a folder was deleted; false when there was no such folder.</returns>
    [NodeName("Directory.Delete")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesFiles)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("deleted")]
    [NodeDescription("Deletes a folder (an empty one, or with its whole content when recursive is true); returns false when it did not exist.")]
    [NodeSearchTags("remove", "rmdir", "erase", "clean", "folder", "directory")]
    public static bool DeleteDirectory(string path, bool recursive = false)
    {
        RequireFolderText(path, "Directory.Delete");
        if (!Directory.Exists(path))
        {
            return false;
        }

        var full = Path.GetFullPath(path);
        if (string.Equals(Path.GetPathRoot(full), full, PathNodes.PathComparison))
        {
            throw new InvalidOperationException("Directory.Delete refuses to delete the drive root '" + path + "'. Give the folder you really want to delete.");
        }

        if (!recursive)
        {
            using (var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator())
            {
                if (entries.MoveNext())
                {
                    throw new IOException("Directory.Delete: the folder '" + path + "' is not empty. Set 'recursive' to true to delete it together with its content.");
                }
            }
        }

        Directory.Delete(path, recursive);
        return true;
    }

    // ------------------------------------------------------------- Appending

    /// <summary>Appends text to the end of a file (UTF-8, no byte-order mark), creating the file and its folder when needed.</summary>
    /// <param name="path">The file to append to.</param>
    /// <param name="text">The text to append.</param>
    /// <param name="newLine">True (default) to end the appended text with a line break.</param>
    /// <returns>The path that was written, for sequencing further file nodes.</returns>
    [NodeName("Text.AppendToFile")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("path")]
    [NodeDescription("Appends text (plus a line break by default) to the end of a text file, creating the file and folder if needed.")]
    [NodeSearchTags("append", "add", "log", "write", "txt", "export")]
    public static string AppendText(string path, string text, bool newLine = true)
    {
        FileNodes.RequireWritablePath(path, "Text.AppendToFile");
        AppendUtf8(path, (text ?? string.Empty) + (newLine ? Environment.NewLine : string.Empty));
        return path;
    }

    /// <summary>
    /// Appends rows to a CSV file with the same quoting and number formatting as CSV.WriteToFile. Headers (when given)
    /// are written only if the file does not exist yet or is empty, so a loop can keep appending to one report.
    /// </summary>
    /// <param name="path">The CSV file to append to.</param>
    /// <param name="rows">List of rows (each row a list of cell values; a scalar becomes a one-cell row).</param>
    /// <param name="delimiter">Single-character cell delimiter.</param>
    /// <param name="headers">Optional header cells, written once when the file is new or empty.</param>
    /// <returns>The path that was written, for sequencing further file nodes.</returns>
    [NodeName("CSV.AppendToFile")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("path")]
    [NodeDescription("Appends rows to a CSV file (same quoting as CSV.WriteToFile), writing the optional headers only when the file is new or empty.")]
    [NodeSearchTags("csv", "append", "add rows", "table", "report", "export")]
    public static string AppendCsv(string path, IList<object?> rows, string delimiter = ",", IList<object?>? headers = null)
    {
        if (rows == null)
        {
            throw new ArgumentNullException(nameof(rows), "CSV.AppendToFile requires a list of rows (each row a list of cells).");
        }

        var separator = FileNodes.RequireSingleCharDelimiter(delimiter, "CSV.AppendToFile");
        FileNodes.RequireWritablePath(path, "CSV.AppendToFile");
        var builder = new StringBuilder();
        var info = new FileInfo(path);
        var isNewOrEmpty = !info.Exists || info.Length == 0;
        if (!isNewOrEmpty && !EndsWithLineBreak(path))
        {
            builder.Append('\n');
        }

        if (isNewOrEmpty && headers != null && headers.Count > 0)
        {
            AppendCsvRow(builder, headers, separator);
        }

        foreach (var row in rows)
        {
            AppendCsvRow(builder, row, separator);
        }

        AppendUtf8(path, builder.ToString());
        return path;
    }

    /// <summary>Appends one timestamped line to a log file (UTF-8), creating the file and folder when needed.</summary>
    /// <param name="path">The log file.</param>
    /// <param name="message">The text to log; line breaks are replaced by " | " so an entry stays on one line.</param>
    /// <param name="level">INFO (default), WARN, ERROR or DEBUG, in any case.</param>
    /// <returns>The line that was written, e.g. "2026-10-01 10:20:35  INFO  message".</returns>
    [NodeName("Log.Write")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("line")]
    [NodeDescription("Appends one line \"yyyy-MM-dd HH:mm:ss  LEVEL  message\" (local time) to a log file and returns that line.")]
    [NodeSearchTags("log", "logging", "audit trail", "timestamp", "journal", "append", "report")]
    public static string WriteLog(string path, string message, [NodeChoices("INFO", "WARN", "ERROR", "DEBUG")] string level = "INFO")
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message), "Log.Write requires a message. Wire text into the 'message' input.");
        }

        var line = FormatLogLine(DateTime.Now, level, message);
        FileNodes.RequireWritablePath(path, "Log.Write");
        var info = new FileInfo(path);
        var prefix = info.Exists && info.Length > 0 && !EndsWithLineBreak(path) ? Environment.NewLine : string.Empty;
        AppendUtf8(path, prefix + line + Environment.NewLine);
        return line;
    }

    // ------------------------------------------------------------------- Zip

    /// <summary>Packs files and folders into a zip archive. Folders keep their structure under their own name.</summary>
    /// <param name="sources">The files and/or folders to pack (a list of paths).</param>
    /// <param name="zipPath">The zip file to create (its folder is created when needed).</param>
    /// <param name="overwrite">True to replace an existing zip file; false (default) to fail instead.</param>
    /// <returns>The zip path, for sequencing further file nodes.</returns>
    [NodeName("Zip.Create")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("path")]
    [NodeDescription("Packs files and folders into a zip archive (a folder is stored with its structure under its own name); a missing source is an error.")]
    [NodeSearchTags("zip", "compress", "archive", "package", "deliverable", "pack", "bundle")]
    public static string CreateZip(IList<object?> sources, string zipPath, bool overwrite = false)
    {
        PathNodes.RequireText(zipPath, "Zip.Create", nameof(zipPath), "the path of the zip file to create");
        if (sources == null)
        {
            throw new ArgumentNullException(nameof(sources), "Zip.Create requires a list of files and/or folders to pack. Wire a list of paths into the 'sources' input.");
        }

        var zipFull = Path.GetFullPath(zipPath);
        var items = new List<ZipItem>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var source in sources)
        {
            index++;
            var text = source as string;
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException(
                    "Zip.Create: source #" + index.ToString(CultureInfo.InvariantCulture) + " is not a path. Every element of 'sources' must be the path of a file or folder.",
                    nameof(sources));
            }

            var full = Path.GetFullPath(text);
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

        FileNodes.RequireWritablePath(zipPath, "Zip.Create");

        // Write beside the target first, so a failure never leaves a half-written (or destroyed) zip behind.
        var temp = zipFull + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
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

            if (File.Exists(zipFull))
            {
                File.Delete(zipFull);
            }

            File.Move(temp, zipFull);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        return zipPath;
    }

    /// <summary>Unpacks a zip archive into a folder. Entries that would land outside the folder ("zip slip") are refused.</summary>
    /// <param name="zipPath">The zip file to unpack.</param>
    /// <param name="directory">The destination folder (created when needed).</param>
    /// <param name="overwrite">True to replace files that already exist; false (default) to fail instead.</param>
    /// <returns>The destination folder, for sequencing further file nodes.</returns>
    [NodeName("Zip.Extract")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesFiles)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [return: NodeName("directory")]
    [NodeDescription("Unpacks a zip archive into a folder; refuses entries that would escape the folder and files that already exist unless overwrite is true.")]
    [NodeSearchTags("unzip", "extract", "decompress", "unpack", "archive", "zip")]
    public static string ExtractZip(string zipPath, string directory, bool overwrite = false)
    {
        RequireFile(zipPath, "Zip.Extract", nameof(zipPath));
        PathNodes.RequireText(directory, "Zip.Extract", nameof(directory), "a destination folder");

        var targetFull = Path.GetFullPath(directory);
        var root = targetFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (root.Length == 0)
        {
            root = targetFull;
        }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? root
            : root + Path.DirectorySeparatorChar;

        using (var archive = OpenZip(zipPath, "Zip.Extract"))
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

            Directory.CreateDirectory(targetFull);
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

                using (var source = entry.Open())
                using (var target = new FileStream(destination, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    source.CopyTo(target);
                }

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

        return directory;
    }

    /// <summary>Lists the entry names stored in a zip archive without extracting anything.</summary>
    /// <param name="zipPath">The zip file to read.</param>
    /// <returns>The entry names as stored (folders end with "/").</returns>
    [NodeName("Zip.List")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("entries")]
    [NodeDescription("Lists the entry names inside a zip archive without extracting it.")]
    [NodeSearchTags("zip", "contents", "list", "inspect", "archive", "entries")]
    public static IList<string> ListZip(string zipPath)
    {
        RequireFile(zipPath, "Zip.List", nameof(zipPath));
        using (var archive = OpenZip(zipPath, "Zip.List"))
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

    /// <summary>Like FileNodes.RequireExistingFile, but names the input that is actually wired (e.g. 'source', 'zipPath').</summary>
    internal static void RequireFile(string path, string nodeName, string inputName)
    {
        PathNodes.RequireText(path, nodeName, inputName, "a file path");
        FileNodes.RequireExistingFile(path, nodeName);
    }

    private static void RequireFolderText(string? path, string nodeName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(nodeName + " requires a folder path. Wire a Directory Path node into the 'path' input.", nameof(path));
        }
    }

    private static string RequireExistingFolder(string? path, string nodeName)
    {
        RequireFolderText(path, nodeName);
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(nodeName + ": the folder '" + path + "' does not exist.");
        }

        return Path.GetFullPath(path!);
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

        FileNodes.RequireWritablePath(destination, nodeName);
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

    private static void AppendUtf8(string path, string text)
    {
        var bytes = Utf8NoBom.GetBytes(text);
        using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            stream.Write(bytes, 0, bytes.Length);
        }
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

    /// <summary>One CSV row, quoted exactly like CSV.WriteToFile (same cell text and escaping).</summary>
    private static void AppendCsvRow(StringBuilder builder, object? row, char separator)
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

            builder.Append(FileNodes.EscapeCsvCell(CellText.Format(cell), separator));
            first = false;
        }

        builder.Append('\n');
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
        }

        public string Path { get; }

        public long Value { get; }
    }

    private static SortKey ParseSortKey(string sortBy)
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
                throw new ArgumentException("Directory.FindFiles: unknown sortBy '" + sortBy + "'. Use name, modified, created or size.", nameof(sortBy));
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

    private static void TryDelete(string path)
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
