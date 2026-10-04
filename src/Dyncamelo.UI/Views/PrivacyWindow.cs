using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dyncamelo.UI.Services;

namespace Dyncamelo.UI.Views;

/// <summary>Shows the privacy policy that is built into the app (Help &gt; Privacy Policy). Built in code so it needs no theme.</summary>
public static class PrivacyWindow
{
    /// <summary>Opens the window.</summary>
    /// <param name="owner">The window to centre on, or null.</param>
    public static void Show(Window? owner = null)
    {
        var background = new SolidColorBrush(Color.FromRgb(0x23, 0x27, 0x2E));
        var text = new SolidColorBrush(Color.FromRgb(0xE6, 0xE9, 0xEE));
        var accent = new SolidColorBrush(Color.FromRgb(0x6F, 0xB1, 0xFF));

        var body = new TextBox
        {
            Text = PrivacyPolicy.Text(),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = background,
            Foreground = text,
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            Padding = new Thickness(18, 14, 18, 14),
        };

        var online = new Button
        {
            Content = "Open the online copy",
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 4, 12, 4),
        };
        online.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(PrivacyPolicy.OnlineUrl) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // No browser association: the text above is the policy.
            }
        };

        var close = new Button { Content = "Close", IsCancel = true, IsDefault = true, Padding = new Thickness(18, 4, 18, 4) };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12),
        };
        buttons.Children.Add(online);
        buttons.Children.Add(close);

        var layout = new DockPanel { Background = background };
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        layout.Children.Add(body);

        var window = new Window
        {
            Title = "CamelGraph privacy policy",
            Width = 720,
            Height = 640,
            Content = layout,
            Background = background,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
        };
        if (owner != null)
        {
            window.Owner = owner;
        }

        close.Click += (_, _) => window.Close();
        window.ShowDialog();
    }
}
