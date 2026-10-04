using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CamelGraph.Core.Loader;
using CamelGraph.Nodes;
using CamelGraph.UI.Services;
using CamelGraph.UI.ViewModels;
using CamelGraph.UI.Views;

namespace CamelGraph.UI.HostProbe;

/// <summary>
/// The editor the way Navisworks hosts it: a WinForms <see cref="System.Windows.Forms.Integration.ElementHost"/> on a form, with no
/// WPF <see cref="Application"/> in the process (Roamer is not a WPF application) and no WPF <see cref="Window"/> around the
/// control. Adds one node of every kind the way the library panel does, then selects in the library, pumping the message loop as it
/// goes. Usage: <c>CamelGraph.UI.HostProbe.exe &lt;palette&gt; &lt;collapsed: true|false&gt;</c>. The last line written is
/// <c>RESULT: OK ...</c> or <c>RESULT: FAILED ...</c>; the exit code is 0 for OK.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var palette = args.Length > 0 ? args[0] : "CamelGraphDark";
        var collapsed = args.Length > 1 && string.Equals(args[1], "true", StringComparison.OrdinalIgnoreCase);

        string report;
        try
        {
            report = Run(palette, collapsed);
        }
        catch (Exception ex)
        {
            report = "FAILED: " + ex;
        }

        var ok = report.StartsWith("OK", StringComparison.Ordinal);
        Console.WriteLine("RESULT: " + report.Replace("\r", string.Empty));
        Console.Out.Flush();

        // Whatever WPF has left to shut down is of no interest to the test: leave now.
        Environment.Exit(ok ? 0 : 1);
        return ok ? 0 : 1;
    }

    private static string Run(string palette, bool collapsed)
    {
        var errors = new List<string>();
        Dispatcher? dispatcher = null;
        System.Windows.Forms.Form? form = null;
        try
        {
            if (Application.Current != null)
            {
                return "FAILED: this process already has a WPF Application, so it does not model Navisworks.";
            }

            dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.UnhandledException += (_, e) =>
            {
                errors.Add("dispatcher: " + e.Exception);
                e.Handled = true;
            };
            System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
            System.Windows.Forms.Application.ThreadException += (_, e) => errors.Add("winforms: " + e.Exception);

            var registry = NodeRegistry.CreateDefault();
            NodeLibrary.RegisterAll(registry);
            var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-host-" + Guid.NewGuid().ToString("N") + ".json"));
            var vm = new GraphEditorViewModel(registry, new QuietDialogs(), settings) { PaletteId = palette };

            var control = new CamelGraphEditorControl { ViewModel = vm };
            var host = new System.Windows.Forms.Integration.ElementHost { Child = control, Dock = System.Windows.Forms.DockStyle.Fill };
            form = new System.Windows.Forms.Form
            {
                Width = 1500,
                Height = 950,
                ShowInTaskbar = false,
                StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                Location = new System.Drawing.Point(20, 20),
            };
            form.Controls.Add(host);
            host.CreateControl();
            form.Show();
            Pump(dispatcher);

            // Pick in the node library first (what a click on a library item does), as the first thing a user does.
            var categories = vm.Library.RootItems.OfType<LibraryCategoryViewModel>().ToList();
            foreach (var category in categories.Take(6))
            {
                category.IsExpanded = true;
                category.IsSelected = true;
                Pump(dispatcher);
                if (errors.Count > 0)
                {
                    errors.Insert(0, "after selecting the library category '" + category.Name + "':");
                    return "FAILED:\n" + string.Join("\n---\n", errors);
                }
            }

            var ids = registry.NodeTypes.OrderBy(t => t, StringComparer.Ordinal)
                .Where(t => t != CamelGraph.Core.Groups.GroupInstanceNode.TypeName)
                .Concat(registry.Definitions.OrderBy(d => d.Id, StringComparer.Ordinal).Select(d => d.Id))
                .ToList();

            // The message loop runs after every node for the first ones (a failure then names the node), and after every batch of
            // them later: layout of a few hundred big nodes after each single add would take minutes for no extra coverage.
            var added = 0;
            const int OneByOne = 30;
            const int BatchSize = 25;
            for (var i = 0; i < ids.Count; i++)
            {
                var node = vm.AddNode(ids[i], new Point(60 + (i % 8) * 340, 60 + (i / 8) * 380));
                if (node == null)
                {
                    continue;
                }

                added++;
                if (collapsed)
                {
                    node.Model.Ui.Collapsed = true;
                }

                if (i < OneByOne || i % BatchSize == 0 || i == ids.Count - 1)
                {
                    Pump(dispatcher);
                    if (errors.Count > 0)
                    {
                        errors.Insert(0, "after adding '" + ids[i] + "' (node " + (i + 1) + " of " + ids.Count + "; the batch started at " +
                                         ids[Math.Max(0, i - BatchSize + 1)] + "):");
                        break;
                    }
                }
            }

            Pump(dispatcher);
            Pump(dispatcher);

            if (errors.Count > 0)
            {
                return "FAILED:\n" + string.Join("\n---\n", errors);
            }

            return added < 50 ? "FAILED: only " + added + " nodes could be added." : "OK, " + added + " nodes added.";
        }
        catch (Exception ex)
        {
            var text = new StringBuilder("FAILED: " + ex);
            foreach (var error in errors)
            {
                text.Append("\n---\n").Append(error);
            }

            return text.ToString();
        }
    }

    // The WinForms message loop drives the ElementHost; the WPF dispatcher needs its own idle pass for layout and rendering.
    private static void Pump(Dispatcher dispatcher)
    {
        for (var i = 0; i < 3; i++)
        {
            System.Windows.Forms.Application.DoEvents();
            dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    private sealed class QuietDialogs : IDialogService
    {
        public string? ShowOpenFile(string filter, string title) => null;

        public string? ShowSaveFile(string filter, string title, string defaultFileName) => null;

        public bool Confirm(string message, string title) => true;

        public SaveChoice AskSaveChanges(string message, string title) => SaveChoice.DontSave;

        public void ShowError(string message, string title)
        {
        }

        public string? Prompt(string message, string title, string defaultValue) => null;

        public string? PickFolder(string title, string initialFolder) => null;
    }
}
