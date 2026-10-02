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
/// Renders the editor with a runnable sample graph to PNG files, for the README. Does nothing unless DYNCAMELO_SCREENSHOT_DIR names a
/// folder (the Windows CI job sets it and keeps the folder as a build artifact), so an ordinary test run writes nothing.
/// </summary>
public class EditorScreenshotTests
{
    private const double ShotWidth = 1600d;
    private const double ShotHeight = 900d;

    [Theory]
    [InlineData("DyncameloDark", "editor-screenshot.png")]
    [InlineData("Light", "editor-screenshot-light.png")]
    public void RenderTheEditorWithASampleGraph(string palette, string fileName)
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

        // Frame the half that has the results (the tables and the Watch Table), so the text is readable at the picture's size.
        StaHost.Run(() =>
        {
            var vm = control!.ViewModel!;
            vm.SelectedItems.Clear();
            foreach (var item in vm.Items.OfType<NodeViewModel>().Where(n => n.Model.X >= 860d))
            {
                vm.SelectedItems.Add(item);
            }

            vm.FrameSelected();
        });
        StaHost.Flush();
        StaHost.Flush();
        StaHost.Run(() => control!.ViewModel!.SelectedItems.Clear());
        StaHost.Flush();

        try
        {
            StaHost.Run(() =>
            {
                control!.UpdateLayout();
                var width = (int)Math.Ceiling(control.ActualWidth);
                var height = (int)Math.Ceiling(control.ActualHeight);
                Assert.True(width == (int)ShotWidth && height == (int)ShotHeight, "the editor was not laid out at its size (" + width + " x " + height + ")");

                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(control);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(folder, fileName)))
                {
                    encoder.Save(stream);
                }
            });
        }
        finally
        {
            StaHost.Run(() => window!.Close());
        }

        Assert.True(new FileInfo(Path.Combine(folder, fileName)).Length > 10000, "the screenshot is empty");
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
