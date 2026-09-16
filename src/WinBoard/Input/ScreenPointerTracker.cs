namespace WinBoard.Input;

/// <summary>
/// Reads the active drag pointer in physical screen pixels.
/// Mouse uses GetCursorPos; touch/pen uses GetPointerInfo. The small fallback
/// scan handles a WinUI pointer id that does not map directly to the Win32 id.
/// </summary>
internal static class ScreenPointerTracker
{
    private const uint PointerFlagInContact = 0x00000004;
    private const uint PointerFlagDown = 0x00010000;
    private const uint PointerFlagUp = 0x00040000;
    private const uint MaxPointerId = 256;

    public static bool TryBegin(
        uint winUiPointerId,
        bool mouse,
        POINT expectedScreenPoint,
        out uint win32PointerId,
        out POINT screenPoint)
    {
        if (mouse)
        {
            win32PointerId = 0;
            return NativeMethods.GetCursorPos(out screenPoint);
        }

        if (TryReadContact(winUiPointerId, out screenPoint))
        {
            win32PointerId = winUiPointerId;
            return true;
        }

        uint bestId = 0;
        POINT bestPoint = default;
        long bestDistanceSquared = long.MaxValue;
        for (uint id = 1; id <= MaxPointerId; id++)
        {
            if (!TryReadContact(id, out POINT candidate))
            {
                continue;
            }

            long dx = candidate.X - expectedScreenPoint.X;
            long dy = candidate.Y - expectedScreenPoint.Y;
            long distanceSquared = (dx * dx) + (dy * dy);
            if (distanceSquared < bestDistanceSquared)
            {
                bestId = id;
                bestPoint = candidate;
                bestDistanceSquared = distanceSquared;
            }
        }

        win32PointerId = bestId;
        screenPoint = bestPoint;
        return bestId != 0;
    }

    public static bool TryTrack(
        uint win32PointerId,
        bool mouse,
        out POINT screenPoint,
        out bool isDown)
    {
        if (mouse)
        {
            isDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VkLButton) & 0x8000) != 0;
            return NativeMethods.GetCursorPos(out screenPoint);
        }

        if (!NativeMethods.GetPointerInfo(win32PointerId, out POINTER_INFO info))
        {
            screenPoint = default;
            isDown = false;
            return false;
        }

        screenPoint = info.ptPixelLocation;
        isDown = (info.pointerFlags & PointerFlagUp) == 0
            && (info.pointerFlags & (PointerFlagInContact | PointerFlagDown)) != 0;
        return true;
    }

    private static bool TryReadContact(uint id, out POINT screenPoint)
    {
        if (id != 0
            && NativeMethods.GetPointerInfo(id, out POINTER_INFO info)
            && (info.pointerFlags & PointerFlagUp) == 0
            && (info.pointerFlags & (PointerFlagInContact | PointerFlagDown)) != 0)
        {
            screenPoint = info.ptPixelLocation;
            return true;
        }

        screenPoint = default;
        return false;
    }
}
