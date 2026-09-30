using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Dyncamelo.UI.Mvvm;
using Dyncamelo.UI.ViewModels;

namespace Dyncamelo.UI.Services;

/// <summary>
/// Installed by the host: an exception raised by Dyncamelo code on the UI thread is written to
/// <c>%APPDATA%\Dyncamelo\errors.log</c> and shown in the status bar instead of ending the host process
/// (in Navisworks an unhandled WPF exception closes Roamer). Exceptions from other components are left alone.
/// </summary>
public static class CrashGuard
{
    private static Dispatcher? _hooked;

    /// <summary>Routes command failures and unhandled UI-thread exceptions from Dyncamelo code to <paramref name="editor"/>.</summary>
    /// <param name="editor">The editor whose status bar reports the failure.</param>
    /// <param name="dispatcher">The UI dispatcher (the one that runs the editor).</param>
    public static void Install(GraphEditorViewModel editor, Dispatcher dispatcher)
    {
        CommandGuard.Handler = ex => Report(editor, "a command", ex);
        if (ReferenceEquals(_hooked, dispatcher))
        {
            return;
        }

        _hooked = dispatcher;
        dispatcher.UnhandledException += (_, e) =>
        {
            if (CommandGuard.IsRecoverable(e.Exception) && IsFromDyncamelo(e.Exception))
            {
                Report(editor, "the editor", e.Exception);
                e.Handled = true;
            }
        };
    }

    /// <summary>True when one of the top frames of the exception's stack belongs to a Dyncamelo assembly.</summary>
    public static bool IsFromDyncamelo(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            var frames = new StackTrace(current, false).GetFrames();
            if (frames == null)
            {
                continue;
            }

            for (var i = 0; i < frames.Length && i < 12; i++)
            {
                var name = frames[i].GetMethod()?.DeclaringType?.Assembly.GetName().Name;
                if (name != null && name.StartsWith("Dyncamelo", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Logs the failure and reports it in the editor's status bar.</summary>
    public static void Report(GraphEditorViewModel editor, string where, Exception exception)
    {
        try
        {
            var directory = UiSettingsService.DefaultDirectory;
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "errors.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + where + Environment.NewLine + exception + Environment.NewLine + Environment.NewLine);
        }
        catch (Exception)
        {
            // Logging must never throw.
        }

        editor.ReportProblem("Something went wrong (" + exception.GetType().Name + ": " + exception.Message + "). Details: %APPDATA%\\Dyncamelo\\errors.log");
    }
}
