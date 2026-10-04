using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CamelGraph.UI.Services;
using CamelGraph.UI.ViewModels;

namespace CamelGraph.UI.Views;

/// <summary>
/// The Script Player pane: a list of scripts, the form of the one selected, a Run button and the results — no canvas, so it
/// opens at once. Put a <see cref="PlayerViewModel"/> in <see cref="ViewModel"/>.
/// </summary>
public partial class PlayerControl : UserControl, IHostKeyTarget
{
    /// <summary>Creates the control.</summary>
    public PlayerControl()
    {
        // The pane may be the first thing opened in the session: the theme needs Nodify loaded already (see ThemeDependencies).
        ThemeDependencies.EnsureLoaded();
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        PreviewKeyDown += OnPlayerPreviewKeyDown;
        PreviewMouseDown += (_, _) => CamelGraph.UI.Services.CrashGuard.NoteActivity();
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue && ViewModel != null)
            {
                // The palette may have been changed in the editor since the pane was last shown; the list may be stale.
                ApplyPalette(ViewModel.PaletteId);
                ViewModel.EnsureScanned();
            }
        };
    }

    /// <summary>The Player view model (stored in the DataContext).</summary>
    public PlayerViewModel? ViewModel
    {
        get => DataContext as PlayerViewModel;
        set => DataContext = value;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PlayerViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is PlayerViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.RenderPump = PumpRender;
            ApplyPalette(viewModel.PaletteId);
        }
    }

    private void ApplyPalette(string paletteId)
    {
        var palette = PaletteCatalog.ById(paletteId) ?? PaletteCatalog.Default;
        ThemeApplier.Apply(Resources, palette);
    }

    // A run blocks the UI thread, so the pane is painted first (progress text, wait cursor) and repainted between the steps.
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.IsListOpen) && ViewModel != null && ViewModel.IsListOpen)
        {
            // Unfolded: typing filters at once.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (SearchBox.IsVisible)
                {
                    SearchBox.Focus();
                    SearchBox.SelectAll();
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        if (e.PropertyName == nameof(PlayerViewModel.IsRunning) && ViewModel != null)
        {
            if (ViewModel.IsRunning)
            {
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

    // Processes rendering, never input: the pane can be repainted but the form cannot be edited under a run.
    private void PumpRender()
    {
        Dispatcher.Invoke(new Action(() => { }), System.Windows.Threading.DispatcherPriority.Render);
    }

    // ----- keys the host would otherwise take ---------------------------------------------------

    private static bool IsTextEditingKey(Key key) =>
        key == Key.Z || key == Key.Y || key == Key.X || key == Key.C || key == Key.V || key == Key.A;

    /// <inheritdoc />
    public bool WantsHostKey(Key key)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var typing = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;
        if (typing && ctrl && IsTextEditingKey(key))
        {
            return true;
        }

        // Ctrl+C / Ctrl+V over a number field copy and paste its value (a pasted coordinate fills several fields).
        return !typing && ctrl && ScrubNumberBox.Hovered != null && (key == Key.C || key == Key.V);
    }

    /// <inheritdoc />
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

        var typing = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;
        return typing && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && IsTextEditingKey(key);
    }

    private void OnPlayerPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var typing = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;
        if (!typing && ScrubNumberBox.Hovered is ScrubNumberBox hovered && hovered.HandleHoverKey(e))
        {
            e.Handled = true;
        }
    }

    // Enter or Esc in the list; the arrow keys move the selection as in any list.
    private void OnScriptListKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel == null)
        {
            return;
        }

        if (e.Key == Key.Enter && ScriptList.SelectedItem != null)
        {
            ChooseScript();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && ViewModel.HasScript)
        {
            ViewModel.CloseList();
            e.Handled = true;
        }
    }

    // Down leaves the search box for the list, Enter chooses the first match, Esc folds the list.
    private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel == null)
        {
            return;
        }

        if (e.Key == Key.Down && ScriptList.Items.Count > 0)
        {
            if (ScriptList.SelectedItem == null)
            {
                ScriptList.SelectedIndex = 0;
            }

            (ScriptList.ItemContainerGenerator.ContainerFromItem(ScriptList.SelectedItem) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && ScriptList.Items.Count > 0)
        {
            if (!(ScriptList.SelectedItem is ScriptListItem chosen) || !ViewModel.Scripts.Contains(chosen))
            {
                ScriptList.SelectedIndex = 0;
            }

            ChooseScript();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && ViewModel.HasScript)
        {
            ViewModel.CloseList();
            e.Handled = true;
        }
    }

    // A click on a script chooses it; a click on a group's name or on empty space does not.
    private void OnScriptListMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null && ScriptList.SelectedItem != null && e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(ScriptList, source) is ListBoxItem)
        {
            ViewModel.CloseList();
        }
    }

    // The list folds away and the Run button is next, so Enter again runs the script.
    private void ChooseScript()
    {
        ViewModel?.CloseList();
        RunButton.Focus();
    }
}
