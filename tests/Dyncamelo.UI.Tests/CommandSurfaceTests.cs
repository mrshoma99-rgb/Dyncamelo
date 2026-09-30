using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Loader;
using Dyncamelo.UI.Mvvm;
using Dyncamelo.UI.Services;
using Dyncamelo.UI.ViewModels;
using Dyncamelo.UI.Views;
using Xunit;

namespace Dyncamelo.UI.Tests;

/// <summary>The keymap, command palette and Settings page as the editor view model runs them.</summary>
public class CommandSurfaceTests
{
    private static GraphEditorViewModel NewEditor(string? path = null)
    {
        var registry = NodeRegistry.CreateDefault();
        var settings = new UiSettingsService(path ?? Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
        return new GraphEditorViewModel(registry, new StubDialogs(), settings);
    }

    private static Dictionary<string, int> Resolver(GraphEditorViewModel vm, params string[] ids)
    {
        var ran = new Dictionary<string, int>();
        var commands = ids.ToDictionary(id => id, id => (ICommand)new RelayCommand(() => ran[id] = ran.TryGetValue(id, out var n) ? n + 1 : 1));
        vm.CommandResolver = id => commands.TryGetValue(id, out var c) ? c : null;
        return ran;
    }

    // ----- settings -------------------------------------------------------------

    [Fact]
    public void EverySettingInTheCatalogueIsBoundToTheEditor()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            foreach (var setting in SettingsCatalog.All)
            {
                var current = vm.ReadSetting(setting.Id);
                if (setting.Kind == SettingKind.Toggle)
                {
                    var flipped = !(bool)current;
                    vm.WriteSetting(setting.Id, flipped);
                    Assert.Equal(flipped, (bool)vm.ReadSetting(setting.Id));
                    vm.WriteSetting(setting.Id, current);
                }
                else
                {
                    foreach (var option in setting.Options)
                    {
                        vm.WriteSetting(setting.Id, option.Value);
                        Assert.Equal(option.Value, vm.ReadSetting(setting.Id));
                    }

                    Assert.NotNull(setting.OptionFor((string)current));
                    vm.WriteSetting(setting.Id, current);
                }
            }
        });
    }

    [Fact]
    public void ADefaultEditorReportsTheCatalogueDefaults()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            foreach (var setting in SettingsCatalog.All)
            {
                if (setting.Kind == SettingKind.Toggle)
                {
                    Assert.True(setting.DefaultToggle == (bool)vm.ReadSetting(setting.Id), setting.Id);
                }
                else
                {
                    Assert.Equal(setting.DefaultChoice, vm.ReadSetting(setting.Id));
                }
            }
        });
    }

    [Fact]
    public void TheSettingsPageListsASectionEditsThePropertyAndFollowsOutsideChanges()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            vm.OpenSettings(null);
            vm.SettingsSection = "Canvas";
            var grid = vm.VisibleSettings.Single(i => i.Id == "showGrid");
            Assert.True(grid.BoolValue);
            Assert.False(grid.IsModified);

            grid.BoolValue = false;
            Assert.False(vm.ShowGrid);
            Assert.True(grid.IsModified);

            vm.ShowGrid = true;                    // changed by a menu or command
            Assert.True(grid.BoolValue);
            Assert.False(grid.IsModified);

            var minimap = vm.VisibleSettings.Single(i => i.Id == "minimap");
            minimap.SelectedLabel = "Always";
            Assert.Equal("on", vm.MinimapMode);
            Assert.True(minimap.IsModified);
            minimap.ResetCommand.Execute(null);
            Assert.Equal("auto", vm.MinimapMode);
        });
    }

    [Fact]
    public void SearchingSettingsListsMatchesFromEverySection()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            vm.OpenSettings("grid");
            Assert.True(vm.IsSettingsOpen);
            Assert.Contains(vm.VisibleSettings, i => i.Id == "showGrid");
            Assert.Contains(vm.VisibleSettings, i => i.Id == "snapToGrid");
            Assert.All(vm.VisibleSettings, i => Assert.Contains("grid", (i.Title + i.Description).ToLowerInvariant()));
            Assert.False(vm.ShowPaletteChoices);
            Assert.False(vm.ShowShortcutTable);

            vm.SelectSettingsSectionCommand.Execute("Shortcuts");
            Assert.Equal(string.Empty, vm.SettingsFilter);
            Assert.True(vm.ShowShortcutTable);
            Assert.Empty(vm.VisibleSettings);

            vm.SelectSettingsSectionCommand.Execute("Appearance");
            Assert.True(vm.ShowPaletteChoices);
            Assert.All(vm.VisibleSettings, i => Assert.Equal("Appearance", i.Section));
        });
    }

    [Fact]
    public void ResettingPreferencesBringsBackDefaultsAndShortcuts()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            vm.ShowGrid = false;
            vm.NodeDensity = "compact";
            vm.OpenSettings(null);
            vm.BeginShortcutCapture(vm.ShortcutRows.Single(r => r.CommandId == "node.mute"));
            Assert.True(vm.CommitShortcutCapture("Ctrl+Alt+M"));
            Assert.Equal("Ctrl+Alt+M", vm.Keymap.ShortcutOf("node.mute"));

            vm.ResetPreferencesCommand.Execute(null);

            Assert.True(vm.ShowGrid);
            Assert.Equal("normal", vm.NodeDensity);
            Assert.Equal(22d, vm.RowBaseHeight);
            Assert.Equal("M", vm.Keymap.ShortcutOf("node.mute"));
        });
    }

    // ----- shortcut rebinding -------------------------------------------------------

    [Fact]
    public void RebindingAShortcutReachesTheKeymapRouterMenusHelpAndDisk()
    {
        StaHost.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json");
            var vm = NewEditor(path);
            var raised = 0;
            vm.KeymapChanged += (s, e) => raised++;

            var row = vm.ShortcutRows.Single(r => r.CommandId == "node.mute");
            row.ChangeCommand.Execute(null);
            Assert.True(vm.IsCapturingShortcut);
            Assert.True(row.IsCapturing);
            Assert.Equal("Press the new shortcut…", row.Display);

            Assert.True(vm.CommitShortcutCapture("Ctrl+Alt+M"));

            Assert.False(vm.IsCapturingShortcut);
            Assert.Equal(1, raised);
            Assert.Equal("Ctrl+Alt+M", row.Shortcut);
            Assert.True(row.IsCustom);
            Assert.Equal("Ctrl+Alt+M", vm.Keymap.ShortcutOf("node.mute"));

            var router = new ShortcutRouter(vm.Keymap);
            Assert.Equal("node.mute", router.Find(Key.M, ModifierKeys.Control | ModifierKeys.Alt)?.Id);
            Assert.Null(router.Find(Key.M, ModifierKeys.None));

            Assert.Contains(vm.HelpSections.SelectMany(s => s.Lines), l => l.Keys == "Ctrl+Alt+M");

            // A new editor on the same settings file has the same shortcut.
            var again = NewEditor(path);
            Assert.Equal("Ctrl+Alt+M", again.Keymap.ShortcutOf("node.mute"));
        });
    }

    [Fact]
    public void AConflictingChordIsRefusedWithAReasonAndTheRowKeepsListening()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var row = vm.ShortcutRows.Single(r => r.CommandId == "node.mute");
            vm.BeginShortcutCapture(row);

            Assert.False(vm.CommitShortcutCapture("Ctrl+Z"));
            Assert.True(row.HasMessage);
            Assert.Contains("already used", row.Message);
            Assert.True(vm.IsCapturingShortcut);
            Assert.Equal("M", vm.Keymap.ShortcutOf("node.mute"));

            vm.CancelShortcutCapture();
            Assert.False(vm.IsCapturingShortcut);
            Assert.False(row.IsCapturing);
        });
    }

    [Fact]
    public void ClearingAndResettingAShortcutWork()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var row = vm.ShortcutRows.Single(r => r.CommandId == "node.mute");

            row.ClearCommand.Execute(null);
            Assert.Null(vm.Keymap.ShortcutOf("node.mute"));
            Assert.Equal("none", row.Display);
            Assert.True(row.IsCustom);
            Assert.Null(new ShortcutRouter(vm.Keymap).Find(Key.M, ModifierKeys.None));

            row.ResetCommand.Execute(null);
            Assert.Equal("M", vm.Keymap.ShortcutOf("node.mute"));
            Assert.False(row.IsCustom);
        });
    }

    [Fact]
    public void BindingTheDefaultChordAgainIsNotACustomShortcut()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var row = vm.ShortcutRows.Single(r => r.CommandId == "node.mute");
            row.ClearCommand.Execute(null);
            vm.BeginShortcutCapture(row);
            Assert.True(vm.CommitShortcutCapture("M"));
            Assert.False(row.IsCustom);
            Assert.Empty(vm.Keymap.OverriddenIds);
        });
    }

    [Fact]
    public void ToolbarTooltipsShowTheShortcutInForce()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            Assert.Equal("Nothing to undo (Ctrl+Z)", vm.UndoTooltip);
            Assert.Equal("Run the graph (F5)", vm.RunTooltip);

            vm.BeginShortcutCapture(vm.ShortcutRows.Single(r => r.CommandId == "graph.run"));
            Assert.True(vm.CommitShortcutCapture("F9"));
            Assert.Equal("Run the graph (F9)", vm.RunTooltip);

            vm.ShortcutRows.Single(r => r.CommandId == "edit.undo").ClearCommand.Execute(null);
            Assert.Equal("Nothing to undo", vm.UndoTooltip);
        });
    }

    [Fact]
    public void TheShortcutTableCanBeFiltered()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            Assert.Equal(CommandCatalog.All.Count, vm.ShortcutRows.Count);

            vm.ShortcutFilter = "mute";
            Assert.NotEmpty(vm.ShortcutRows);
            Assert.All(vm.ShortcutRows, r => Assert.Contains("mute", (r.Title + r.Category + r.Shortcut).ToLowerInvariant()));

            vm.ShortcutFilter = string.Empty;
            Assert.Equal(CommandCatalog.All.Count, vm.ShortcutRows.Count);
        });
    }

    [Fact]
    public void ChordsFromKeyPressesRoundTripThroughTheKeymap()
    {
        var samples = new[]
        {
            (Key.L, ModifierKeys.Control | ModifierKeys.Shift), (Key.F9, ModifierKeys.None), (Key.D5, ModifierKeys.Control),
            (Key.Home, ModifierKeys.None), (Key.Delete, ModifierKeys.Control), (Key.OemPlus, ModifierKeys.Alt),
        };
        foreach (var (key, mods) in samples)
        {
            var chord = ShortcutRouter.ChordOf(key, mods);
            Assert.True(Shortcuts.TryParse(chord.ToString(), out var parsed), chord.ToString());
            Assert.Equal(chord, parsed);
        }

        Assert.True(ShortcutRouter.IsModifierKey(Key.LeftCtrl));
        Assert.False(ShortcutRouter.IsModifierKey(Key.A));
    }

    // ----- command palette --------------------------------------------------------------

    [Fact]
    public void ThePaletteListsOnlyCommandsThatCanRunAndFindsThemByWords()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var ran = Resolver(vm, "node.mute", "view.fit", "graph.arrangeall", "help.palette");

            vm.OpenPalette();
            Assert.True(vm.IsPaletteOpen);
            Assert.Equal(
                new[] { "graph.arrangeall", "node.mute", "view.fit" },
                vm.PaletteResults.Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray());
            Assert.DoesNotContain(vm.PaletteResults, e => e.Id == "help.palette");

            vm.PaletteQuery = "arr";
            Assert.Equal("graph.arrangeall", vm.PaletteSelected?.Id);

            Assert.True(vm.RunPaletteEntry());
            Assert.False(vm.IsPaletteOpen);
            Assert.Equal(1, ran["graph.arrangeall"]);
        });
    }

    [Fact]
    public void PaletteEntriesShowTheShortcutInForce()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            Resolver(vm, "node.mute");
            vm.OpenPalette();
            Assert.Equal("M", vm.PaletteResults.Single().Shortcut);

            var row = vm.ShortcutRows.Single(r => r.CommandId == "node.mute");
            vm.BeginShortcutCapture(row);
            vm.CommitShortcutCapture("Ctrl+Alt+M");
            vm.OpenPalette();
            Assert.Equal("Ctrl+Alt+M", vm.PaletteResults.Single().Shortcut);
        });
    }

    [Fact]
    public void ThePaletteFindsSettingsAndOpensThePageOnThem()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            Resolver(vm, "node.mute");
            vm.OpenPalette();
            vm.PaletteQuery = "grid";
            var entry = vm.PaletteResults.First(e => e.IsSetting && e.Id == "showGrid");
            Assert.StartsWith("Settings", entry.Category);

            Assert.True(vm.RunPaletteEntry(entry));

            Assert.False(vm.IsPaletteOpen);
            Assert.True(vm.IsSettingsOpen);
            Assert.Contains(vm.VisibleSettings, i => i.Id == "showGrid");
        });
    }

    [Fact]
    public void AnEmptyPaletteQueryListsCommandsOnly()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            Resolver(vm, "node.mute", "view.fit");
            vm.OpenPalette();
            Assert.All(vm.PaletteResults, e => Assert.False(e.IsSetting));
        });
    }

    [Fact]
    public void ThePaletteHighlightWrapsAtTheEnds()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            Resolver(vm, "node.mute", "view.fit", "graph.arrangeall");
            vm.OpenPalette();
            var first = vm.PaletteSelected;

            vm.MovePaletteSelection(-1);
            Assert.Equal(vm.PaletteResults[vm.PaletteResults.Count - 1], vm.PaletteSelected);
            vm.MovePaletteSelection(1);
            Assert.Equal(first, vm.PaletteSelected);
        });
    }

    [Fact]
    public void OverlaysAreExclusive()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            Resolver(vm, "node.mute");
            vm.IsHelpOpen = true;
            vm.OpenSettings(null);
            Assert.False(vm.IsHelpOpen);
            Assert.True(vm.IsSettingsOpen);

            vm.OpenPalette();
            Assert.False(vm.IsSettingsOpen);
            Assert.True(vm.IsPaletteOpen);

            vm.OpenSettings("grid");
            Assert.False(vm.IsPaletteOpen);
            vm.IsSettingsOpen = false;
            Assert.False(vm.IsCapturingShortcut);
        });
    }

    [Fact]
    public void ClosingTheSettingsPageStopsListeningForAShortcut()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            vm.OpenSettings(null);
            vm.BeginShortcutCapture(vm.ShortcutRows[0]);
            Assert.True(vm.IsCapturingShortcut);
            vm.IsSettingsOpen = false;
            Assert.False(vm.IsCapturingShortcut);
        });
    }

    [Fact]
    public void RunCommandByIdRunsTheResolvedCommandAndRejectsUnknownOnes()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var ran = Resolver(vm, "view.fit");
            Assert.True(vm.RunCommandById("view.fit"));
            Assert.False(vm.RunCommandById("view.minimap"));
            vm.RunCommandIdCommand.Execute("view.fit");
            Assert.Equal(2, ran["view.fit"]);
        });
    }

    [Fact]
    public void EveryCommandOfTheCatalogueCanBeRebound()
    {
        // Parity: no command is fixed, and none is missing from the rebinding table.
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            Assert.Equal(CommandCatalog.All.Select(c => c.Id), vm.ShortcutRows.Select(r => r.CommandId));
        });
    }
}

/// <summary>The palette, Settings page and rebinding as the real control shows and routes them.</summary>
public class CommandSurfaceViewTests
{
    private sealed class Host : IDisposable
    {
        public GraphEditorViewModel Vm = null!;
        public System.Windows.Window Window = null!;
        public DyncameloEditorControl Control => (DyncameloEditorControl)Window.Content;

        public void Dispose() => StaHost.Run(() => Window.Close());
    }

    private static Host Build()
    {
        var host = new Host();
        StaHost.Run(() =>
        {
            var registry = NodeRegistry.CreateDefault();
            var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
            host.Vm = new GraphEditorViewModel(registry, new StubDialogs(), settings);
            host.Window = new System.Windows.Window
            {
                Width = 1400,
                Height = 900,
                Content = new DyncameloEditorControl { ViewModel = host.Vm },
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStyle = System.Windows.WindowStyle.None,
            };
            host.Window.Show();
        });
        StaHost.Flush();
        return host;
    }

    private static IEnumerable<System.Windows.Controls.MenuItem> AllMenuItems(System.Windows.Controls.ItemsControl root)
    {
        foreach (var item in root.Items.OfType<System.Windows.Controls.MenuItem>())
        {
            yield return item;
            foreach (var child in AllMenuItems(item))
            {
                yield return child;
            }
        }
    }

    [Fact]
    public void CtrlShiftPTogglesThePaletteThroughTheHostKeyPath()
    {
        using var host = Build();
        StaHost.Run(() =>
        {
            host.Control.ModifierProvider = () => ModifierKeys.Control | ModifierKeys.Shift;
            Assert.True(host.Control.WantsHostKey(Key.P));

            Assert.True(host.Control.ProcessHostKey(Key.P));
            Assert.True(host.Vm.IsPaletteOpen);
            Assert.NotEmpty(host.Vm.PaletteResults);            // the view supplied the command resolver

            host.Control.ModifierProvider = () => ModifierKeys.None;
            Assert.True(host.Control.WantsHostKey(Key.Escape));
            Assert.True(host.Control.ProcessHostKey(Key.Escape));
            Assert.False(host.Vm.IsPaletteOpen);
            Assert.False(host.Control.WantsHostKey(Key.Escape));
        });
    }

    [Fact]
    public void EveryPaletteCommandRunsFromTheViewResolver()
    {
        using var host = Build();
        StaHost.Run(() =>
        {
            host.Vm.OpenPalette();
            var ids = host.Vm.PaletteResults.Select(e => e.Id).ToList();
            Assert.Contains("file.new", ids);
            Assert.Contains("graph.arrangeall", ids);
            Assert.Contains("view.minimap", ids);
            Assert.DoesNotContain("help.palette", ids);

            // Running by id reaches the same commands the menus use.
            host.Vm.ClosePalette();
            Assert.True(host.Vm.RunCommandById("view.minimap"));
            Assert.Equal("on", host.Vm.MinimapMode);
            Assert.True(host.Vm.RunCommandById("view.settings"));
            Assert.True(host.Vm.IsSettingsOpen);
        });
    }

    [Fact]
    public void ARebindingIsRoutedByTheViewAndShownInTheMenus()
    {
        using var host = Build();
        StaHost.Run(() =>
        {
            var menu = (System.Windows.Controls.Menu)host.Control.FindName("HeaderMenu");
            var mute = CommandCatalog.Find("node.mute")!;
            Assert.Contains(AllMenuItems(menu), m => m.InputGestureText == "M" && (m.Header as System.Windows.Controls.TextBlock)?.Text == mute.Title);

            host.Vm.OpenSettings(null);
            host.Vm.BeginShortcutCapture(host.Vm.ShortcutRows.Single(r => r.CommandId == "node.mute"));

            // While recording, every key belongs to the recorder — including chords that are otherwise commands.
            host.Control.ModifierProvider = () => ModifierKeys.Control | ModifierKeys.Alt;
            Assert.True(host.Control.WantsHostKey(Key.Z));
            Assert.True(host.Control.ProcessHostKey(Key.M));
            Assert.False(host.Vm.IsCapturingShortcut);
            Assert.Equal("Ctrl+Alt+M", host.Vm.Keymap.ShortcutOf("node.mute"));

            Assert.Contains(AllMenuItems(menu), m => m.InputGestureText == "Ctrl+Alt+M" && (m.Header as System.Windows.Controls.TextBlock)?.Text == mute.Title);
            Assert.DoesNotContain(AllMenuItems(menu), m => m.InputGestureText == "M" && (m.Header as System.Windows.Controls.TextBlock)?.Text == mute.Title);

            // The router now honours the new chord and no longer the old one.
            host.Vm.IsSettingsOpen = false;
            host.Control.ModifierProvider = () => ModifierKeys.None;
            Assert.False(host.Control.WantsHostKey(Key.M));
            host.Control.ModifierProvider = () => ModifierKeys.Control | ModifierKeys.Alt;
            Assert.True(host.Control.WantsHostKey(Key.M));
        });
    }

    [Fact]
    public void TheGridSettingSwitchesTheCanvasBackground()
    {
        using var host = Build();
        StaHost.Run(() =>
        {
            var editor = (Nodify.NodifyEditor)host.Control.FindName("Editor");
            Assert.IsType<System.Windows.Media.DrawingBrush>(editor.Background);

            host.Vm.ShowGrid = false;
            Assert.IsType<System.Windows.Media.SolidColorBrush>(editor.Background);

            host.Vm.ShowGrid = true;
            Assert.IsType<System.Windows.Media.DrawingBrush>(editor.Background);

            host.Vm.ShowGrid = false;
            host.Vm.ResetPreferencesCommand.Execute(null);
            Assert.IsType<System.Windows.Media.DrawingBrush>(editor.Background);
        });
    }

    [Fact]
    public void TheCanvasContextMenuFollowsRebinding()
    {
        using var host = Build();
        StaHost.Run(() =>
        {
            var editor = (Nodify.NodifyEditor)host.Control.FindName("Editor");
            string Gesture(string id) => editor.ContextMenu.Items.OfType<System.Windows.Controls.MenuItem>().Single(m => (string?)m.Tag == id).InputGestureText;

            Assert.Equal("Ctrl+D", Gesture("edit.duplicate"));

            host.Vm.BeginShortcutCapture(host.Vm.ShortcutRows.Single(r => r.CommandId == "edit.duplicate"));
            Assert.True(host.Vm.CommitShortcutCapture("Ctrl+Alt+D"));
            Assert.Equal("Ctrl+Alt+D", Gesture("edit.duplicate"));

            host.Vm.ResetAllShortcutsCommand.Execute(null);
            Assert.Equal("Ctrl+D", Gesture("edit.duplicate"));
        });
    }

    [Fact]
    public void EscapeAndBackspaceDriveTheRecorder()
    {
        using var host = Build();
        StaHost.Run(() =>
        {
            host.Control.ModifierProvider = () => ModifierKeys.None;
            var row = host.Vm.ShortcutRows.Single(r => r.CommandId == "node.mute");

            host.Vm.BeginShortcutCapture(row);
            Assert.True(host.Control.ProcessHostKey(Key.LeftCtrl));         // a lone modifier is ignored
            Assert.True(host.Vm.IsCapturingShortcut);
            Assert.True(host.Control.ProcessHostKey(Key.Escape));
            Assert.False(host.Vm.IsCapturingShortcut);
            Assert.Equal("M", host.Vm.Keymap.ShortcutOf("node.mute"));

            host.Vm.BeginShortcutCapture(row);
            Assert.True(host.Control.ProcessHostKey(Key.Back));
            Assert.Null(host.Vm.Keymap.ShortcutOf("node.mute"));
        });
    }

    [Fact]
    public void TheSettingsPageAndPaletteAreVisibleOnlyWhileOpen()
    {
        using var host = Build();
        StaHost.Run(() =>
        {
            var box = (System.Windows.Controls.TextBox)host.Control.FindName("PaletteBox");
            Assert.Equal(System.Windows.Visibility.Collapsed, FindAncestorBorder(box).Visibility);

            host.Vm.OpenPalette();
            Assert.Equal(System.Windows.Visibility.Visible, FindAncestorBorder(box).Visibility);
            host.Vm.ClosePalette();
            Assert.Equal(System.Windows.Visibility.Collapsed, FindAncestorBorder(box).Visibility);
        });
    }

    private static System.Windows.Controls.Border FindAncestorBorder(System.Windows.DependencyObject start)
    {
        var current = System.Windows.Media.VisualTreeHelper.GetParent(start);
        while (current != null)
        {
            if (current is System.Windows.Controls.Border border && border.Panel_ZIndex() == 960)
            {
                return border;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        throw new InvalidOperationException("Palette container not found.");
    }
}

internal static class BorderExtensions
{
    public static int Panel_ZIndex(this System.Windows.UIElement element) => System.Windows.Controls.Panel.GetZIndex(element);
}
