using System.Runtime.InteropServices;

namespace WinBoard.Input;

/// <summary>
/// Win32 interop used by key injection and the no-activate floating window.
/// </summary>
internal static class NativeMethods
{
    internal const int GwlExStyle = -20;
    internal const int WsExNoActivate = 0x08000000;
    internal const int WsExToolWindow = 0x00000080;
    internal const int WsExLayered = 0x00080000;
    internal const uint LwaAlpha = 0x00000002;

    internal const uint WmMouseActivate = 0x0021;
    internal const uint WmPointerActivate = 0x024B;
    internal const uint WmNcDestroy = 0x0082;
    internal const uint WmWindowPosChanging = 0x0046;
    internal const uint WmWindowPosChanged = 0x0047;
    internal const uint WmStyleChanging = 0x007C;
    internal const uint WmShowWindow = 0x0018;
    internal const nint MaNoActivate = 3;

    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpFrameChanged = 0x0020;
    internal const uint SwpHideWindow = 0x0080;
    internal const int WsExTopmost = 0x00000008;

    /// <summary>HWND_TOPMOST. Changing WS_EX styles with SWP_NOZORDER drops this.</summary>
    internal static readonly nint HwndTopmost = new(-1);

    internal const uint GaRoot = 2;

    internal const int SwRestore = 9;

    internal const uint WmActivate = 0x0006;
    internal const uint WmSetFocus = 0x0007;
    internal const uint WmNcActivate = 0x0086;
    internal const uint WmActivateApp = 0x001C;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentProcessId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

    internal const uint InputKeyboard = 1;
    internal const uint KeyeventfKeyup = 0x0002;
    internal const uint KeyeventfUnicode = 0x0004;
    internal const ushort VkBack = 0x08;
    internal const ushort VkReturn = 0x0D;
    internal const ushort VkControl = 0x11;
    internal const ushort VkLeft = 0x25;
    internal const ushort VkRight = 0x27;
    internal const int VkLButton = 0x01;
    internal const uint KeyeventfExtendedKey = 0x0001;

    internal delegate nint SubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam, nuint uIdSubclass, nint dwRefData);

    internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPointerInfo(uint pointerId, out POINTER_INFO pointerInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

    /// <summary>
    /// Re-assert HWND_TOPMOST without moving or activating. Needed after
    /// SetWindowLongPtr(GWL_EXSTYLE), AppWindow.Show, settings, and drag.
    /// </summary>
    internal static void AssertTopmost(nint hwnd)
    {
        if (hwnd == nint.Zero)
        {
            return;
        }

        SetWindowPos(
            hwnd,
            HwndTopmost,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    internal static bool IsTopmost(nint hwnd)
    {
        if (hwnd == nint.Zero)
        {
            return false;
        }

        return WinBoard.Core.TopmostPolicy.HasTopmost(GetWindowLongPtr(hwnd, GwlExStyle));
    }

    internal static bool IsTopLevel(nint hwnd) =>
        hwnd != nint.Zero && GetAncestor(hwnd, GaRoot) == hwnd;

    internal static void MoveResizeNoActivate(nint hwnd, int x, int y, int cx, int cy)
    {
        SetWindowPos(
            hwnd,
            HwndTopmost,
            x,
            y,
            cx,
            cy,
            SwpNoActivate);
    }

    internal static void MoveNoActivate(nint hwnd, int x, int y)
    {
        SetWindowPos(
            hwnd,
            HwndTopmost,
            x,
            y,
            0,
            0,
            SwpNoSize | SwpNoActivate);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumChildWindows(nint hWndParent, EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, nint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RemoveWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    internal static extern nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

    internal static nint GetWindowLongPtr(nint hWnd, int nIndex) =>
        Environment.Is64BitProcess
            ? GetWindowLongPtr64(hWnd, nIndex)
            : GetWindowLong32(hWnd, nIndex);

    internal static nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong) =>
        Environment.Is64BitProcess
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : SetWindowLong32(hWnd, nIndex, (int)dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    internal static extern nint GetAncestor(nint hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenClipboard(nint hWndNewOwner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    internal static extern nint SetClipboardData(uint uFormat, nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll")]
    internal static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalUnlock(nint hMem);

    [DllImport("kernel32.dll")]
    internal static extern nint GlobalFree(nint hMem);

    internal const uint CfUnicodeText = 13;
    internal const uint GmemMoveable = 0x0002;

    /// <summary>CF_UNICODETEXT via Win32 (fallback when WinRT clipboard fails).</summary>
    internal static bool TrySetClipboardText(string text, nint ownerHwnd)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        nint data = nint.Zero;
        try
        {
            int bytes = (text.Length + 1) * 2;
            data = GlobalAlloc(GmemMoveable, (nuint)bytes);
            if (data == nint.Zero)
            {
                return false;
            }

            nint locked = GlobalLock(data);
            if (locked == nint.Zero)
            {
                GlobalFree(data);
                data = nint.Zero;
                return false;
            }

            char[] chars = text.ToCharArray();
            Marshal.Copy(chars, 0, locked, chars.Length);
            Marshal.WriteInt16(locked, chars.Length * 2, 0);
            GlobalUnlock(data);

            if (!OpenClipboard(ownerHwnd))
            {
                GlobalFree(data);
                data = nint.Zero;
                return false;
            }

            try
            {
                EmptyClipboard();
                if (SetClipboardData(CfUnicodeText, data) == nint.Zero)
                {
                    return false;
                }

                data = nint.Zero;
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            if (data != nint.Zero)
            {
                GlobalFree(data);
            }
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPOS
{
    public nint hwnd;
    public nint hwndInsertAfter;
    public int x;
    public int y;
    public int cx;
    public int cy;
    public uint flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct STYLESTRUCT
{
    public uint styleOld;
    public uint styleNew;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINTER_INFO
{
    public uint pointerType;
    public uint pointerId;
    public uint frameId;
    public uint pointerFlags;
    public nint sourceDevice;
    public nint hwndTarget;
    public POINT ptPixelLocation;
    public POINT ptHimetricLocation;
    public POINT ptPixelLocationRaw;
    public POINT ptHimetricLocationRaw;
    public uint dwTime;
    public uint historyCount;
    public int InputData;
    public uint dwKeyStates;
    public ulong PerformanceCount;
    public uint ButtonChangeType;
}

[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint Type;
    public InputUnion Union;
}

[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)] public MOUSEINPUT Mouse;
    [FieldOffset(0)] public KEYBDINPUT Keyboard;
    [FieldOffset(0)] public HARDWAREINPUT Hardware;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort Vk;
    public ushort Scan;
    public uint Flags;
    public uint Time;
    public nuint ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int Dx;
    public int Dy;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public nuint ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HARDWAREINPUT
{
    public uint Msg;
    public ushort ParamL;
    public ushort ParamH;
}
