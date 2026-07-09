using Autodesk.Navisworks.Api.Plugins;
using NavisApplication = Autodesk.Navisworks.Api.Application;

namespace Dyncamelo.App;

/// <summary>
/// The "Dyncamelo" ribbon/add-in button: shows and activates the Dyncamelo
/// dock pane. Plugin id: "Dyncamelo.Launch.DYNC".
/// </summary>
[Plugin("Dyncamelo.Launch", "DYNC",
    DisplayName = "Dyncamelo",
    ToolTip = "Open the Dyncamelo visual programming editor",
    ExtendedToolTip = "Wire nodes on a canvas to automate Navisworks: selection, properties, viewpoints, clash, TimeLiner and more.")]
[AddInPlugin(AddInLocation.AddIn)]
public class DyncameloLaunchPlugin : AddInPlugin
{
    /// <inheritdoc />
    public override int Execute(params string[] parameters)
    {
        var gui = NavisApplication.Gui;
        if (gui != null)
        {
            gui.SetDockPanePluginVisibility(DyncameloDockPanePlugin.PluginId, true);
            gui.SetDockPanePluginActive(DyncameloDockPanePlugin.PluginId);
        }

        return 0;
    }
}
