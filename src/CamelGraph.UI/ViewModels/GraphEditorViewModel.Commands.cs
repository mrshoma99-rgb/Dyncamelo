using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CamelGraph.Core.Editing;
using CamelGraph.UI.Mvvm;

namespace CamelGraph.UI.ViewModels;

/// <summary>
/// The command surface of the editor: the keymap in force, the command palette (Ctrl+Shift+P) and the sectioned
/// Settings page with shortcut rebinding. Everything here is driven by <see cref="CommandCatalog"/> and
/// <see cref="SettingsCatalog"/>, so a new command or preference appears in the menus, palette, help and settings at once.
/// </summary>
public partial class GraphEditorViewModel
{
    private Keymap _keymap = new Keymap();
    private IReadOnlyList<HelpSection> _helpSections = new List<HelpSection>();

    // palette
    private bool _isPaletteOpen;
    private string _paletteQuery = string.Empty;
    private PaletteEntry? _paletteSelected;
    private ICommand? _togglePaletteCommand;
    private ICommand? _closePaletteCommand;
    private ICommand? _runPaletteEntryCommand;
    private ICommand? _runCommandIdCommand;

    // settings page
    private bool _isSettingsOpen;
    private string _settingsSection = SettingsCatalog.Sections[0];
    private string _settingsFilter = string.Empty;
    private string _shortcutFilter = string.Empty;
    private List<SettingItemViewModel> _settingItems = new List<SettingItemViewModel>();
    private List<ShortcutRowViewModel> _shortcutAllRows = new List<ShortcutRowViewModel>();
    private ShortcutRowViewModel? _capturingRow;
    private ICommand? _toggleSettingsCommand;
    private ICommand? _closeSettingsCommand;
    private ICommand? _selectSettingsSectionCommand;
    private ICommand? _resetPreferencesCommand;
    private ICommand? _resetAllShortcutsCommand;

    /// <summary>Raised after the shortcuts in force changed (the view rebuilds its menus and key router).</summary>
    public event EventHandler? KeymapChanged;

    /// <summary>Maps a catalogue command id to the command that runs it (supplied by the view); null when not available.</summary>
    public Func<string, ICommand?>? CommandResolver { get; set; }

    /// <summary>The element routed commands run against (the canvas).</summary>
    public IInputElement? CommandTarget { get; set; }

    /// <summary>The shortcuts in force: catalogue defaults plus the user's rebindings.</summary>
    public Keymap Keymap => _keymap;

    /// <summary>The keyboard and mouse help, built from the keymap in force.</summary>
    public IReadOnlyList<HelpSection> HelpSections => _helpSections;

    private void InitCommandSurface()
    {
        _keymap = new Keymap(_settings.ShortcutOverrides);
        _helpSections = HelpContent.Build(_keymap);
        _settingItems = SettingsCatalog.All
            .Select(d => new SettingItemViewModel(d, () => GetSetting(d.Id), v => SetSetting(d.Id, v)))
            .ToList();
        _shortcutAllRows = CommandCatalog.All
            .Select(c => new ShortcutRowViewModel(c, BeginShortcutCapture, row => ApplyShortcut(row, string.Empty), ResetShortcut))
            .ToList();
        foreach (var row in _shortcutAllRows)
        {
            row.Refresh(_keymap);
        }

        ShortcutRows = new ObservableCollection<ShortcutRowViewModel>(_shortcutAllRows);
        VisibleSettings = new ObservableCollection<SettingItemViewModel>();
        PaletteResults = new ObservableCollection<PaletteEntry>();
        RebuildVisibleSettings();
        PropertyChanged += OnOwnPropertyChangedForSettings;
    }

    private void OnOwnPropertyChangedForSettings(object? sender, PropertyChangedEventArgs e)
    {
        // A toolbar toggle or a menu check changed a preference: keep the open page in step.
        if (_isSettingsOpen && e.PropertyName != nameof(IsSettingsOpen) && e.PropertyName != nameof(SettingsSection))
        {
            foreach (var item in _settingItems)
            {
                item.Refresh();
            }
        }
    }

    // A change made through Settings, the palette or a reset: refresh everything derived from stored preferences.
    private void RefreshAfterSettingsChange()
    {
        var overrides = _settings.ShortcutOverrides;
        var changedKeymap = !SameOverrides(overrides);
        if (changedKeymap)
        {
            _keymap = new Keymap(overrides);
            _helpSections = HelpContent.Build(_keymap);
            foreach (var row in _shortcutAllRows)
            {
                row.Refresh(_keymap);
            }

            OnPropertyChanged(nameof(Keymap));
            OnPropertyChanged(nameof(HelpSections));
            OnPropertyChanged(nameof(UndoTooltip));
            OnPropertyChanged(nameof(RedoTooltip));
            OnPropertyChanged(nameof(RunTooltip));
            OnPropertyChanged(nameof(MinimapTooltip));
            OnPropertyChanged(nameof(ProblemsTooltip));
            if (_isPaletteOpen)
            {
                RefreshPalette();
            }
            RefreshHint();
            KeymapChanged?.Invoke(this, EventArgs.Empty);
        }

        Views.ScrubNumberBox.WrapPointerAtScreenEdge = ScrubWrapsPointer;

        // The typed preferences keep a copy of the stored value.
        if (_doubleClickAction != _settings.DoubleClickAction)
        {
            _doubleClickAction = _settings.DoubleClickAction;
            OnPropertyChanged(nameof(DoubleClickAction));
        }

        if (_paletteId != _settings.PaletteId)
        {
            _paletteId = _settings.PaletteId;
            OnPropertyChanged(nameof(PaletteId));
        }

        if (_previewSelection != _settings.PreviewSelection)
        {
            _previewSelection = _settings.PreviewSelection;
            OnPropertyChanged(nameof(PreviewSelection));
        }

        if (_confirmUntrustedRuns != _settings.ConfirmUntrustedRuns)
        {
            _confirmUntrustedRuns = _settings.ConfirmUntrustedRuns;
            OnPropertyChanged(nameof(ConfirmUntrustedRuns));
        }

        if (_checkForUpdates != _settings.CheckForUpdates)
        {
            _checkForUpdates = _settings.CheckForUpdates;
            OnPropertyChanged(nameof(CheckForUpdates));
        }

        if (Library.ShowDescriptions != _settings.ShowLibraryDescriptions)
        {
            Library.ShowDescriptions = _settings.ShowLibraryDescriptions;
        }

        UpdateChoiceSelection();
        foreach (var item in _settingItems)
        {
            item.Refresh();
        }
    }

    private bool SameOverrides(IReadOnlyDictionary<string, string> overrides)
    {
        var current = _keymap.OverriddenIds;
        if (current.Count != overrides.Count(p => CommandCatalog.Find(p.Key) != null))
        {
            return false;
        }

        foreach (var pair in overrides)
        {
            if (CommandCatalog.Find(pair.Key) != null &&
                !string.Equals(_keymap.ShortcutOf(pair.Key) ?? string.Empty, NormalChord(pair.Value), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalChord(string chord) => Shortcuts.TryParse(chord, out var parsed) ? parsed.ToString() : string.Empty;

    // ===== running commands by id ====================================================

    /// <summary>Runs a catalogue command by id; returns false when it is unavailable or cannot run right now.</summary>
    /// <param name="commandId">Id from <see cref="CommandCatalog"/>.</param>
    public bool RunCommandById(string commandId)
    {
        var command = CommandResolver?.Invoke(commandId);
        return command != null && CommandInvoker.Run(command, CommandTarget);
    }

    /// <summary>Runs the catalogue command named by the parameter (used by buttons in the Settings page).</summary>
    public ICommand RunCommandIdCommand => _runCommandIdCommand ??= new RelayCommand<string>(id =>
    {
        if (!string.IsNullOrEmpty(id))
        {
            RunCommandById(id!);
        }
    });

    // ===== command palette ===========================================================

    /// <summary>The entries matching the palette text, best first.</summary>
    public ObservableCollection<PaletteEntry> PaletteResults { get; private set; } = new ObservableCollection<PaletteEntry>();

    /// <summary>True while the command palette is shown (Ctrl+Shift+P).</summary>
    public bool IsPaletteOpen
    {
        get => _isPaletteOpen;
        set
        {
            if (SetProperty(ref _isPaletteOpen, value) && value)
            {
                RefreshPalette();
            }
        }
    }

    /// <summary>What was typed into the palette.</summary>
    public string PaletteQuery
    {
        get => _paletteQuery;
        set
        {
            if (SetProperty(ref _paletteQuery, value ?? string.Empty))
            {
                RefreshPalette();
            }
        }
    }

    /// <summary>The highlighted palette entry.</summary>
    public PaletteEntry? PaletteSelected
    {
        get => _paletteSelected;
        set => SetProperty(ref _paletteSelected, value);
    }

    /// <summary>Shows or hides the palette (Ctrl+Shift+P).</summary>
    public ICommand TogglePaletteCommand => _togglePaletteCommand ??= new RelayCommand(() =>
    {
        if (_isPaletteOpen)
        {
            ClosePalette();
        }
        else
        {
            OpenPalette();
        }
    });

    /// <summary>Hides the palette.</summary>
    public ICommand ClosePaletteCommand => _closePaletteCommand ??= new RelayCommand(ClosePalette);

    /// <summary>Runs the palette entry given as the parameter.</summary>
    public ICommand RunPaletteEntryCommand => _runPaletteEntryCommand ??= new RelayCommand<PaletteEntry>(entry => RunPaletteEntry(entry));

    /// <summary>Opens the palette with an empty search.</summary>
    /// <param name="initialQuery">Text to start with ("@" lists the nodes on the canvas).</param>
    public void OpenPalette(string initialQuery = "")
    {
        CloseQuickSearch();
        IsHelpOpen = false;
        IsSettingsOpen = false;
        _paletteQuery = initialQuery ?? string.Empty;
        OnPropertyChanged(nameof(PaletteQuery));
        if (_isPaletteOpen)
        {
            RefreshPalette();          // already open: start over with an empty search
        }
        else
        {
            IsPaletteOpen = true;      // refreshes on opening
        }
    }

    /// <summary>Closes the palette.</summary>
    public void ClosePalette() => IsPaletteOpen = false;

    /// <summary>Moves the highlight up or down the palette list, wrapping at the ends.</summary>
    /// <param name="delta">+1 for the next entry, -1 for the previous.</param>
    public void MovePaletteSelection(int delta)
    {
        if (PaletteResults.Count == 0)
        {
            return;
        }

        var index = _paletteSelected == null ? -1 : PaletteResults.IndexOf(_paletteSelected);
        index = (index + delta + PaletteResults.Count) % PaletteResults.Count;
        PaletteSelected = PaletteResults[index];
    }

    /// <summary>
    /// Closes the palette and runs the entry (the highlighted one when none is given). A preference entry opens the
    /// Settings page filtered to it.
    /// </summary>
    /// <returns>True when something ran.</returns>
    public bool RunPaletteEntry(PaletteEntry? entry = null)
    {
        entry ??= _paletteSelected;
        if (entry == null)
        {
            return false;
        }

        ClosePalette();
        if (entry.IsNode)
        {
            return GoToNode(entry.Node!);
        }

        if (entry.IsSetting)
        {
            var setting = SettingsCatalog.Find(entry.Id);
            OpenSettings(setting == null ? null : setting.Title);
            return true;
        }

        return RunCommandById(entry.Id);
    }

    private void RefreshPalette()
    {
        var commands = CommandCatalog.All.Where(c => c.Id != "help.palette" && IsRunnable(c.Id));
        var entries = new List<PaletteEntry>();

        // "@name" looks for nodes on the canvas only; plain text finds them along with commands and preferences.
        var nodesOnly = _paletteQuery.StartsWith(NodeSearchPrefix, StringComparison.Ordinal);
        if (nodesOnly)
        {
            entries.AddRange(NodeEntries(_paletteQuery));
        }
        else
        {
            foreach (var info in CommandSearch.Rank(_paletteQuery, commands))
            {
                entries.Add(new PaletteEntry(info.Id, info.Title, info.Category, _keymap.ShortcutOf(info.Id) ?? string.Empty, false));
            }

            entries.AddRange(NodeEntries(_paletteQuery).Take(8));
        }

        // Preferences are found the same way, and only when something was typed (the empty list is commands only).
        if (!nodesOnly && _paletteQuery.Trim().Length > 0)
        {
            foreach (var setting in SettingsCatalog.Search(_paletteQuery).Take(8))
            {
                entries.Add(new PaletteEntry(setting.Id, setting.Title, "Settings · " + setting.Section, string.Empty, true));
            }
        }

        PaletteResults.Clear();
        foreach (var entry in entries)
        {
            PaletteResults.Add(entry);
        }

        PaletteSelected = PaletteResults.Count > 0 ? PaletteResults[0] : null;
    }

    private bool IsRunnable(string id)
    {
        var command = CommandResolver?.Invoke(id);
        return command != null && CommandInvoker.CanRun(command, CommandTarget);
    }

    // ===== settings page =============================================================

    /// <summary>The preferences shown right now (a section, or the matches of the search box).</summary>
    public ObservableCollection<SettingItemViewModel> VisibleSettings { get; private set; } = new ObservableCollection<SettingItemViewModel>();

    /// <summary>The shortcut rows shown right now (filtered by <see cref="ShortcutFilter"/>).</summary>
    public ObservableCollection<ShortcutRowViewModel> ShortcutRows { get; private set; } = new ObservableCollection<ShortcutRowViewModel>();

    /// <summary>The names of the Settings sections, in order.</summary>
    public IReadOnlyList<string> SettingsSectionNames => SettingsCatalog.Sections;

    /// <summary>True while the Settings page is shown.</summary>
    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set
        {
            if (SetProperty(ref _isSettingsOpen, value))
            {
                if (value)
                {
                    foreach (var item in _settingItems)
                    {
                        item.Refresh();
                    }
                }
                else
                {
                    CancelShortcutCapture();
                }
            }
        }
    }

    /// <summary>The section listed on the page.</summary>
    public string SettingsSection
    {
        get => _settingsSection;
        set
        {
            if (!string.IsNullOrEmpty(value) && SetProperty(ref _settingsSection, value))
            {
                CancelShortcutCapture();
                RebuildVisibleSettings();
            }
        }
    }

    /// <summary>Text typed into the settings search box; matches are listed across all sections.</summary>
    public string SettingsFilter
    {
        get => _settingsFilter;
        set
        {
            if (SetProperty(ref _settingsFilter, value ?? string.Empty))
            {
                RebuildVisibleSettings();
            }
        }
    }

    /// <summary>Text typed into the shortcut search box.</summary>
    public string ShortcutFilter
    {
        get => _shortcutFilter;
        set
        {
            if (SetProperty(ref _shortcutFilter, value ?? string.Empty))
            {
                RebuildShortcutRows();
            }
        }
    }

    /// <summary>True when the page shows the Appearance extras (the colour palette).</summary>
    public bool ShowPaletteChoices => _settingsFilter.Trim().Length == 0 && _settingsSection == "Appearance";

    /// <summary>True when the page shows the shortcut table.</summary>
    public bool ShowShortcutTable => _settingsFilter.Trim().Length == 0 && _settingsSection == "Shortcuts";

    /// <summary>True when the page shows the diagnostics buttons.</summary>
    public bool ShowDiagnosticsPage => _settingsFilter.Trim().Length == 0 && _settingsSection == "Diagnostics";

    /// <summary>Shows or hides the Settings page.</summary>
    public ICommand ToggleSettingsCommand => _toggleSettingsCommand ??= new RelayCommand(() =>
    {
        if (_isSettingsOpen)
        {
            IsSettingsOpen = false;
        }
        else
        {
            OpenSettings(null);
        }
    });

    /// <summary>Hides the Settings page.</summary>
    public ICommand CloseSettingsCommand => _closeSettingsCommand ??= new RelayCommand(() => IsSettingsOpen = false);

    /// <summary>Shows the section named by the parameter.</summary>
    public ICommand SelectSettingsSectionCommand => _selectSettingsSectionCommand ??= new RelayCommand<string>(name =>
    {
        if (!string.IsNullOrEmpty(name))
        {
            SettingsFilter = string.Empty;
            SettingsSection = name!;
        }
    });

    /// <summary>Restores every preference and shortcut to its default.</summary>
    public ICommand ResetPreferencesCommand => _resetPreferencesCommand ??= new RelayCommand(() =>
    {
        _settings.ResetPreferences();
        StatusMessage = "All settings and shortcuts are back to their defaults.";
    });

    /// <summary>Restores the default shortcut of every command.</summary>
    public ICommand ResetAllShortcutsCommand => _resetAllShortcutsCommand ??= new RelayCommand(() =>
    {
        CancelShortcutCapture();
        _settings.ResetShortcuts();
    });

    /// <summary>Opens the Settings page, optionally searching for a preference.</summary>
    /// <param name="filter">Text for the search box, or null to show the current section.</param>
    public void OpenSettings(string? filter)
    {
        CloseQuickSearch();
        IsHelpOpen = false;
        ClosePalette();
        SettingsFilter = filter ?? string.Empty;
        IsSettingsOpen = true;
    }

    private void RebuildVisibleSettings()
    {
        IEnumerable<SettingItemViewModel> items;
        if (_settingsFilter.Trim().Length > 0)
        {
            var ids = new HashSet<string>(SettingsCatalog.Search(_settingsFilter).Select(s => s.Id));
            items = _settingItems.Where(i => ids.Contains(i.Id));
        }
        else
        {
            items = _settingItems.Where(i => i.Section == _settingsSection);
        }

        VisibleSettings.Clear();
        foreach (var item in items)
        {
            VisibleSettings.Add(item);
        }

        OnPropertyChanged(nameof(ShowPaletteChoices));
        OnPropertyChanged(nameof(ShowShortcutTable));
        OnPropertyChanged(nameof(ShowDiagnosticsPage));
    }

    private void RebuildShortcutRows()
    {
        var words = _shortcutFilter.ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        ShortcutRows.Clear();
        foreach (var row in _shortcutAllRows)
        {
            var hay = (row.Title + " " + row.Category + " " + row.Shortcut).ToLowerInvariant();
            if (words.All(hay.Contains))
            {
                ShortcutRows.Add(row);
            }
        }
    }

    private object GetSetting(string id)
    {
        switch (id)
        {
            case "density": return NodeDensity;
            case "cbGlyphs": return ColourBlindGlyphs;
            case "libraryPanel": return IsLibraryVisible;
            case "libraryDescriptions": return Library.ShowDescriptions;
            case "nodePreviews": return ShowNodePreviews;
            case "showGrid": return ShowGrid;
            case "snapToGrid": return SnapToGrid;
            case "straightWires": return StraightWires;
            case "minimap": return MinimapMode;
            case "layeredArrange": return UseLayeredArrange;
            case "scrubSpeed": return ScrubSpeed;
            case "liveScrub": return LiveScrubEvaluation;
            case "hideUnusedDefault": return HideUnusedByDefault;
            case "autoOffset": return AutoOffsetOnInsert;
            case "deleteReconnectsReroutes": return DeleteReconnectsReroutes;
            case "escCancels": return EscCancelsRun;
            case "autosave": return IsAutosaveEnabled;
            case "uiScale": return UiScale;
            case "statusHints": return ShowStatusHints;
            case "wireFocus": return FocusSelectedWires;
            case "scrubWrap": return ScrubWrapsPointer;
            case "emptyHints": return ShowEmptyCanvasHints;
            case "previewSelection": return PreviewSelection;
            case "confirmUntrustedRuns": return ConfirmUntrustedRuns;
            case "checkForUpdates": return CheckForUpdates;
            case "doubleClick": return DoubleClickAction;
            default: throw new ArgumentException("Unknown setting '" + id + "'.", nameof(id));
        }
    }

    private void SetSetting(string id, object value)
    {
        switch (id)
        {
            case "density": NodeDensity = (string)value; break;
            case "cbGlyphs": ColourBlindGlyphs = (bool)value; break;
            case "libraryPanel": IsLibraryVisible = (bool)value; break;
            case "libraryDescriptions": Library.ShowDescriptions = (bool)value; break;
            case "nodePreviews": ShowNodePreviews = (bool)value; break;
            case "showGrid": ShowGrid = (bool)value; break;
            case "snapToGrid": SnapToGrid = (bool)value; break;
            case "straightWires": StraightWires = (bool)value; break;
            case "minimap": MinimapMode = (string)value; break;
            case "layeredArrange": UseLayeredArrange = (bool)value; break;
            case "scrubSpeed": ScrubSpeed = (string)value; break;
            case "liveScrub": LiveScrubEvaluation = (bool)value; break;
            case "hideUnusedDefault": HideUnusedByDefault = (bool)value; break;
            case "autoOffset": AutoOffsetOnInsert = (bool)value; break;
            case "deleteReconnectsReroutes": DeleteReconnectsReroutes = (bool)value; break;
            case "escCancels": EscCancelsRun = (bool)value; break;
            case "autosave": IsAutosaveEnabled = (bool)value; break;
            case "uiScale": UiScale = (string)value; break;
            case "statusHints": ShowStatusHints = (bool)value; break;
            case "wireFocus": FocusSelectedWires = (bool)value; break;
            case "scrubWrap": ScrubWrapsPointer = (bool)value; break;
            case "emptyHints": ShowEmptyCanvasHints = (bool)value; break;
            case "previewSelection": PreviewSelection = (bool)value; break;
            case "confirmUntrustedRuns": ConfirmUntrustedRuns = (bool)value; break;
            case "checkForUpdates": CheckForUpdates = (bool)value; break;
            case "doubleClick": DoubleClickAction = (string)value; break;
            default: throw new ArgumentException("Unknown setting '" + id + "'.", nameof(id));
        }
    }

    /// <summary>Reads a preference by its <see cref="SettingsCatalog"/> id (bool for toggles, option value for choices).</summary>
    /// <param name="id">Settings id.</param>
    public object ReadSetting(string id) => GetSetting(id);

    /// <summary>Writes a preference by its <see cref="SettingsCatalog"/> id.</summary>
    /// <param name="id">Settings id.</param>
    /// <param name="value">A bool for toggles, an option value for choices.</param>
    public void WriteSetting(string id, object value) => SetSetting(id, value);

    // ===== shortcut rebinding ========================================================

    /// <summary>True while a shortcut row waits for the next key press.</summary>
    public bool IsCapturingShortcut => _capturingRow != null;

    /// <summary>The row that is waiting for a key press, or null.</summary>
    public ShortcutRowViewModel? CapturingShortcut => _capturingRow;

    /// <summary>Starts listening for the new chord of a command.</summary>
    /// <param name="row">The row whose Change button was pressed.</param>
    public void BeginShortcutCapture(ShortcutRowViewModel row)
    {
        CancelShortcutCapture();
        row.Message = string.Empty;
        row.IsCapturing = true;
        _capturingRow = row;
        OnPropertyChanged(nameof(IsCapturingShortcut));
        OnPropertyChanged(nameof(CapturingShortcut));
    }

    /// <summary>Stops listening without changing anything.</summary>
    public void CancelShortcutCapture()
    {
        if (_capturingRow == null)
        {
            return;
        }

        _capturingRow.IsCapturing = false;
        _capturingRow = null;
        OnPropertyChanged(nameof(IsCapturingShortcut));
        OnPropertyChanged(nameof(CapturingShortcut));
    }

    /// <summary>
    /// Offers the pressed chord to the row that is listening. When the keymap refuses it the reason is shown on the
    /// row and listening continues; otherwise the binding is stored and listening ends.
    /// </summary>
    /// <param name="chord">Chord text such as "Ctrl+Shift+L"; empty unbinds the command.</param>
    /// <returns>True when the binding was accepted.</returns>
    public bool CommitShortcutCapture(string chord)
    {
        var row = _capturingRow;
        if (row == null)
        {
            return false;
        }

        if (!ApplyShortcut(row, chord))
        {
            return false;
        }

        CancelShortcutCapture();
        return true;
    }

    private bool ApplyShortcut(ShortcutRowViewModel row, string chord)
    {
        var reason = _keymap.Validate(row.CommandId, chord);
        if (reason != null)
        {
            row.Message = reason;
            return false;
        }

        row.Message = string.Empty;
        var next = _keymap.With(row.CommandId, chord);
        if (next.ContainsKey(row.CommandId))
        {
            _settings.SetShortcut(row.CommandId, next[row.CommandId]);
        }
        else
        {
            _settings.ResetShortcuts(row.CommandId);
        }

        return true;
    }

    private void ResetShortcut(ShortcutRowViewModel row)
    {
        row.Message = string.Empty;
        if (_capturingRow == row)
        {
            CancelShortcutCapture();
        }

        _settings.ResetShortcuts(row.CommandId);
    }
}
