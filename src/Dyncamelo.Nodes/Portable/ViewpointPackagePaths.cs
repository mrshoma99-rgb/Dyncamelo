using System;
using System.IO;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes.Portable;

/// <summary>
/// Path handling for the viewpoint package nodes, hardened against the ways a
/// user-typed path fails with a bare "access denied" on Windows: a folder
/// instead of a file (Windows reports writing onto a directory as access
/// denied), a relative path (the host process' working directory is the
/// Navisworks install folder under Program Files — admin-only), and paths
/// pasted with surrounding quotes from Explorer's "Copy as path". Pure
/// (no Navisworks types) so it is fully unit-testable.
/// </summary>
[IsVisibleInLibrary(false)]
public static class ViewpointPackagePaths
{
    /// <summary>The file name used when the given path is a folder.</summary>
    public const string DefaultFileName = "dyncamelo-viewpoints.json";

    /// <summary>
    /// Resolves a user-supplied path for WRITING a package: trims quotes,
    /// anchors relative paths under <paramref name="defaultBaseFolder"/>
    /// (never the process working directory), and appends
    /// <see cref="DefaultFileName"/> when the path is an existing folder or
    /// ends with a separator.
    /// </summary>
    public static string ResolveForWrite(string filePath, string defaultBaseFolder)
    {
        var path = Anchor(Clean(filePath), defaultBaseFolder);
        if (EndsWithSeparator(path) || Directory.Exists(path))
        {
            path = Path.Combine(path, DefaultFileName);
        }

        return path;
    }

    /// <summary>
    /// Resolves a user-supplied path for READING a package: trims quotes,
    /// anchors relative paths under <paramref name="defaultBaseFolder"/>, and
    /// looks for <see cref="DefaultFileName"/> inside when the path is a
    /// folder — so the same input that fed ExportFile finds the file again.
    /// </summary>
    public static string ResolveForRead(string filePath, string defaultBaseFolder)
    {
        var path = Anchor(Clean(filePath), defaultBaseFolder);
        if (EndsWithSeparator(path) || Directory.Exists(path))
        {
            path = Path.Combine(path, DefaultFileName);
        }

        return path;
    }

    private static string Clean(string filePath)
    {
        var trimmed = (filePath ?? string.Empty).Trim();
        while (trimmed.Length >= 2 &&
               ((trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"') ||
                (trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'')))
        {
            trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();
        }

        if (trimmed.Length == 0)
        {
            throw new ArgumentException("No file path provided.", nameof(filePath));
        }

        return trimmed;
    }

    private static string Anchor(string path, string defaultBaseFolder)
    {
        if (Path.IsPathRooted(path) || string.IsNullOrEmpty(defaultBaseFolder))
        {
            return path;
        }

        return Path.Combine(defaultBaseFolder, path);
    }

    private static bool EndsWithSeparator(string path)
    {
        var last = path[path.Length - 1];
        return last == '\\' || last == '/';
    }
}
