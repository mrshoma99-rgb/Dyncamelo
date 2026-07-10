using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Dyncamelo.UI.Services;

/// <summary>
/// Persisted editor preferences — favourite library nodes and recently opened
/// .dyc files — stored as JSON in <c>%APPDATA%\Dyncamelo\ui-settings.json</c>.
/// Robust by design: a missing, corrupt or unwritable settings file silently
/// falls back to defaults and must never take down the host application.
/// </summary>
public class UiSettingsService
{
    private const int MaxRecentFiles = 10;

    private readonly string _settingsPath;
    private readonly List<string> _favoriteNodeIds = new List<string>();
    private readonly List<string> _recentFiles = new List<string>();

    /// <summary>Creates the service backed by the default per-user settings file.</summary>
    public UiSettingsService()
        : this(GetDefaultSettingsPath())
    {
    }

    /// <summary>Creates the service backed by an explicit settings file (testing/hosting).</summary>
    /// <param name="settingsPath">Full path of the JSON settings file.</param>
    public UiSettingsService(string settingsPath)
    {
        _settingsPath = settingsPath ?? throw new ArgumentNullException(nameof(settingsPath));
        Load();
    }

    /// <summary>Library ids of the starred nodes, in the order they were starred.</summary>
    public IReadOnlyList<string> FavoriteNodeIds => _favoriteNodeIds;

    /// <summary>Recently opened/saved .dyc paths, most recent first (max 10, missing files pruned).</summary>
    public IReadOnlyList<string> RecentFiles => _recentFiles;

    /// <summary>True when the node id is starred.</summary>
    /// <param name="nodeId">Library id (zero-touch definition id or node type tag).</param>
    public bool IsFavorite(string nodeId)
    {
        return _favoriteNodeIds.Contains(nodeId);
    }

    /// <summary>Stars or un-stars a node id and persists the change.</summary>
    /// <param name="nodeId">Library id.</param>
    /// <param name="favorite">True to star, false to un-star.</param>
    public void SetFavorite(string nodeId, bool favorite)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            return;
        }

        bool changed = favorite
            ? AddIfMissing(_favoriteNodeIds, nodeId)
            : _favoriteNodeIds.Remove(nodeId);

        if (changed)
        {
            Save();
        }
    }

    /// <summary>
    /// Records a file at the front of the recent list (deduplicated, capped at
    /// ten, files that no longer exist pruned) and persists the change.
    /// </summary>
    /// <param name="path">Full path of the opened or saved .dyc file.</param>
    public void AddRecentFile(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        RemoveWhereEquals(_recentFiles, path);
        _recentFiles.Insert(0, path);
        PruneRecentFiles();
        Save();
    }

    /// <summary>Removes one entry from the recent list (e.g. after a failed open) and persists.</summary>
    /// <param name="path">The stale path to drop.</param>
    public void RemoveRecentFile(string path)
    {
        if (RemoveWhereEquals(_recentFiles, path))
        {
            Save();
        }
    }

    /// <summary>Drops recent entries whose files no longer exist. Does not save by itself.</summary>
    public void PruneRecentFiles()
    {
        for (int i = _recentFiles.Count - 1; i >= 0; i--)
        {
            if (!FileExistsSafe(_recentFiles[i]))
            {
                _recentFiles.RemoveAt(i);
            }
        }

        while (_recentFiles.Count > MaxRecentFiles)
        {
            _recentFiles.RemoveAt(_recentFiles.Count - 1);
        }
    }

    /// <summary>Writes the settings file, creating the directory as needed. Failures are swallowed.</summary>
    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var data = new SettingsData
            {
                FavoriteNodeIds = new List<string>(_favoriteNodeIds),
                RecentFiles = new List<string>(_recentFiles),
            };

            // Write-to-temp-then-replace so a crash (or a concurrent reader in
            // another Navisworks session) never sees a truncated settings file,
            // which Load() would silently interpret as "reset to defaults".
            var tempPath = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, JsonConvert.SerializeObject(data, Formatting.Indented));
            try
            {
                if (File.Exists(_settingsPath))
                {
                    File.Replace(tempPath, _settingsPath, null);
                }
                else
                {
                    File.Move(tempPath, _settingsPath);
                }
            }
            catch (Exception)
            {
                File.Delete(tempPath);
                throw;
            }
        }
        catch (Exception)
        {
            // Settings persistence is best-effort; never surface I/O problems.
        }
    }

    private void Load()
    {
        SettingsData? data = null;
        try
        {
            if (File.Exists(_settingsPath))
            {
                data = JsonConvert.DeserializeObject<SettingsData>(File.ReadAllText(_settingsPath));
            }
        }
        catch (Exception)
        {
            // Corrupt or unreadable file: start from defaults.
            data = null;
        }

        _favoriteNodeIds.Clear();
        _recentFiles.Clear();
        if (data == null)
        {
            return;
        }

        if (data.FavoriteNodeIds != null)
        {
            foreach (var id in data.FavoriteNodeIds)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    AddIfMissing(_favoriteNodeIds, id);
                }
            }
        }

        if (data.RecentFiles != null)
        {
            foreach (var path in data.RecentFiles)
            {
                if (!string.IsNullOrEmpty(path) && !ContainsEquals(_recentFiles, path))
                {
                    _recentFiles.Add(path);
                }
            }
        }

        PruneRecentFiles();
    }

    private static string GetDefaultSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Dyncamelo", "ui-settings.json");
    }

    private static bool AddIfMissing(List<string> list, string value)
    {
        if (list.Contains(value))
        {
            return false;
        }

        list.Add(value);
        return true;
    }

    private static bool ContainsEquals(List<string> list, string path)
    {
        foreach (var existing in list)
        {
            if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RemoveWhereEquals(List<string> list, string path)
    {
        bool removed = false;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (string.Equals(list[i], path, StringComparison.OrdinalIgnoreCase))
            {
                list.RemoveAt(i);
                removed = true;
            }
        }

        return removed;
    }

    private static bool FileExistsSafe(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception)
        {
            // An unreachable network path must not break the editor; keep the entry.
            return true;
        }
    }

    private class SettingsData
    {
        [JsonProperty("favoriteNodeIds")]
        public List<string>? FavoriteNodeIds { get; set; }

        [JsonProperty("recentFiles")]
        public List<string>? RecentFiles { get; set; }
    }
}
