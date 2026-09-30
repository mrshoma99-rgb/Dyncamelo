using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Dyncamelo.UI.Views;

/// <summary>
/// A see-through window over the whole desktop that turns the next click into a colour: the colour of the pixel under the
/// pointer, wherever it is — the Navisworks viewport, another window, the desktop. A left click picks, Esc or a right click
/// cancels. The window is hidden before the pixel is read so it never tints what it samples.
/// </summary>
public sealed class EyedropperOverlay : Window
{
    private readonly Action<Color> _picked;
    private readonly Action _cancelled;
    private bool _done;

    private EyedropperOverlay(Action<Color> picked, Action cancelled)
    {
        _picked = picked;
        _cancelled = cancelled;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;

        // Almost fully transparent, but not quite: a window with no opacity at all lets the click fall through to whatever is below.
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        Cursor = Cursors.Cross;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            Finish(sample: true);
        };
        MouseRightButtonDown += (_, e) =>
        {
            e.Handled = true;
            Finish(sample: false);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Finish(sample: false);
            }
        };
        Deactivated += (_, _) => Finish(sample: false);
        Loaded += (_, _) =>
        {
            Activate();
            Focus();
        };
    }

    /// <summary>Shows the overlay; exactly one of the callbacks runs, once.</summary>
    /// <param name="picked">Called with the colour of the pixel that was clicked.</param>
    /// <param name="cancelled">Called when the user pressed Esc, right-clicked, or switched away.</param>
    public static void Pick(Action<Color> picked, Action cancelled)
    {
        new EyedropperOverlay(picked, cancelled).Show();
    }

    private void Finish(bool sample)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        Color? colour = null;
        if (sample && GetCursorPos(out var point))
        {
            // Out of the way first, then give the desktop a moment to repaint without us.
            Hide();
            System.Threading.Thread.Sleep(60);
            colour = ReadPixel(point.X, point.Y);
        }

        Close();
        if (colour.HasValue)
        {
            _picked(colour.Value);
        }
        else
        {
            _cancelled();
        }
    }

    private static Color? ReadPixel(int x, int y)
    {
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var value = GetPixel(screen, x, y);
            if (value == 0xFFFFFFFFu)
            {
                return null;
            }

            return Color.FromRgb((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr dc, int x, int y);
}
