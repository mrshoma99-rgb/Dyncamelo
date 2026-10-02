using System;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace Dyncamelo.UI.Tests;

/// <summary>The themed text box must take the room its Padding asks for — no more.</summary>
public class TextBoxLayoutTests
{
    [Fact]
    public void TheThemedTextBoxCountsItsPaddingOnceLikeAPlainOne()
    {
        Window? window = null;
        try
        {
            TextBox? themed = null;
            TextBox? plain = null;
            StaHost.Run(() =>
            {
                var theme = new ResourceDictionary { Source = new Uri("pack://application:,,,/Dyncamelo.UI;component/Themes/DyncameloDark.xaml") };
                var style = (Style)theme["Dyc.TextBox"];
                themed = new TextBox { Style = style, Text = "Wg", FontSize = 12, Padding = new Thickness(12, 8, 12, 8), BorderThickness = new Thickness(1) };
                plain = new TextBox { Text = "Wg", FontSize = 12, Padding = new Thickness(12, 8, 12, 8), BorderThickness = new Thickness(1) };
                var panel = new StackPanel();
                panel.Children.Add(themed);
                panel.Children.Add(plain);
                window = new Window
                {
                    Width = 300,
                    Height = 200,
                    Content = panel,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStyle = WindowStyle.None,
                };
                window.Show();
            });
            StaHost.Flush();

            StaHost.Run(() =>
            {
                window!.UpdateLayout();
                // The same height (a line plus the padding and the border), give or take a pixel of rounding. With the padding counted
                // twice it was 16 pixels taller here.
                Assert.True(Math.Abs(themed!.ActualHeight - plain!.ActualHeight) <= 2d, "themed " + themed.ActualHeight + " vs plain " + plain.ActualHeight);
            });
        }
        finally
        {
            if (window != null)
            {
                StaHost.Run(() => window.Close());
            }
        }
    }
}
