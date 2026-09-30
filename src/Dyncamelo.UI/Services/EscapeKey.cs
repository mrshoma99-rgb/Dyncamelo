using System;
using System.Runtime.InteropServices;

namespace Dyncamelo.UI.Services;

/// <summary>
/// Reads the Esc key straight from the keyboard state. A graph run blocks the UI thread (Navisworks API calls must run on the
/// host's main thread), so no key message can be delivered while it works; the run polls this between its steps instead.
/// Only counts while a window of this process is in the foreground, so Esc pressed in another application never cancels.
/// </summary>
public static class EscapeKey
{
    private const int VkEscape = 0x1B;

    /// <summary>Forgets an Esc press that happened before now (call when a run starts).</summary>
    public static void Reset()
    {
        try
        {
            GetAsyncKeyState(VkEscape);
        }
        catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
        {
            // Not on Windows: there is nothing to poll.
        }
    }

    /// <summary>True when Esc is down, or was pressed since the last call, while this application is in the foreground.</summary>
    public static bool WasPressed()
    {
        try
        {
            // Bit 15: down now. Bit 0: pressed since the previous call — a tap during one long node is not lost.
            var state = GetAsyncKeyState(VkEscape);
            if ((state & 0x8001) == 0)
            {
                return false;
            }

            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return false;
            }

            GetWindowThreadProcessId(foreground, out var processId);
            return processId == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        }
        catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
