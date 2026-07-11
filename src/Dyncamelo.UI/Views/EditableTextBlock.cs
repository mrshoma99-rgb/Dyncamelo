using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dyncamelo.UI.Views;

/// <summary>
/// A label that becomes an inline editor on double-click. Shows a
/// <see cref="TextBlock"/> normally; a left double-click swaps in a
/// <see cref="TextBox"/> (all text selected). Enter or losing focus commits the
/// edit back through the two-way bound <see cref="Text"/> property; Escape
/// cancels. Used for the node header title (renaming a placed node) — the theme
/// dictionary that hosts the node template has no code-behind, so the edit
/// interaction is encapsulated here.
/// </summary>
public class EditableTextBlock : UserControl
{
    /// <summary>The edited text; two-way bound by default (commits flow to the source).</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(EditableTextBlock),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnTextChanged));

    private readonly TextBlock _display;
    private readonly TextBox _editor;
    private bool _editing;

    /// <summary>Creates the control (display mode).</summary>
    public EditableTextBlock()
    {
        // The control itself is not a tab stop; only its editor takes focus.
        Focusable = false;

        _display = new TextBlock
        {
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // The header sits on a dark node band with white text; give the inline
        // editor matching dark-input styling (the theme brushes are not
        // reachable at construction time — this control is created inside a
        // DataTemplate before it joins the visual tree — so the values mirror
        // Dyc.InputBackgroundBrush / Dyc.AccentBrush).
        _editor = new TextBox
        {
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(1, 0, 1, 0),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x1B, 0x1E, 0x25)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x1F, 0xAE, 0xFF)),
            SelectionBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x1F, 0xAE, 0xFF)),
        };
        _editor.PreviewKeyDown += OnEditorPreviewKeyDown;
        _editor.LostKeyboardFocus += OnEditorLostKeyboardFocus;

        var grid = new Grid();
        grid.Children.Add(_display);
        grid.Children.Add(_editor);
        Content = grid;
    }

    /// <summary>The edited text.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((EditableTextBlock)d)._display.Text = e.NewValue as string ?? string.Empty;
    }

    /// <inheritdoc />
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);

        // Catch the double-click at tunnel time so Nodify never treats it as a
        // node drag / canvas gesture. Single clicks pass through (caret /
        // selection while editing).
        if (!_editing && e.ClickCount == 2)
        {
            BeginEdit();
            e.Handled = true;
        }
    }

    private void BeginEdit()
    {
        if (_editing)
        {
            return;
        }

        _editing = true;
        _editor.Text = Text ?? string.Empty;
        _editor.Visibility = Visibility.Visible;
        _display.Visibility = Visibility.Collapsed;

        // Focus after the editor is laid out so SelectAll targets a realized box.
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                _editor.Focus();
                _editor.SelectAll();
            }),
            DispatcherPriority.Input);
    }

    private void CommitEdit()
    {
        if (!_editing)
        {
            return;
        }

        _editing = false;
        // SetCurrentValue pushes to the two-way source (NodeViewModel.Title ->
        // Model.Name) without discarding the binding.
        SetCurrentValue(TextProperty, _editor.Text);
        EndEdit();
    }

    private void CancelEdit()
    {
        if (!_editing)
        {
            return;
        }

        _editing = false;
        EndEdit();
    }

    private void EndEdit()
    {
        _editor.Visibility = Visibility.Collapsed;
        _display.Visibility = Visibility.Visible;
    }

    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelEdit();
            e.Handled = true;
        }
    }

    private void OnEditorLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CommitEdit();
    }
}
