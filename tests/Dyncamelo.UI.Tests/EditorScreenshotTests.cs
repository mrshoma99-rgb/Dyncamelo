using System;
using System.IO;
using System.Windows;
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

            control = new DyncameloEditorControl { ViewModel = vm };
            window = new Window
            {
                Width = 1500,
                Height = 900,
                Content = control,
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
            StaHost.Run(() =>
            {
                control!.UpdateLayout();
                var width = (int)Math.Ceiling(control.ActualWidth);
                var height = (int)Math.Ceiling(control.ActualHeight);
                Assert.True(width > 600 && height > 400, "the editor was not laid out (" + width + " x " + height + ")");

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
