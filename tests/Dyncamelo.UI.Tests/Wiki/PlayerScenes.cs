using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Dyncamelo.UI.Services;
using Dyncamelo.UI.ViewModels;
using Dyncamelo.UI.Views;

namespace Dyncamelo.UI.Tests.Wiki;

/// <summary>The Script Player pane: a script chosen with its form, and the same after a run.</summary>
internal static class PlayerScenes
{
    private const double PlayerWidth = 560d;
    private const double PlayerHeight = 1400d;

    public static IEnumerable<WikiScene> Fixed()
    {
        yield return new WikiScene("wiki-player-form", "existing player test", ctx => Draw(ctx, run: false));
        yield return new WikiScene("wiki-player-results", "code, run", ctx => Draw(ctx, run: true));
    }

    private static WikiPictures Draw(WikiContext ctx, bool run)
    {
        // The folder's name is shown in the list, so it gets a name worth showing.
        var scripts = ctx.TempPath("Dyncamelo Scripts");
        Directory.CreateDirectory(scripts);
        foreach (var name in new[] { "Table Summary from Text.dyc", "Getting Started - Math and Watch.dyc", "string-report.dyc", "csv-roundtrip.dyc", "list-lacing.dyc" })
        {
            var source = Path.Combine(WikiPaths.SamplesDirectory(), name);
            if (!File.Exists(source))
            {
                ctx.Skip("samples/" + name + " does not exist");
            }

            File.Copy(source, Path.Combine(scripts, name));
        }

        var settings = new UiSettingsService(ctx.TempPath("player-settings.json"));
        settings.SetPlayerFolders(new[] { scripts });
        settings.SetPaletteId(ctx.Palette);
        var player = new PlayerViewModel(ctx.Registry, new StubDialogs(), settings) { CancelPoll = () => false };
        player.Refresh();

        var control = new PlayerControl { ViewModel = player, Width = PlayerWidth, Height = PlayerHeight };
        var canvas = new Canvas();
        canvas.Children.Add(control);
        var window = new Window
        {
            Width = PlayerWidth,
            Height = PlayerHeight,
            Content = canvas,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        ctx.Track(window);
        window.Show();
        WikiUi.Settle();

        player.SelectedScript = player.Scripts.Single(s => s.Name == "Table Summary from Text");
        if (run)
        {
            var previous = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = ctx.TempFolder;
                if (!player.Run())
                {
                    throw new InvalidOperationException("The script did not run: " + player.StatusText);
                }
            }
            finally
            {
                Environment.CurrentDirectory = previous;
            }

            // "Finished in 0.0 s": how long it took depends on the machine.
            var summary = Regex.Replace(player.ResultSummary, @" in [\d.,]+ s", " in 0.1 s");
            EditorRig.SetPrivate(player, nameof(PlayerViewModel.ResultSummary), summary);
        }

        player.CloseList();
        WikiUi.Settle();
        control.UpdateLayout();

        return ctx.Both(
            id =>
            {
                // The palette is applied to the pane the way the pane itself does it when it is shown.
                ThemeApplier.Apply(control.Resources, PaletteCatalog.ById(id) ?? PaletteCatalog.Default);
                WikiUi.Settle();
                control.UpdateLayout();
            },
            () =>
            {
                var bitmap = new RenderTargetBitmap((int)PlayerWidth, (int)PlayerHeight, 96d, 96d, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(control);
                return Compose.TrimBottom(bitmap, 24);
            });
    }
}
