using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Dyncamelo.UI.Tests;

/// <summary>
/// One shared STA thread with a running dispatcher and a single WPF
/// <see cref="Application"/> (needed for pack:// resource URIs). Every UI test
/// marshals its body onto it, so tests can create real windows and controls.
/// </summary>
internal static class StaHost
{
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
                    Console.Error.WriteLine("UNHANDLED on the WPF test thread: " + e.Exception);
                    Console.Error.Flush();
                    e.Handled = true;
                };

                ready.Set();
                System.Windows.Threading.Dispatcher.Run();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Name = "Dyncamelo.UI.Tests STA";
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
        // A test that blocks the dispatcher must fail with a message instead of hanging the whole run.
        Dispatcher.Invoke(action, DispatcherPriority.Normal, System.Threading.CancellationToken.None, TimeSpan.FromSeconds(120));
    }

    /// <summary>Lets pending layout, binding and render work finish.</summary>
    public static void Flush()
    {
        Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
