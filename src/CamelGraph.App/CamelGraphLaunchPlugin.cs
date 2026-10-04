using Autodesk.Navisworks.Api.Plugins;
using NavisApplication = Autodesk.Navisworks.Api.Application;

namespace CamelGraph.App;

/// <summary>
/// The "CamelGraph" ribbon/add-in button: shows and activates the CamelGraph
/// dock pane. Plugin id: "CamelGraph.Launch.DYNC".
/// </summary>
[Plugin("CamelGraph.Launch", "DYNC",
    DisplayName = "CamelGraph",
    ToolTip = "Open the CamelGraph visual programming editor",
    ExtendedToolTip = "Wire nodes on a canvas to automate Navisworks: selection, properties, viewpoints, clash, TimeLiner and more.")]
[AddInPlugin(AddInLocation.AddIn)]
public class CamelGraphLaunchPlugin : AddInPlugin
{
    /// <inheritdoc />
    public override int Execute(params string[] parameters)
    {
        var gui = NavisApplication.Gui;
        if (gui != null)
        {
            gui.SetDockPanePluginVisibility(CamelGraphDockPanePlugin.PluginId, true);
            gui.SetDockPanePluginActive(CamelGraphDockPanePlugin.PluginId);
        }

        return 0;
    }
}
