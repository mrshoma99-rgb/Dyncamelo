using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CamelGraph.Core.Loader;
using CamelGraph.UI.Services;
using CamelGraph.UI.ViewModels;
using CamelGraph.UI.Views;
using Xunit;

namespace CamelGraph.UI.Tests;

/// <summary>Menus and drop-downs must not show a scroll bar (or reserve room for one) when everything fits.</summary>
public class MenuScrollTests
{
    private sealed class Host : IDisposable
    {
        public GraphEditorViewModel Vm = null!;
        public Window Window = null!;
        public CamelGraphEditorControl Control => (CamelGraphEditorControl)Window.Content;

        public void Dispose() => StaHost.Run(() => Window.Close());
    }

    private static Host Build()
    {
        var host = new Host();
        StaHost.Run(() =>
        {
            var registry = NodeRegistry.CreateDefault();
            registry.RegisterNodeType("TestEditors", () => new EditorsNode());
            var settings = new UiSettingsService(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
            host.Vm = new GraphEditorViewModel(registry, new StubDialogs(), settings);
            host.Vm.Graph.AddNode(new EditorsNode { X = 100, Y = 100 });
            host.Window = new Window
            {
                Width = 1400,
                Height = 900,
                Content = new CamelGraphEditorControl { ViewModel = host.Vm },
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStyle = WindowStyle.None,
            };
            host.Window.Show();
        });
        StaHost.Flush();
        return host;
    }

    private static List<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var found = new List<T>();
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                found.Add(match);
            }

            found.AddRange(Descendants<T>(child));
        }

        return found;
    }

    private static string Report(FrameworkElement popupChild)
    {
        var viewers = Descendants<ScrollViewer>(popupChild);
        return string.Join(
            "; ",
            viewers.Select(v => "viewer(vis=" + v.ComputedVerticalScrollBarVisibility + ", extent=" + v.ExtentHeight + ", viewport=" + v.ViewportHeight
                                + ", actual=" + v.ActualHeight.ToString("0.#") + ")"));
    }

    [Fact]
    public void NoHeaderMenuShowsAScrollBar()
    {
        using var host = Build();
        var menu = (Menu)null!;
        StaHost.Run(() => menu = (Menu)host.Control.FindName("HeaderMenu"));
        var tops = 0;
        StaHost.Run(() => tops = menu.Items.Count);
        Assert.True(tops >= 5);

        for (var i = 0; i < tops; i++)
        {
            var index = i;
            StaHost.Run(() => ((MenuItem)menu.Items[index]).IsSubmenuOpen = true);
            StaHost.Flush();
            StaHost.Run(() =>
            {
                var top = (MenuItem)menu.Items[index];
                var popup = (Popup)top.Template.FindName("PART_Popup", top);
                var child = (FrameworkElement)popup.Child;
                var visibleBars = Descendants<ScrollBar>(child).Where(b => b.IsVisible).ToList();
                Assert.True(
                    visibleBars.Count == 0,
                    "The '" + top.Header + "' menu shows " + visibleBars.Count + " scroll bar(s): " + Report(child));
                top.IsSubmenuOpen = false;
            });
        }
    }

    [Fact]
    public void TheCanvasContextMenuAndADropDownShowNoScrollBar()
    {
        using var host = Build();
        StaHost.Run(() =>
        {
            var editor = (Nodify.NodifyEditor)host.Control.FindName("Editor");
            editor.ContextMenu.PlacementTarget = editor;
            editor.ContextMenu.IsOpen = true;
        });
        StaHost.Flush();
        StaHost.Run(() =>
        {
            var editor = (Nodify.NodifyEditor)host.Control.FindName("Editor");
            var menu = editor.ContextMenu;
            var bars = Descendants<ScrollBar>(menu).Where(b => b.IsVisible).ToList();
            Assert.True(bars.Count == 0, "The canvas context menu shows " + bars.Count + " scroll bar(s): " + Report(menu));
            menu.IsOpen = false;

            var combo = Descendants<ComboBox>(host.Window).First(c => c.Items.Count > 3);
            combo.IsDropDownOpen = true;
        });
        StaHost.Flush();
        StaHost.Run(() =>
        {
            var combo = Descendants<ComboBox>(host.Window).First(c => c.Items.Count > 3);
            var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
            var child = (FrameworkElement)popup.Child;
            var bars = Descendants<ScrollBar>(child).Where(b => b.IsVisible).ToList();
            Assert.True(bars.Count == 0 || combo.Items.Count > 8, "A drop-down of " + combo.Items.Count + " items shows a scroll bar: " + Report(child));
            combo.IsDropDownOpen = false;
        });
    }

    [Fact]
    public void ScrollBarsAreTheThemedSlimOnes()
    {
        StaHost.Run(() =>
        {
            var theme = new ResourceDictionary { Source = new Uri("pack://application:,,,/CamelGraph.UI;component/Themes/CamelGraphDark.xaml") };
            var style = (Style)theme[typeof(ScrollBar)];
            Assert.True(style.Setters.OfType<Setter>().Any(s => s.Property == FrameworkElement.WidthProperty && (double)s.Value == 11d));
        });
    }
}
