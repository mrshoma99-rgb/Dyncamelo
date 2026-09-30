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

                dispatcher = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Name = "Dyncamelo.UI.Tests STA";
            thread.Start();
            ready.Wait();
        }

        return dispatcher!;
    }

    /// <summary>Runs <paramref name="action"/> on the STA thread; exceptions propagate to the caller.</summary>
    public static void Run(Action action)
    {
        Dispatcher.Invoke(action);
    }

    /// <summary>Lets pending layout, binding and render work finish.</summary>
    public static void Flush()
    {
        Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
