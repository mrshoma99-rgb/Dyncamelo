using System;
using System.Runtime.InteropServices;

namespace CamelGraph.UI.Services;

/// <summary>Moves the mouse pointer across the screen area (the whole desktop, all monitors), for drags that run past the edge.</summary>
public static class PointerWrap
{
    private const int SmXVirtualScreen = 76;
    private const int SmCxVirtualScreen = 78;

    /// <summary>Left edge and width of the desktop in pixels.</summary>
    public static void ScreenSpan(out int left, out int width)
    {
        left = GetSystemMetrics(SmXVirtualScreen);
        width = GetSystemMetrics(SmCxVirtualScreen);
    }

    /// <summary>Moves the pointer to a screen position in pixels. Failures are ignored: the drag simply stops at the edge.</summary>
    public static void MoveTo(int x, int y)
    {
        try
        {
            SetCursorPos(x, y);
        }
        catch (Exception)
        {
            // No desktop to move the pointer on (a remote or locked session): nothing to do.
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);
}
