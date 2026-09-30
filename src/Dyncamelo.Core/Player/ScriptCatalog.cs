using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Dyncamelo.Core.Player;

/// <summary>A script file the Player lists: its place and name, without having opened it.</summary>
public sealed class ScriptEntry
{
    /// <summary>Creates an entry.</summary>
    public ScriptEntry(string path, string name, string folder, DateTime lastWriteUtc)
    {
        Path = path;
        Name = name;
        Folder = folder;
        LastWriteUtc = lastWriteUtc;
    }

    /// <summary>Full path of the .dyc file.</summary>
    public string Path { get; }

    /// <summary>File name without the extension.</summary>
    public string Name { get; }

    /// <summary>Where the script sits: the name of its scripts folder, followed by any sub-folders ("Scripts ▸ Clash").</summary>
    public string Folder { get; }

    /// <summary>When the file was last changed (UTC).</summary>
    public DateTime LastWriteUtc { get; }
}

/// <summary>Finds the scripts in the folders the Player was pointed at.</summary>
public static class ScriptCatalog
{
    /// <summary>Most scripts listed; a folder with more than this is almost certainly not a scripts folder.</summary>
    public const int MaxScripts = 2000;

    /// <summary>How many folder levels below a scripts folder are searched.</summary>
    public const int MaxDepth = 4;

    /// <summary>
    /// Lists the .dyc files in the folders and their sub-folders, folder by folder and by name. A folder that does not exist
    /// or cannot be read is skipped. The same file found through two folders is listed once.
    /// </summary>
    /// <param name="folders">The scripts folders.</param>
    public static IReadOnlyList<ScriptEntry> Scan(IEnumerable<string> folders)
    {
        if (folders == null)
        {
            throw new ArgumentNullException(nameof(folders));
        }

        var found = new List<ScriptEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in folders.Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            string fullRoot;
            try
            {
                fullRoot = System.IO.Path.GetFullPath(root.Trim());
            }
            catch (Exception)
            {
                continue;
            }

            Walk(fullRoot, fullRoot, 0, found, seen);
            if (found.Count >= MaxScripts)
            {
                break;
            }
        }

        return found
            .OrderBy(e => e.Folder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void Walk(string root, string directory, int depth, List<ScriptEntry> found, HashSet<string> seen)
    {
        string[] files;
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            files = Directory.GetFiles(directory, "*.dyc");
        }
        catch (Exception)
        {
            return;
        }

        var label = Label(root, directory);
        foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(file);
            if (name.StartsWith("~", StringComparison.Ordinal) || name.StartsWith(".", StringComparison.Ordinal) || !seen.Add(file))
            {
                continue;
            }

            DateTime written;
            try
            {
                written = File.GetLastWriteTimeUtc(file);
            }
            catch (Exception)
            {
                written = DateTime.MinValue;
            }

            found.Add(new ScriptEntry(file, name, label, written));
            if (found.Count >= MaxScripts)
            {
                return;
            }
        }

        if (depth >= MaxDepth)
        {
            return;
        }

        string[] subfolders;
        try
        {
            subfolders = Directory.GetDirectories(directory);
        }
        catch (Exception)
        {
            return;
        }

        foreach (var sub in subfolders.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var leaf = System.IO.Path.GetFileName(sub);
            if (leaf.StartsWith(".", StringComparison.Ordinal) || leaf.StartsWith("~", StringComparison.Ordinal))
            {
                continue;
            }

            Walk(root, sub, depth + 1, found, seen);
            if (found.Count >= MaxScripts)
            {
                return;
            }
        }
    }

    // "Scripts" for the folder itself, "Scripts ▸ Clash ▸ Weekly" below it.
    private static string Label(string root, string directory)
    {
        var rootName = System.IO.Path.GetFileName(root.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        if (rootName.Length == 0)
        {
            rootName = root;
        }

        if (string.Equals(root, directory, StringComparison.OrdinalIgnoreCase))
        {
            return rootName;
        }

        var relative = directory.Substring(root.Length).Trim(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        return rootName + " ▸ " + string.Join(" ▸ ", relative.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
    }
}
