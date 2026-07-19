using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dyncamelo.App;

/// <summary>
/// The About dialog — shown from the ribbon "About" button. Borderless black-and-white
/// BIMCamel window, the same design as the installer and as the IFC exporter's About, so
/// every BIMCamel tool presents itself identically. Built in code (no XAML) so the identical
/// file can live in each plug-in's project regardless of its xaml build configuration; WPF
/// layout (wrapping + SizeToContent) means nothing can clip.
/// </summary>
internal static class AboutDialog
{
    public const string Url = "https://www.bimcamel.com/plugins/dyncamelo";
    public const string IfcExporterUrl = "https://www.bimcamel.com/Export-Navisworks-to-Ifc";

    // BIMCamel black-and-white palette (matches the installer).
    private static readonly Brush Ink = Fill("#0B0B0D");
    private static readonly Brush Paper = Brushes.White;
    private static readonly Brush Paper70 = Fill("#B3FFFFFF");
    private static readonly Brush Paper45 = Fill("#73FFFFFF");
    private static readonly Brush Hairline = Fill("#26FFFFFF");

    public static void Show()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version == null ? "dev" : version.Major + "." + version.Minor + "." + Math.Max(version.Build, 0);

        var window = new Window
        {
            Title = "About Dyncamelo",
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            SizeToContent = SizeToContent.Height,
            Width = 480,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            FontFamily = new FontFamily("Segoe UI"),
        };

        var body = new StackPanel { Margin = new Thickness(28) };

        // Header: logo · title/tagline · ✕
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var logo = LoadLogo("dyncamelo_32.png") ?? LoadLogo("camel_32.png");
        if (logo != null)
        {
            var img = new Image { Source = logo, Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 14, 0) };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            header.Children.Add(img);
        }

        var titles = new StackPanel();
        titles.Children.Add(new TextBlock
        {
            Text = "Dyncamelo",
            Foreground = Paper,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
        });
        titles.Children.Add(new TextBlock
        {
            Text = "Visual programming for Autodesk Navisworks — wire nodes, no code.",
            Foreground = Paper70,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        });
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);

        var close = ChromeClose(window);
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        body.Children.Add(header);

        body.Children.Add(new TextBlock
        {
            Text = "Automate selection, properties, search, viewpoints, clash, TimeLiner and more. " +
                   "Visit bimcamel.com for guides, the node library and updates:",
            Foreground = Paper70,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 20, 0, 0),
        });
        body.Children.Add(LinkBlock(Url, "bimcamel.com/plugins/dyncamelo", Paper, 13.5, FontWeights.SemiBold, topMargin: 10));
        body.Children.Add(LinkBlock(IfcExporterUrl, "Also from BIMCamel: the free IFC Exporter — fast Navisworks → IFC", Paper70, 12.5, FontWeights.Normal, topMargin: 6));

        body.Children.Add(new Border { BorderBrush = Hairline, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 20, 0, 0) });

        // Footer: version · Close
        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(new TextBlock
        {
            Text = "Version " + versionText + "   ·   Part of the BIMCamel toolset",
            Foreground = Paper45,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var closeButton = PrimaryButton("Close", () => window.Close());
        Grid.SetColumn(closeButton, 1);
        footer.Children.Add(closeButton);
        body.Children.Add(footer);

        var root = new Border
        {
            Background = Ink,
            BorderBrush = Hairline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = body,
        };
        window.Content = root;
        window.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) window.DragMove(); };
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) window.Close(); };
        window.ShowDialog();
    }

    // ── building blocks (identical in every BIMCamel About) ─────────────────────

    private static TextBlock LinkBlock(string url, string text, Brush brush, double size, FontWeight weight, double topMargin)
    {
        var link = new Hyperlink(new Run(text)) { Foreground = brush, TextDecorations = null };
        link.Click += (_, _) => Open(url);
        link.MouseEnter += (_, _) => link.TextDecorations = TextDecorations.Underline;
        link.MouseLeave += (_, _) => link.TextDecorations = null;
        return new TextBlock(link)
        {
            FontSize = size,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, topMargin, 0, 0),
        };
    }

    private static FrameworkElement PrimaryButton(string text, Action onClick)
    {
        var border = new Border
        {
            Background = Paper,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24, 8, 24, 8),
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = text, Foreground = Fill("#0B0B0D"), FontSize = 13.5, FontWeight = FontWeights.SemiBold },
        };
        border.MouseEnter += (_, _) => border.Background = Fill("#E2E2E2");
        border.MouseLeave += (_, _) => border.Background = Paper;
        border.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return border;
    }

    private static FrameworkElement ChromeClose(Window window)
    {
        var border = new Border
        {
            Width = 30,
            Height = 26,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = "✕",
                Foreground = Paper45,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        border.MouseEnter += (_, _) => border.Background = Fill("#1AFFFFFF");
        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        border.MouseLeftButtonUp += (_, e) => { e.Handled = true; window.Close(); };
        return border;
    }

    /// <summary>Loads a PNG from the plugin's deployed Resources folder (next to the DLL).</summary>
    private static ImageSource? LoadLogo(string file)
    {
        try
        {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var path = dir == null ? null : Path.Combine(dir, "Resources", file);
            if (path == null || !File.Exists(path))
            {
                return null;
            }

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // don't lock the file
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Browser launch failed; nothing useful to do.
        }
    }

    private static Brush Fill(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
