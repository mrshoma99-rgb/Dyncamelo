using System;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Autodesk.Navisworks.Api.Plugins;
using Dyncamelo.Navisworks;
using Dyncamelo.UI.Views;
using NavisApplication = Autodesk.Navisworks.Api.Application;

namespace Dyncamelo.App;

/// <summary>
/// The dockable Script Player: lists the scripts in the scripts folders and runs one with a small form for its inputs, without
/// loading the node editor. Plugin id: "Dyncamelo.PlayerPane.DYNC".
/// </summary>
[Plugin("Dyncamelo.PlayerPane", "DYNC",
    DisplayName = "Dyncamelo Player",
    ToolTip = "Run Dyncamelo scripts without opening the node editor")]
[DockPanePlugin(420, 640, AutoScroll = false, FixedSize = false, MinimumWidth = 320, MinimumHeight = 300)]
public class DyncameloPlayerDockPanePlugin : DockPanePlugin
{
    /// <summary>Plugin id used with SetDockPanePluginVisibility.</summary>
    public const string PluginId = "Dyncamelo.PlayerPane.DYNC";

    private PaneKeyGuard? _keyGuard;

    /// <summary>Shows and activates the Player pane (creating it the first time).</summary>
    public static void Show()
    {
        var gui = NavisApplication.Gui;
        if (gui != null)
        {
            gui.SetDockPanePluginVisibility(PluginId, true);
            gui.SetDockPanePluginActive(PluginId);
        }
    }

    /// <inheritdoc />
    public override Control CreateControlPane()
    {
        var player = DyncameloHost.Player;
        player.OpenInEditorRequested -= OnOpenInEditorRequested;
        player.OpenInEditorRequested += OnOpenInEditorRequested;

        var control = new PlayerControl { ViewModel = player };
        var host = new ElementHost
        {
            Child = control,
            Dock = DockStyle.Fill,
        };
        host.CreateControl();

        // Navisworks binds Ctrl+Z, Ctrl+V, Delete … itself; typing into the Player's boxes must not edit the model.
        _keyGuard?.Dispose();
        _keyGuard = new PaneKeyGuard(host, control);
        return host;
    }

    /// <inheritdoc />
    public override void DestroyControlPane(Control pane)
    {
        if (DyncameloHost.PlayerCreated)
        {
            DyncameloHost.Player.OpenInEditorRequested -= OnOpenInEditorRequested;
        }

        _keyGuard?.Dispose();
        _keyGuard = null;
        pane.Dispose();
    }

    // "Edit" in the Player: show the editor and hand it the script (asking first if it has unsaved work).
    private static void OnOpenInEditorRequested(object? sender, string path)
    {
        var editor = DyncameloHost.Editor;
        if (editor == null)
        {
            // The editor pane is created by the call below and picks the script up as it starts.
            DyncameloHost.PendingEditorPath = path;
        }

        var gui = NavisApplication.Gui;
        if (gui != null)
        {
            gui.SetDockPanePluginVisibility(DyncameloDockPanePlugin.PluginId, true);
            gui.SetDockPanePluginActive(DyncameloDockPanePlugin.PluginId);
        }

        editor?.OpenDroppedFiles(new[] { path });
    }
}
