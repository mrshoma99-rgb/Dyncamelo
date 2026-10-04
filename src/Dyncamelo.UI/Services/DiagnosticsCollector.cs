using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Serialization;
using Dyncamelo.UI.ViewModels;

namespace Dyncamelo.UI.Services;

/// <summary>
/// Gathers the facts for <see cref="DiagnosticsReport"/> from the machine and the editor, and hands back the finished text with names
/// that identify the person or the computer removed. Every part is optional: a fact that cannot be read is left out, never an error.
/// </summary>
public static class DiagnosticsCollector
{
    // Libraries Dyncamelo shares with whatever else is loaded in the host. A copy of one of these from another add-in is the usual
    // reason a visual or a node fails only in the host.
    private static readonly string[] SharedLibraryPrefixes = { "Nodify", "Newtonsoft.Json", "AutomaticGraphLayout", "Microsoft.Msagl" };

    /// <summary>Gathers the facts and builds the report text.</summary>
    /// <param name="editor">The editor whose state is described.</param>
    /// <param name="hostDescription">The host application as the host app reports it, or null.</param>
    public static string Build(GraphEditorViewModel editor, string? hostDescription)
    {
        if (editor == null)
        {
            throw new ArgumentNullException(nameof(editor));
        }

        var text = DiagnosticsReport.Build(Collect(editor, hostDescription), DateTime.Now);
        return DiagnosticsReport.Redact(text, Replacements());
    }

    /// <summary>The facts, before they are turned into text.</summary>
    /// <param name="editor">The editor whose state is described.</param>
    /// <param name="hostDescription">The host application as the host app reports it, or null.</param>
    public static DiagnosticsInfo Collect(GraphEditorViewModel editor, string? hostDescription)
    {
        var info = new DiagnosticsInfo
        {
            DyncameloVersion = DyncameloVersion(),
            Host = string.IsNullOrWhiteSpace(hostDescription) ? "no host application reported (CamelGraph running on its own)" : hostDescription!,
            Process = Attempt(() => Process.GetCurrentProcess().ProcessName + ", " + (Environment.Is64BitProcess ? "64-bit" : "32-bit")),
            OperatingSystem = Attempt(() => Environment.OSVersion.VersionString),
            Runtime = Attempt(() => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription),
        };

        foreach (var bundle in InstalledBundles())
        {
            info.InstalledBundles.Add(bundle);
        }

        foreach (var line in LoadedLibraries())
        {
            info.LoadedLibraries.Add(line);
        }

        foreach (var line in EditorLines(editor))
        {
            info.Editor.Add(line);
        }

        var logPath = Path.Combine(UiSettingsService.DefaultDirectory, "errors.log");
        try
        {
            if (File.Exists(logPath))
            {
                info.ErrorLogTail = DiagnosticsReport.Tail(File.ReadAllText(logPath), 60, 8000);
            }
            else
            {
                info.ErrorLogMissing = true;
            }
        }
        catch (IOException)
        {
            info.ErrorLogTail = "(errors.log could not be read)";
        }
        catch (UnauthorizedAccessException)
        {
            info.ErrorLogTail = "(errors.log could not be read)";
        }

        return info;
    }

    /// <summary>The names to take out of the text (profile folder, user, computer, domain) and what to put there.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Replacements()
    {
        var list = new List<KeyValuePair<string, string>>();
        Add(list, () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%");
        Add(list, () => Environment.UserName, "<user>");
        Add(list, () => Environment.MachineName, "<computer>");
        Add(list, () => Environment.UserDomainName, "<domain>");
        return list;
    }

    private static void Add(List<KeyValuePair<string, string>> list, Func<string> read, string replacement)
    {
        try
        {
            var value = read();
            if (!string.IsNullOrWhiteSpace(value))
            {
                list.Add(new KeyValuePair<string, string>(value, replacement));
            }
        }
        catch (Exception)
        {
            // A name that cannot be read cannot be removed; the rest still is.
        }
    }

    /// <summary>The Dyncamelo version as shown in About ("0.45.1").</summary>
    public static string DyncameloVersion()
    {
        var version = typeof(DiagnosticsCollector).Assembly.GetName().Version;
        return version == null ? "unknown" : version.Major + "." + version.Minor + "." + Math.Max(version.Build, 0);
    }

    private static string Attempt(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static IEnumerable<string> InstalledBundles()
    {
        var roots = new[]
        {
            Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "this user"),
            Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "all users"),
            Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Program Files"),
        };
        var found = new List<string>();
        foreach (var root in roots)
        {
            try
            {
                var folder = Path.Combine(root.Item1, "Autodesk", "ApplicationPlugins");
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                found.AddRange(Directory.GetDirectories(folder, "*.bundle").Select(d => Path.GetFileName(d) + " (" + root.Item2 + ")"));
            }
            catch (IOException)
            {
                // Not readable: left out.
            }
            catch (UnauthorizedAccessException)
            {
                // Not readable: left out.
            }
        }

        return found.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(80);
    }

    private static IEnumerable<string> LoadedLibraries()
    {
        var lines = new List<string>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name, StringComparer.OrdinalIgnoreCase))
        {
            string name;
            string location;
            try
            {
                name = assembly.GetName().Name ?? string.Empty;
                if (assembly.IsDynamic)
                {
                    continue;
                }

                location = assembly.Location;
            }
            catch (Exception)
            {
                continue;
            }

            var shared = SharedLibraryPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            var ours = name.StartsWith("Dyncamelo", StringComparison.OrdinalIgnoreCase);
            if (!shared && !ours)
            {
                continue;
            }

            var line = name + " " + assembly.GetName().Version + "  " + (string.IsNullOrEmpty(location) ? "(no file)" : location);
            if (shared && location.IndexOf("Dyncamelo.bundle", StringComparison.OrdinalIgnoreCase) < 0)
            {
                line += "   <- not from Dyncamelo.bundle";
            }

            lines.Add(line);
        }

        return lines;
    }

    private static IEnumerable<string> EditorLines(GraphEditorViewModel editor)
    {
        var lines = new List<string>();
        try
        {
            lines.Add("Installed from: " + DistributionChannel.Detect(System.IO.Path.GetDirectoryName(typeof(DiagnosticsCollector).Assembly.Location)));
            lines.Add("Node library: " + (editor.Registry.Definitions.Count + editor.Registry.NodeTypes.Count) + " nodes");
            lines.Add("Palette: " + editor.PaletteId + "; UI scale: " + editor.UiScale);
            lines.Add("Ask before running graphs from files: " + (editor.ConfirmUntrustedRuns ? "on" : "off"));
            var graph = editor.DocumentGraph;
            var missing = graph.Nodes.Count(n => n is MissingNodeModel);
            lines.Add("Open graph: " + graph.Nodes.Count + " nodes, " + graph.Connections.Count + " wires, " + editor.ErrorCount +
                      " in error, " + editor.WarningCount + " with warnings, " + missing + " of a type that is not installed; run " +
                      (graph.RunType == RunType.Automatic ? "automatically" : "manually"));
        }
        catch (Exception ex)
        {
            lines.Add("(the editor's state could not be read: " + ex.GetType().Name + ")");
        }

        return lines;
    }
}
