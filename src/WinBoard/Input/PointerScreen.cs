namespace WinBoard.Input;

/// <summary>
/// True screen coordinates for touch/pen/mouse on a no-activate WinUI window.
///
/// Why this exists:
/// - WinUI <c>Pointer.PointerId</c> often does not match Win32 <c>GetPointerInfo</c>.
/// - Local DIP deltas go to ~0 once the HWND follows the finger, so
///   <c>PointerMoved</c> may stop firing.
/// - <c>GetCursorPos</c> stays at the last *mouse* position during touch.
/// - Pointer messages land on the XAML island child HWND, not the top-level
///   window that <see cref="NoActivateWindow"/> subclasses first.
/// - Moving the HWND under a captured touch often raises <c>PointerCaptureLost</c>.
///
/// Strategy: cache WM_POINTER* from every subclassed HWND, poll
/// <c>GetPointerInfo</c> for INCONTACT pointers, and drive the drag from a
/// dispatcher timer so tracking does not depend on WinUI moved events.
/// </summary>
internal static class PointerScreen
{
    internal const uint PointerFlagInRange = 0x00000002;
    internal const uint PointerFlagInContact = 0x00000004;
    internal const uint PointerFlagPrimary = 0x00000100;
    internal const uint PointerFlagDown = 0x00010000;
    internal const uint PointerFlagUpdate = 0x00020000;
    internal const uint PointerFlagUp = 0x00040000;

    private const uint MaxScanId = 64;

    private static readonly object Gate = new();
    private static readonly Dictionary<uint, POINT> ById = [];
    private static POINT _last;
    private static bool _hasLast;
    private static bool _mouseInPointer;

    public static void EnsureMouseInPointer()
    {
        if (_mouseInPointer)
        {
            return;
        }

        NativeMethods.EnableMouseInPointer(true);
        _mouseInPointer = true;
    }

    /// <summary>
    /// Subclass the XAML island (and nested children) so WM_POINTER* is cached.
    /// Safe to call repeatedly; children often appear only after first Show.
    /// </summary>
    public static void AttachIslands(nint topLevelHwnd)
    {
        if (topLevelHwnd == nint.Zero)
        {
            return;
        }

        NoActivateWindow.SubclassPointerSink(topLevelHwnd);
        NativeMethods.EnumChildWindows(topLevelHwnd, NoActivateWindow.EnumChildSink, nint.Zero);
    }

    public static void ObserveWin32(uint msg, nint wParam)
    {
        if (msg is not (
            NativeMethods.WmPointerDown or NativeMethods.WmPointerUpdate
            or NativeMethods.WmNcPointerDown or NativeMethods.WmNcPointerUpdate
            or NativeMethods.WmPointerUp or NativeMethods.WmNcPointerUp))
        {
            return;
        }

        uint id = NativeMethods.PointerIdFromWParam(wParam);
        if (msg is NativeMethods.WmPointerUp or NativeMethods.WmNcPointerUp)
        {
            Forget(id);
            return;
        }

        if (NativeMethods.GetPointerInfo(id, out POINTER_INFO info))
        {
            Remember(id, info.ptPixelLocation);
        }
    }

    /// <summary>
    /// Snapshot at drag start. <paramref name="win32Id"/> is 0 for mouse-only
    /// (track with <c>GetCursorPos</c>); otherwise a real Win32 pointer id.
    /// </summary>
    public static bool TryBegin(uint winuiPointerId, bool mouse, out uint win32Id, out POINT point)
    {
        EnsureMouseInPointer();

        if (winuiPointerId != 0 && TryRead(winuiPointerId, out point) && IsContactOrDown(winuiPointerId))
        {
            win32Id = winuiPointerId;
            return true;
        }

        if (TryReadAnyContact(out win32Id, out point))
        {
            return true;
        }

        if (winuiPointerId != 0 && TryRead(winuiPointerId, out point))
        {
            win32Id = winuiPointerId;
            return true;
        }

        lock (Gate)
        {
            if (_hasLast)
            {
                win32Id = winuiPointerId;
                point = _last;
                return true;
            }
        }

        if (mouse && NativeMethods.GetCursorPos(out point))
        {
            win32Id = 0;
            return true;
        }

        win32Id = 0;
        point = default;
        return false;
    }

    /// <summary>Live screen position while dragging. Does not use <c>GetCursorPos</c> for touch.</summary>
    public static bool TryTrack(uint win32Id, bool mouse, out POINT point)
    {
        if (win32Id != 0 && TryRead(win32Id, out point))
        {
            return true;
        }

        if (TryReadAnyContact(out _, out point))
        {
            return true;
        }

        lock (Gate)
        {
            if (_hasLast)
            {
                point = _last;
                return true;
            }
        }

        if (mouse)
        {
            return NativeMethods.GetCursorPos(out point);
        }

        point = default;
        return false;
    }

    /// <summary>
    /// True when Win32 reports the contact/button is gone. Unknown (no pointer
    /// info yet) is not treated as up — PointerReleased still ends the drag.
    /// </summary>
    public static bool IsExplicitlyUp(uint win32Id, bool mouse)
    {
        if (win32Id != 0 && NativeMethods.GetPointerInfo(win32Id, out POINTER_INFO info))
        {
            if ((info.pointerFlags & PointerFlagUp) != 0)
            {
                return true;
            }

            if ((info.pointerFlags & PointerFlagInContact) != 0
                || (info.pointerFlags & PointerFlagDown) != 0)
            {
                return false;
            }
        }

        if (TryReadAnyContact(out _, out _))
        {
            return false;
        }

        if (mouse)
        {
            return (NativeMethods.GetAsyncKeyState(NativeMethods.VkLbutton) & 0x8000) == 0;
        }

        return false;
    }

    private static bool IsContactOrDown(uint id) =>
        NativeMethods.GetPointerInfo(id, out POINTER_INFO info)
        && (info.pointerFlags & (PointerFlagInContact | PointerFlagDown)) != 0;

    private static bool TryRead(uint id, out POINT point)
    {
        if (id != 0 && NativeMethods.GetPointerInfo(id, out POINTER_INFO info))
        {
            Remember(id, info.ptPixelLocation);
            point = info.ptPixelLocation;
            return true;
        }

        lock (Gate)
        {
            return ById.TryGetValue(id, out point);
        }
    }

    private static bool TryReadAnyContact(out uint id, out POINT point)
    {
        uint fallbackId = 0;
        POINT fallback = default;
        bool hasFallback = false;

        for (uint i = 1; i <= MaxScanId; i++)
        {
            if (!NativeMethods.GetPointerInfo(i, out POINTER_INFO info))
            {
                continue;
            }

            Remember(info.pointerId, info.ptPixelLocation);
            uint flags = info.pointerFlags;
            if ((flags & PointerFlagUp) != 0)
            {
                continue;
            }

            bool contact = (flags & PointerFlagInContact) != 0;
            bool downish = (flags & (PointerFlagDown | PointerFlagUpdate | PointerFlagInRange)) != 0;
            if (!contact && !downish)
            {
                continue;
            }

            if (contact || (flags & PointerFlagPrimary) != 0)
            {
                id = info.pointerId;
                point = info.ptPixelLocation;
                return true;
            }

            if (!hasFallback)
            {
                fallbackId = info.pointerId;
                fallback = info.ptPixelLocation;
                hasFallback = true;
            }
        }

        id = fallbackId;
        point = fallback;
        return hasFallback;
    }

    private static void Remember(uint id, POINT point)
    {
        lock (Gate)
        {
            ById[id] = point;
            _last = point;
            _hasLast = true;
        }
    }

    private static void Forget(uint id)
    {
        lock (Gate)
        {
            ById.Remove(id);
        }
    }
}
