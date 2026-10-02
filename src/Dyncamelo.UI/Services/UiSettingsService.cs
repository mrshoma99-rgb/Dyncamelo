using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
    private const int MaxRecentNodes = 12;

    private readonly string _settingsPath;
    private readonly List<string> _favoriteNodeIds = new List<string>();
    private readonly List<string> _recentFiles = new List<string>();
    private readonly List<string> _recentNodeIds = new List<string>();
    private readonly List<string> _playerFolders = new List<string>();
    private readonly Dictionary<string, string> _playerConfirmed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _editorConfirmed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private bool _confirmUntrustedRuns = true;
    private readonly Dictionary<string, JObject> _playerValues = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
    private string _playerLastScript = string.Empty;
    private bool _showLibraryDescriptions = true;
    private string _doubleClickAction = "string";
    private bool _previewSelection;
    private string _paletteId = "DyncameloDark";
    private bool _liveScrubEvaluation;
    private readonly Dictionary<string, JToken> _values = new Dictionary<string, JToken>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _shortcuts = new Dictionary<string, string>(StringComparer.Ordinal);

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

    // ----- the Script Player ----------------------------------------------------------------

    /// <summary>The folder scripts are looked for in without being asked (Documents\Dyncamelo\Scripts).</summary>
    public static string DefaultScriptsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Dyncamelo", "Scripts");

    /// <summary>The scripts folders the user added, besides <see cref="DefaultScriptsFolder"/>.</summary>
    public IReadOnlyList<string> PlayerFolders => _playerFolders;

    /// <summary>Replaces the added scripts folders and saves.</summary>
    /// <param name="folders">The folders to keep (blank and duplicate entries are dropped).</param>
    public void SetPlayerFolders(IEnumerable<string> folders)
    {
        _playerFolders.Clear();
        foreach (var folder in folders ?? new string[0])
        {
            var trimmed = (folder ?? string.Empty).Trim();
            if (trimmed.Length > 0 && !ContainsEquals(_playerFolders, trimmed))
            {
                _playerFolders.Add(trimmed);
            }
        }

        Save();
    }

    /// <summary>The script last run in the Player (empty if none).</summary>
    public string PlayerLastScript => _playerLastScript;

    /// <summary>Remembers the script last run.</summary>
    /// <param name="path">Its full path.</param>
    public void SetPlayerLastScript(string path)
    {
        if (!string.Equals(_playerLastScript, path, StringComparison.OrdinalIgnoreCase))
        {
            _playerLastScript = path ?? string.Empty;
            Save();
        }
    }

    /// <summary>The hash of the script as it was when the user last agreed to run it, or empty.</summary>
    /// <param name="path">The script's full path.</param>
    public string PlayerConfirmedHash(string path) =>
        _playerConfirmed.TryGetValue(path, out var hash) ? hash : string.Empty;

    /// <summary>Records that the user agreed to run the script as it is now.</summary>
    /// <param name="path">The script's full path.</param>
    /// <param name="hash">Its hash.</param>
    public void SetPlayerConfirmed(string path, string hash)
    {
        _playerConfirmed[path] = hash;
        Trim(_playerConfirmed, MaxPlayerMemory);
        Save();
    }

    /// <summary>The hash of the graph file as it was when the user last agreed to run it in the editor (or saved it from there), or empty.</summary>
    /// <param name="path">The graph's full path.</param>
    public string EditorConfirmedHash(string path) =>
        _editorConfirmed.TryGetValue(path, out var hash) ? hash : string.Empty;

    /// <summary>Records that the user agreed to run the graph file as it is now (or saved it themselves).</summary>
    /// <param name="path">The graph's full path.</param>
    /// <param name="hash">The file's hash.</param>
    public void SetEditorConfirmed(string path, string hash)
    {
        if (_editorConfirmed.TryGetValue(path, out var current) && current == hash)
        {
            return;
        }

        _editorConfirmed[path] = hash;
        Trim(_editorConfirmed, MaxPlayerMemory);
        Save();
    }

    /// <summary>True (the default) to ask before running a graph opened from a file that starts programs, uses the network or changes existing files.</summary>
    public bool ConfirmUntrustedRuns => _confirmUntrustedRuns;

    /// <summary>Persists the "ask before running graphs from files" preference.</summary>
    /// <param name="enabled">True to ask.</param>
    public void SetConfirmUntrustedRuns(bool enabled)
    {
        if (_confirmUntrustedRuns != enabled)
        {
            _confirmUntrustedRuns = enabled;
            Save();
        }
    }

    /// <summary>The values last used in the script's form, or null.</summary>
    /// <param name="path">The script's full path.</param>
    public JObject? PlayerValues(string path) =>
        _playerValues.TryGetValue(path, out var values) ? (JObject)values.DeepClone() : null;

    /// <summary>Remembers the values used in the script's form (an empty set forgets them).</summary>
    /// <param name="path">The script's full path.</param>
    /// <param name="values">The values by field key.</param>
    public void SetPlayerValues(string path, JObject values)
    {
        if (values == null || values.Count == 0)
        {
            if (!_playerValues.Remove(path))
            {
                return;
            }
        }
        else
        {
            _playerValues[path] = (JObject)values.DeepClone();
            Trim(_playerValues, MaxPlayerMemory);
        }

        Save();
    }

    private const int MaxPlayerMemory = 300;

    private static void Trim<T>(Dictionary<string, T> memory, int limit)
    {
        while (memory.Count > limit)
        {
            memory.Remove(memory.Keys.First());
        }
    }

    /// <summary>Library ids of the nodes added most recently, newest first (max 12).</summary>
    public IReadOnlyList<string> RecentNodeIds => _recentNodeIds;

    /// <summary>Remembers that a node was just added, so quick search can offer it first next time.</summary>
    /// <param name="nodeId">Library id of the node.</param>
    public void AddRecentNode(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId) || (_recentNodeIds.Count > 0 && _recentNodeIds[0] == nodeId))
        {
            return;
        }

        _recentNodeIds.Remove(nodeId);
        _recentNodeIds.Insert(0, nodeId);
        if (_recentNodeIds.Count > MaxRecentNodes)
        {
            _recentNodeIds.RemoveRange(MaxRecentNodes, _recentNodeIds.Count - MaxRecentNodes);
        }

        Save();
    }

    /// <summary>Recently opened/saved .dyc paths, most recent first (max 10, missing files pruned).</summary>
    public IReadOnlyList<string> RecentFiles => _recentFiles;

    /// <summary>True when the library shows a description line under each node name (default on).</summary>
    public bool ShowLibraryDescriptions => _showLibraryDescriptions;

    /// <summary>Persists the library descriptions toggle.</summary>
    /// <param name="show">True to show the description line under each node name.</param>
    public void SetShowLibraryDescriptions(bool show)
    {
        if (_showLibraryDescriptions != show)
        {
            _showLibraryDescriptions = show;
            Save();
        }
    }

    /// <summary>True to highlight the selected node's output items in Navisworks (default off).</summary>
    public bool PreviewSelection => _previewSelection;

    /// <summary>Persists the "highlight selected node's elements" toggle.</summary>
    /// <param name="enabled">True to mirror the selected node's output into the Navisworks selection.</param>
    public void SetPreviewSelection(bool enabled)
    {
        if (_previewSelection != enabled)
        {
            _previewSelection = enabled;
            Save();
        }
    }

    /// <summary>What double-clicking the empty canvas does ("string", "number", "note" or "none"; default "string").</summary>
    public string DoubleClickAction => _doubleClickAction;

    /// <summary>Persists the empty-canvas double-click action.</summary>
    /// <param name="action">Action id ("string", "number", "note" or "none").</param>
    public void SetDoubleClickAction(string action)
    {
        if (!string.IsNullOrEmpty(action) && _doubleClickAction != action)
        {
            _doubleClickAction = action;
            Save();
        }
    }

    /// <summary>Selected UI colour palette id (default "DyncameloDark").</summary>
    public string PaletteId => _paletteId;

    /// <summary>Persists the selected UI colour palette.</summary>
    /// <param name="paletteId">Palette id from the palette catalog.</param>
    public void SetPaletteId(string paletteId)
    {
        if (!string.IsNullOrEmpty(paletteId) && _paletteId != paletteId)
        {
            _paletteId = paletteId;
            Save();
        }
    }

    /// <summary>True to commit number fields while dragging (re-running the graph live) instead of on release (default false).</summary>
    public bool LiveScrubEvaluation => _liveScrubEvaluation;

    /// <summary>Persists the live-scrub choice.</summary>
    /// <param name="live">True to evaluate while scrubbing.</param>
    public void SetLiveScrubEvaluation(bool live)
    {
        if (_liveScrubEvaluation != live)
        {
            _liveScrubEvaluation = live;
            Save();
        }
    }

    /// <summary>Raised after any preference changed and was saved (used to refresh dependent views).</summary>
    public event EventHandler? Changed;

    /// <summary>A stored text preference, or <paramref name="fallback"/> when unset.</summary>
    /// <param name="key">Preference key.</param>
    /// <param name="fallback">Value when nothing is stored.</param>
    public string GetString(string key, string fallback)
    {
        return _values.TryGetValue(key, out var token) && token.Type == JTokenType.String ? token.Value<string>() ?? fallback : fallback;
    }

    /// <summary>A stored on/off preference, or <paramref name="fallback"/> when unset.</summary>
    /// <param name="key">Preference key.</param>
    /// <param name="fallback">Value when nothing is stored.</param>
    public bool GetBool(string key, bool fallback)
    {
        return _values.TryGetValue(key, out var token) && token.Type == JTokenType.Boolean ? token.Value<bool>() : fallback;
    }

    /// <summary>Stores a text or on/off preference (null removes it, restoring the default) and saves.</summary>
    /// <param name="key">Preference key.</param>
    /// <param name="value">A string, a bool or null.</param>
    public void SetValue(string key, object? value)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        var changed = value == null ? _values.Remove(key) : SetToken(key, JToken.FromObject(value));
        if (changed)
        {
            Save();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool SetToken(string key, JToken token)
    {
        if (_values.TryGetValue(key, out var existing) && JToken.DeepEquals(existing, token))
        {
            return false;
        }

        _values[key] = token;
        return true;
    }

    /// <summary>User-chosen shortcuts by command id ("edit.undo" → "Ctrl+Z"); commands not listed keep their default.</summary>
    public IReadOnlyDictionary<string, string> ShortcutOverrides => _shortcuts;

    /// <summary>Rebinds one command (an empty chord unbinds it) and saves.</summary>
    /// <param name="commandId">Command id from the catalogue.</param>
    /// <param name="chord">"Ctrl+Shift+L"-style chord, or empty for none.</param>
    public void SetShortcut(string commandId, string chord)
    {
        if (string.IsNullOrEmpty(commandId))
        {
            return;
        }

        chord = chord ?? string.Empty;
        if (_shortcuts.TryGetValue(commandId, out var existing) && existing == chord)
        {
            return;
        }

        _shortcuts[commandId] = chord;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Restores the default shortcut of one command, or of every command when <paramref name="commandId"/> is null.</summary>
    /// <param name="commandId">Command id, or null for all.</param>
    public void ResetShortcuts(string? commandId = null)
    {
        var changed = commandId == null ? _shortcuts.Count > 0 : _shortcuts.Remove(commandId);
        if (commandId == null)
        {
            _shortcuts.Clear();
        }

        if (changed)
        {
            Save();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Restores every preference and shortcut to its default (favourites and recent files are kept).</summary>
    public void ResetPreferences()
    {
        _showLibraryDescriptions = true;
        _doubleClickAction = "string";
        _previewSelection = false;
        _confirmUntrustedRuns = true;
        _paletteId = "DyncameloDark";
        _liveScrubEvaluation = false;
        _values.Clear();
        _shortcuts.Clear();
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

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
                RecentNodeIds = new List<string>(_recentNodeIds),
                PlayerFolders = new List<string>(_playerFolders),
                PlayerConfirmed = new Dictionary<string, string>(_playerConfirmed),
                EditorConfirmed = new Dictionary<string, string>(_editorConfirmed),
                ConfirmUntrustedRuns = _confirmUntrustedRuns,
                PlayerValues = new Dictionary<string, JObject>(_playerValues),
                PlayerLastScript = _playerLastScript,
                ShowLibraryDescriptions = _showLibraryDescriptions,
                DoubleClickAction = _doubleClickAction,
                PreviewSelection = _previewSelection,
                PaletteId = _paletteId,
                LiveScrubEvaluation = _liveScrubEvaluation,
                Values = new Dictionary<string, JToken>(_values),
                Shortcuts = new Dictionary<string, string>(_shortcuts),
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
        _recentNodeIds.Clear();
        _playerFolders.Clear();
        _playerConfirmed.Clear();
        _editorConfirmed.Clear();
        _confirmUntrustedRuns = true;
        _playerValues.Clear();
        _playerLastScript = string.Empty;
        _showLibraryDescriptions = true;
        _doubleClickAction = "string";
        _previewSelection = false;
        _paletteId = "DyncameloDark";
        _liveScrubEvaluation = false;
        _values.Clear();
        _shortcuts.Clear();
        if (data == null)
        {
            return;
        }

        if (data.Values != null)
        {
            foreach (var pair in data.Values)
            {
                if (!string.IsNullOrEmpty(pair.Key) && pair.Value != null)
                {
                    _values[pair.Key] = pair.Value;
                }
            }
        }

        if (data.Shortcuts != null)
        {
            foreach (var pair in data.Shortcuts)
            {
                if (!string.IsNullOrEmpty(pair.Key) && pair.Value != null)
                {
                    _shortcuts[pair.Key] = pair.Value;
                }
            }
        }

        _showLibraryDescriptions = data.ShowLibraryDescriptions ?? true;
        _doubleClickAction = string.IsNullOrEmpty(data.DoubleClickAction) ? "string" : data.DoubleClickAction!;
        _previewSelection = data.PreviewSelection ?? false;
        _confirmUntrustedRuns = data.ConfirmUntrustedRuns ?? true;
        _paletteId = string.IsNullOrEmpty(data.PaletteId) ? "DyncameloDark" : data.PaletteId!;
        _liveScrubEvaluation = data.LiveScrubEvaluation ?? false;

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

        if (data.PlayerFolders != null)
        {
            foreach (var folder in data.PlayerFolders)
            {
                if (!string.IsNullOrWhiteSpace(folder) && !ContainsEquals(_playerFolders, folder))
                {
                    _playerFolders.Add(folder);
                }
            }
        }

        if (data.PlayerConfirmed != null)
        {
            foreach (var pair in data.PlayerConfirmed)
            {
                if (!string.IsNullOrEmpty(pair.Key) && !string.IsNullOrEmpty(pair.Value))
                {
                    _playerConfirmed[pair.Key] = pair.Value;
                }
            }
        }

        if (data.EditorConfirmed != null)
        {
            foreach (var pair in data.EditorConfirmed)
            {
                if (!string.IsNullOrEmpty(pair.Key) && !string.IsNullOrEmpty(pair.Value))
                {
                    _editorConfirmed[pair.Key] = pair.Value;
                }
            }
        }

        if (data.PlayerValues != null)
        {
            foreach (var pair in data.PlayerValues)
            {
                if (!string.IsNullOrEmpty(pair.Key) && pair.Value != null)
                {
                    _playerValues[pair.Key] = pair.Value;
                }
            }
        }

        _playerLastScript = data.PlayerLastScript ?? string.Empty;

        if (data.RecentNodeIds != null)
        {
            foreach (var id in data.RecentNodeIds)
            {
                if (!string.IsNullOrEmpty(id) && !_recentNodeIds.Contains(id) && _recentNodeIds.Count < MaxRecentNodes)
                {
                    _recentNodeIds.Add(id);
                }
            }
        }

        PruneRecentFiles();
    }

    /// <summary>The per-user folder holding settings and the error log (%APPDATA%\Dyncamelo).</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dyncamelo");

    /// <summary>
    /// The folder of autosaved copies (%APPDATA%\Dyncamelo\recovery). A service made on an explicit settings file keeps its
    /// autosaves beside it, so tests and side-by-side installs never see each other's.
    /// </summary>
    public string RecoveryDirectory =>
        string.Equals(_settingsPath, GetDefaultSettingsPath(), StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(DefaultDirectory, "recovery")
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_settingsPath)) ?? string.Empty, Path.GetFileNameWithoutExtension(_settingsPath) + "-recovery");

    private static string GetDefaultSettingsPath() => Path.Combine(DefaultDirectory, "ui-settings.json");

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

        [JsonProperty("playerFolders")]
        public List<string>? PlayerFolders { get; set; }

        [JsonProperty("playerConfirmed")]
        public Dictionary<string, string>? PlayerConfirmed { get; set; }

        [JsonProperty("editorConfirmed")]
        public Dictionary<string, string>? EditorConfirmed { get; set; }

        [JsonProperty("confirmUntrustedRuns")]
        public bool? ConfirmUntrustedRuns { get; set; }

        [JsonProperty("playerValues")]
        public Dictionary<string, JObject>? PlayerValues { get; set; }

        [JsonProperty("playerLastScript")]
        public string? PlayerLastScript { get; set; }

        [JsonProperty("recentNodeIds")]
        public List<string>? RecentNodeIds { get; set; }

        [JsonProperty("showLibraryDescriptions")]
        public bool? ShowLibraryDescriptions { get; set; }

        public bool? PreviewSelection { get; set; }

        [JsonProperty("doubleClickAction")]
        public string? DoubleClickAction { get; set; }

        [JsonProperty("paletteId")]
        public string? PaletteId { get; set; }

        [JsonProperty("liveScrubEvaluation")]
        public bool? LiveScrubEvaluation { get; set; }

        [JsonProperty("values")]
        public Dictionary<string, JToken>? Values { get; set; }

        [JsonProperty("shortcuts")]
        public Dictionary<string, string>? Shortcuts { get; set; }
    }
}
