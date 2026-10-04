using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CamelGraph.Core.Editing;

/// <summary>An autosaved graph left behind by a session that ended without saving it.</summary>
public sealed class RecoveryCandidate
{
    internal RecoveryCandidate(string graphPath, string metaPath, string? originalPath, DateTime savedAtUtc, string graphName)
    {
        GraphPath = graphPath;
        MetaPath = metaPath;
        OriginalPath = originalPath;
        SavedAtUtc = savedAtUtc;
        GraphName = graphName;
    }

    /// <summary>The autosaved .dyc file.</summary>
    public string GraphPath { get; }

    /// <summary>The small JSON file describing it.</summary>
    public string MetaPath { get; }

    /// <summary>The file the graph was opened from, or null when it was never saved.</summary>
    public string? OriginalPath { get; }

    /// <summary>When the autosave was written (UTC).</summary>
    public DateTime SavedAtUtc { get; }

    /// <summary>The graph's name.</summary>
    public string GraphName { get; }

    /// <summary>"name (file.dyc), saved 14:32" for the recovery prompt.</summary>
    public string Describe()
    {
        var what = GraphName.Length > 0 ? "'" + GraphName + "'" : "An untitled graph";
        if (OriginalPath != null)
        {
            what += " (" + Path.GetFileName(OriginalPath) + ")";
        }

        return what + ", last autosaved " + SavedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }
}

/// <summary>
/// The folder of autosaved copies of graphs with unsaved changes. Each editor session writes its own pair of files
/// (<c>autosave-&lt;process&gt;-&lt;guid&gt;.dyc</c> plus a <c>.json</c> note); a clean save, New or Open deletes them, and so
/// does closing the editor with nothing unsaved. What is still there when the editor starts next belongs to a session that
/// died (a crash, a killed Navisworks) and is offered back — unless its process is still running (another Navisworks) or
/// another editor in this same process owns it.
/// </summary>
public sealed class RecoveryStore : IDisposable
{
    private const string Prefix = "autosave-";
    private static readonly object Gate = new object();
    private static readonly HashSet<string> Owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private readonly string _directory;
    private readonly string _baseName;

    /// <summary>Creates a store in a folder (created on the first write).</summary>
    /// <param name="directory">Folder for the autosave files.</param>
    public RecoveryStore(string directory)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _baseName = Prefix + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
        lock (Gate)
        {
            Owned.Add(GraphPath);
        }
    }

    /// <summary>The folder the store works in.</summary>
    public string Directory => _directory;

    /// <summary>This session's autosave file.</summary>
    public string GraphPath => Path.Combine(_directory, _baseName + ".dyc");

    private string MetaPath => Path.Combine(_directory, _baseName + ".json");

    /// <summary>True when this session has an autosave on disk.</summary>
    public bool HasAutosave => File.Exists(GraphPath);

    /// <summary>
    /// Writes this session's autosave. The graph goes to a temporary file first and is moved over the old copy, so a crash in
    /// the middle of a write leaves the previous autosave intact.
    /// </summary>
    /// <param name="writeGraph">Writes the graph to the path it is given.</param>
    /// <param name="originalPath">The file the graph came from, or null.</param>
    /// <param name="graphName">The graph's name.</param>
    /// <returns>True when the autosave was written; failures (a full disk, a locked folder) are swallowed — autosave must never disturb editing.</returns>
    public bool Write(Action<string> writeGraph, string? originalPath, string graphName)
    {
        try
        {
            System.IO.Directory.CreateDirectory(_directory);
            var temp = GraphPath + ".tmp";
            writeGraph(temp);
            if (File.Exists(GraphPath))
            {
                File.Delete(GraphPath);
            }

            File.Move(temp, GraphPath);

            var meta = new JObject
            {
                ["originalPath"] = originalPath,
                ["savedAtUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["name"] = graphName ?? string.Empty,
            };
            File.WriteAllText(MetaPath, meta.ToString());
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Deletes this session's autosave (the graph was saved, replaced, or the editor closed with nothing unsaved).</summary>
    public void Clear()
    {
        TryDelete(GraphPath);
        TryDelete(GraphPath + ".tmp");
        TryDelete(MetaPath);
    }

    /// <summary>The autosaves of sessions that no longer exist, newest first.</summary>
    public IReadOnlyList<RecoveryCandidate> FindOrphans()
    {
        var found = new List<RecoveryCandidate>();
        string[] graphs;
        try
        {
            if (!System.IO.Directory.Exists(_directory))
            {
                return found;
            }

            graphs = System.IO.Directory.GetFiles(_directory, Prefix + "*.dyc");
        }
        catch (Exception)
        {
            return found;
        }

        foreach (var graph in graphs)
        {
            bool owned;
            lock (Gate)
            {
                owned = Owned.Contains(graph);
            }

            if (owned || IsLiveOtherProcess(graph))
            {
                continue;
            }

            var meta = Path.ChangeExtension(graph, ".json");
            string? original = null;
            var savedAt = File.GetLastWriteTimeUtc(graph);
            var name = string.Empty;
            try
            {
                if (File.Exists(meta))
                {
                    var json = JObject.Parse(File.ReadAllText(meta));
                    original = json.Value<string>("originalPath");
                    name = json.Value<string>("name") ?? string.Empty;
                    if (DateTime.TryParse(json.Value<string>("savedAtUtc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                    {
                        savedAt = parsed.ToUniversalTime();
                    }
                }
            }
            catch (Exception)
            {
                // A damaged note does not make the autosave any less worth offering.
            }

            found.Add(new RecoveryCandidate(graph, meta, original, savedAt, name));
        }

        return found.OrderByDescending(c => c.SavedAtUtc).ToList();
    }

    /// <summary>Deletes an orphan's files (it was restored or declined).</summary>
    /// <param name="candidate">The autosave to remove.</param>
    public void Discard(RecoveryCandidate candidate)
    {
        TryDelete(candidate.GraphPath);
        TryDelete(candidate.MetaPath);
    }

    /// <summary>Ends the session: the files stay on disk if they hold unsaved work, and stop being this session's own.</summary>
    public void Dispose()
    {
        lock (Gate)
        {
            Owned.Remove(GraphPath);
        }
    }

    // "autosave-1234-<guid>.dyc": the number is the process that wrote it. A running process means another Navisworks is
    // still using it (this process's own files are in Owned), so it is not an orphan.
    private static bool IsLiveOtherProcess(string graphPath)
    {
        var name = Path.GetFileNameWithoutExtension(graphPath);
        var parts = name.Split('-');
        if (parts.Length < 3 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
        {
            return false;
        }

        if (pid == Process.GetCurrentProcess().Id)
        {
            return false;
        }

        try
        {
            using (Process.GetProcessById(pid))
            {
                return true;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (Exception)
        {
            return true;
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
        catch (Exception)
        {
            // Leftover files are offered (or deleted) next time.
        }
    }
}
