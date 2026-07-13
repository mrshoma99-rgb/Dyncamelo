using System.Windows;
using Microsoft.Win32;

namespace Dyncamelo.UI.Services;

/// <summary>
/// Default <see cref="IDialogService"/> backed by the standard WPF dialogs.
/// </summary>
public class WpfDialogService : IDialogService
{
    /// <inheritdoc />
    public string? ShowOpenFile(string filter, string title)
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            Title = title,
            CheckFileExists = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? ShowSaveFile(string filter, string title, string defaultFileName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            Title = title,
            FileName = defaultFileName,
            AddExtension = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public bool Confirm(string message, string title)
    {
        return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;
    }

    /// <inheritdoc />
    public void ShowError(string message, string title)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <inheritdoc />
    public string? Prompt(string message, string title, string defaultValue)
    {
        var dialog = new Views.TextInputDialog(message, title, defaultValue);
        // Owner keeps the dialog above the host; Application.Current is null in
        // the Navisworks WinForms/ElementHost, so this is best-effort.
        var owner = System.Windows.Application.Current?.MainWindow;
        if (owner != null)
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true ? dialog.ResponseText : null;
    }

    /// <inheritdoc />
    public (int A, int R, int G, int B)? PickColor(int a, int r, int g, int b)
    {
        var dialog = new Views.ColorPickerDialog(a, r, g, b);
        var owner = System.Windows.Application.Current?.MainWindow;
        if (owner != null)
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true ? (dialog.A, dialog.R, dialog.G, dialog.B) : ((int, int, int, int)?)null;
    }
}
