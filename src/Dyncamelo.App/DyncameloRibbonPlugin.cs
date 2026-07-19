using System;
using Autodesk.Navisworks.Api.Plugins;

namespace Dyncamelo.App;

/// <summary>
/// Ribbon commands for Dyncamelo, presented on the shared "BIMCamel" ribbon tab
/// (same tab id as the other BIMCamel tools so their panels co-locate).
/// The ribbon layout (Dyncamelo.xaml) must deploy to an en-US\ subfolder next to
/// the plugin DLL, and the icons to a Resources\ subfolder — the
/// LayoutNavisworksPlugin / DeployToBundle build targets arrange both.
/// </summary>
[Plugin("Dyncamelo.Command", "DYNC",
    DisplayName = "Dyncamelo",
    ToolTip = "Dyncamelo — visual programming for Navisworks")]
[RibbonLayout("Dyncamelo.xaml")]
[RibbonTab("ID_Tab_BIMCamel")]
[Command("ID_Button_Dyncamelo",
    DisplayName = "Dyncamelo",
    Icon = "Resources\\dyncamelo_16.png",
    LargeIcon = "Resources\\dyncamelo_32.png",
    ToolTip = "Open the Dyncamelo node editor panel")]
[Command("ID_Button_DyncameloAbout",
    DisplayName = "About",
    Icon = "Resources\\info_16.png",
    LargeIcon = "Resources\\info_32.png",
    ToolTip = "About Dyncamelo")]
public class DyncameloRibbonPlugin : CommandHandlerPlugin
{
    // The dock-pane lookup key is "<pluginId>.<developerId>".
    private const string DockPaneKey = "Dyncamelo.DockPane.DYNC";

    /// <inheritdoc />
    public override int ExecuteCommand(string commandId, params string[] parameters)
    {
        try
        {
            if (commandId == "ID_Button_DyncameloAbout")
            {
                // Version comes from the assembly (stamped by Directory.Build.props),
                // never a hard-coded string — Major.Minor.Build, e.g. "0.5.1".
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                var versionText = version == null
                    ? "dev"
                    : version.Major + "." + version.Minor + "." + version.Build;

                ShowInfo(
                    "Dyncamelo — visual programming for Autodesk Navisworks.\n\n" +
                    "Wire nodes on a canvas to automate selection, properties, search, " +
                    "viewpoints, clash, TimeLiner and more — no code.\n\n" +
                    "Visit bimcamel.com for guides, the node library and updates:\n" +
                    "https://www.bimcamel.com/plugins/dyncamelo\n\n" +
                    "Also from BIMCamel: the free IFC Exporter plug-in (fast Navisworks → IFC):\n" +
                    "https://www.bimcamel.com/Export-Navisworks-to-Ifc\n\n" +
                    "Version " + versionText + "   ·   Part of the BIMCamel toolset");
                return 0;
            }

            var record = Autodesk.Navisworks.Api.Application.Plugins.FindPlugin(DockPaneKey);
            if (record == null)
            {
                ShowError(
                    "The Dyncamelo editor panel is not registered with Navisworks " +
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
                ShowError("The Dyncamelo panel registered as an unexpected plugin type: " +
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
                ShowError("The Dyncamelo panel failed to load (LoadedPlugin was " +
                          (dockRecord.LoadedPlugin?.GetType().Name ?? "null") + ").");
            }
        }
        catch (Exception ex)
        {
            ShowError("Dyncamelo could not open the node editor panel:\n\n" + ex);
        }

        return 0;
    }

    /// <inheritdoc />
    public override CommandState CanExecuteCommand(string commandId) =>
        new CommandState { IsEnabled = true, IsVisible = true };

    // Navisworks swallows exceptions thrown from a command handler, so any failure
    // here would otherwise be invisible. Surface it to the user instead.
    private static void ShowError(string message) =>
        System.Windows.Forms.MessageBox.Show(
            message, "Dyncamelo",
            System.Windows.Forms.MessageBoxButtons.OK,
            System.Windows.Forms.MessageBoxIcon.Warning);

    private static void ShowInfo(string message) =>
        System.Windows.Forms.MessageBox.Show(
            message, "Dyncamelo",
            System.Windows.Forms.MessageBoxButtons.OK,
            System.Windows.Forms.MessageBoxIcon.Information);
}
