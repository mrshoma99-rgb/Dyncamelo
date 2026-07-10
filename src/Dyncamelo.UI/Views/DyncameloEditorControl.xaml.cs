using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dyncamelo.UI.ViewModels;
using Nodify;

namespace Dyncamelo.UI.Views;

/// <summary>
/// The complete Dyncamelo editor: toolbar, node library browser, Nodify canvas
/// and status bar. Host-agnostic — the hosting layer sets <see cref="ViewModel"/>
/// (or the DataContext) to a configured <see cref="GraphEditorViewModel"/>.
/// </summary>
public partial class DyncameloEditorControl : UserControl
{
    private const string DragDataFormat = "Dyncamelo.LibraryEntryId";

    private Point _libraryDragStart;
    private LibraryEntryViewModel? _libraryDragEntry;

    /// <summary>Creates the control. Assign <see cref="ViewModel"/> before showing it.</summary>
    public DyncameloEditorControl()
    {
        InitializeComponent();

        // Double-clicking empty canvas inserts a String input node at the click
        // position (Dynamo-style quick node). handledEventsToo because the
        // editor consumes mouse-downs for selection.
        Editor.AddHandler(
            MouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnEditorMouseLeftButtonDown),
            handledEventsToo: true);

        PreviewKeyDown += OnControlPreviewKeyDown;

        // "Find in library" raises a reveal request on the library view model;
        // scrolling the tree is a view job (containers may be virtualized).
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is GraphEditorViewModel oldViewModel)
        {
            oldViewModel.Library.EntryRevealRequested -= OnLibraryEntryRevealRequested;
            oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is GraphEditorViewModel newViewModel)
        {
            newViewModel.Library.EntryRevealRequested += OnLibraryEntryRevealRequested;
            newViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // A freshly loaded graph can have content anywhere in graph space —
        // the samples put their "HOW TO USE" note above the origin — while the
        // viewport stays wherever it was. Fit once the containers exist;
        // FitToScreen is a no-op on an empty (new) graph.
        if (e.PropertyName == nameof(GraphEditorViewModel.Graph))
        {
            Dispatcher.BeginInvoke(
                new System.Action(() => Editor.FitToScreen(null)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OnControlPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+D (duplicate), Ctrl+G (group) and F5 (run) are not consumed by
        // TextBox editing (unlike Ctrl+C/Ctrl+V), so they would bubble up to
        // the UserControl's KeyBindings while the user types in an inline
        // TextBox (string/number/note/group-title/search) — and since clicking
        // into a node's TextBox also selects that node, they would silently
        // duplicate/group it mid-edit. Swallow them while a text box has focus.
        if (!(Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase))
        {
            return;
        }

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if ((ctrl && (e.Key == Key.D || e.Key == Key.G)) || e.Key == Key.F5)
        {
            e.Handled = true;
        }
    }

    /// <summary>The editor view model (stored in the DataContext).</summary>
    public GraphEditorViewModel? ViewModel
    {
        get => DataContext as GraphEditorViewModel;
        set => DataContext = value;
    }

    /// <summary>Center of the current viewport in graph-space coordinates.</summary>
    private Point ViewportCenter => new Point(
        Editor.ViewportLocation.X + Editor.ViewportSize.Width / 2,
        Editor.ViewportLocation.Y + Editor.ViewportSize.Height / 2);

    private void OnAddNoteClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.AddNote(ViewportCenter);
    }

    private void OnOpenRecentClick(object sender, RoutedEventArgs e)
    {
        // The dropdown (recent files + sample graphs) is the button's
        // ContextMenu, opened on left click (split-button behaviour). Samples
        // re-enumerate on every open so newly deployed files appear; empty
        // submenus disable themselves via a HasItems style trigger.
        if (!(sender is Button button) || button.ContextMenu == null || ViewModel == null)
        {
            return;
        }

        ViewModel.RefreshSampleGraphs();
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }

    private void OnOpenRecentContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // Right-clicking the button opens the same ContextMenu through WPF's
        // built-in behaviour, bypassing OnOpenRecentClick: refresh the sample
        // list and pin the placement so both paths show an identical, current
        // dropdown.
        if (sender is Button button && button.ContextMenu != null && ViewModel != null)
        {
            ViewModel.RefreshSampleGraphs();
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        }
    }

    private void OnLibraryItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // MouseDoubleClick bubbles through every ancestor TreeViewItem; only the
        // one directly under the mouse is selected.
        if (sender is TreeViewItem item &&
            item.IsSelected &&
            item.DataContext is LibraryEntryViewModel entry)
        {
            ViewModel?.AddNode(entry.Id, ViewportCenter);
            e.Handled = true;
        }
    }

    private void OnLibraryMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Shared by the category tree and the search-results list. Clicks on
        // the favourite star toggle a command; they must not arm a drag.
        if (IsWithinButton(e.OriginalSource))
        {
            _libraryDragEntry = null;
            return;
        }

        _libraryDragStart = e.GetPosition(sender as IInputElement);
        _libraryDragEntry = (e.OriginalSource as FrameworkElement)?.DataContext as LibraryEntryViewModel;
    }

    private static bool IsWithinButton(object originalSource)
    {
        var current = originalSource as DependencyObject;
        while (current != null && !(current is TreeViewItem) && !(current is ListBoxItem))
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase)
            {
                return true;
            }

            current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private void OnLibraryMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _libraryDragEntry == null)
        {
            return;
        }

        var position = e.GetPosition(sender as IInputElement);
        if (System.Math.Abs(position.X - _libraryDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            System.Math.Abs(position.Y - _libraryDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var entry = _libraryDragEntry;
        _libraryDragEntry = null;
        var data = new DataObject(DragDataFormat, entry.Id);
        DragDrop.DoDragDrop(sender as DependencyObject ?? LibraryTree, data, DragDropEffects.Copy);
    }

    private void OnLibraryResultsDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Double-clicking a search hit inserts it at the viewport center,
        // matching the tree behaviour. Star-button double clicks are ignored.
        if (!IsWithinButton(e.OriginalSource) &&
            (e.OriginalSource as FrameworkElement)?.DataContext is LibraryEntryViewModel entry)
        {
            ViewModel?.AddNode(entry.Id, ViewportCenter);
            e.Handled = true;
        }
    }

    private void OnLibraryPanelPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Esc in the library: first press clears the search (restoring the
        // tree), second press clears the selection highlight.
        if (e.Key != Key.Escape || ViewModel == null)
        {
            return;
        }

        if (ViewModel.Library.SearchText.Length > 0)
        {
            ViewModel.Library.SearchText = string.Empty;
        }
        else
        {
            ViewModel.Library.ClearSelection();
        }

        e.Handled = true;
    }

    private void OnLibraryPanelIsKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // The library highlight is not a sticky selection: leaving the panel
        // (clicking the canvas, the toolbar, a node...) clears it.
        if (!(bool)e.NewValue)
        {
            ViewModel?.Library.ClearSelection();
        }
    }

    private void OnEditorPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Any click on the canvas (or its items) dismisses the library highlight,
        // even when the canvas interaction does not move keyboard focus.
        ViewModel?.Library.ClearSelection();
    }

    private void OnEditorMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || ViewModel == null || !IsEmptyCanvasHit(e.OriginalSource))
        {
            return;
        }

        // MouseLocation is already in graph-space coordinates.
        var node = ViewModel.AddNode(Dyncamelo.Core.Nodes.StringInputNode.TypeName, Editor.MouseLocation);
        if (node != null)
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// True when the original event source lies on the editor's empty canvas —
    /// not on a node/note/group container, a port, or a wire.
    /// </summary>
    private bool IsEmptyCanvasHit(object originalSource)
    {
        var current = originalSource as DependencyObject;
        while (current != null && !ReferenceEquals(current, Editor))
        {
            if (current is ItemContainer ||
                current is Connector ||
                current is BaseConnection ||
                current is ConnectionContainer ||
                current is GroupingNode ||
                current is System.Windows.Controls.Primitives.ScrollBar)
            {
                return false;
            }

            current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return ReferenceEquals(current, Editor);
    }

    private void OnEditorDrop(object sender, DragEventArgs e)
    {
        if (ViewModel == null || !e.Data.GetDataPresent(DragDataFormat))
        {
            return;
        }

        if (e.Data.GetData(DragDataFormat) is string id)
        {
            var location = Editor.GetLocationInsideEditor(e);
            ViewModel.AddNode(id, location);
            e.Handled = true;
        }
    }

    // ----- find in library (tree reveal) --------------------------------------

    private void OnLibraryEntryRevealRequested(object? sender, LibraryRevealEventArgs e)
    {
        // The view model already cleared any search, expanded the category
        // chain and selected the entry; walk the tree path materializing each
        // (possibly virtualized) container and scroll the last one into view.
        ItemsControl host = LibraryTree;
        TreeViewItem? container = null;
        foreach (var item in e.Path)
        {
            container = MaterializeTreeItem(host, item);
            if (container == null)
            {
                return;
            }

            host = container;
        }

        container?.BringIntoView();
    }

    /// <summary>
    /// Returns the TreeViewItem for <paramref name="item"/> inside
    /// <paramref name="parent"/>, forcing the virtualizing items host to
    /// generate it when it is scrolled out of view.
    /// </summary>
    private static TreeViewItem? MaterializeTreeItem(ItemsControl parent, object item)
    {
        parent.ApplyTemplate();
        parent.UpdateLayout();
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem direct)
        {
            return direct;
        }

        int index = parent.Items.IndexOf(item);
        if (index < 0)
        {
            return null;
        }

        if (FindItemsHostPanel(parent) is VirtualizingPanel virtualizing)
        {
            virtualizing.BringIndexIntoViewPublic(index);
            parent.UpdateLayout();
        }

        return parent.ItemContainerGenerator.ContainerFromIndex(index) as TreeViewItem;
    }

    /// <summary>Finds the items host panel generated for an ItemsControl.</summary>
    private static Panel? FindItemsHostPanel(ItemsControl control)
    {
        return FindItemsHostPanelRecursive(control, control);
    }

    private static Panel? FindItemsHostPanelRecursive(DependencyObject current, ItemsControl owner)
    {
        int count = VisualTreeHelper.GetChildrenCount(current);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(current, i);
            if (child is Panel panel && panel.IsItemsHost && ItemsControl.GetItemsOwner(panel) == owner)
            {
                return panel;
            }

            // Child items own their nested panels; do not search inside them.
            if (child is TreeViewItem)
            {
                continue;
            }

            var result = FindItemsHostPanelRecursive(child, owner);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}
