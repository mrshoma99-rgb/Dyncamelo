using System;

namespace CamelGraph.UI.Mvvm;

/// <summary>
/// Keeps a failing command from taking the host application down. Inside Navisworks an exception escaping a
/// WPF handler on the UI thread ends the whole process, so the host installs a <see cref="Handler"/> that
/// reports the problem in the editor instead. With no handler (unit tests) exceptions propagate unchanged.
/// </summary>
public static class CommandGuard
{
    /// <summary>Receives exceptions swallowed from commands; null = let them propagate.</summary>
    public static Action<Exception>? Handler { get; set; }

    /// <summary>False for failures that must never be swallowed (memory, aborts).</summary>
    public static bool IsRecoverable(Exception exception) =>
        !(exception is OutOfMemoryException || exception is System.Threading.ThreadAbortException || exception is AccessViolationException);

    /// <summary>Runs <paramref name="action"/>, routing a recoverable exception to <see cref="Handler"/>.</summary>
    public static void Run(Action action)
    {
        var handler = Handler;
        if (handler == null)
        {
            action();
            return;
        }

        try
        {
            action();
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            handler(ex);
        }
    }
}
