using System;
using System.IO;

namespace CamelGraph.Core.Editing;

/// <summary>
/// Brings along what the product stored under its old name. CamelGraph was called Dyncamelo up to version 0.48 and kept its
/// settings in <c>%APPDATA%\Dyncamelo</c> and its scripts in <c>Documents\Dyncamelo\Scripts</c>; from the first run of a version
/// with the new name they live under CamelGraph and the old folder is moved across, so nobody loses a setting by updating.
/// </summary>
public static class LegacyData
{
    /// <summary>The name of the per-user folders before the rename.</summary>
    public const string OldFolderName = "Dyncamelo";

    /// <summary>The per-user data folder before the rename (<c>%APPDATA%\Dyncamelo</c>).</summary>
    public static string OldDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), OldFolderName);

    /// <summary>The default scripts folder before the rename (<c>Documents\Dyncamelo\Scripts</c>).</summary>
    public static string OldScriptsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), OldFolderName, "Scripts");

    /// <summary>
    /// Moves <paramref name="oldDirectory"/> to <paramref name="newDirectory"/> when only the old one exists. When both exist
    /// (the new version already ran once), the files the new folder lacks are copied across and nothing is overwritten. Never
    /// throws: a locked or read-only folder just leaves the old one where it is.
    /// </summary>
    /// <param name="oldDirectory">The folder from before the rename.</param>
    /// <param name="newDirectory">The folder the current version uses.</param>
    /// <returns>True when something was moved or copied.</returns>
    public static bool MigrateDirectory(string oldDirectory, string newDirectory)
    {
        try
        {
            if (string.IsNullOrEmpty(oldDirectory) || string.IsNullOrEmpty(newDirectory) || !Directory.Exists(oldDirectory))
            {
                return false;
            }

            var from = Path.GetFullPath(oldDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var to = Path.GetFullPath(newDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!Directory.Exists(to))
            {
                var parent = Path.GetDirectoryName(to);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                try
                {
                    Directory.Move(from, to);
                    return true;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // A file in the old folder is open (or the volumes differ): copy instead and leave the old folder.
                }
            }

            return CopyMissing(from, to);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
        {
            return false;
        }
    }

    private static bool CopyMissing(string from, string to)
    {
        var copied = false;
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            var target = Path.Combine(to, Path.GetFileName(file));
            if (!File.Exists(target))
            {
                File.Copy(file, target);
                copied = true;
            }
        }

        foreach (var directory in Directory.GetDirectories(from))
        {
            if (CopyMissing(directory, Path.Combine(to, Path.GetFileName(directory))))
            {
                copied = true;
            }
        }

        return copied;
    }
}
