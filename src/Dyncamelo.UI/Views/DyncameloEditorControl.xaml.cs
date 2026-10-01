using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dyncamelo.UI.Services;
using Dyncamelo.UI.Mvvm;
using Dyncamelo.UI.ViewModels;
using Nodify;

namespace Dyncamelo.UI.Views;

/// <summary>
/// The complete Dyncamelo editor: toolbar, node library browser, Nodify canvas
/// and status bar. Host-agnostic — the hosting layer sets <see cref="ViewModel"/>
/// (or the DataContext) to a configured <see cref="GraphEditorViewModel"/>.
/// </summary>
public partial class DyncameloEditorControl : UserControl, IHostKeyTarget
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

        // A socket's tooltip shows the value it holds now, which changes with every run. Re-read it as the pointer enters
        // (the tooltip itself only appears after its show delay, so the fresh text is in place by then).
        EventManager.RegisterClassHandler(typeof(NodeInput), Mouse.MouseEnterEvent, new MouseEventHandler(OnSocketMouseEnter));
        EventManager.RegisterClassHandler(typeof(NodeOutput), Mouse.MouseEnterEvent, new MouseEventHandler(OnSocketMouseEnter));
    }

    private static void OnSocketMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ConnectorViewModel connector)
        {
            connector.RefreshToolTip();
        }
    }

    /// <summary>Creates the control. Assign <see cref="ViewModel"/> before showing it.</summary>
    public DyncameloEditorControl()
    {
        ThemeDependencies.EnsureLoaded();
        InitializeComponent();
        AliasLibrarySelectionBrushes();

        // Double-clicking empty canvas inserts a String input node at the click
        // position (Dynamo-style quick node). handledEventsToo because the
        // editor consumes mouse-downs for selection.
        Editor.AddHandler(
            MouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnEditorMouseLeftButtonDown),
            handledEventsToo: true);

        PreviewKeyDown += OnControlPreviewKeyDown;

        // Anything done in the editor counts as recent activity for the crash guard: a WPF failure that follows is the editor's.
        PreviewMouseDown += (_, _) => CrashGuard.NoteActivity();
        PreviewKeyDown += (_, _) => CrashGuard.NoteActivity();

        // The pane's own chrome (dragging or docking it) lives outside this control. A mouse capture that outlived
        // its gesture would keep the host from receiving those clicks, so drop any stale one when the pointer leaves.
        MouseLeave += (_, _) => ReleaseStaleMouseCapture();
        IsKeyboardFocusWithinChanged += (_, e) =>
        {
            if (!(bool)e.NewValue)
            {
                ReleaseStaleMouseCapture();
            }
        };

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

    /// <summary>
    /// Releases a mouse capture held by an element of this control when no mouse button is down and no menu is open
    /// (a gesture that ended without releasing it). Captures inside popups belong to the popups and are left alone.
    /// </summary>
    public void ReleaseStaleMouseCapture()
    {
        if (Mouse.LeftButton != MouseButtonState.Released || Mouse.RightButton != MouseButtonState.Released ||
            Mouse.MiddleButton != MouseButtonState.Released)
        {
            return;
        }

        foreach (var item in HeaderMenu.Items)
        {
            if (item is MenuItem menuItem && menuItem.IsSubmenuOpen)
            {
                return;
            }
        }

        if (Mouse.Captured is Visual captured && IsAncestorOf(captured))
        {
            Mouse.Capture(null);
        }
    }

    // The minimap reports wheel zoom as a request; the editor does the zooming around that point.
    private void OnMinimapZoom(object sender, Nodify.Events.ZoomEventArgs e) => Editor.ZoomAtPosition(e.Zoom, e.Location);

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

    private ShortcutRouter _router = new ShortcutRouter(new[] { "graph.addnode" });
    private RelayCommand? _guideCommand;
    private RelayCommand? _hudCommand;
    private RelayCommand? _previewsCommand;
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
                "view.minimap" => vm.IsMinimapVisible,
                "view.library" => vm.IsLibraryVisible,
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
            },
            vm.Keymap);
    }

    // The user rebound a shortcut (or reset them): the key router and the menus show the new chords.
    private void OnKeymapChanged(object? sender, System.EventArgs e)
    {
        if (ViewModel != null)
        {
            RebuildKeyRouter(ViewModel);
            BuildHeaderMenu(ViewModel);
        }
    }

    // The canvas context menu is written in XAML; its shortcut text follows the keymap like the header menus.
    private void ApplyContextMenuShortcuts(GraphEditorViewModel vm)
    {
        if (Editor.ContextMenu == null)
        {
            return;
        }

        foreach (var item in Editor.ContextMenu.Items)
        {
            if (item is MenuItem menuItem && menuItem.Tag is string id)
            {
                menuItem.InputGestureText = vm.Keymap.ShortcutOf(id) ?? string.Empty;
            }
        }
    }

    private GridLength _libraryWidth = new GridLength(230d);

    // The library column takes its width back where it left off; hidden, the column, its splitter and the panel are gone.
    // The selection colours of the library tree are the theme's own brushes (the palette changes their Color in place), so the
    // selection follows a palette change. Written in XAML as a Binding with a DynamicResource Source, WPF rejected it — and only
    // when a library item was first selected, which is what closed Navisworks when a node was picked from the library.
    private void AliasLibrarySelectionBrushes()
    {
        var resources = LibraryTree.Resources;
        resources[SystemColors.HighlightBrushKey] = (Brush)FindResource("Dyc.HoverBrush");
        resources[SystemColors.HighlightTextBrushKey] = (Brush)FindResource("Dyc.TextBrush");
        resources[SystemColors.InactiveSelectionHighlightBrushKey] = (Brush)FindResource("Dyc.PanelBorderBrush");
        resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = (Brush)FindResource("Dyc.TextBrush");
    }

    private void ApplyLibraryVisibility(bool visible)
    {
        if (visible)
        {
            LibraryColumn.MinWidth = 150d;
            LibraryColumn.Width = _libraryWidth;
            SplitterColumn.Width = new GridLength(4d);
            LibraryPanel.Visibility = Visibility.Visible;
            LibrarySplitter.Visibility = Visibility.Visible;
        }
        else
        {
            if (LibraryColumn.Width.Value > 0d)
            {
                _libraryWidth = LibraryColumn.Width;
            }

            LibraryColumn.MinWidth = 0d;
            LibraryColumn.Width = new GridLength(0d);
            SplitterColumn.Width = new GridLength(0d);
            LibraryPanel.Visibility = Visibility.Collapsed;
            LibrarySplitter.Visibility = Visibility.Collapsed;
        }
    }

    private Brush? _gridBackground;

    // Grid lines are the canvas background tile; without them the canvas is the plain background colour.
    private void ApplyGrid(bool show)
    {
        _gridBackground ??= Editor.Background;
        Editor.Background = show ? _gridBackground : TryFindResource("Dyc.CanvasBrush") as Brush ?? _gridBackground;
    }

    private void RebuildKeyRouter(GraphEditorViewModel vm)
    {
        ApplyContextMenuShortcuts(vm);
        // Space over the canvas has special handling (it opens the search at the pointer); a rebound chord goes through the router.
        var skip = vm.Keymap.ShortcutOf("graph.addnode") == "Space" ? new[] { "graph.addnode" } : new string[0];
        _router = new ShortcutRouter(vm.Keymap, skip);
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
            case "edit.cut": return vm.CutSelectionCommand;
            case "edit.selectall": return vm.SelectAllItemsCommand;
            case "edit.copy": return vm.CopySelectionCommand;
            case "edit.paste": return vm.PasteCommand;
            case "edit.duplicate": return vm.DuplicateSelectionCommand;
            case "edit.delete": return vm.DeleteSelectionCommand;
            case "edit.deletereconnect": return vm.DeleteAndReconnectCommand;
            case "edit.selectdownstream": return vm.SelectDownstreamCommand;
            case "edit.selectupstream": return vm.SelectUpstreamCommand;
            case "edit.selectsimilar": return vm.SelectSimilarCommand;
            case "edit.navleft": return vm.NavigateLeftCommand;
            case "edit.navright": return vm.NavigateRightCommand;
            case "edit.navup": return vm.NavigateUpCommand;
            case "edit.navdown": return vm.NavigateDownCommand;
            case "edit.history": return vm.ToggleHistoryCommand;
            case "view.frameselected": return vm.FrameSelectedCommand;
            case "view.problems": return vm.ToggleProblemsCommand;
            case "view.bookmarks": return vm.ToggleBookmarksCommand;
            case "view.addbookmark": return vm.AddBookmarkCommand;
            case "graph.runtohere": return vm.RunToHereCommand;
            case "graph.nextproblem": return vm.NextProblemCommand;
            case "graph.prevproblem": return vm.PreviousProblemCommand;
            case "graph.findnode": return vm.FindNodeCommand;
            case "node.explain": return vm.ExplainSelectedCommand;
            case "view.fit": return EditorCommands.FitToScreen;
            case "view.zoomin": return EditorCommands.ZoomIn;
            case "view.zoomout": return EditorCommands.ZoomOut;
            case "view.collapseall": return vm.CollapseAllCommand;
            case "view.expandall": return vm.ExpandAllCommand;
            case "node.collapse": return vm.ToggleCollapseSelectedCommand;
            case "node.hideunused": return vm.ToggleHideUnusedSelectedCommand;
            case "node.mute": return vm.ToggleMuteSelectedCommand;
            case "node.freeze": return vm.ToggleFreezeSelectedCommand;
            case "node.player": return vm.TogglePlayerNodeCommand;
            case "node.playerinputs": return vm.TogglePlayerInputsCommand;
            case "graph.describe": return vm.DescribeGraphCommand;
            case "view.player": return vm.OpenPlayerCommand;
            case "group.make": return vm.MakeGroupCommand;
            case "group.ungroup": return vm.UngroupNodeGroupCommand;
            case "group.edit": return vm.ToggleGroupEditCommand;
            case "group.exit": return vm.ExitGroupCommand;
            case "group.rename": return vm.RenameNodeGroupCommand;
            case "group.singleuser": return vm.MakeSingleUserCommand;
            case "group.addinput": return vm.AddGroupInputCommand;
            case "group.addoutput": return vm.AddGroupOutputCommand;
            case "group.purge": return vm.PurgeNodeGroupsCommand;
            case "node.autoconnect": return vm.AutoConnectCommand;
            case "node.resetinputs": return vm.ResetSelectedInputsCommand;
            case "node.insertonwire": return vm.InsertIntoSelectedWireCommand;
            case "wire.swap": return vm.SwapSelectedLinksCommand;
            case "wire.earlier": return vm.MoveSelectedWiresEarlierCommand;
            case "wire.later": return vm.MoveSelectedWiresLaterCommand;
            case "view.minimap": return vm.ToggleMinimapCommand;
            case "view.library": return vm.ToggleLibraryCommand;
            case "view.resetwidth": return vm.ResetSelectedWidthCommand;
            case "help.guide": return _guideCommand ??= new RelayCommand(OpenGuide);
            case "wire.mute": return vm.MuteSelectedWiresCommand;
            case "wire.reroute": return vm.RerouteSelectedWiresCommand;
            case "wire.disconnect": return vm.DisconnectSelectedWiresCommand;
            case "help.keys": return vm.ToggleHelpCommand;
            case "view.hud": return _hudCommand;
            case "view.previews": return _previewsCommand ??= new RelayCommand(() => vm.ShowNodePreviews = !vm.ShowNodePreviews);
            case "view.settings": return vm.ToggleSettingsCommand;
            case "help.palette": return vm.TogglePaletteCommand;
            case "graph.autorun": return _autoRunCommand ??= new RelayCommand(() => vm.IsAutoRun = !vm.IsAutoRun);
            case "graph.run": return vm.RunCommand;
            case "graph.rename": return vm.RenameCommand;
            case "graph.addnote": return _addNoteCommand;
            case "graph.group": return vm.GroupSelectionCommand;
            case "graph.fitframe": return vm.FitFrameCommand;
            case "graph.ungroup": return vm.UngroupSelectedCommand;
            case "graph.arrange": return vm.ArrangeSelectionCommand;
            case "graph.arrangeall": return vm.ArrangeAllCommand;
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

    private static void OpenGuide()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "https://github.com/mrshoma99-rgb/Dyncamelo/blob/main/docs/UI_GUIDE.md") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No browser association: the guide is also in the docs folder of the repository.
        }
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
            oldViewModel.KeymapChanged -= OnKeymapChanged;
            oldViewModel.GroupNavigated -= OnGroupNavigated;
            oldViewModel.RevealRequested -= OnRevealRequested;
        }

        if (e.NewValue is GraphEditorViewModel newViewModel)
        {
            newViewModel.Library.EntryRevealRequested += OnLibraryEntryRevealRequested;
            newViewModel.PropertyChanged += OnViewModelPropertyChanged;
            newViewModel.KeymapChanged += OnKeymapChanged;
            newViewModel.GroupNavigated += OnGroupNavigated;
            newViewModel.RevealRequested += OnRevealRequested;
            newViewModel.ViewProvider = () => new ViewSnapshot(ViewportCenter, Editor.ViewportZoom);
            newViewModel.CommandResolver = id => ResolveCommand(newViewModel, id);
            newViewModel.CommandTarget = Editor;
            newViewModel.PointerLocation = () => Editor.MouseLocation;
            newViewModel.RenderPump = PumpRender;
            // Once the window is up: start the autosave timer and offer the graph of a session that died unsaved.
            Dispatcher.BeginInvoke(
                new System.Action(() =>
                {
                    newViewModel.StartAutosave();
                    newViewModel.OfferRecovery();
                }),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            // Apply the persisted palette once the view model is attached.
            ApplyPalette(newViewModel.PaletteId);
            RebuildKeyRouter(newViewModel);
            BuildHeaderMenu(newViewModel);
            ApplyGrid(newViewModel.ShowGrid);
            ApplyLibraryVisibility(newViewModel.IsLibraryVisible);
            ApplyUiScale(newViewModel.UiScaleFactor);
            UpdateHintVisibility();
        }
    }

    // ----- revealing parts of the canvas -------------------------------------------------------

    // The most the canvas zooms in when framing a few small things (a single node should not fill the screen).
    private const double FrameMaxZoom = 1.25d;

    private void OnRevealRequested(object? sender, RevealEventArgs e)
    {
        if (e.Zoom < 0d)
        {
            // Frame: fit the rectangle, but never closer than a comfortable size.
            Editor.FitToScreen(e.Bounds);
            if (Editor.ViewportZoom > FrameMaxZoom)
            {
                Editor.ViewportZoom = FrameMaxZoom;
                Editor.BringIntoView(e.Bounds);
            }

            return;
        }

        if (e.Zoom > 0d)
        {
            Editor.ViewportZoom = e.Zoom;
        }

        var view = new Rect(Editor.ViewportLocation, Editor.ViewportSize);
        var inside = view.Contains(e.Bounds.TopLeft) && view.Contains(e.Bounds.BottomRight);
        if (e.AlwaysCenter || !inside)
        {
            Editor.BringIntoView(e.Bounds);
        }
    }

    // A list row was clicked (not its remove button): go there.
    private void OnInfoPanelItemClick(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source != null && !ReferenceEquals(source, InfoPanelList))
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase)
            {
                return;     // the remove button handles its own click
            }

            if (source is ListBoxItem item && item.DataContext is PanelItemViewModel row)
            {
                row.ActivateCommand.Execute(null);
                e.Handled = true;
                return;
            }

            source = source is Visual || source is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
    }

    // ----- window scale and the hint line -------------------------------------------------------

    private void ApplyUiScale(double factor)
    {
        LayoutRoot.LayoutTransform = Math.Abs(factor - 1d) < 0.001d ? Transform.Identity : new ScaleTransform(factor, factor);
    }

    private void OnStatusBarSizeChanged(object sender, SizeChangedEventArgs e) => UpdateHintVisibility();

    // The hint goes first when the pane is narrow: the run figures and the status message matter more.
    private void UpdateHintVisibility()
    {
        var room = StatusBar.ActualWidth - 560d;
        var wanted = ViewModel?.ShowStatusHints ?? true;
        HintLabel.Visibility = wanted && room >= 120d ? Visibility.Visible : Visibility.Collapsed;
        HintLabel.MaxWidth = Math.Max(0d, Math.Min(room, 520d));
    }

    // Opening a node group shows its body fitted to the canvas; closing it puts the view back where it was.
    private readonly System.Collections.Generic.Stack<(Point Location, double Zoom)> _viewStack = new System.Collections.Generic.Stack<(Point, double)>();

    private void OnGroupNavigated(object? sender, GroupNavigationEventArgs e)
    {
        if (e.Entered)
        {
            _viewStack.Push((Editor.ViewportLocation, Editor.ViewportZoom));
            Dispatcher.BeginInvoke(
                new System.Action(() => EditorCommands.FitToScreen.Execute(null, Editor)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else if (_viewStack.Count > 0)
        {
            var view = _viewStack.Pop();
            Dispatcher.BeginInvoke(
                new System.Action(() =>
                {
                    Editor.ViewportZoom = view.Zoom;
                    Editor.ViewportLocation = view.Location;
                }),
                System.Windows.Threading.DispatcherPriority.Loaded);
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
        else if (e.PropertyName == nameof(GraphEditorViewModel.IsLibraryVisible) && ViewModel != null)
        {
            ApplyLibraryVisibility(ViewModel.IsLibraryVisible);
        }
        else if (e.PropertyName == nameof(GraphEditorViewModel.UiScaleFactor) && ViewModel != null)
        {
            ApplyUiScale(ViewModel.UiScaleFactor);
        }
        else if (e.PropertyName == nameof(GraphEditorViewModel.ShowStatusHints))
        {
            UpdateHintVisibility();
        }
        else if (e.PropertyName == nameof(GraphEditorViewModel.ShowGrid) && ViewModel != null)
        {
            ApplyGrid(ViewModel.ShowGrid);
        }
        else if (e.PropertyName == nameof(GraphEditorViewModel.IsPaletteOpen) && ViewModel?.IsPaletteOpen == true)
        {
            Dispatcher.BeginInvoke(
                new System.Action(() =>
                {
                    PaletteBox.Focus();
                    PaletteBox.SelectAll();
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
                PumpRender();
            }
            else
            {
                Mouse.OverrideCursor = null;
            }
        }
    }

    // Processes rendering (and bindings), never input: a run can be repainted but the graph cannot be edited under it.
    private void PumpRender()
    {
        Dispatcher.Invoke(new System.Action(() => { }), System.Windows.Threading.DispatcherPriority.Render);
    }

    /// <summary>
    /// Recolours the UI: <see cref="Dyncamelo.UI.Services.ThemeApplier"/> puts the palette into the theme dictionary, and everything
    /// that refers to a palette colour does so with DynamicResource, so styles, templates and popups all follow. Node header and
    /// group colours are data colours and deliberately not themed.
    /// </summary>
    private void ApplyPalette(string paletteId)
    {
        var palette = Dyncamelo.UI.Services.PaletteCatalog.ById(paletteId)
            ?? Dyncamelo.UI.Services.PaletteCatalog.Default;
        Dyncamelo.UI.Services.ThemeApplier.Apply(Resources, palette);
        ViewModel?.RefreshPortColours();
    }

    /// <summary>The modifier keys currently held; replaceable so tests can press Ctrl without a keyboard.</summary>
    public Func<ModifierKeys> ModifierProvider { get; set; } = () => Keyboard.Modifiers;

    private ModifierKeys Modifiers => ModifierProvider();

    private static bool IsTextEditingKey(Key key) =>
        key == Key.Z || key == Key.Y || key == Key.X || key == Key.C || key == Key.V || key == Key.A;

    /// <summary>
    /// True when a key pressed while this pane has the keyboard focus is Dyncamelo's to handle: one of its
    /// shortcuts, a text-editing chord inside a text box (Ctrl+Z/Y/X/C/V/A edit the text, not the host's document),
    /// or a hover key of a number field. The host uses this to decide whether to keep the key from its own
    /// accelerators (Navisworks binds Ctrl+Z, Ctrl+Y, F1, Delete … and would otherwise act on its own document).
    /// </summary>
    public bool WantsHostKey(Key key)
    {
        if (ViewModel == null)
        {
            return false;
        }

        // While a shortcut is being recorded every key belongs to the recorder, and Esc closes an open overlay.
        if (ViewModel.IsCapturingShortcut ||
            (key == Key.Escape && (ViewModel.IsHelpOpen || ViewModel.IsPaletteOpen || ViewModel.IsSettingsOpen)))
        {
            return true;
        }

        var modifiers = Modifiers;
        var ctrl = (modifiers & ModifierKeys.Control) != 0;
        var typing = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;
        if (typing && ctrl && (IsTextEditingKey(key) || key == Key.D || key == Key.G))
        {
            return true;
        }

        if (!typing && ctrl && ScrubNumberBox.Hovered != null && (key == Key.C || key == Key.V))
        {
            return true;
        }

        return _router.Find(key, modifiers) != null;
    }

    /// <summary>
    /// Runs a key the host was about to take through the normal WPF key path (the same handlers a key that reached
    /// WPF directly would run) and reports whether it was consumed. A text-editing chord in a text box counts as
    /// consumed even when the box has nothing to undo, so it never falls through to the host.
    /// </summary>
    /// <param name="key">The pressed key.</param>
    /// <returns>True when the key must not be given to the host.</returns>
    public bool ProcessHostKey(Key key)
    {
        var source = PresentationSource.FromVisual(this);
        if (source == null)
        {
            return false;
        }

        var target = Keyboard.FocusedElement ?? this;
        var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(preview);
        if (preview.Handled)
        {
            return true;
        }

        var down = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent };
        target.RaiseEvent(down);
        if (down.Handled)
        {
            return true;
        }

        var typing = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;
        return typing && (Modifiers & ModifierKeys.Control) != 0 && IsTextEditingKey(key);
    }

    private void OnControlPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool typing = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;

        if (ViewModel != null && ViewModel.IsCapturingShortcut)
        {
            CaptureShortcutKey(ViewModel, e);
            return;
        }

        if (e.Key == Key.Escape && ViewModel != null && CloseTopOverlay(ViewModel))
        {
            e.Handled = true;
            return;
        }

        // Under an open overlay (settings, palette, help) the canvas shortcuts must not act on the graph behind it.
        var overlayOpen = ViewModel != null && (ViewModel.IsSettingsOpen || ViewModel.IsPaletteOpen || ViewModel.IsHelpOpen);

        if (e.Key == Key.F12 && Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && _perfHud != null)
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
            !overlayOpen &&
            e.Key == Key.Space &&
            Modifiers == ModifierKeys.None &&
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
            bool ctrl = (Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
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
                Modifiers,
                typing || overlayOpen,
                Editor.IsKeyboardFocusWithin,
                id => ResolveCommand(ViewModel, id),
                Editor))
        {
            e.Handled = true;
        }
    }

    // Closes the top-most overlay (palette, then settings, then help); false when none was open.
    private bool CloseTopOverlay(GraphEditorViewModel vm)
    {
        if (vm.IsPaletteOpen)
        {
            vm.ClosePalette();
        }
        else if (vm.IsSettingsOpen)
        {
            vm.IsSettingsOpen = false;
        }
        else if (vm.IsHelpOpen)
        {
            vm.IsHelpOpen = false;
        }
        else
        {
            return false;
        }

        Editor.Focus();
        return true;
    }

    // A Change button was pressed: the next chord goes to the row (Esc cancels, Backspace removes the shortcut).
    private void CaptureShortcutKey(GraphEditorViewModel vm, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (ShortcutRouter.IsModifierKey(key))
        {
            return;
        }

        var modifiers = Modifiers;
        if (modifiers == ModifierKeys.None && key == Key.Escape)
        {
            vm.CancelShortcutCapture();
        }
        else if (modifiers == ModifierKeys.None && key == Key.Back)
        {
            vm.CommitShortcutCapture(string.Empty);
        }
        else
        {
            vm.CommitShortcutCapture(ShortcutRouter.ChordOf(key, modifiers).ToString());
        }
    }

    // ----- command palette (Ctrl+Shift+P) --------------------------------------

    private void OnPaletteBoxKeyDown(object sender, KeyEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                Editor.Focus();
                vm.RunPaletteEntry();
                e.Handled = true;
                break;
            case Key.Down:
                vm.MovePaletteSelection(1);
                ScrollPaletteSelectionIntoView();
                e.Handled = true;
                break;
            case Key.Up:
                vm.MovePaletteSelection(-1);
                ScrollPaletteSelectionIntoView();
                e.Handled = true;
                break;
        }
    }

    private void ScrollPaletteSelectionIntoView()
    {
        if (PaletteList.SelectedItem != null)
        {
            PaletteList.ScrollIntoView(PaletteList.SelectedItem);
        }
    }

    private void OnPaletteListClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null && (e.OriginalSource as FrameworkElement)?.DataContext is PaletteEntry entry)
        {
            Editor.Focus();
            ViewModel.RunPaletteEntry(entry);
            e.Handled = true;
        }
    }

    private void OnPaletteFocusChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue && ViewModel != null && ViewModel.IsPaletteOpen)
        {
            ViewModel.ClosePalette();
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

    // Files dragged in from Explorer: a .dyc graph is opened (after asking about unsaved changes).
    private static string[] DroppedFiles(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files ? files : new string[0];

    private void OnEditorDragOver(object sender, DragEventArgs e)
    {
        if (DroppedFiles(e).Any(f => f.EndsWith(".dyc", System.StringComparison.OrdinalIgnoreCase)))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnEditorDrop(object sender, DragEventArgs e)
    {
        if (ViewModel == null)
        {
            return;
        }

        var files = DroppedFiles(e);
        if (files.Length > 0)
        {
            e.Handled = true;

            // Opening shows dialogs; leave the drag-and-drop loop first so the source window is released.
            var viewModel = ViewModel;
            Dispatcher.BeginInvoke(
                new System.Action(() => viewModel.OpenDroppedFiles(files)),
                System.Windows.Threading.DispatcherPriority.Background);
            return;
        }

        if (!e.Data.GetDataPresent(DragDataFormat))
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
