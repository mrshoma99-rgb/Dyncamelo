using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Input;
using CamelGraph.UI.Views;

namespace CamelGraph.App;

/// <summary>
/// Keeps Navisworks from taking CamelGraph's keys. Navisworks translates its own accelerators (Ctrl+Z, Ctrl+Y,
/// Delete, F1 …) before a key reaches the WPF editor hosted in the dock pane, so Ctrl+Z inside the pane undid the
/// Navisworks document instead of the graph. This hook looks at every key the UI thread is about to receive; when
/// the keyboard focus is inside the pane and the editor wants the key, it runs the key through the editor and turns
/// the message into a no-op so the host never sees it. Keys the editor does not want pass through untouched.
/// </summary>
internal sealed class PaneKeyGuard : IDisposable
{
    private const int WhGetMessage = 3;
    private const int PmRemove = 1;
    private const uint WmKeyDown = 0x0100;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmNull = 0x0000;
    private const int VkControl = 0x11;
    private const int VkDelete = 0x2E;
    private const int VkF1 = 0x70;
    private const int VkF12 = 0x7B;

    private readonly HookProc _proc;
    private readonly Control _host;
    private readonly IHostKeyTarget _editor;
    private IntPtr _hook;
    private bool _busy;

    /// <summary>Installs the hook on the calling (UI) thread.</summary>
    /// <param name="host">The dock pane's control; focus inside it means the key is for CamelGraph.</param>
    /// <param name="editor">The editor that gets first pick of the keys.</param>
    public PaneKeyGuard(Control host, IHostKeyTarget editor)
    {
        _host = host;
        _editor = editor;
        _proc = OnHook;
        _hook = SetWindowsHookEx(WhGetMessage, _proc, IntPtr.Zero, GetCurrentThreadId());
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    /// <summary>Removes the hook.</summary>
    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr OnHook(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0 && wParam.ToInt64() == PmRemove && !_busy)
            {
                var msg = Marshal.PtrToStructure<Msg>(lParam);
                if ((msg.Message == WmKeyDown || msg.Message == WmSysKeyDown) && IsCandidate((int)msg.WParam) && FocusIsInPane())
                {
                    var key = KeyInterop.KeyFromVirtualKey((int)msg.WParam);
                    if (key != Key.None && _editor.WantsHostKey(key))
                    {
                        _busy = true;
                        try
                        {
                            if (_editor.ProcessHostKey(key))
                            {
                                msg.Message = WmNull;
                                Marshal.StructureToPtr(msg, lParam, false);
                            }
                        }
                        finally
                        {
                            _busy = false;
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // A hook must never throw into the host's message loop; the key simply falls through.
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    // Only chords the host is known to grab: anything with Ctrl, the function keys and Delete.
    private static bool IsCandidate(int virtualKey) =>
        (GetKeyState(VkControl) & 0x8000) != 0 || (virtualKey >= VkF1 && virtualKey <= VkF12) || virtualKey == VkDelete;

    private bool FocusIsInPane()
    {
        if (!_host.IsHandleCreated)
        {
            return false;
        }

        var focus = GetFocus();
        return focus != IntPtr.Zero && (focus == _host.Handle || IsChild(_host.Handle, focus));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PtX;
        public int PtY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
