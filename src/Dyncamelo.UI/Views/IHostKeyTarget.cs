using System.Windows.Input;

namespace Dyncamelo.UI.Views;

/// <summary>
/// A pane that keeps the host application's accelerators from taking its keys. Navisworks translates its own shortcuts
/// (Ctrl+Z, Ctrl+V, Delete, F1 …) before a key reaches the WPF content of a dock pane; the host asks the pane whether a key is
/// its own and, if so, runs it through the pane instead.
/// </summary>
public interface IHostKeyTarget
{
    /// <summary>True when the key is this pane's to handle (a shortcut of its own, or a text-editing chord inside a text box).</summary>
    /// <param name="key">The pressed key.</param>
    bool WantsHostKey(Key key);

    /// <summary>Runs the key through the pane's normal key handling.</summary>
    /// <param name="key">The pressed key.</param>
    /// <returns>True when the key was consumed and must not reach the host.</returns>
    bool ProcessHostKey(Key key);
}
