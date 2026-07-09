using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dyncamelo.UI.ViewModels;

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
        _libraryDragStart = e.GetPosition(LibraryTree);
        _libraryDragEntry = (e.OriginalSource as FrameworkElement)?.DataContext as LibraryEntryViewModel;
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
