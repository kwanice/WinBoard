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
/// 6. WM_NCHITTEST returns HTCAPTION on the slim top bandeau so Windows runs
///    its native caption-drag loop (mouse + touch). Keys stay HTCLIENT.
/// </summary>
internal static class NoActivateWindow
{
    private const nuint SubclassId = 1;
    // Keep delegates alive; a collected callback would crash native code.
    private static readonly NativeMethods.SubclassProc SubclassCallback = OnSubclassProc;
    private static readonly NativeMethods.EnumWindowsProc EnumChildSink = OnEnumChild;
    private static readonly HashSet<nint> Subclassed = [];
    private static readonly object CaptionGate = new();

    private static nint _captionHwnd;
    private static int _captionX;
    private static int _captionY;
    private static int _captionW;
    private static int _captionH;

    public static nint GetHwnd(Window window) => WindowNative.GetWindowHandle(window);

    public static void Apply(nint hwnd, byte? layeredAlpha = null)
    {
        nint exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle);
        exStyle |= NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow;
        if (layeredAlpha is byte alpha && alpha < 250)
        {
            exStyle |= NativeMethods.WsExLayered;
        }
        else
        {
            exStyle &= ~NativeMethods.WsExLayered;
        }

        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, exStyle);

        NativeMethods.SetWindowPos(
            hwnd,
            nint.Zero,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpFrameChanged);

        if (layeredAlpha is byte a && a < 250)
        {
            NativeMethods.SetLayeredWindowAttributes(hwnd, 0, a, NativeMethods.LwaAlpha);
        }

        SubclassIfNeeded(hwnd);
        NativeMethods.EnumChildWindows(hwnd, EnumChildSink, nint.Zero);
    }

    /// <summary>
    /// Client-pixel rectangle (top-level HWND) that should behave as a caption.
    /// Gear / close sit outside this rect so they stay real buttons.
    /// </summary>
    public static void SetCaptionRect(nint topLevelHwnd, int x, int y, int width, int height)
    {
        lock (CaptionGate)
        {
            _captionHwnd = topLevelHwnd;
            _captionX = x;
            _captionY = y;
            _captionW = Math.Max(0, width);
            _captionH = Math.Max(0, height);
        }

        SubclassIfNeeded(topLevelHwnd);
        NativeMethods.EnumChildWindows(topLevelHwnd, EnumChildSink, nint.Zero);
    }

    /// <summary>
    /// Classic borderless-window move: Windows caption loop via
    /// <c>ReleaseCapture</c> + <c>WM_NCLBUTTONDOWN / HTCAPTION</c>.
    /// </summary>
    public static void BeginCaptionDrag(nint hwnd)
    {
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(hwnd, NativeMethods.WmNcLButtonDown, NativeMethods.HtCaption, 0);
    }

    private static void SubclassIfNeeded(nint hwnd)
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

            if (NativeMethods.SetWindowSubclass(hwnd, SubclassCallback, SubclassId, nint.Zero))
            {
                Subclassed.Add(hwnd);
            }
        }
    }

    private static bool OnEnumChild(nint hwnd, nint lParam)
    {
        SubclassIfNeeded(hwnd);
        return true;
    }

    private static nint OnSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam, nuint uIdSubclass, nint dwRefData)
    {
        if (uMsg == NativeMethods.WmNcHitTest && IsCaptionHit(lParam))
        {
            return NativeMethods.HtCaption;
        }

        if (uMsg is NativeMethods.WmMouseActivate or NativeMethods.WmPointerActivate)
        {
            return NativeMethods.MaNoActivate;
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

    private static bool IsCaptionHit(nint lParam)
    {
        nint hwnd;
        int x;
        int y;
        int w;
        int h;
        lock (CaptionGate)
        {
            hwnd = _captionHwnd;
            x = _captionX;
            y = _captionY;
            w = _captionW;
            h = _captionH;
        }

        if (hwnd == nint.Zero || w <= 0 || h <= 0)
        {
            return false;
        }

        var pt = new POINT
        {
            X = NativeMethods.GetXlParam(lParam),
            Y = NativeMethods.GetYlParam(lParam),
        };
        if (!NativeMethods.ScreenToClient(hwnd, ref pt))
        {
            return false;
        }

        return pt.X >= x && pt.X < x + w && pt.Y >= y && pt.Y < y + h;
    }
}
