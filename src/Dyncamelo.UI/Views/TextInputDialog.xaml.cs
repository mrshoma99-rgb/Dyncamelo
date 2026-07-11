using System.Windows;
using System.Windows.Input;

namespace Dyncamelo.UI.Views;

/// <summary>
/// A small dark-themed modal prompt: a message, a single-line text box and
/// OK/Cancel. Used by <see cref="Services.WpfDialogService.Prompt"/> (e.g. to
/// rename the open .dyc file). Returns the entered text via
/// <see cref="ResponseText"/> when <c>ShowDialog()</c> is true.
/// </summary>
public partial class TextInputDialog : Window
{
    /// <summary>Creates the dialog.</summary>
    /// <param name="message">Prompt text shown above the box.</param>
    /// <param name="title">Dialog caption.</param>
    /// <param name="defaultValue">Pre-filled, pre-selected text.</param>
    public TextInputDialog(string message, string title, string defaultValue)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ResponseBox.Text = defaultValue ?? string.Empty;
        Loaded += (_, _) =>
        {
            ResponseBox.Focus();
            ResponseBox.SelectAll();
        };
    }

    /// <summary>The text the user entered (valid when the dialog returned true).</summary>
    public string ResponseText => ResponseBox.Text;

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnResponseKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Accept();
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => Accept();

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Accept()
    {
        DialogResult = true;
    }
}
