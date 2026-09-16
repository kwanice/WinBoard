using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace WinBoard.Input;

/// <summary>
/// Keeps a WinUI 3 window interactive without stealing foreground focus.
///
/// Approach (WinUI + Win32):
/// 1. WS_EX_NOACTIVATE — the HWND can receive pointer/touch input without
///    becoming the foreground window, so SendInput still targets the app
///    the user was typing in.
/// 2. WS_EX_TOOLWINDOW — treat the overlay as a tool window (no taskbar button).
/// 3. Subclass WndProc and return MA_NOACTIVATE for WM_MOUSEACTIVATE and
///    WM_POINTERACTIVATE. Windows still tries to activate on click otherwise.
/// 4. Show with AppWindow.Show(activateWindow: false) rather than Window.Activate().
/// 5. OverlappedPresenter.IsAlwaysOnTop keeps the keyboard above other windows.
/// </summary>
internal static class NoActivateWindow
{
    private const nuint SubclassId = 1;
    // Keep the delegate alive; a collected callback would crash native code.
    private static readonly NativeMethods.SubclassProc SubclassCallback = OnSubclassProc;
    private static nint _subclassedHwnd;

    public static nint GetHwnd(Window window) => WindowNative.GetWindowHandle(window);

    public static void Apply(nint hwnd)
    {
        nint exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle);
        exStyle |= NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, exStyle);

        NativeMethods.SetWindowPos(
            hwnd,
            nint.Zero,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpFrameChanged);

        if (_subclassedHwnd != hwnd)
        {
            if (_subclassedHwnd != nint.Zero)
            {
                NativeMethods.RemoveWindowSubclass(_subclassedHwnd, SubclassCallback, SubclassId);
            }

            NativeMethods.SetWindowSubclass(hwnd, SubclassCallback, SubclassId, nint.Zero);
            _subclassedHwnd = hwnd;
        }
    }

    private static nint OnSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam, nuint uIdSubclass, nint dwRefData)
    {
        if (uMsg is NativeMethods.WmMouseActivate or NativeMethods.WmPointerActivate)
        {
            return NativeMethods.MaNoActivate;
        }

        if (uMsg == NativeMethods.WmNcDestroy)
        {
            NativeMethods.RemoveWindowSubclass(hWnd, SubclassCallback, SubclassId);
            if (_subclassedHwnd == hWnd)
            {
                _subclassedHwnd = nint.Zero;
            }
        }

        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
}
