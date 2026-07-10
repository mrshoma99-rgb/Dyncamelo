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
        // The recent-files dropdown is the button's ContextMenu, opened on left
        // click (split-button behaviour). Nothing to show for an empty list.
        if (!(sender is Button button) || button.ContextMenu == null ||
            ViewModel == null || ViewModel.RecentFiles.Count == 0)
        {
            return;
        }

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
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
        // Clicks on the favourite star toggle a command; they must not arm a drag.
        if (IsWithinButton(e.OriginalSource))
        {
            _libraryDragEntry = null;
            return;
        }

        _libraryDragStart = e.GetPosition(LibraryTree);
        _libraryDragEntry = (e.OriginalSource as FrameworkElement)?.DataContext as LibraryEntryViewModel;
    }

    private static bool IsWithinButton(object originalSource)
    {
        var current = originalSource as DependencyObject;
        while (current != null && !(current is TreeViewItem))
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

        var position = e.GetPosition(LibraryTree);
        if (System.Math.Abs(position.X - _libraryDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            System.Math.Abs(position.Y - _libraryDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var entry = _libraryDragEntry;
        _libraryDragEntry = null;
        var data = new DataObject(DragDataFormat, entry.Id);
        DragDrop.DoDragDrop(LibraryTree, data, DragDropEffects.Copy);
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
}
