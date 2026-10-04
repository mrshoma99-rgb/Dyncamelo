using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CamelGraph.Core.Editing;

/// <summary>What <see cref="DiagnosticsReport"/> prints. Everything is text the caller has already gathered; nothing here touches the machine.</summary>
public sealed class DiagnosticsInfo
{
    /// <summary>The CamelGraph version ("0.45.1").</summary>
    public string CamelGraphVersion { get; set; } = string.Empty;

    /// <summary>The host application ("Autodesk Navisworks Manage 2024 (API 21.0)"), or why it is not known.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>The process the editor runs in and its bitness ("Roamer, 64-bit").</summary>
    public string Process { get; set; } = string.Empty;

    /// <summary>Windows version.</summary>
    public string OperatingSystem { get; set; } = string.Empty;

    /// <summary>.NET runtime.</summary>
    public string Runtime { get; set; } = string.Empty;

    /// <summary>Names of the Autodesk plug-in bundles installed (folder names only).</summary>
    public IList<string> InstalledBundles { get; } = new List<string>();

    /// <summary>Lines about the libraries CamelGraph shares with other add-ins that are loaded (name, version, where from).</summary>
    public IList<string> LoadedLibraries { get; } = new List<string>();

    /// <summary>Lines about the editor: preferences that change behaviour, node library size, the open graph's size.</summary>
    public IList<string> Editor { get; } = new List<string>();

    /// <summary>The end of <c>errors.log</c>, or empty.</summary>
    public string ErrorLogTail { get; set; } = string.Empty;

    /// <summary>True when <c>errors.log</c> does not exist (nothing has been logged).</summary>
    public bool ErrorLogMissing { get; set; }
}

/// <summary>
/// The text a person pastes into a bug report: versions, what else is installed, the end of the error log. Built to be safe to post
/// in public: file paths and names that identify the person or the computer are replaced (see <see cref="Redact"/>), and the model,
/// graph names and graph file paths are never included.
/// </summary>
public static class DiagnosticsReport
{
    /// <summary>Builds the report.</summary>
    /// <param name="info">What was gathered.</param>
    /// <param name="generatedAt">When it was made (local time).</param>
    public static string Build(DiagnosticsInfo info, DateTime generatedAt)
    {
        if (info == null)
        {
            throw new ArgumentNullException(nameof(info));
        }

        var text = new StringBuilder();
        text.AppendLine("CamelGraph diagnostics");
        text.AppendLine("Made " + generatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " (local time)");
        text.AppendLine();
        text.AppendLine("CamelGraph: " + Or(info.CamelGraphVersion));
        text.AppendLine("Host:      " + Or(info.Host));
        text.AppendLine("Process:   " + Or(info.Process));
        text.AppendLine("Windows:   " + Or(info.OperatingSystem));
        text.AppendLine(".NET:      " + Or(info.Runtime));

        Section(text, "Other Autodesk plug-in bundles installed (names only)", info.InstalledBundles, "none found");
        Section(text, "Shared libraries loaded (a copy from another add-in is the usual cause of odd failures)", info.LoadedLibraries, "none of the shared libraries is loaded yet");
        Section(text, "Editor", info.Editor, "not available");

        text.AppendLine();
        text.AppendLine("End of errors.log");
        if (info.ErrorLogMissing)
        {
            text.AppendLine("  (no errors.log: nothing has been logged)");
        }
        else if (string.IsNullOrWhiteSpace(info.ErrorLogTail))
        {
            text.AppendLine("  (empty)");
        }
        else
        {
            foreach (var line in info.ErrorLogTail.Replace("\r\n", "\n").Split('\n'))
            {
                text.AppendLine("  " + line);
            }
        }

        return text.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>
    /// Replaces each needle (the user's profile folder, user name, computer name) in <paramref name="text"/> with its replacement,
    /// ignoring case, longest needle first, so a path posted in public does not name the person. Needles shorter than three characters
    /// are ignored (they would mangle ordinary words).
    /// </summary>
    /// <param name="text">The text to clean.</param>
    /// <param name="replacements">Needle and replacement pairs.</param>
    public static string Redact(string text, IEnumerable<KeyValuePair<string, string>> replacements)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        if (replacements == null)
        {
            throw new ArgumentNullException(nameof(replacements));
        }

        foreach (var pair in replacements.Where(r => !string.IsNullOrEmpty(r.Key) && r.Key.Length >= 3).OrderByDescending(r => r.Key.Length))
        {
            var index = 0;
            while ((index = text.IndexOf(pair.Key, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                text = text.Substring(0, index) + pair.Value + text.Substring(index + pair.Key.Length);
                index += pair.Value.Length;
            }
        }

        return text;
    }

    /// <summary>The last lines of a text, kept within a number of characters, with a marker when something was cut.</summary>
    /// <param name="text">The whole text.</param>
    /// <param name="maxLines">Most lines to keep.</param>
    /// <param name="maxChars">Most characters to keep.</param>
    public static string Tail(string text, int maxLines, int maxChars)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        var cut = lines.Length > maxLines;
        var kept = cut ? lines.Skip(lines.Length - maxLines).ToArray() : lines;
        var joined = string.Join("\n", kept);
        if (joined.Length > maxChars)
        {
            joined = joined.Substring(joined.Length - maxChars);
            var firstBreak = joined.IndexOf('\n');
            joined = firstBreak >= 0 ? joined.Substring(firstBreak + 1) : joined;
            cut = true;
        }

        return cut ? "(earlier lines left out)\n" + joined : joined;
    }

    private static string Or(string value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value;

    private static void Section(StringBuilder text, string title, IList<string> lines, string whenEmpty)
    {
        text.AppendLine();
        text.AppendLine(title);
        if (lines.Count == 0)
        {
            text.AppendLine("  (" + whenEmpty + ")");
            return;
        }

        foreach (var line in lines)
        {
            text.AppendLine("  " + line);
        }
    }
}
