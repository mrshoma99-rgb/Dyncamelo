using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace CamelGraph.UI.Tests;

/// <summary>
/// One shared STA thread with a running dispatcher and a single WPF
/// <see cref="Application"/> (needed for pack:// resource URIs). Every UI test
/// marshals its body onto it, so tests can create real windows and controls.
/// </summary>
internal static class StaHost
{
    /// <summary>Exceptions that escaped to the WPF test thread's dispatcher (they are logged and swallowed so the run goes on).</summary>
    public static readonly System.Collections.Concurrent.ConcurrentQueue<string> Unhandled = new System.Collections.Concurrent.ConcurrentQueue<string>();

    private static readonly Dispatcher Dispatcher = Start();

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using (var ready = new ManualResetEventSlim())
        {
            var thread = new Thread(() =>
            {
                if (Application.Current == null)
                {
                    new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                }

                dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

                // An exception escaping a timer or posted callback on this thread would otherwise take the whole test
                // host down (or park it behind an invisible crash dialog on a CI desktop). Log it where the run
                // output shows it, and carry on.
                dispatcher.UnhandledException += (_, e) =>
                {
                    Unhandled.Enqueue(e.Exception.ToString());
                    Console.Error.WriteLine("UNHANDLED on the WPF test thread: " + e.Exception);
                    Console.Error.Flush();
                    e.Handled = true;
                };

                ready.Set();
                System.Windows.Threading.Dispatcher.Run();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Name = "CamelGraph.UI.Tests STA";
            thread.Start();
            ready.Wait();
        }

        // Let the dispatcher loop end with the process instead of being torn down mid-message.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                dispatcher!.BeginInvokeShutdown(DispatcherPriority.Send);
            }
            catch (Exception)
            {
                // Already shut down.
            }
        };

        return dispatcher!;
    }

    /// <summary>Runs <paramref name="action"/> on the STA thread; exceptions propagate to the caller.</summary>
    public static void Run(Action action)
    {
        Invoke(action, DispatcherPriority.Normal, TimeSpan.FromSeconds(120), "A UI test call");
    }

    /// <summary>Lets pending layout, binding and render work finish.</summary>
    public static void Flush()
    {
        Invoke(() => { }, DispatcherPriority.ApplicationIdle, TimeSpan.FromSeconds(60), "Waiting for the dispatcher to go idle");
        Invoke(() => { }, DispatcherPriority.ApplicationIdle, TimeSpan.FromSeconds(60), "Waiting for the dispatcher to go idle");
    }

    // A blocked dispatcher, or one that never goes idle, must fail the test that caused it with a message —
    // Dispatcher.Invoke with a timeout just returns, and the run then sits until the hang detector kills it.
    private static void Invoke(Action action, DispatcherPriority priority, TimeSpan timeout, string what)
    {
        ExceptionDispatchInfo? error = null;
        var operation = Dispatcher.BeginInvoke(priority, new Action(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ExceptionDispatchInfo.Capture(ex);
            }
        }));

        var status = operation.Wait(timeout);
        if (status != DispatcherOperationStatus.Completed)
        {
            operation.Abort();
            throw new TimeoutException(what + " did not finish within " + timeout.TotalSeconds + " s (" + status + "): the WPF dispatcher is blocked or never goes idle.");
        }

        error?.Throw();
    }
}
