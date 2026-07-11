namespace Dyncamelo.UI.Services;

/// <summary>
/// UI dialogs abstracted away from view models so they stay testable and the
/// host (Navisworks dock pane, future sandbox) can substitute its own dialogs.
/// </summary>
public interface IDialogService
{
    /// <summary>Shows an open-file dialog.</summary>
    /// <param name="filter">WPF file dialog filter string.</param>
    /// <param name="title">Dialog caption.</param>
    /// <returns>The selected path, or null when cancelled.</returns>
    string? ShowOpenFile(string filter, string title);

    /// <summary>Shows a save-file dialog.</summary>
    /// <param name="filter">WPF file dialog filter string.</param>
    /// <param name="title">Dialog caption.</param>
    /// <param name="defaultFileName">Pre-filled file name.</param>
    /// <returns>The selected path, or null when cancelled.</returns>
    string? ShowSaveFile(string filter, string title, string defaultFileName);

    /// <summary>Shows a yes/no confirmation.</summary>
    /// <param name="message">Question text.</param>
    /// <param name="title">Dialog caption.</param>
    /// <returns>True when the user confirmed.</returns>
    bool Confirm(string message, string title);

    /// <summary>Shows an error message box.</summary>
    /// <param name="message">Error text.</param>
    /// <param name="title">Dialog caption.</param>
    void ShowError(string message, string title);

    /// <summary>Prompts for a single line of text.</summary>
    /// <param name="message">Prompt text.</param>
    /// <param name="title">Dialog caption.</param>
    /// <param name="defaultValue">Pre-filled, pre-selected text.</param>
    /// <returns>The entered text, or null when cancelled.</returns>
    string? Prompt(string message, string title, string defaultValue);
}
