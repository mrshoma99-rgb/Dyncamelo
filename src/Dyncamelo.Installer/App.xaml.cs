using System;
using System.Linq;
using System.Windows;

namespace Dyncamelo.Installer;

/// <summary>
/// Installer entry point. Interactive by default; command-line modes:
/// <c>/uninstall</c> opens straight on the removal screen (this is what the
/// Add/Remove Programs entry runs), and adding <c>/silent</c> to either mode
/// performs the action without any window (for scripted deployment).
/// </summary>
public partial class App : Application
{
    /// <summary>Whether the process was started in uninstall mode.</summary>
    public static bool UninstallMode { get; private set; }

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = e.Args.Select(a => a.TrimStart('-', '/').ToLowerInvariant()).ToArray();
        UninstallMode = args.Contains("uninstall");
        var silent = args.Contains("silent") || args.Contains("s") || args.Contains("q");

        if (silent)
        {
            RunSilent();
            return;
        }

        new MainWindow().Show();
    }

    private void RunSilent()
    {
        try
        {
            if (UninstallMode)
            {
                InstallerEngine.Uninstall();
            }
            else
            {
                InstallerEngine.Install((_, _) => { });
            }

            Shutdown(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("DyncameloSetup: " + ex.Message);
            Shutdown(1);
        }
    }
}
