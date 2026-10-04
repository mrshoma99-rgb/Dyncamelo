using System;
using Autodesk.Navisworks.Api.Plugins;

namespace Dyncamelo.App;

/// <summary>
/// Ribbon commands for Dyncamelo, presented on the "BIMCamel" ribbon tab.
/// The ribbon layout (Dyncamelo.xaml) must deploy to an en-US\ subfolder next to
/// the plugin DLL, and the icons to a Resources\ subfolder — the
/// LayoutNavisworksPlugin / DeployToBundle build targets arrange both.
/// </summary>
[Plugin("Dyncamelo.Command", "DYNC",
    DisplayName = "CamelGraph",
    ToolTip = "CamelGraph — visual programming for Navisworks")]
[RibbonLayout("Dyncamelo.xaml")]
[RibbonTab("ID_Tab_BIMCamel")]
[Command("ID_Button_Dyncamelo",
    DisplayName = "CamelGraph",
    Icon = "Resources\\dyncamelo_16.png",
    LargeIcon = "Resources\\dyncamelo_32.png",
    ToolTip = "Open the CamelGraph node editor panel")]
[Command("ID_Button_DyncameloPlayer",
    DisplayName = "Player",
    Icon = "Resources\\player_16.png",
    LargeIcon = "Resources\\player_32.png",
    ToolTip = "Run CamelGraph scripts without opening the node editor")]
[Command("ID_Button_DyncameloAbout",
    DisplayName = "About",
    // The camel logo, same as the IFC exporter's About button — every BIMCamel
    // tool uses the brand mark for About (camel_*.png already ships in Resources).
    Icon = "Resources\\camel_16.png",
    LargeIcon = "Resources\\camel_32.png",
    ToolTip = "About CamelGraph")]
public class DyncameloRibbonPlugin : CommandHandlerPlugin
{
    // The dock-pane lookup key is "<pluginId>.<developerId>".
    private const string DockPaneKey = "Dyncamelo.DockPane.DYNC";

    /// <inheritdoc />
    public override int ExecuteCommand(string commandId, params string[] parameters)
    {
        try
        {
            if (commandId == "ID_Button_DyncameloPlayer")
            {
                DyncameloPlayerDockPanePlugin.Show();
                return 0;
            }

            if (commandId == "ID_Button_DyncameloAbout")
            {
                // The shared BIMCamel About window (same design as the IFC exporter's and the
                // installer); version comes from the assembly, stamped by Directory.Build.props.
                AboutDialog.Show();
                return 0;
            }

            var record = Autodesk.Navisworks.Api.Application.Plugins.FindPlugin(DockPaneKey);
            if (record == null)
            {
                ShowError(
                    "The CamelGraph editor panel is not registered with Navisworks " +
                    "(looked up \"" + DockPaneKey + "\" and found nothing).\n\n" +
                    "This usually means the Dyncamelo.App.dll in this Navisworks year folder " +
                    "was built against a different Navisworks release. A DLL built for the " +
                    "wrong year still loads its ribbon button but its dock pane silently " +
                    "fails to register.\n\n" +
                    "Fix: install the build that matches this Navisworks version (2024 = API v21).");
                return 0;
            }

            if (!(record is DockPanePluginRecord dockRecord))
            {
                ShowError("The CamelGraph panel registered as an unexpected plugin type: " +
                          record.GetType().Name + ".");
                return 0;
            }

            if (!dockRecord.IsLoaded)
            {
                dockRecord.LoadPlugin();
            }

            if (dockRecord.LoadedPlugin is DockPanePlugin dockPane)
            {
                dockPane.Visible = !dockPane.Visible;
            }
            else
            {
                ShowError("The CamelGraph panel failed to load (LoadedPlugin was " +
                          (dockRecord.LoadedPlugin?.GetType().Name ?? "null") + ").");
            }
        }
        catch (Exception ex)
        {
            ShowError("CamelGraph could not open the node editor panel:\n\n" + ex);
        }

        return 0;
    }

    // Navisworks swallows exceptions thrown from a command handler, so any failure
    // here would otherwise be invisible. Surface it to the user instead.
    private static void ShowError(string message) =>
        System.Windows.Forms.MessageBox.Show(
            message, "CamelGraph",
            System.Windows.Forms.MessageBoxButtons.OK,
            System.Windows.Forms.MessageBoxIcon.Warning);
}
