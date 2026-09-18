namespace WinBoard.Core;

/// <summary>
/// Pure policy for keeping the no-activate keyboard in the Win32 TOPMOST
/// band. WinUI/AppWindow often posts HWND_NOTOPMOST or SWP_NOZORDER after
/// style, show, drag, or a second window in the process — that drops the
/// overlay behind ordinary apps. The WndProc uses this to rewrite the
/// incoming WINDOWPOS / EXSTYLE without activating.
/// </summary>
public static class TopmostPolicy
{
    public static readonly nint HwndTopmost = new(-1);

    public static readonly nint HwndNoTopmost = new(-2);

    public static readonly nint HwndTop = nint.Zero;

    public static readonly nint HwndBottom = new(1);

    /// <summary>GWL_EXSTYLE</summary>
    public const int GwlExStyle = -20;

    public const uint SwpNoZOrder = 0x0004;

    public const uint SwpHideWindow = 0x0080;

    public const uint SwpNoActivate = 0x0010;

    public const uint WsExTopmost = 0x00000008;

    public const uint WsExNoActivate = 0x08000000;

    public const uint WsExToolWindow = 0x00000080;

    public static bool IsExStyleIndex(nint wParam) => wParam == GwlExStyle;

    /// <summary>Keep TOPMOST + NOACTIVATE + TOOLWINDOW; do not touch LAYERED.</summary>
    public static uint PreserveExStyle(uint styleNew) =>
        styleNew | WsExTopmost | WsExNoActivate | WsExToolWindow;

    public static bool HasTopmost(nint exStyle) =>
        (exStyle.ToInt64() & WsExTopmost) != 0;

    /// <summary>
    /// Returns true when <paramref name="insertAfter"/> / <paramref name="flags"/>
    /// should be rewritten so a visible top-level overlay stays TOPMOST.
    /// Does not bump an already-topmost window over a sibling (Settings / Diag).
    /// </summary>
    public static bool TryRewriteWindowPos(
        uint flags,
        nint insertAfter,
        bool alreadyTopmost,
        bool isTopLevel,
        out nint newInsertAfter,
        out uint newFlags)
    {
        newInsertAfter = insertAfter;
        newFlags = flags;
        if (!isTopLevel || (flags & SwpHideWindow) != 0)
        {
            return false;
        }

        bool noZ = (flags & SwpNoZOrder) != 0;
        if (alreadyTopmost && noZ)
        {
            return false;
        }

        if (alreadyTopmost && insertAfter == HwndTopmost)
        {
            return false;
        }

        bool demote = insertAfter == HwndNoTopmost
            || insertAfter == HwndBottom
            || insertAfter == HwndTop;
        if (alreadyTopmost && !demote && !noZ)
        {
            return false;
        }

        if (!alreadyTopmost || demote)
        {
            newInsertAfter = HwndTopmost;
            newFlags = (flags & ~SwpNoZOrder) | SwpNoActivate;
            return newInsertAfter != insertAfter || newFlags != flags;
        }

        return false;
    }
}
