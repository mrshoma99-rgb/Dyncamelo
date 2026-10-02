using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dyncamelo.Core.Loader;
using Dyncamelo.Nodes;
using Dyncamelo.UI.Services;
using Dyncamelo.UI.ViewModels;
using Dyncamelo.UI.Views;
using Xunit;

namespace Dyncamelo.UI.Tests;

/// <summary>
/// Renders the editor and the Script Player with a runnable sample graph to PNG files, for the README and the Autodesk App Store listing.
/// Does nothing unless DYNCAMELO_SCREENSHOT_DIR names a folder (the Windows CI job sets it and keeps the folder as a build artifact), so an
/// ordinary test run writes nothing. The pictures show Dyncamelo on its own: pictures with a model behind it have to be taken in Navisworks.
/// </summary>
public class EditorScreenshotTests
{
    private const double ShotWidth = 1900d;
    private const double ShotHeight = 1000d;
    private const double PlayerWidth = 560d;
    private const double PlayerHeight = 1400d;

    [Theory]
    [InlineData("DyncameloDark", "editor-screenshot.png")]
    [InlineData("Light", "editor-screenshot-light.png")]
    public void RenderTheEditorWithASampleGraph(string palette, string fileName)
    {
        RenderEditor(palette, fileName, null);
    }

    [Fact]
    public void RenderTheQuickNodeSearch()
    {
        RenderEditor("DyncameloDark", "editor-quick-search.png", vm =>
        {
            vm.OpenQuickSearch(new Point(0, 0));
            vm.QuickSearchText = "table";
        });
    }

    [Fact]
    public void RenderTheCommandPalette()
    {
        RenderEditor("DyncameloDark", "editor-command-palette.png", vm =>
        {
            vm.IsPaletteOpen = true;
            vm.PaletteQuery = "arrange";
        });
    }

    [Fact]
    public void RenderTheShortcutSheet()
    {
        RenderEditor("DyncameloDark", "editor-shortcuts.png", vm => vm.IsHelpOpen = true);
    }

    [Fact]
    public void RenderTheSettings()
    {
        RenderEditor("DyncameloDark", "editor-settings.png", vm => vm.IsSettingsOpen = true);
    }

    [Theory]
    [InlineData("DyncameloDark", "player.png", false)]
    [InlineData("Light", "player-light.png", false)]
    [InlineData("DyncameloDark", "player-list.png", true)]
    public void RenderTheScriptPlayerWithAScriptAndItsResults(string palette, string fileName, bool listOpen)
    {
        var folder = Environment.GetEnvironmentVariable("DYNCAMELO_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        // The folder's name is shown in the list, so it gets a name worth showing.
        var root = Path.Combine(Path.GetTempPath(), "dyc-shot-player-" + Guid.NewGuid().ToString("N"));
        var scripts = Path.Combine(root, "Dyncamelo Scripts");
        Directory.CreateDirectory(scripts);
        foreach (var name in new[] { "Table Summary from Text.dyc", "Getting Started - Math and Watch.dyc", "string-report.dyc", "csv-roundtrip.dyc", "list-lacing.dyc" })
        {
            File.Copy(Path.Combine(RepoRoot(), "samples", name), Path.Combine(scripts, name));
        }

        Window? window = null;
        PlayerControl? control = null;
        try
        {
            StaHost.Run(() =>
            {
                var registry = NodeRegistry.CreateDefault();
                NodeLibrary.RegisterAll(registry);
                var settings = new UiSettingsService(Path.Combine(root, "settings.json"));
                settings.SetPlayerFolders(new[] { scripts });
                settings.SetPaletteId(palette);
                var player = new PlayerViewModel(registry, new StubDialogs(), settings) { CancelPoll = () => false };
                player.Refresh();

                control = new PlayerControl { ViewModel = player, Width = PlayerWidth, Height = PlayerHeight };
                var canvas = new Canvas();
                canvas.Children.Add(control);
                window = new Window
                {
                    Width = PlayerWidth,
                    Height = PlayerHeight,
                    Content = canvas,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStyle = WindowStyle.None,
                };
                window.Show();
                player.SelectedScript = player.Scripts.Single(s => s.Name == "Table Summary from Text");
                Assert.True(player.Run());
                if (listOpen)
                {
                    player.IsListOpen = true;
                }
                else
                {
                    player.CloseList();
                }
            });
            StaHost.Flush();
            StaHost.Flush();

            Save(control!, folder, fileName, (int)PlayerWidth, (int)PlayerHeight);
        }
        finally
        {
            if (window != null)
            {
                StaHost.Run(() => window.Close());
            }

            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void RenderEditor(string palette, string fileName, Action<GraphEditorViewModel>? state)
    {
        var folder = Environment.GetEnvironmentVariable("DYNCAMELO_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        var sample = Path.Combine(RepoRoot(), "samples", "Table Summary from Text.dyc");
        Assert.True(File.Exists(sample), "sample graph missing: " + sample);

        Window? window = null;
        DyncameloEditorControl? control = null;
        StaHost.Run(() =>
        {
            var registry = NodeRegistry.CreateDefault();
            NodeLibrary.RegisterAll(registry);
            var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-shot-" + Guid.NewGuid().ToString("N") + ".json"));
            var vm = new GraphEditorViewModel(registry, new StubDialogs(), settings) { PaletteId = palette };
            Assert.True(vm.OpenFromPath(sample));
            vm.IsAutoRun = false;
            vm.RunGraph();

            // A fixed size on a canvas, not the window's: a CI machine's small screen would otherwise cut the picture.
            control = new DyncameloEditorControl { ViewModel = vm, Width = ShotWidth, Height = ShotHeight };
            var canvas = new Canvas();
            canvas.Children.Add(control);
            window = new Window
            {
                Width = ShotWidth,
                Height = ShotHeight,
                Content = canvas,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStyle = WindowStyle.None,
            };
            window.Show();
        });
        StaHost.Flush();
        StaHost.Flush();

        try
        {
            // Frame the half that has the results (the tables and the Watch Table), so the text is readable at the picture's size.
            StaHost.Run(() =>
            {
                var vm = control!.ViewModel!;
                vm.SelectedItems.Clear();
                var nodes = vm.Items.OfType<NodeViewModel>().ToList();
                var firstColumn = nodes.Single(n => n.Model.Name == "Table.FromRows").Model.X;
                foreach (var item in nodes.Where(n => n.Model.X >= firstColumn))
                {
                    vm.SelectedItems.Add(item);
                }

                vm.FrameSelected();
            });
            StaHost.Flush();
            StaHost.Flush();
            StaHost.Run(() => control!.ViewModel!.SelectedItems.Clear());
            StaHost.Flush();

            if (state != null)
            {
                StaHost.Run(() => state(control!.ViewModel!));
                StaHost.Flush();
                StaHost.Flush();
            }

            Save(control!, folder, fileName, (int)ShotWidth, (int)ShotHeight);
        }
        finally
        {
            StaHost.Run(() => window!.Close());
        }
    }

    private static void Save(FrameworkElement control, string folder, string fileName, int expectedWidth, int expectedHeight)
    {
        StaHost.Run(() =>
        {
            control.UpdateLayout();
            var width = (int)Math.Ceiling(control.ActualWidth);
            var height = (int)Math.Ceiling(control.ActualHeight);
            Assert.True(width == expectedWidth && height == expectedHeight, "the control was not laid out at its size (" + width + " x " + height + ")");

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(control);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(folder, fileName)))
            {
                encoder.Save(stream);
            }
        });

        Assert.True(new FileInfo(Path.Combine(folder, fileName)).Length > 10000, "the screenshot is empty: " + fileName);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dyncamelo.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
