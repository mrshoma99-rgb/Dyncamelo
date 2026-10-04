using Autodesk.Navisworks.Api.Plugins;

namespace Dyncamelo.App;

/// <summary>
/// Runs a Dyncamelo script by path: <c>Execute("C:\\Scripts\\audit.dyc")</c>. It has no ribbon button — other add-ins, the
/// Navisworks Automation API (<c>ExecuteAddInPlugin</c>) and the Batch Utility call it by its id, "Dyncamelo.Run.DYNC". The
/// script runs exactly as it does in the Player; a script that changes the model asks the first time.
/// </summary>
[Plugin("Dyncamelo.Run", "DYNC",
    DisplayName = "CamelGraph Run Script",
    ToolTip = "Run a CamelGraph script given its file path")]
[AddInPlugin(AddInLocation.None)]
public class DyncameloRunPlugin : AddInPlugin
{
    /// <summary>Runs the script named by the first parameter.</summary>
    /// <param name="parameters">The path of the .dyc file.</param>
    /// <returns>0 when the script ran without a failed node, 1 otherwise (including a missing path).</returns>
    public override int Execute(params string[] parameters)
    {
        if (parameters == null || parameters.Length == 0)
        {
            return 1;
        }

        return DyncameloHost.Player.RunScript(parameters[0]) ? 0 : 1;
    }
}
