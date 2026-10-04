using CamelGraph.Core.Editing;
using CamelGraph.Core.Loader;

namespace CamelGraph.UI.Services;

/// <summary>Opens the dialog a <see cref="PathPick"/> asks for.</summary>
public static class PathPickExtensions
{
    /// <summary>
    /// Shows the folder chooser, the save dialog or the open dialog that <paramref name="pick"/> names, and returns what the user chose.
    /// </summary>
    /// <param name="dialogs">The dialog service.</param>
    /// <param name="pick">What to open (see <see cref="PathPicker.Resolve"/>).</param>
    /// <param name="current">The path now in the box: where a folder chooser starts and what a save dialog pre-fills.</param>
    /// <returns>The chosen path, or null when cancelled.</returns>
    public static string? PickPath(this IDialogService dialogs, PathPick pick, string current)
    {
        switch (pick.Mode)
        {
            case NodePathMode.Folder:
                return dialogs.PickFolder(pick.Title, current);
            case NodePathMode.Save:
                return dialogs.ShowSaveFile(pick.Filter, pick.Title, current);
            default:
                return dialogs.ShowOpenFile(pick.Filter, pick.Title);
        }
    }
}
