using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes;

/// <summary>
/// Path nodes: split, change, resolve and compare file-system paths as TEXT.
/// None of them touches the disk (the file or folder does not have to exist);
/// they only rewrite strings, so they are safe to run anywhere in a script.
/// </summary>
[NodeCategory("File")]
public static class PathNodes
{
    /// <summary>True on Windows (backslash separators, case-insensitive paths).</summary>
    internal static bool IsWindows => Path.DirectorySeparatorChar == '\\';

    /// <summary>How two paths are compared for equality on this operating system.</summary>
    internal static StringComparison PathComparison => IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Returns the file name of a path, including its extension.</summary>
    /// <param name="path">The path to split, e.g. "C:\Models\site.nwd".</param>
    /// <returns>The last part of the path ("site.nwd"); empty when the path ends with a separator.</returns>
    [NodeName("Path.GetFileName")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Create)]
    [return: NodeName("fileName")]
    [NodeDescription("Returns the file name of a path including its extension (\"C:\\Models\\site.nwd\" gives \"site.nwd\").")]
    [NodeSearchTags("filename", "basename", "name", "split", "path")]
    public static string GetFileName(string path)
    {
        return Guarded("Path.GetFileName", path, Path.GetFileName);
    }

    /// <summary>Returns the file name of a path without its extension.</summary>
    /// <param name="path">The path to split, e.g. "C:\Models\site.nwd".</param>
    /// <returns>The file name without the last extension ("site").</returns>
    [NodeName("Path.GetFileNameWithoutExtension")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Create)]
    [return: NodeName("name")]
    [NodeDescription("Returns the file name of a path without its extension (\"C:\\Models\\site.nwd\" gives \"site\").")]
    [NodeSearchTags("filename", "basename", "stem", "name", "split", "path")]
    public static string GetFileNameWithoutExtension(string path)
    {
        return Guarded("Path.GetFileNameWithoutExtension", path, Path.GetFileNameWithoutExtension);
    }

    /// <summary>Returns the extension of a path, including the leading dot.</summary>
    /// <param name="path">The path to inspect.</param>
    /// <returns>The extension with its dot (".nwd"); empty when the file name has none.</returns>
    [NodeName("Path.GetExtension")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Create)]
    [return: NodeName("extension")]
    [NodeDescription("Returns the extension of a path with its leading dot (\".nwd\"), or empty text when there is none.")]
    [NodeSearchTags("extension", "suffix", "type", "ext", "path")]
    public static string GetExtension(string path)
    {
        return Guarded("Path.GetExtension", path, Path.GetExtension);
    }

    /// <summary>Returns the folder part of a path.</summary>
    /// <param name="path">The path to split.</param>
    /// <returns>The path without its last part; empty for a drive/root or a bare file name.</returns>
    [NodeName("Path.GetDirectory")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Create)]
    [return: NodeName("directory")]
    [NodeDescription("Returns the folder part of a path (\"C:\\Models\\site.nwd\" gives \"C:\\Models\"); empty text for a root or a bare file name.")]
    [NodeSearchTags("folder", "parent", "dirname", "directory", "path")]
    public static string GetDirectory(string path)
    {
        return Guarded("Path.GetDirectory", path, p => Path.GetDirectoryName(p) ?? string.Empty);
    }

    /// <summary>Replaces (or removes) the extension of a path.</summary>
    /// <param name="path">The path to change.</param>
    /// <param name="extension">The new extension, with or without the leading dot; empty text removes the extension.</param>
    /// <returns>The path with the new extension.</returns>
    [NodeName("Path.ChangeExtension")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Create)]
    [return: NodeName("path")]
    [NodeDescription("Replaces the extension of a path (\"a.nwd\" + \"nwf\" gives \"a.nwf\"); empty text removes the extension.")]
    [NodeSearchTags("extension", "rename", "replace", "suffix", "convert", "path")]
    public static string ChangeExtension(string path, string extension)
    {
        if (extension == null)
        {
            throw new ArgumentNullException(
                nameof(extension),
                "Path.ChangeExtension requires the new extension (e.g. \".nwf\"; empty text removes it). Wire text into the 'extension' input.");
        }

        var bare = extension.Trim();
        if (bare.StartsWith(".", StringComparison.Ordinal))
        {
            bare = bare.Substring(1);
        }

        // Path.ChangeExtension(path, "") leaves a dangling dot ("a."); null is what removes the extension.
        var replacement = bare.Length == 0 ? null : "." + bare;
        return Guarded("Path.ChangeExtension", path, p => Path.ChangeExtension(p, replacement));
    }

    /// <summary>Resolves a path to an absolute path, using the current folder for relative paths.</summary>
    /// <param name="path">The (possibly relative) path.</param>
    /// <returns>The absolute path with "." and ".." resolved.</returns>
    [NodeName("Path.GetFullPath")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Create)]
    [return: NodeName("path")]
    [NodeDescription("Resolves a path to an absolute path (relative paths start from the current folder); the file does not have to exist.")]
    [NodeSearchTags("absolute", "resolve", "full", "expand", "path")]
    public static string GetFullPath(string path)
    {
        return Guarded("Path.GetFullPath", path, Path.GetFullPath);
    }

    /// <summary>Expresses a path relative to a base folder.</summary>
    /// <param name="path">The path to express (absolute, or relative to the current folder).</param>
    /// <param name="baseDirectory">The folder the result is relative to.</param>
    /// <returns>The relative path ("..\other\a.nwd"), "." when both are the same folder, or the full path when they have different roots (e.g. different drives).</returns>
    [NodeName("Path.GetRelativePath")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Create)]
    [return: NodeName("path")]
    [NodeDescription("Expresses a path relative to a base folder; returns the full path when they are on different drives.")]
    [NodeSearchTags("relative", "base", "make relative", "path")]
    public static string GetRelativePath(string path, string baseDirectory)
    {
        RequireText(path, "Path.GetRelativePath", nameof(path), "a path");
        RequireText(baseDirectory, "Path.GetRelativePath", nameof(baseDirectory), "a base folder");

        string fullPath;
        string fullBase;
        try
        {
            fullPath = Path.GetFullPath(path);
            fullBase = Path.GetFullPath(baseDirectory);
        }
        catch (Exception ex) when (IsInvalidPath(ex))
        {
            throw new ArgumentException("Path.GetRelativePath: '" + path + "' or '" + baseDirectory + "' is not a valid path. " + ex.Message, nameof(path), ex);
        }

        var pathRoot = Path.GetPathRoot(fullPath) ?? string.Empty;
        var baseRoot = Path.GetPathRoot(fullBase) ?? string.Empty;
        if (!string.Equals(pathRoot, baseRoot, PathComparison))
        {
            return fullPath;
        }

        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var target = fullPath.Substring(pathRoot.Length).Split(separators, StringSplitOptions.RemoveEmptyEntries);
        var from = fullBase.Substring(baseRoot.Length).Split(separators, StringSplitOptions.RemoveEmptyEntries);

        var common = 0;
        while (common < target.Length && common < from.Length && string.Equals(target[common], from[common], PathComparison))
        {
            common++;
        }

        var parts = new List<string>();
        for (var i = common; i < from.Length; i++)
        {
            parts.Add("..");
        }

        for (var i = common; i < target.Length; i++)
        {
            parts.Add(target[i]);
        }

        return parts.Count == 0 ? "." : string.Join(Path.DirectorySeparatorChar.ToString(), parts);
    }

    /// <summary>Tests whether a path is absolute (it starts at a drive or share root, not at the current folder).</summary>
    /// <param name="path">The path to test.</param>
    /// <returns>True for "C:\a", "\\server\share" and "/home/a"; false for "a\b", "..\a" and drive-relative "C:a".</returns>
    [NodeName("Path.IsAbsolute")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("isAbsolute")]
    [NodeDescription("Tests whether a path is absolute (starts at a drive, network share or root) rather than relative.")]
    [NodeSearchTags("absolute", "relative", "rooted", "check", "path")]
    public static bool IsAbsolute(string path)
    {
        RequireText(path, "Path.IsAbsolute", nameof(path), "a path");
        if (IsWindows)
        {
            return (path.Length >= 3 && IsAsciiLetter(path[0]) && path[1] == ':' && IsSeparator(path[2])) ||
                   (path.Length >= 2 && IsSeparator(path[0]) && IsSeparator(path[1]));
        }

        return path[0] == '/';
    }

    /// <summary>
    /// Cleans a path as text: both slash kinds become the separator of this operating system, repeated and trailing
    /// separators go, "." parts are dropped and ".." parts are resolved. The disk is never consulted.
    /// </summary>
    /// <param name="path">The path to clean.</param>
    /// <returns>The normalized path; "." when a relative path cancels out completely.</returns>
    [NodeName("Path.Normalize")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Create)]
    [return: NodeName("path")]
    [NodeDescription("Cleans a path as text: one separator style, no trailing separator, \".\" and \"..\" resolved (the disk is never read).")]
    [NodeSearchTags("clean", "tidy", "canonical", "separator", "slash", "simplify", "path")]
    public static string Normalize(string path)
    {
        RequireText(path, "Path.Normalize", nameof(path), "a path");

        var sep = Path.DirectorySeparatorChar;
        var unified = path.Replace('/', sep).Replace('\\', sep);
        var root = string.Empty;
        var rest = unified;

        if (sep == '\\')
        {
            if (unified.Length >= 2 && IsAsciiLetter(unified[0]) && unified[1] == ':')
            {
                root = unified.Substring(0, 2);
                rest = unified.Substring(2);
                if (rest.Length > 0 && rest[0] == sep)
                {
                    root += sep;
                }
            }
            else if (unified.Length >= 2 && unified[0] == sep && unified[1] == sep)
            {
                // UNC: \\server\share is the root and can never be climbed out of.
                var parts = unified.Substring(2).Split(new[] { sep }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    root = new string(sep, 2) + parts[0] + sep + parts[1] + sep;
                    rest = string.Join(sep.ToString(), parts, 2, parts.Length - 2);
                }
                else
                {
                    root = new string(sep, 2) + string.Join(sep.ToString(), parts);
                    rest = string.Empty;
                }
            }
            else if (unified.Length > 0 && unified[0] == sep)
            {
                root = sep.ToString();
            }
        }
        else if (unified.Length > 0 && unified[0] == sep)
        {
            root = sep.ToString();
        }

        // "C:" alone (no separator) is relative to the drive's current folder, so ".." must be kept there.
        var canClimb = root.Length == 0 || (root.Length == 2 && root[1] == ':');
        var stack = new List<string>();
        foreach (var part in rest.Split(new[] { sep }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (stack.Count > 0 && stack[stack.Count - 1] != "..")
                {
                    stack.RemoveAt(stack.Count - 1);
                }
                else if (canClimb)
                {
                    stack.Add("..");
                }

                continue;
            }

            stack.Add(part);
        }

        var joined = new StringBuilder(root);
        for (var i = 0; i < stack.Count; i++)
        {
            if (i > 0)
            {
                joined.Append(sep);
            }

            joined.Append(stack[i]);
        }

        return joined.Length == 0 ? "." : joined.ToString();
    }

    // ------------------------------------------------------------------
    // Helpers (not imported as nodes: non-public).
    // ------------------------------------------------------------------

    private static bool IsSeparator(char c) => c == '\\' || c == '/';

    private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    private static bool IsInvalidPath(Exception ex) =>
        ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException;

    /// <summary>Throws a clear error when a text input is null or blank.</summary>
    internal static void RequireText(string? value, string nodeName, string inputName, string what)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                nodeName + " requires " + what + ". Wire text (or a File Path node) into the '" + inputName + "' input.",
                inputName);
        }
    }

    /// <summary>Runs a text-only path operation and turns the framework's invalid-path errors into a readable message.</summary>
    private static string Guarded(string nodeName, string path, Func<string, string> operation)
    {
        RequireText(path, nodeName, nameof(path), "a path");
        try
        {
            return operation(path);
        }
        catch (Exception ex) when (IsInvalidPath(ex))
        {
            throw new ArgumentException(nodeName + ": '" + path + "' is not a valid path. " + ex.Message, nameof(path), ex);
        }
    }
}
