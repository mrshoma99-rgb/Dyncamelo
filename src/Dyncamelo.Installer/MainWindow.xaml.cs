using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Dyncamelo.Installer;

/// <summary>
/// The single installer window. Four states (welcome, progress, done, error)
/// live in one borderless black-and-white layout; code-behind switches
/// between them — the flow is too small to justify MVVM.
/// </summary>
public partial class MainWindow : Window
{
    private bool _uninstalling;

    /// <summary>Creates the window and applies the launch mode.</summary>
    public MainWindow()
    {
        InitializeComponent();

        VersionText.Text = "v" + InstallerEngine.SetupVersion();
        InstallPathText.Text = "Installs for this user only (no admin rights): " + InstallerEngine.BundleDir;

        var installed = InstallerEngine.InstalledVersion();
        if (installed != null)
        {
            RemoveButton.Visibility = Visibility.Visible;
            var display = Version.TryParse(installed, out var v)
                ? v.Major + "." + v.Minor + "." + Math.Max(v.Build, 0)
                : installed;
            InstallButton.Content = "Update install (v" + display + " found)";
        }

        if (InstallerEngine.IsNavisworksRunning())
        {
            RunningWarning.Visibility = Visibility.Visible;
        }

        if (App.UninstallMode)
        {
            // Launched from Add/Remove Programs: confirm, then remove.
            Loaded += (_, _) => Remove_Click(this, new RoutedEventArgs());
        }
    }

    // ------------------------------------------------------------- actions

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        _uninstalling = false;
        ShowPanel(ProgressPanel);
        ProgressTitle.Text = "Installing…";

        Task.Run(() =>
        {
            try
            {
                InstallerEngine.Install((percent, status) => Dispatcher.Invoke(() =>
                {
                    Progress.Value = percent;
                    ProgressStatus.Text = status;
                }));
                Dispatcher.Invoke(ShowInstallDone);
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => ShowError(ex.Message));
            }
        });
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            this,
            "Remove Dyncamelo from this machine?\n\nYour saved .dyc graphs are not touched.",
            "Dyncamelo Setup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm == MessageBoxResult.Yes)
        {
            _uninstalling = true;
            RunUninstall();
        }
    }

    private void RunUninstall()
    {
        ShowPanel(ProgressPanel);
        ProgressTitle.Text = "Removing…";
        Progress.IsIndeterminate = true;

        Task.Run(() =>
        {
            try
            {
                InstallerEngine.Uninstall();
                Dispatcher.Invoke(() =>
                {
                    Progress.IsIndeterminate = false;
                    DoneTitle.Text = "Removed.";
                    DoneBody.Text = "Dyncamelo has been uninstalled. Your saved .dyc graphs were left untouched.";
                    ShowPanel(DonePanel);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    Progress.IsIndeterminate = false;
                    ShowError(ex.Message);
                });
            }
        });
    }

    private void ShowInstallDone()
    {
        DoneTitle.Text = "Installed.";
        DoneBody.Text = InstallerEngine.IsNavisworksRunning()
            ? "Restart Navisworks Manage or Simulate 2024 — the BIMCamel ribbon tab appears with the Dyncamelo button."
            : "Start Navisworks Manage or Simulate 2024 — the BIMCamel ribbon tab appears with the Dyncamelo button.";
        ShowPanel(DonePanel);
    }

    private void ShowError(string message)
    {
        ErrorBody.Text = message;
        ShowPanel(ErrorPanel);
    }

    private void ShowPanel(UIElement active)
    {
        WelcomePanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Collapsed;
        DonePanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        active.Visibility = Visibility.Visible;
    }

    // -------------------------------------------------------------- chrome

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (_uninstalling)
        {
            RunUninstall();
        }
        else
        {
            Install_Click(sender, e);
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Website_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://www.bimcamel.com/plugins/dyncamelo")
        {
            UseShellExecute = true,
        });
}
