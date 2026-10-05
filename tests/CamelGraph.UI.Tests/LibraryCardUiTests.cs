using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CamelGraph.Core.Loader;
using CamelGraph.UI.Services;
using CamelGraph.UI.ViewModels;
using CamelGraph.UI.Views;
using Xunit;

namespace CamelGraph.UI.Tests;

/// <summary>The node library as a rounded card floating on the canvas: its shape, its search row, and the two edge tabs that hide and show it.</summary>
public class LibraryCardUiTests
{
    private sealed class Host : IDisposable
    {
        public GraphEditorViewModel Vm = null!;
        public Window Window = null!;
        public CamelGraphEditorControl Control => (CamelGraphEditorControl)Window.Content;

        public T Named<T>(string name) where T : class => (T)Control.FindName(name);

        public void Dispose() => StaHost.Run(() => Window.Close());
    }

    private static Host Build(string? palette = null)
    {
        var host = new Host();
        StaHost.Run(() =>
        {
            var registry = NodeRegistry.CreateDefault();
            var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
            host.Vm = new GraphEditorViewModel(registry, new StubDialogs(), settings);
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
        if (palette != null)
        {
            StaHost.Run(() => host.Vm.PaletteId = palette);
            StaHost.Flush();
        }

        return host;
    }

    private static Rect BoundsIn(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(0d, 0d, element.ActualWidth, element.ActualHeight));

    // The automation click is queued on the dispatcher like a real one, so the caller flushes before it looks at the result.
    private static void Click(Button button)
    {
        var invoke = (IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke);
        invoke.Invoke();
    }

    [Fact]
    public void TheLibraryIsARoundedCardWithAMarginAndASoftShadowOfItsOwn()
    {
        using var host = Build();
        StaHost.Run(() =>
        {
            var card = host.Named<Border>("LibraryCard");
            var shadow = host.Named<Border>("LibraryShadow");
            var hostGrid = host.Named<Grid>("LibraryHost");
            var tree = host.Named<TreeView>("LibraryTree");

            Assert.Equal(14d, card.CornerRadius.TopLeft);
            Assert.Equal(14d, card.CornerRadius.BottomRight);
            Assert.Equal(new Thickness(1d), card.BorderThickness);
            Assert.True(hostGrid.Margin.Left >= 8d && hostGrid.Margin.Top >= 8d && hostGrid.Margin.Bottom >= 8d, "margin " + hostGrid.Margin);

            // The margin is real: the card does not touch the left edge, the top or the bottom of the area it is in.
            var area = (FrameworkElement)hostGrid.Parent;
            var rect = BoundsIn(card, area);
            Assert.True(rect.Left >= 8d - 0.5d, "left gap " + rect.Left);
            Assert.True(rect.Top >= 8d - 0.5d, "top gap " + rect.Top);
            Assert.True(area.ActualHeight - rect.Bottom >= 8d - 0.5d, "bottom gap " + (area.ActualHeight - rect.Bottom));

            // The shadow is its own element behind the card; nothing from the tree up to the window runs through an effect.
            Assert.IsType<DropShadowEffect>(shadow.Effect);
            Assert.Equal(card.ActualWidth, shadow.ActualWidth, 1);
            Assert.Equal(card.ActualHeight, shadow.ActualHeight, 1);
            for (DependencyObject? d = tree; d != null && !ReferenceEquals(d, host.Window); d = VisualTreeHelper.GetParent(d))
            {
                Assert.True(d is not UIElement ui || ui.Effect == null, d.GetType().Name + " around the tree has an effect");
            }

            // The splitter column is the quiet gap between the card and the canvas.
            Assert.Equal(8d, host.Named<ColumnDefinition>("SplitterColumn").Width.Value);
            Assert.IsType<GridSplitter>(host.Named<object>("LibrarySplitter"));
        });
    }

    [Theory]
    [InlineData("CamelGraphDark")]
    [InlineData("Light")]
    public void TheCardIsLiftedFromTheCanvasAndItsBorderShowsInEveryPalette(string palette)
    {
        using var host = Build(palette);
        StaHost.Run(() =>
        {
            var card = host.Named<Border>("LibraryCard");
            var expected = PaletteCatalog.ById(palette)!;
            var fill = ((SolidColorBrush)card.Background).Color;
            var edge = ((SolidColorBrush)card.BorderBrush).Color;

            Assert.Equal(expected.Colors["Dyc.PanelBrush"], fill);
            Assert.NotEqual(expected.Colors["Dyc.CanvasBrush"], fill);
            Assert.Equal(expected.Colors["Dyc.PanelBorderBrush"], edge);
            Assert.NotEqual(fill, edge);

            // The tab follows the palette too: tinted, with the palette's own muted text colour on it.
            var tab = host.Named<Button>("LibraryHideTab");
            Assert.Equal(expected.Colors["Dyc.HoverBrush"], ((SolidColorBrush)tab.Background).Color);
            Assert.Equal(expected.Colors["Dyc.SubtleTextBrush"], ((SolidColorBrush)tab.Foreground).Color);
        });
    }

    [Fact]
    public void TheSearchBoxHasARowOfItsOwnAcrossTheCardAndTheRoundButtonsSitUnderIt()
    {
        using var host = Build();
        StaHost.Run(() =>
        {
            var card = host.Named<Border>("LibraryCard");
            var search = host.Named<TextBox>("LibrarySearchBox");
            var expand = host.Named<Button>("LibraryExpandAllButton");
            var collapse = host.Named<Button>("LibraryCollapseAllButton");

            Assert.True(search.ActualHeight >= 34d && search.ActualHeight <= 38d, "search box height " + search.ActualHeight);
            // The card minus its border and the 10 px margin on each side of the row.
            Assert.True(search.ActualWidth >= card.ActualWidth - 2d - 20d - 0.5d, "search box " + search.ActualWidth + " in a card " + card.ActualWidth);

            var searchRect = BoundsIn(search, host.Window);
            var expandRect = BoundsIn(expand, host.Window);
            var collapseRect = BoundsIn(collapse, host.Window);
            Assert.True(expandRect.Top >= searchRect.Bottom && collapseRect.Top >= searchRect.Bottom, "the buttons are not under the search box");
            Assert.True(Math.Abs(collapseRect.Right - searchRect.Right) < 1d, "the buttons end where the search box ends");
            Assert.True(expandRect.Right <= collapseRect.Left, "expand is left of collapse");

            foreach (var button in new[] { expand, collapse })
            {
                // Small, not the 28 px of the other round buttons: a + and a − in a ring that is no taller than a line of the list.
                Assert.InRange(button.ActualWidth, 18d, 22d);
                Assert.Equal(button.ActualWidth, button.ActualHeight);
                button.ApplyTemplate();
                Assert.IsType<System.Windows.Shapes.Ellipse>(button.Template.FindName("Chrome", button));
            }

            // A plain plus expands and a plain minus collapses (no arrows).
            Assert.Same(host.Control.FindResource("Dyc.Icon.Plus"), ((System.Windows.Shapes.Path)expand.Content).Data);
            Assert.Same(host.Control.FindResource("Dyc.Icon.Minus"), ((System.Windows.Shapes.Path)collapse.Content).Data);

            Assert.Equal("Expand all categories", expand.ToolTip);
            Assert.Equal("Collapse all categories", collapse.ToolTip);
            Assert.Same(host.Vm.Library.ExpandAllCommand, expand.Command);
            Assert.Same(host.Vm.Library.CollapseAllCommand, collapse.Command);
        });
    }

    [Fact]
    public void TheSearchBoxClearsWithTheXAndWithEsc()
    {
        using var host = Build();
        Button? clear = null;
        StaHost.Run(() =>
        {
            var search = host.Named<TextBox>("LibrarySearchBox");
            clear = Descendants<Button>(host.Window).First(b => (b.ToolTip as string) == "Clear search");
            Assert.False(clear.IsVisible);

            search.Text = "clash";
            host.Window.UpdateLayout();
            Assert.Equal("clash", host.Vm.Library.SearchText);
            Assert.True(clear.IsVisible);
            Click(clear);
        });
        StaHost.Flush();
        StaHost.Run(() =>
        {
            Assert.Equal(string.Empty, host.Vm.Library.SearchText);
            Assert.False(clear!.IsVisible);

            var search = host.Named<TextBox>("LibrarySearchBox");
            search.Text = "table";
            Assert.Equal("table", host.Vm.Library.SearchText);
            var esc = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(search), Environment.TickCount, Key.Escape)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };
            search.RaiseEvent(esc);
            Assert.True(esc.Handled);
            Assert.Equal(string.Empty, host.Vm.Library.SearchText);
        });
    }

    [Fact]
    public void TheHideTabHidesTheCardAndTheNodesTabOnTheCanvasEdgeBringsItBack()
    {
        using var host = Build();
        StaHost.Run(() =>
        {
            var card = host.Named<Border>("LibraryCard");
            var hide = host.Named<Button>("LibraryHideTab");
            var show = host.Named<Button>("LibraryShowTab");

            // The HIDE tab is glued to the right edge of the card, vertically centred, its label running along it.
            Assert.Equal("HIDE", hide.Content);
            Assert.Equal("Hide the node library (View ▸ Node Library Panel)", hide.ToolTip);
            Assert.Same(host.Vm.ToggleLibraryCommand, hide.Command);
            Assert.True(hide.IsVisible);
            Assert.True(hide.ActualHeight > hide.ActualWidth * 1.5d, "the tab is " + hide.ActualWidth + " x " + hide.ActualHeight);
            var cardRect = BoundsIn(card, host.Window);
            var hideRect = BoundsIn(hide, host.Window);
            Assert.True(Math.Abs(hideRect.Right - (cardRect.Right - 1d)) < 1.5d, "tab right " + hideRect.Right + ", card right " + cardRect.Right);
            Assert.True(Math.Abs((hideRect.Top + hideRect.Bottom) / 2d - (cardRect.Top + cardRect.Bottom) / 2d) < 2d, "the tab is not in the middle of the card edge");

            // Square on the glued side, round on the free side.
            hide.ApplyTemplate();
            var chrome = (Border)hide.Template.FindName("Chrome", hide);
            Assert.Equal(0d, chrome.CornerRadius.TopRight);
            Assert.Equal(0d, chrome.CornerRadius.BottomRight);
            Assert.True(chrome.CornerRadius.TopLeft > 0d && chrome.CornerRadius.BottomLeft > 0d);

            // While the library is shown, the way back is not offered.
            Assert.False(show.IsVisible);
            Click(hide);
        });
        StaHost.Flush();
        StaHost.Run(() =>
        {
            var show = host.Named<Button>("LibraryShowTab");
            var editor = host.Named<Nodify.NodifyEditor>("Editor");
            host.Window.UpdateLayout();

            Assert.False(host.Vm.IsLibraryVisible);
            Assert.False(host.Named<Border>("LibraryCard").IsVisible);
            Assert.Equal(0d, host.Named<ColumnDefinition>("LibraryColumn").Width.Value);

            // Hidden: the matching tab is on the left edge of the canvas, in the middle of it, rounded on its free (right) side.
            Assert.True(show.IsVisible);
            Assert.Equal("NODES", show.Content);
            Assert.StartsWith("Show the node library", (string)show.ToolTip);
            Assert.Same(host.Vm.ToggleLibraryCommand, show.Command);
            var showRect = BoundsIn(show, host.Window);
            var editorRect = BoundsIn(editor, host.Window);
            Assert.True(Math.Abs(showRect.Left - editorRect.Left) < 1d, "the tab starts at " + showRect.Left + ", the canvas at " + editorRect.Left);
            Assert.True(Math.Abs((showRect.Top + showRect.Bottom) / 2d - (editorRect.Top + editorRect.Bottom) / 2d) < 2d, "the tab is not in the middle of the canvas edge");
            show.ApplyTemplate();
            var chrome = (Border)show.Template.FindName("Chrome", show);
            Assert.Equal(0d, chrome.CornerRadius.TopLeft);
            Assert.True(chrome.CornerRadius.TopRight > 0d && chrome.CornerRadius.BottomRight > 0d);
            Click(show);
        });
        StaHost.Flush();
        StaHost.Run(() =>
        {
            host.Window.UpdateLayout();
            Assert.True(host.Vm.IsLibraryVisible);
            Assert.True(host.Named<Border>("LibraryCard").IsVisible);
            Assert.False(host.Named<Button>("LibraryShowTab").IsVisible);
            Assert.Equal(230d, host.Named<ColumnDefinition>("LibraryColumn").Width.Value);
        });
    }

    [Fact]
    public void TheCardKeepsTheWidthItWasResizedToWhenItIsHiddenAndShownAgain()
    {
        using var host = Build();
        StaHost.Run(() =>
        {
            var column = host.Named<ColumnDefinition>("LibraryColumn");
            column.Width = new GridLength(310d);    // what dragging the splitter does
            host.Vm.ToggleLibraryCommand.Execute(null);
            Assert.Equal(0d, column.Width.Value);
            host.Vm.ToggleLibraryCommand.Execute(null);
            Assert.Equal(310d, column.Width.Value);
            Assert.Equal(150d, column.MinWidth);
        });
    }

    [Fact]
    public void ThePanelsDictionaryDefinesTheSharedStyles()
    {
        StaHost.Run(() =>
        {
            var panels = new ResourceDictionary { Source = new Uri("pack://application:,,,/CamelGraph.UI;component/Themes/Panels.xaml") };
            foreach (var key in new[]
            {
                "Dyc.Panel.Card", "Dyc.Panel.CardShadow", "Dyc.Panel.Section", "Dyc.Panel.Banner", "Dyc.Panel.IconWell", "Dyc.Panel.Eyebrow",
                "Dyc.Panel.IconButton", "Dyc.Panel.EdgeTab", "Dyc.Panel.GhostButton", "Dyc.Panel.GhostIconButton", "Dyc.Panel.PrimaryButton",
                "Dyc.Panel.SearchBox",
            })
            {
                Assert.True(panels.Contains(key), "missing style: " + key);
                Assert.IsType<Style>(panels[key]);
            }
        });
    }

    private static List<T> Descendants<T>(DependencyObject root) where T : DependencyObject
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
}
