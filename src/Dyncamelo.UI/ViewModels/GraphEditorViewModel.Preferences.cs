using System.Linq;
using System.Collections.Generic;
using System.Windows.Input;
using Dyncamelo.UI.Mvvm;

namespace Dyncamelo.UI.ViewModels;

/// <summary>Keys of the preferences kept in <see cref="Dyncamelo.UI.Services.UiSettingsService"/> (besides the older typed ones).</summary>
public static class SettingKeys
{
    /// <summary>"auto" (show from <see cref="GraphEditorViewModel.MinimapAutoNodeCount"/> nodes), "on" or "off".</summary>
    public const string Minimap = "minimap";

    /// <summary>"compact", "normal" or "comfortable" row height.</summary>
    public const string Density = "density";

    /// <summary>Draw wires as straight lines instead of curves.</summary>
    public const string StraightWires = "straightWires";

    /// <summary>Show a letter naming the type inside each socket.</summary>
    public const string ColourBlindGlyphs = "cbGlyphs";

    /// <summary>Hide unwired optional inputs on nodes that have no choice of their own.</summary>
    public const string HideUnusedByDefault = "hideUnusedDefault";

    /// <summary>"slow", "normal" or "fast" number-field dragging.</summary>
    public const string ScrubSpeed = "scrubSpeed";

    /// <summary>Snap dragged nodes to the grid.</summary>
    public const string SnapToGrid = "snapToGrid";

    /// <summary>Push nodes right to make room when one is inserted on a wire.</summary>
    public const string AutoOffset = "autoOffset";

    /// <summary>Deleting a reroute keeps the wire (otherwise it is removed with it).</summary>
    public const string DeleteReconnectsReroutes = "deleteReconnectsReroutes";

    /// <summary>Show the canvas grid lines.</summary>
    public const string ShowGrid = "showGrid";

    /// <summary>Arrange with the layered (MSAGL) engine; off = built-in columns.</summary>
    public const string LayeredArrange = "layeredArrange";

    /// <summary>Show the node library panel on the left of the canvas.</summary>
    public const string LibraryVisible = "libraryVisible";

    /// <summary>Esc cancels a running graph.</summary>
    public const string EscCancels = "escCancels";

    /// <summary>Keep an autosaved copy of a graph with unsaved changes.</summary>
    public const string Autosave = "autosave";

    /// <summary>Dragging a number past the screen edge continues on the other side.</summary>
    public const string ScrubWrap = "scrubWrap";

    /// <summary>Draw the wires of the selected nodes heavier and the others fainter.</summary>
    public const string WireFocus = "wireFocus";

    /// <summary>Show the hint line in the status bar.</summary>
    public const string StatusHints = "statusHints";

    /// <summary>Show getting-started hints on an empty canvas.</summary>
    public const string EmptyHints = "emptyHints";

    /// <summary>Window scale in percent.</summary>
    public const string UiScale = "uiScale";
}

/// <summary>
/// Preferences as the editor sees them: each one reads and writes the persisted setting and raises
/// change notifications, so a menu item, the settings page and the command palette all drive the same property.
/// </summary>
public partial class GraphEditorViewModel
{
    /// <summary>Node count from which the minimap is shown when its mode is "auto".</summary>
    public const int MinimapAutoNodeCount = 40;

    private ICommand? _toggleMinimapCommand;

    private static readonly IReadOnlyList<string> AllPreferenceProperties = new[]
    {
        nameof(MinimapMode), nameof(IsMinimapVisible), nameof(MinimapTooltip), nameof(NodeDensity), nameof(RowBaseHeight), nameof(StraightWires),
        nameof(WireLowDetail), nameof(ColourBlindGlyphs), nameof(HideUnusedByDefault), nameof(ScrubSpeed), nameof(ScrubPixelsPerStep),
        nameof(SnapToGrid), nameof(GridCellSize), nameof(AutoOffsetOnInsert), nameof(DeleteReconnectsReroutes), nameof(ShowGrid),
        nameof(UseLayeredArrange), nameof(IsLibraryVisible), nameof(IsLibraryHidden), nameof(EscCancelsRun), nameof(IsAutosaveEnabled),
        nameof(ShowStatusHints), nameof(ShowEmptyCanvasHints), nameof(FocusSelectedWires), nameof(ScrubWrapsPointer), nameof(UiScale), nameof(UiScaleFactor),
    };

    /// <summary>Minimap mode: "auto", "on" or "off".</summary>
    public string MinimapMode
    {
        get => _settings.GetString(SettingKeys.Minimap, "auto");
        set => SetPreference(SettingKeys.Minimap, value, "auto", nameof(MinimapMode), nameof(IsMinimapVisible), nameof(MinimapTooltip));
    }

    /// <summary>True when the minimap is shown.</summary>
    public bool IsMinimapVisible
    {
        get
        {
            var mode = MinimapMode;
            return mode == "on" || (mode == "auto" && NodeCount >= MinimapAutoNodeCount);
        }
    }

    /// <summary>Shows or hides the minimap (Ctrl+M): sets an explicit on/off, ending the automatic behaviour.</summary>
    public ICommand ToggleMinimapCommand => _toggleMinimapCommand ??= new RelayCommand(() => MinimapMode = IsMinimapVisible ? "off" : "on");

    private ICommand? _toggleLibraryCommand;

    /// <summary>True while the node library panel is shown (default); persisted.</summary>
    public bool IsLibraryVisible
    {
        get => _settings.GetBool(SettingKeys.LibraryVisible, true);
        set => SetPreference(SettingKeys.LibraryVisible, value, true, nameof(IsLibraryVisible), nameof(IsLibraryHidden));
    }

    /// <summary>True while the library panel is hidden (shows the edge handle that brings it back).</summary>
    public bool IsLibraryHidden => !IsLibraryVisible;

    /// <summary>Hides or shows the node library panel (Ctrl+B).</summary>
    public ICommand ToggleLibraryCommand => _toggleLibraryCommand ??= new RelayCommand(() => IsLibraryVisible = !IsLibraryVisible);

    /// <summary>True when Esc stops a running graph (default).</summary>
    public bool EscCancelsRun
    {
        get => _settings.GetBool(SettingKeys.EscCancels, true);
        set => SetPreference(SettingKeys.EscCancels, value, true, nameof(EscCancelsRun));
    }

    /// <summary>Row height preset: "compact", "normal" or "comfortable".</summary>
    public string NodeDensity
    {
        get => _settings.GetString(SettingKeys.Density, "normal");
        set => SetPreference(SettingKeys.Density, value, "normal", nameof(NodeDensity), nameof(RowBaseHeight));
    }

    /// <summary>Height of a normal node row in pixels, from <see cref="NodeDensity"/>.</summary>
    public double RowBaseHeight
    {
        get
        {
            switch (NodeDensity)
            {
                case "compact": return 18d;
                case "comfortable": return 28d;
                default: return 22d;
            }
        }
    }

    /// <summary>True to draw wires as straight lines.</summary>
    public bool StraightWires
    {
        get => _settings.GetBool(SettingKeys.StraightWires, false);
        set => SetPreference(SettingKeys.StraightWires, value, false, nameof(StraightWires), nameof(WireLowDetail));
    }

    /// <summary>True when wires are drawn as straight hairlines (the preference, or the overview zoom).</summary>
    public bool WireLowDetail => StraightWires || IsOverviewLod;

    /// <summary>True to draw a type letter in every socket (colour-blind aid).</summary>
    public bool ColourBlindGlyphs
    {
        get => _settings.GetBool(SettingKeys.ColourBlindGlyphs, false);
        set => SetPreference(SettingKeys.ColourBlindGlyphs, value, false, nameof(ColourBlindGlyphs));
    }

    /// <summary>True to hide unwired optional inputs on nodes that have no choice of their own.</summary>
    public bool HideUnusedByDefault
    {
        get => _settings.GetBool(SettingKeys.HideUnusedByDefault, false);
        set => SetPreference(SettingKeys.HideUnusedByDefault, value, false, nameof(HideUnusedByDefault));
    }

    /// <summary>Number-field drag speed: "slow", "normal" or "fast".</summary>
    public string ScrubSpeed
    {
        get => _settings.GetString(SettingKeys.ScrubSpeed, "normal");
        set => SetPreference(SettingKeys.ScrubSpeed, value, "normal", nameof(ScrubSpeed), nameof(ScrubPixelsPerStep));
    }

    /// <summary>Pixels of mouse travel per step of a number field.</summary>
    public double ScrubPixelsPerStep
    {
        get
        {
            switch (ScrubSpeed)
            {
                case "slow": return 16d;
                case "fast": return 4d;
                default: return 8d;
            }
        }
    }

    /// <summary>True to snap dragged nodes to the grid.</summary>
    public bool SnapToGrid
    {
        get => _settings.GetBool(SettingKeys.SnapToGrid, true);
        set => SetPreference(SettingKeys.SnapToGrid, value, true, nameof(SnapToGrid), nameof(GridCellSize));
    }

    /// <summary>Grid cell size for dragging: 15 with snapping, 1 without.</summary>
    public uint GridCellSize => SnapToGrid ? 15u : 1u;

    /// <summary>True to shift nodes downstream to make room when one is inserted on a wire.</summary>
    public bool AutoOffsetOnInsert
    {
        get => _settings.GetBool(SettingKeys.AutoOffset, true);
        set => SetPreference(SettingKeys.AutoOffset, value, true, nameof(AutoOffsetOnInsert));
    }

    /// <summary>True when deleting a reroute keeps the wire flowing.</summary>
    public bool DeleteReconnectsReroutes
    {
        get => _settings.GetBool(SettingKeys.DeleteReconnectsReroutes, true);
        set => SetPreference(SettingKeys.DeleteReconnectsReroutes, value, true, nameof(DeleteReconnectsReroutes));
    }

    /// <summary>True to draw the canvas grid lines.</summary>
    public bool ShowGrid
    {
        get => _settings.GetBool(SettingKeys.ShowGrid, true);
        set => SetPreference(SettingKeys.ShowGrid, value, true, nameof(ShowGrid));
    }

    /// <summary>False to always use the built-in column layout for Arrange.</summary>
    public bool UseLayeredArrange
    {
        get => _settings.GetBool(SettingKeys.LayeredArrange, true);
        set => SetPreference(SettingKeys.LayeredArrange, value, true, nameof(UseLayeredArrange));
    }

    /// <summary>Re-reads socket and wire colours after a palette change (light palettes use darker family colours).</summary>
    public void RefreshPortColours()
    {
        foreach (var node in Items.OfType<NodeViewModel>())
        {
            foreach (var connector in node.Inputs.Concat(node.Outputs))
            {
                connector.RefreshBrushes();
            }
        }

        foreach (var wire in Connections)
        {
            wire.RefreshBrushes();
        }
    }

    // Writes a preference (the default value clears it so old settings files stay small) and tells the views.
    private void SetPreference(string key, object value, object fallback, params string[] changed)
    {
        _settings.SetValue(key, Equals(value, fallback) ? null : value);
        foreach (var name in changed)
        {
            OnPropertyChanged(name);
        }
    }

    // A reset or a change made elsewhere: refresh everything that depends on a stored preference.
    private void OnSettingsChanged(object? sender, System.EventArgs e)
    {
        foreach (var name in AllPreferenceProperties)
        {
            OnPropertyChanged(name);
        }

        OnPropertyChanged(nameof(LiveScrubEvaluation));
        RefreshAfterSettingsChange();
    }
}
