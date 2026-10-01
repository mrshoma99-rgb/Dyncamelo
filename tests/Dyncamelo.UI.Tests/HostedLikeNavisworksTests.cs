using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Forms.Integration;
using System.Windows.Threading;
using Dyncamelo.Core.Loader;
using Dyncamelo.Nodes;
using Dyncamelo.UI.Services;
using Dyncamelo.UI.ViewModels;
using Dyncamelo.UI.Views;
using Xunit;

namespace Dyncamelo.UI.Tests;

/// <summary>
/// The editor the way Navisworks hosts it: a WinForms <see cref="ElementHost"/> on a dock pane, with no WPF
/// <see cref="Application"/> in the process (Roamer is not a WPF application) and no WPF <see cref="Window"/> around the
/// control. The ordinary UI tests share one STA thread that has an Application, so a node that only fails in the host — as the
/// Color Picker did, and then any node taken from the library — never fails there. This runs in an AppDomain of its own, where
/// no Application exists.
/// </summary>
public class HostedLikeNavisworksTests
{
    [Theory]
    [InlineData("DyncameloDark", false)]
    [InlineData("Light", false)]
    [InlineData("DyncameloDark", true)]
    public void EveryNodeCanBeAddedFromTheLibraryInsideAnElementHostWithoutAnApplication(string palette, bool collapsed)
    {
        // CodeBase is where the assembly came from even when the test runner shadow-copied it.
        var folder = Path.GetDirectoryName(new Uri(typeof(HostRunner).Assembly.CodeBase).LocalPath)!;
        var domain = AppDomain.CreateDomain("hosted-like-navisworks", null, new AppDomainSetup { ApplicationBase = folder });
        try
        {
            var runner = (HostRunner)domain.CreateInstanceAndUnwrap(typeof(HostRunner).Assembly.FullName, typeof(HostRunner).FullName);
            var report = runner.AddEveryNode(palette, collapsed);
            Assert.True(report.StartsWith("OK", StringComparison.Ordinal), report);
        }
        finally
        {
            AppDomain.Unload(domain);
        }
    }
}

/// <summary>Runs inside the extra AppDomain (so it must be public and a MarshalByRefObject).</summary>
public sealed class HostRunner : MarshalByRefObject
{
    /// <summary>Adds one node of every kind the way the library panel does, pumping the message loop after each.</summary>
    public string AddEveryNode(string palette, bool collapsed)
    {
        string report = "The host thread did not finish.";
        var thread = new Thread(() => report = Run(palette, collapsed));
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return thread.Join(TimeSpan.FromMinutes(4)) ? report : "The host thread did not finish within 4 minutes (the message loop is stuck).";
    }

    private static string Run(string palette, bool collapsed)
    {
        var errors = new List<string>();
        try
        {
            if (Application.Current != null)
            {
                return "FAILED: this AppDomain already has a WPF Application, so it does not model Navisworks.";
            }

            var dispatcher = Dispatcher.CurrentDispatcher;
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
            var vm = new GraphEditorViewModel(registry, new StubDialogs(), settings) { PaletteId = palette };

            var control = new DyncameloEditorControl { ViewModel = vm };
            var host = new ElementHost { Child = control, Dock = System.Windows.Forms.DockStyle.Fill };
            var form = new System.Windows.Forms.Form
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

            var ids = registry.NodeTypes.OrderBy(t => t, StringComparer.Ordinal)
                .Where(t => t != Dyncamelo.Core.Groups.GroupInstanceNode.TypeName)
                .Concat(registry.Definitions.OrderBy(d => d.Id, StringComparer.Ordinal).Select(d => d.Id))
                .ToList();

            var added = 0;
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

                Pump(dispatcher);
                if (errors.Count > 0)
                {
                    errors.Insert(0, "after adding '" + ids[i] + "' (node " + (i + 1) + " of " + ids.Count + "):");
                    break;
                }
            }

            Pump(dispatcher);
            Pump(dispatcher);
            form.Close();

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
}
