using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinBoard.Core;
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
/// 5. WS_EX_TOPMOST + SetWindowPos(HWND_TOPMOST) after every style/show/move
///    (OverlappedPresenter.IsAlwaysOnTop alone is lost on unpackaged WinUI
///    after SetWindowLongPtr, drag, tray show, and opacity).
/// 6. Rewrite WM_WINDOWPOSCHANGING / WM_STYLECHANGING on the <b>root</b> HWND
///    so WinUI cannot clear TOPMOST (AppWindow.MoveAndResize, a second window
///    in-process, DWM). Child XAML HWNDs only get MA_NOACTIVATE — they must
///    not become topmost independently.
/// </summary>
internal static class NoActivateWindow
{
    private const nuint SubclassId = 1;
    private static readonly nint RootData = new(1);
    // Keep delegates alive; a collected callback would crash native code.
    private static readonly NativeMethods.SubclassProc SubclassCallback = OnSubclassProc;
    private static readonly NativeMethods.EnumWindowsProc EnumChildSink = OnEnumChild;
    private static readonly HashSet<nint> Subclassed = [];

    public static nint GetHwnd(Window window) => WindowNative.GetWindowHandle(window);

    public static void Apply(nint hwnd, byte? layeredAlpha = null)
    {
        nint exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle);
        exStyle |= NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow | NativeMethods.WsExTopmost;
        if (layeredAlpha is byte alpha && alpha < 250)
        {
            exStyle |= NativeMethods.WsExLayered;
        }
        else
        {
            exStyle &= ~NativeMethods.WsExLayered;
        }

        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, exStyle);

        // Style changes drop z-order unless HWND_TOPMOST is passed here.
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HwndTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged);

        if (layeredAlpha is byte a && a < 250)
        {
            NativeMethods.SetLayeredWindowAttributes(hwnd, 0, a, NativeMethods.LwaAlpha);
        }

        NativeMethods.AssertTopmost(hwnd);
        SubclassIfNeeded(hwnd, root: true);
        NativeMethods.EnumChildWindows(hwnd, EnumChildSink, nint.Zero);
    }

    private static void SubclassIfNeeded(nint hwnd, bool root)
    {
        if (hwnd == nint.Zero)
        {
            return;
        }

        lock (Subclassed)
        {
            if (Subclassed.Contains(hwnd))
            {
                return;
            }

            nint data = root ? RootData : nint.Zero;
            if (NativeMethods.SetWindowSubclass(hwnd, SubclassCallback, SubclassId, data))
            {
                Subclassed.Add(hwnd);
            }
        }
    }

    private static bool OnEnumChild(nint hwnd, nint lParam)
    {
        SubclassIfNeeded(hwnd, root: false);
        return true;
    }

    private static nint OnSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam, nuint uIdSubclass, nint dwRefData)
    {
        if (uMsg is NativeMethods.WmMouseActivate or NativeMethods.WmPointerActivate)
        {
            return NativeMethods.MaNoActivate;
        }

        if (dwRefData == RootData)
        {
            HandleRootZOrder(hWnd, uMsg, wParam, lParam);
        }

        if (uMsg == NativeMethods.WmNcDestroy)
        {
            NativeMethods.RemoveWindowSubclass(hWnd, SubclassCallback, SubclassId);
            lock (Subclassed)
            {
                Subclassed.Remove(hWnd);
            }
        }

        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private static void HandleRootZOrder(nint hWnd, uint uMsg, nint wParam, nint lParam)
    {
        if (uMsg == NativeMethods.WmStyleChanging && TopmostPolicy.IsExStyleIndex(wParam) && lParam != nint.Zero)
        {
            var changing = Marshal.PtrToStructure<STYLESTRUCT>(lParam);
            uint preserved = TopmostPolicy.PreserveExStyle(changing.styleNew);
            if (preserved != changing.styleNew)
            {
                changing.styleNew = preserved;
                Marshal.StructureToPtr(changing, lParam, fDeleteOld: false);
            }

            return;
        }

        if (uMsg == NativeMethods.WmWindowPosChanging && lParam != nint.Zero)
        {
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            bool already = NativeMethods.IsTopmost(hWnd);
            if (TopmostPolicy.TryRewriteWindowPos(
                    pos.flags,
                    pos.hwndInsertAfter,
                    already,
                    isTopLevel: true,
                    out nint after,
                    out uint flags))
            {
                pos.hwndInsertAfter = after;
                pos.flags = flags;
                Marshal.StructureToPtr(pos, lParam, fDeleteOld: false);
            }
        }
    }
}
