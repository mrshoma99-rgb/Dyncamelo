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
    private static Func<int>? _recover;
    private static long _activityTicks;

    /// <summary>
    /// How long after something the editor did (a node added, a command run) a WPF failure that names none of our code is still put
    /// down to us. A template that cannot be built is reported by WPF with only WPF frames and, for a control of someone else's
    /// (Nodify), a file that is not ours.
    /// </summary>
    private static readonly TimeSpan ActivityWindow = TimeSpan.FromSeconds(10);

    /// <summary>Routes command failures and unhandled UI-thread exceptions from Dyncamelo code to <paramref name="editor"/>.</summary>
    /// <param name="editor">The editor whose status bar reports the failure.</param>
    /// <param name="dispatcher">The UI dispatcher (the one that runs the editor).</param>
    public static void Install(GraphEditorViewModel editor, Dispatcher dispatcher)
    {
        CommandGuard.Handler = ex => Report(editor, "a command", ex);
        _recover = editor.DiscardRecentlyAddedNodes;
        Hook(dispatcher, editor.ReportProblem);
    }

    /// <summary>Records that the editor just did something that builds visuals (adds a node, opens a graph), for attributing a WPF failure that follows.</summary>
    public static void NoteActivity() => System.Threading.Interlocked.Exchange(ref _activityTicks, DateTime.UtcNow.Ticks);

    private static bool HadRecentActivity(TimeSpan window)
    {
        var ticks = System.Threading.Interlocked.Read(ref _activityTicks);
        return ticks != 0 && DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) <= window;
    }

    /// <summary>
    /// True when a failure on the UI thread should be treated as the editor's: its stack or the file it was reading is ours, or it
    /// is a WPF/XAML load failure (a template, resource or type that could not be built) soon after the editor did something.
    /// </summary>
    /// <param name="exception">The unhandled exception.</param>
    public static bool IsOurFailure(Exception exception) =>
        IsFromDyncamelo(exception) || (IsWpfLoadFailure(exception) && HadRecentActivity(ActivityWindow));

    /// <summary>True when the exception, or one inside it, is WPF failing to build something from XAML, a resource or an assembly.</summary>
    /// <param name="exception">The exception to look through.</param>
    public static bool IsWpfLoadFailure(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            var ns = current.GetType().Namespace ?? string.Empty;
            if (ns.StartsWith("System.Windows.Markup", StringComparison.Ordinal) ||
                ns.StartsWith("System.Xaml", StringComparison.Ordinal) ||
                current is System.Windows.ResourceReferenceKeyNotFoundException ||
                current is TypeInitializationException ||
                current is TypeLoadException ||
                current is FileNotFoundException ||
                current is FileLoadException ||
                current is BadImageFormatException)
            {
                return true;
            }
        }

        return false;
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
            if (CommandGuard.IsRecoverable(e.Exception) && IsOurFailure(e.Exception))
            {
                Log("the editor", e.Exception);

                // A visual that failed to build stays half-made and fails again at every layout pass: take away what was just added.
                var removed = IsWpfLoadFailure(e.Exception) ? TryRecover() : 0;
                _notify?.Invoke(Describe(e.Exception) + (removed > 0 ? " The node that could not be drawn was taken off the canvas." : string.Empty));
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
        if (exceptionObject is Exception exception && (IsFromDyncamelo(exception) || HadRecentActivity(TimeSpan.FromMinutes(2))))
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

    private static int TryRecover()
    {
        try
        {
            return _recover?.Invoke() ?? 0;
        }
        catch (Exception ex)
        {
            Log("recovering after a failure", ex);
            return 0;
        }
    }

    private static string Describe(Exception exception) =>
        "Something went wrong (" + exception.GetType().Name + ": " + exception.Message + "). Details: %APPDATA%\\Dyncamelo\\errors.log";

    // What Exception.ToString leaves out: where in which XAML file a parse failed, and the exception types down the chain.
    private static string Details(Exception exception)
    {
        var lines = new System.Text.StringBuilder();
        var chain = new System.Collections.Generic.List<string>();
        for (var current = exception; current != null; current = current.InnerException)
        {
            chain.Add(current.GetType().FullName ?? current.GetType().Name);
            if (current is System.Windows.Markup.XamlParseException xaml)
            {
                lines.Append("XAML: line ").Append(xaml.LineNumber).Append(", position ").Append(xaml.LinePosition)
                    .Append(", file ").AppendLine(xaml.BaseUri?.OriginalString ?? "(unknown)");
            }
        }

        lines.Append("Chain: ").AppendLine(string.Join(" > ", chain));
        return lines.ToString();
    }

    private static void Log(string where, Exception exception)
    {
        try
        {
            var directory = UiSettingsService.DefaultDirectory;
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "errors.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + where + Environment.NewLine + exception + Environment.NewLine +
                Details(exception) + Environment.NewLine);
        }
        catch (Exception)
        {
            // Logging must never throw.
        }
    }
}
