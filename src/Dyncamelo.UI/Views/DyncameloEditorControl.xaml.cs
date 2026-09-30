using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dyncamelo.UI.Mvvm;
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
    private PerfHud? _perfHud;

    /// <summary>
    /// Deleting a node moves keyboard focus to a neighbouring node, and Nodify
    /// answers every focus change by animating the viewport to the newly
    /// focused container (<c>NodifyEditor.AutoPanOnNodeFocus</c>, default on) —
    /// so the canvas drifted after every delete. Turning the switch off keeps
    /// the viewport exactly where the user parked it; panning stays entirely
    /// manual (middle-drag, wheel, Fit to Screen). It is a process-wide static,
    /// which is right here: this is the only Nodify editor in the host.
    /// (An earlier attempt intercepted WPF's RequestBringIntoView; Nodify
    /// never listens to that event, so it changed nothing.)
    /// </summary>
    static DyncameloEditorControl()
    {
        NodifyEditor.AutoPanOnNodeFocus = false;
    }

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

        // While a node is dragged, light up the wire it would be inserted on.
        _insertTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Input)
        {
            Interval = System.TimeSpan.FromMilliseconds(40),
        };
        _insertTimer.Tick += OnInsertTimerTick;
        System.ComponentModel.DependencyPropertyDescriptor
            .FromProperty(NodifyEditor.IsDraggingProperty, typeof(NodifyEditor))
            .AddValueChanged(Editor, OnEditorIsDraggingChanged);

        // Diagnostics overlay (Ctrl+Shift+F12), hosted in the same grid cell as the canvas.
        if (Editor.Parent is Grid canvasGrid)
        {
            _perfHud = new PerfHud(Editor);
            Grid.SetColumn(_perfHud, Grid.GetColumn(Editor));
            canvasGrid.Children.Add(_perfHud);
        }

        // "Find in library" raises a reveal request on the library view model;
        // scrolling the tree is a view job (containers may be virtualized).
        DataContextChanged += OnDataContextChanged;
    }

    private readonly System.Windows.Threading.DispatcherTimer _insertTimer;

    private void OnEditorIsDraggingChanged(object? sender, System.EventArgs e)
    {
        if (Editor.IsDragging)
        {
            _insertTimer.Start();
        }
        else
        {
            _insertTimer.Stop();
            ViewModel?.ClearInsertCandidate();
        }
    }

    private void OnInsertTimerTick(object? sender, System.EventArgs e)
    {
        var vm = ViewModel;
        if (vm == null)
        {
            return;
        }

        if (vm.SelectedItems.Count != 1 || !(vm.SelectedItems[0] is NodeViewModel node) ||
            !(Editor.ItemContainerGenerator.ContainerFromItem(node) is ItemContainer container))
        {
            vm.UpdateInsertCandidate(null, Rect.Empty, Editor.MouseLocation);
            return;
        }

        // Optimised dragging moves the container by a render transform; Location only updates on release.
        var shift = container.RenderTransform is System.Windows.Media.TranslateTransform t ? new Vector(t.X, t.Y) : default(Vector);
        var rect = new Rect(container.Location + shift, container.RenderSize);
        rect.Inflate(-8d, -8d);
        vm.UpdateInsertCandidate(node, rect, Editor.MouseLocation);
    }

    private readonly ShortcutRouter _router = new ShortcutRouter(new[] { "graph.addnode" });
    private RelayCommand? _hudCommand;
    private RelayCommand? _previewsCommand;
    private RelayCommand? _settingsCommand;
    private RelayCommand? _autoRunCommand;
    private RelayCommand? _addNoteCommand;
    private RelayCommand? _addNodeCommand;

    private void BuildHeaderMenu(GraphEditorViewModel vm)
    {
        _hudCommand ??= new RelayCommand(() => _perfHud?.Toggle());
        _addNoteCommand ??= new RelayCommand(() => ViewModel?.AddNote(ViewportCenter));
        _addNodeCommand ??= new RelayCommand(OpenQuickSearchFromMenu);

        EditorMenuBuilder.Build(
            HeaderMenu,
            TryFindResource("Dyc.TopMenuItem") as Style,
            id => ResolveCommand(vm, id),
            Editor,
            id => id switch
            {
                "view.hud" => _perfHud != null && _perfHud.Visibility == Visibility.Visible,
                "view.previews" => vm.ShowNodePreviews,
                "graph.autorun" => vm.IsAutoRun,
                _ => false,
            },
            (category, top) =>
            {
                if (category == "File")
                {
                    AddFileSubmenus(vm, top);
                }
                else if (category == "Graph")
                {
                    AddFrameColorSubmenu(vm, top);
                }
            });
    }

    private ICommand? ResolveCommand(GraphEditorViewModel vm, string id)
    {
        switch (id)
        {
            case "file.new": return vm.NewCommand;
            case "file.open": return vm.OpenCommand;
            case "file.save": return vm.SaveCommand;
            case "file.saveas": return vm.SaveAsCommand;
            case "edit.undo": return vm.UndoCommand;
            case "edit.redo": return vm.RedoCommand;
            case "edit.copy": return vm.CopySelectionCommand;
            case "edit.paste": return vm.PasteCommand;
            case "edit.duplicate": return vm.DuplicateSelectionCommand;
            case "edit.delete": return vm.DeleteSelectionCommand;
            case "edit.deletereconnect": return vm.DeleteAndReconnectCommand;
            case "edit.selectdownstream": return vm.SelectDownstreamCommand;
            case "edit.selectupstream": return vm.SelectUpstreamCommand;
            case "edit.selectsimilar": return vm.SelectSimilarCommand;
            case "view.fit": return EditorCommands.FitToScreen;
            case "view.zoomin": return EditorCommands.ZoomIn;
            case "view.zoomout": return EditorCommands.ZoomOut;
            case "view.collapseall": return vm.CollapseAllCommand;
            case "view.expandall": return vm.ExpandAllCommand;
            case "node.collapse": return vm.ToggleCollapseSelectedCommand;
            case "node.hideunused": return vm.ToggleHideUnusedSelectedCommand;
            case "node.mute": return vm.ToggleMuteSelectedCommand;
            case "node.autoconnect": return vm.AutoConnectCommand;
            case "wire.mute": return vm.MuteSelectedWiresCommand;
            case "wire.reroute": return vm.RerouteSelectedWiresCommand;
            case "wire.disconnect": return vm.DisconnectSelectedWiresCommand;
            case "help.keys": return vm.ToggleHelpCommand;
            case "view.hud": return _hudCommand;
            case "view.previews": return _previewsCommand ??= new RelayCommand(() => vm.ShowNodePreviews = !vm.ShowNodePreviews);
            case "view.settings": return _settingsCommand ??= new RelayCommand(() => SettingsButton.IsChecked = true);
            case "graph.autorun": return _autoRunCommand ??= new RelayCommand(() => vm.IsAutoRun = !vm.IsAutoRun);
            case "graph.run": return vm.RunCommand;
            case "graph.rename": return vm.RenameCommand;
            case "graph.addnote": return _addNoteCommand;
            case "graph.group": return vm.GroupSelectionCommand;
            case "graph.fitframe": return vm.FitFrameCommand;
            case "graph.ungroup": return vm.UngroupSelectedCommand;
            case "graph.arrange": return vm.ArrangeSelectionCommand;
            case "graph.addnode": return _addNodeCommand;
            default: return null;
        }
    }

    private static void AddFrameColorSubmenu(GraphEditorViewModel vm, MenuItem graph)
    {
        var colors = new MenuItem { Header = "Frame Colour" };
        foreach (var color in GraphEditorViewModel.FrameColors)
        {
            colors.Items.Add(new MenuItem
            {
                Header = color.Key,
                Command = vm.SetFrameColorCommand,
                CommandParameter = color.Value,
            });
        }

        graph.Items.Add(colors);
    }

    private void AddFileSubmenus(GraphEditorViewModel vm, MenuItem file)
    {
        file.Items.Add(new Separator());

        var recent = new MenuItem { Header = "Recent Files" };
        recent.Items.Add(new MenuItem { Header = "(none)", IsEnabled = false });
        recent.SubmenuOpened += (sender, args) =>
        {
            recent.Items.Clear();
            foreach (var path in vm.RecentFiles)
            {
                recent.Items.Add(new MenuItem
                {
                    // TextBlock header: a string header would eat underscores as access keys.
                    Header = new TextBlock { Text = path, MaxWidth = 480, TextTrimming = TextTrimming.CharacterEllipsis },
                    Command = vm.OpenRecentFileCommand,
                    CommandParameter = path,
                });
            }

            if (recent.Items.Count == 0)
            {
                recent.Items.Add(new MenuItem { Header = "(none)", IsEnabled = false });
            }
        };
        file.Items.Add(recent);

        var samples = new MenuItem { Header = "Sample Graphs" };
        samples.Items.Add(new MenuItem { Header = "(none)", IsEnabled = false });
        samples.SubmenuOpened += (sender, args) =>
        {
            vm.RefreshSampleGraphs();
            samples.Items.Clear();
            foreach (var sample in vm.SampleGraphs)
            {
                samples.Items.Add(new MenuItem
                {
                    Header = new TextBlock { Text = sample.Name, MaxWidth = 360, TextTrimming = TextTrimming.CharacterEllipsis },
                    ToolTip = sample.FilePath,
                    Command = sample.OpenCommand,
                    CommandParameter = sample,
                });
            }

            if (samples.Items.Count == 0)
            {
                samples.Items.Add(new MenuItem { Header = "(none)", IsEnabled = false });
            }
        };
        file.Items.Add(samples);
    }

    private void OpenQuickSearchFromMenu()
    {
        if (ViewModel == null || ViewModel.IsQuickSearchOpen)
        {
            return;
        }

        ViewModel.OpenQuickSearch(ViewportCenter);
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
            // Apply the persisted palette once the view model is attached.
            ApplyPalette(newViewModel.PaletteId);
            BuildHeaderMenu(newViewModel);
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
        else if (e.PropertyName == nameof(GraphEditorViewModel.IsQuickSearchOpen) && ViewModel?.IsQuickSearchOpen == true)
        {
            // Whoever opened it (Space, menu, a dropped wire): put the caret in the search box.
            Dispatcher.BeginInvoke(
                new System.Action(() =>
                {
                    QuickSearchBox.Focus();
                    QuickSearchBox.SelectAll();
                }),
                System.Windows.Threading.DispatcherPriority.Input);
        }
        else if (e.PropertyName == nameof(GraphEditorViewModel.PaletteId) && ViewModel != null)
        {
            ApplyPalette(ViewModel.PaletteId);
        }
        else if (e.PropertyName == nameof(GraphEditorViewModel.IsRunning) && ViewModel != null)
        {
            if (ViewModel.IsRunning)
            {
                // Graph execution is synchronous on this (UI) thread, so nothing
                // will repaint until it returns. Show an OS-drawn wait cursor —
                // which the system keeps drawing even while the thread is blocked —
                // and force one render pass so the "Running…" overlay is on screen
                // before we block. Without this the run looks like a freeze.
                Mouse.OverrideCursor = Cursors.Wait;
                UpdateLayout();
                Dispatcher.Invoke(
                    new System.Action(() => { }),
                    System.Windows.Threading.DispatcherPriority.Render);
            }
            else
            {
                Mouse.OverrideCursor = null;
            }
        }
    }

    /// <summary>
    /// Recolours the UI by mutating each theme brush's <c>Color</c> in place.
    /// The theme is referenced entirely through StaticResource, so the shared
    /// (unfrozen) <see cref="SolidColorBrush"/> instances must be mutated rather
    /// than replaced for already-rendered elements to update. Node header and
    /// group colours are view-model/hardcoded and deliberately not themed.
    /// </summary>
    private void ApplyPalette(string paletteId)
    {
        var palette = Dyncamelo.UI.Services.PaletteCatalog.ById(paletteId)
            ?? Dyncamelo.UI.Services.PaletteCatalog.Default;

        foreach (var pair in palette.Colors)
        {
            if (TryFindResource(pair.Key) is SolidColorBrush brush && !brush.IsFrozen)
            {
                brush.Color = pair.Value;
            }
        }
    }

    private void OnControlPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool typing = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;

        if (e.Key == Key.Escape && ViewModel != null && ViewModel.IsHelpOpen)
        {
            ViewModel.IsHelpOpen = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F12 && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && _perfHud != null)
        {
            _perfHud.Toggle();
            e.Handled = true;
            return;
        }

        // Hover shortcuts of the number field under the pointer: Ctrl+C / Ctrl+V copy and paste
        // its value, Backspace resets it, "-" negates it.
        if (!typing && ScrubNumberBox.Hovered is ScrubNumberBox hovered && hovered.HandleHoverKey(e))
        {
            e.Handled = true;
            return;
        }

        // Space over the canvas opens the quick node search (Dynamo-style):
        // type to filter, Enter inserts at the spot the cursor was on.
        if (!typing &&
            e.Key == Key.Space &&
            Keyboard.Modifiers == ModifierKeys.None &&
            ViewModel != null &&
            !ViewModel.IsQuickSearchOpen &&
            Editor.IsMouseOver)
        {
            // MouseLocation is the cursor in graph space, maintained by Nodify.
            ViewModel.OpenQuickSearch(Editor.MouseLocation);
            e.Handled = true;
            return;
        }

        // Ctrl+D (duplicate), Ctrl+G (group) and F5 (run) are not consumed by
        // TextBox editing (unlike Ctrl+C/Ctrl+V), so they would run while the user
        // types in an inline TextBox (string/number/note/group-title/search) — and since
        // clicking into a node's TextBox also selects that node, they would silently
        // duplicate/group it mid-edit. Swallow them while a text box has focus.
        if (typing)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if ((ctrl && (e.Key == Key.D || e.Key == Key.G)) || e.Key == Key.F5)
            {
                e.Handled = true;
                return;
            }
        }

        // Every other catalogue shortcut: menus, help and keys all come from one list.
        if (ViewModel != null &&
            _router.TryDispatch(
                e.Key == Key.System ? e.SystemKey : e.Key,
                Keyboard.Modifiers,
                typing,
                Editor.IsKeyboardFocusWithin,
                id => ResolveCommand(ViewModel, id),
                Editor))
        {
            e.Handled = true;
        }
    }

    // ----- quick node search (Space) -------------------------------------------

    private void OnQuickSearchBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel == null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                ViewModel.CloseQuickSearch();
                Editor.Focus();
                e.Handled = true;
                break;
            case Key.Enter:
                ViewModel.CommitQuickSearch();
                Editor.Focus();
                e.Handled = true;
                break;
            case Key.Down:
                ViewModel.MoveQuickSearchSelection(1);
                ScrollQuickSearchSelectionIntoView();
                e.Handled = true;
                break;
            case Key.Up:
                ViewModel.MoveQuickSearchSelection(-1);
                ScrollQuickSearchSelectionIntoView();
                e.Handled = true;
                break;
        }
    }

    private void ScrollQuickSearchSelectionIntoView()
    {
        if (QuickSearchList.SelectedItem != null)
        {
            QuickSearchList.ScrollIntoView(QuickSearchList.SelectedItem);
        }
    }

    private void OnQuickSearchListClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null &&
            (e.OriginalSource as FrameworkElement)?.DataContext is LibraryEntryViewModel entry)
        {
            ViewModel.CommitQuickSearch(entry);
            Editor.Focus();
            e.Handled = true;
        }
    }

    private void OnQuickSearchFocusChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Clicking anywhere else (canvas, toolbar, library) dismisses the popup.
        if (!(bool)e.NewValue && ViewModel != null && ViewModel.IsQuickSearchOpen)
        {
            ViewModel.CloseQuickSearch();
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
        if (e.ClickCount == 2 && ViewModel != null && WireUnder(e.OriginalSource) is ConnectionViewModel wire)
        {
            // Double-click a wire to bend it with a reroute.
            ViewModel.InsertRerouteOnWire(wire, Editor.MouseLocation);
            e.Handled = true;
            return;
        }

        if (e.ClickCount != 2 || ViewModel == null || !IsEmptyCanvasHit(e.OriginalSource))
        {
            return;
        }

        // MouseLocation is already in graph-space coordinates. What the
        // double-click does is a persisted setting (Settings ▸ double-click).
        switch (ViewModel.DoubleClickAction)
        {
            case "none":
                return;
            case "note":
                ViewModel.AddNote(Editor.MouseLocation);
                e.Handled = true;
                return;
            case "number":
                if (ViewModel.AddNode(Dyncamelo.Core.Nodes.NumberInputNode.TypeName, Editor.MouseLocation) != null)
                {
                    e.Handled = true;
                }

                return;
            case "string":
            default:
                if (ViewModel.AddNode(Dyncamelo.Core.Nodes.StringInputNode.TypeName, Editor.MouseLocation) != null)
                {
                    e.Handled = true;
                }

                return;
        }
    }

    private object? WireUnder(object originalSource)
    {
        var current = originalSource as DependencyObject;
        while (current != null && !ReferenceEquals(current, Editor))
        {
            if (current is BaseConnection connection && connection.DataContext is ConnectionViewModel wire)
            {
                return wire;
            }

            current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
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
            ViewModel.AddNodeOnWire(id, location);
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
