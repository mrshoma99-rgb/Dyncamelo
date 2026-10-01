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
    private static Action<string>? _notify;

    /// <summary>Routes command failures and unhandled UI-thread exceptions from Dyncamelo code to <paramref name="editor"/>.</summary>
    /// <param name="editor">The editor whose status bar reports the failure.</param>
    /// <param name="dispatcher">The UI dispatcher (the one that runs the editor).</param>
    public static void Install(GraphEditorViewModel editor, Dispatcher dispatcher)
    {
        CommandGuard.Handler = ex => Report(editor, "a command", ex);
        Hook(dispatcher, editor.ReportProblem);
    }

    /// <summary>
    /// The same protection for a pane that has no editor behind it (the Script Player opened on its own). An editor installed
    /// later takes over the reporting.
    /// </summary>
    /// <param name="notify">Shows a message to the user.</param>
    /// <param name="dispatcher">The UI dispatcher.</param>
    public static void Install(Action<string> notify, Dispatcher dispatcher)
    {
        if (notify == null)
        {
            throw new ArgumentNullException(nameof(notify));
        }

        Hook(dispatcher, notify);
    }

    private static void Hook(Dispatcher dispatcher, Action<string> notify)
    {
        _notify = notify;
        if (ReferenceEquals(_hooked, dispatcher))
        {
            return;
        }

        _hooked = dispatcher;
        dispatcher.UnhandledException += (_, e) =>
        {
            if (CommandGuard.IsRecoverable(e.Exception) && IsFromDyncamelo(e.Exception))
            {
                Log("the editor", e.Exception);
                _notify?.Invoke(Describe(e.Exception));
                e.Handled = true;
            }
        };
    }

    /// <summary>True when a XAML file's location (as WPF reports it in a parse error) is one of Dyncamelo's own resources.</summary>
    /// <param name="baseUri">The file's pack URI, or null.</param>
    public static bool IsDyncameloFile(Uri? baseUri) =>
        baseUri != null && baseUri.OriginalString.IndexOf("Dyncamelo", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>True when one of the top frames of the exception's stack belongs to a Dyncamelo assembly.</summary>
    public static bool IsFromDyncamelo(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            // A template or resource of ours that fails to load is reported by WPF with only WPF frames; the file it was
            // reading says whose it is.
            if (current is System.Windows.Markup.XamlParseException xaml && IsDyncameloFile(xaml.BaseUri))
            {
                return true;
            }

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

    /// <summary>
    /// For the host's unhandled-exception event: when a failure that ends the process is Dyncamelo's, leave its full text (inner
    /// exceptions included) in <c>errors.log</c>, since a crash dialog only names the outer exception.
    /// </summary>
    /// <param name="exceptionObject">The event's exception object.</param>
    public static void LogFatal(object? exceptionObject)
    {
        if (exceptionObject is Exception exception && IsFromDyncamelo(exception))
        {
            Log("a failure that ended the host", exception);
        }
    }

    /// <summary>Logs the failure and reports it in the editor's status bar.</summary>
    public static void Report(GraphEditorViewModel editor, string where, Exception exception)
    {
        Log(where, exception);
        editor.ReportProblem(Describe(exception));
    }

    private static string Describe(Exception exception) =>
        "Something went wrong (" + exception.GetType().Name + ": " + exception.Message + "). Details: %APPDATA%\\Dyncamelo\\errors.log";

    private static void Log(string where, Exception exception)
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
    }
}
