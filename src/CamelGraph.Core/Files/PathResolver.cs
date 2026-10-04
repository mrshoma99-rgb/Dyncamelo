using System;
using System.IO;

namespace CamelGraph.Core.Files;

/// <summary>
/// Turns the text typed or pasted into a file node's path box into a path the file system can use. Every node that opens, writes,
/// lists, copies or deletes a file calls <see cref="Resolve(string?)"/> on the paths it is given, so they all agree on what a
/// relative path means and on what to do with quotes.
/// </summary>
public static class PathResolver
{
    /// <summary>
    /// Cleans a path and makes it absolute. Surrounding spaces and the quotes Explorer puts on "Copy as path" (<c>"C:\x\y.txt"</c>;
    /// also single and typographic quotes) are removed. A relative path is placed under <see cref="GraphContext.Folder"/>, or the
    /// process's current directory when the host set none; <c>..</c> and <c>.</c> are folded away. An absolute path (a drive, a
    /// network share) is returned as typed. A blank path comes back unchanged, and this method never throws: a path it cannot
    /// make sense of is returned cleaned, for the node to report in its own words.
    /// </summary>
    /// <param name="path">The text from the node's path input.</param>
    /// <returns>The cleaned, absolute path; the input itself when it is null or blank.</returns>
    public static string Resolve(string? path) => Resolve(path, GraphContext.Folder);

    /// <summary>Like <see cref="Resolve(string?)"/> with the folder given instead of read from <see cref="GraphContext"/>.</summary>
    /// <param name="path">The text from the node's path input.</param>
    /// <param name="baseFolder">The folder relative paths belong to; null or blank for the process's current directory.</param>
    public static string Resolve(string? path, string? baseFolder)
    {
        if (path == null)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var cleaned = Clean(path);
        if (cleaned.Length == 0)
        {
            return cleaned;
        }

        try
        {
            if (Path.IsPathRooted(cleaned))
            {
                return cleaned;
            }

            var folder = string.IsNullOrWhiteSpace(baseFolder) ? Directory.GetCurrentDirectory() : baseFolder!;
            return Path.GetFullPath(Path.Combine(folder, cleaned));
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException ||
                                   ex is System.Security.SecurityException || ex is IOException)
        {
            return cleaned;
        }
    }

    /// <summary>
    /// Only the cleaning of <see cref="Resolve(string?)"/>: surrounding spaces and one pair of surrounding quotes removed, no
    /// folder added. Null becomes an empty string.
    /// </summary>
    /// <param name="path">The text from the node's path input.</param>
    public static string Clean(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var text = path!.Trim();
        if (text.Length >= 2 && IsQuotePair(text[0], text[text.Length - 1]))
        {
            text = text.Substring(1, text.Length - 2).Trim();
        }

        return text;
    }

    private static bool IsQuotePair(char first, char last) =>
        (first == '"' && last == '"') || (first == '\'' && last == '\'') ||
        (first == '\u201C' && last == '\u201D') || (first == '\u201C' && last == '\u201C') ||
        (first == '\u2018' && last == '\u2019');
}
