using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dyncamelo.UI.Services;
using Dyncamelo.UI.ViewModels;

namespace Dyncamelo.UI.Views;

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
        PreviewMouseDown += (_, _) => Dyncamelo.UI.Services.CrashGuard.NoteActivity();
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

    private void OnScriptListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel != null && ViewModel.RunCommand.CanExecute(null))
        {
            ViewModel.RunCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnScriptListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only a click on a script (not the group header or empty space) runs it.
        if (ViewModel != null && ScriptList.SelectedItem != null && e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(ScriptList, source) is ListBoxItem &&
            ViewModel.RunCommand.CanExecute(null))
        {
            ViewModel.RunCommand.Execute(null);
            e.Handled = true;
        }
    }
}
