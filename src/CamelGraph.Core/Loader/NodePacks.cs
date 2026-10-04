using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace CamelGraph.Core.Loader;

/// <summary>What became of one DLL found in a node pack folder.</summary>
public enum NodePackStatus
{
    /// <summary>The DLL was loaded and its nodes are in the library (a DLL without nodes counts too: it is a dependency of a pack).</summary>
    Loaded,

    /// <summary>The DLL was left alone on purpose: CamelGraph or Navisworks already has its own copy, or an earlier folder has a file of that name.</summary>
    Skipped,

    /// <summary>The DLL could not be loaded; the reason is in <see cref="NodePackResult.Problem"/>.</summary>
    Failed,
}

/// <summary>The outcome for one DLL in a node pack folder.</summary>
public sealed class NodePackResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="path">Full path of the DLL (or of the folder that could not be read).</param>
    /// <param name="status">What became of it.</param>
    /// <param name="nodes">How many nodes it added to the library.</param>
    /// <param name="problem">Why it was skipped or failed; null when loaded.</param>
    public NodePackResult(string path, NodePackStatus status, int nodes, string? problem)
    {
        Path = path;
        Status = status;
        Nodes = nodes;
        Problem = problem;
    }

    /// <summary>Full path of the DLL.</summary>
    public string Path { get; }

    /// <summary>What became of it.</summary>
    public NodePackStatus Status { get; }

    /// <summary>How many nodes it added to the library.</summary>
    public int Nodes { get; }

    /// <summary>Why it was skipped or failed; null when it was loaded.</summary>
    public string? Problem { get; }
}

/// <summary>Everything one scan of the node pack folders found, in the order the folders were searched.</summary>
public sealed class NodePackReport
{
    /// <summary>A report of a scan that has not happened (or found nothing).</summary>
    public static readonly NodePackReport Empty = new NodePackReport(Array.Empty<string>(), Array.Empty<NodePackResult>());

    /// <summary>Creates a report.</summary>
    /// <param name="folders">The folders that exist and were scanned.</param>
    /// <param name="results">One entry per DLL.</param>
    public NodePackReport(IReadOnlyList<string> folders, IReadOnlyList<NodePackResult> results)
    {
        Folders = folders;
        Results = results;
    }

    /// <summary>The folders that exist and were scanned.</summary>
    public IReadOnlyList<string> Folders { get; }

    /// <summary>One entry per DLL, in the order they were met.</summary>
    public IReadOnlyList<NodePackResult> Results { get; }

    /// <summary>How many DLLs were loaded.</summary>
    public int LoadedCount => Results.Count(r => r.Status == NodePackStatus.Loaded);

    /// <summary>How many DLLs could not be loaded.</summary>
    public int FailedCount => Results.Count(r => r.Status == NodePackStatus.Failed);

    /// <summary>How many nodes the loaded DLLs added.</summary>
    public int NodeCount => Results.Where(r => r.Status == NodePackStatus.Loaded).Sum(r => r.Nodes);

    /// <summary>One sentence for a status line: what was loaded and whether anything failed.</summary>
    public string Summary()
    {
        var inv = CultureInfo.InvariantCulture;
        var loaded = Results.Where(r => r.Status == NodePackStatus.Loaded && r.Nodes > 0).ToList();
        var failed = FailedCount;
        if (loaded.Count == 0 && failed == 0)
        {
            return "No node packs are installed.";
        }

        var text = new StringBuilder();
        text.Append(loaded.Count.ToString(inv)).Append(loaded.Count == 1 ? " node pack" : " node packs")
            .Append(" loaded (").Append(NodeCount.ToString(inv)).Append(NodeCount == 1 ? " node)" : " nodes)");
        if (failed > 0)
        {
            text.Append(", ").Append(failed.ToString(inv)).Append(" could not be loaded");
        }

        text.Append('.');
        return text.ToString();
    }

    /// <summary>The report as lines a person can read: what loaded, what did not and why.</summary>
    public IReadOnlyList<string> Lines()
    {
        var inv = CultureInfo.InvariantCulture;
        var lines = new List<string>();
        foreach (var result in Results)
        {
            var name = System.IO.Path.GetFileName(result.Path);
            switch (result.Status)
            {
                case NodePackStatus.Loaded when result.Nodes > 0:
                    lines.Add(name + ": " + result.Nodes.ToString(inv) + (result.Nodes == 1 ? " node" : " nodes"));
                    break;
                case NodePackStatus.Loaded:
                    lines.Add(name + ": loaded, no nodes (a library the pack uses)");
                    break;
                case NodePackStatus.Skipped:
                    lines.Add(name + ": not used. " + result.Problem);
                    break;
                default:
                    lines.Add(name + ": NOT LOADED. " + result.Problem);
                    break;
            }
        }

        return lines;
    }
}

/// <summary>
/// Finds and loads node packs: the DLLs of other people's (or your own) nodes. A pack is a folder with a DLL in it. Packs are looked
/// for in the per-user folder <see cref="UserFolder"/> first, which an update of CamelGraph leaves alone, and then in the
/// <c>Packages</c> folder next to the plug-in (the place older versions documented, which an update replaces).
/// </summary>
public static class NodePacks
{
    // Libraries that CamelGraph and Navisworks already have. A copy of one in a pack folder would only add its public methods to the
    // library as nodes and put a second copy of the library in the process.
    private static readonly string[] SharedPrefixes =
    {
        "CamelGraph.", "Autodesk.", "Newtonsoft.Json", "Nodify", "AutomaticGraphLayout", "Microsoft.Msagl", "System.",
    };

    /// <summary>The report of the scan the host made when it built the node library; <see cref="NodePackReport.Empty"/> before that.</summary>
    public static NodePackReport Last { get; set; } = NodePackReport.Empty;

    /// <summary>
    /// The folder for your own packs, <c>%APPDATA%\CamelGraph\Packages</c>. It lives with the settings, so installing a new version of
    /// CamelGraph (which replaces the plug-in folder) leaves it alone.
    /// </summary>
    public static string UserFolder => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CamelGraph", "Packages");

    /// <summary>The folders to search, in order: the per-user folder, then <c>Packages</c> next to the plug-in.</summary>
    /// <param name="pluginDirectory">The folder of CamelGraph.App.dll, or null when it is not known.</param>
    /// <param name="userFolder">The per-user folder; <see cref="UserFolder"/> when null.</param>
    public static IReadOnlyList<string> Folders(string? pluginDirectory, string? userFolder = null)
    {
        var folders = new List<string> { userFolder ?? UserFolder };
        if (!string.IsNullOrEmpty(pluginDirectory))
        {
            folders.Add(System.IO.Path.Combine(pluginDirectory!, "Packages"));
        }

        return folders;
    }

    /// <summary>
    /// Loads every DLL under the folders into the registry. One bad DLL never stops the others: it is recorded and the scan goes on.
    /// A file name met in an earlier folder wins, so a pack in the per-user folder replaces an older copy beside the plug-in.
    /// </summary>
    /// <param name="registry">Where the nodes go.</param>
    /// <param name="folders">The folders to search (those that do not exist are ignored).</param>
    /// <param name="load">Turns a DLL path into an assembly (the host passes <c>Assembly.LoadFrom</c>).</param>
    public static NodePackReport Load(NodeRegistry registry, IEnumerable<string> folders, Func<string, Assembly> load)
    {
        if (registry == null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        if (load == null)
        {
            throw new ArgumentNullException(nameof(load));
        }

        var scanned = new List<string>();
        var results = new List<NodePackResult>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folders)
        {
            string[] files;
            try
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                files = Directory.GetFiles(folder, "*.dll", SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                results.Add(new NodePackResult(folder, NodePackStatus.Failed, 0, "The folder could not be read: " + ex.Message));
                continue;
            }

            scanned.Add(folder);
            foreach (var file in files)
            {
                results.Add(LoadOne(registry, file, load, seen));
            }
        }

        return new NodePackReport(scanned, results);
    }

    private static NodePackResult LoadOne(NodeRegistry registry, string file, Func<string, Assembly> load, Dictionary<string, string> seen)
    {
        var name = System.IO.Path.GetFileName(file);
        if (IsShared(name))
        {
            return new NodePackResult(file, NodePackStatus.Skipped, 0, "CamelGraph and Navisworks already have their own copy; leave it out of the pack.");
        }

        if (seen.TryGetValue(name, out var earlier))
        {
            return new NodePackResult(file, NodePackStatus.Skipped, 0, "A file with the same name was loaded from " + System.IO.Path.GetDirectoryName(earlier) + ".");
        }

        try
        {
            var definitions = registry.RegisterAssembly(load(file));
            seen[name] = file;
            return new NodePackResult(file, NodePackStatus.Loaded, definitions.Count, null);
        }
        catch (Exception ex)
        {
            return new NodePackResult(file, NodePackStatus.Failed, 0, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static bool IsShared(string fileName) =>
        SharedPrefixes.Any(prefix => fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
