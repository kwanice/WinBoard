using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace WinBoard.Input;

/// <summary>
/// Unpackaged notification-area icon via Win32 <c>Shell_NotifyIcon</c>.
/// No extra NuGet (H.NotifyIcon / Community Toolkit tray) — those are optional
/// and version-sensitive with WASDK 2.4 + net9 unpackaged.
///
/// The icon lives on a message-only HWND so toggling the keyboard does not
/// require activating WinBoard. Menu clicks are marshalled to the UI dispatcher.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const string ClassName = "WinBoardTrayWnd";
    private const uint IconId = 1;

    private static readonly TrayNative.WndProc WndProcDelegate = OnWndProc;
    private static readonly Dictionary<nint, TrayIcon> Instances = [];
    private static bool _classRegistered;
    private static nint _wndProcPtr;

    private readonly DispatcherQueue _dispatcher;
    private nint _hwnd;
    private nint _icon;
    private bool _added;
    private bool _disposed;

    public event Action? ToggleRequested;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    public TrayIcon(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _wndProcPtr = Marshal.GetFunctionPointerForDelegate(WndProcDelegate);
        nint module = TrayNative.GetModuleHandle(null);
        EnsureClass(module);
        _icon = LoadIcon();
        _hwnd = TrayNative.CreateWindowEx(
            0,
            ClassName,
            "WinBoardTray",
            0,
            0,
            0,
            0,
            0,
            TrayNative.HwndMessage,
            nint.Zero,
            module,
            nint.Zero);
        if (_hwnd == nint.Zero)
        {
            Debug.WriteLine($"WinBoard: tray window failed ({Marshal.GetLastWin32Error()}).");
            return;
        }

        Instances[_hwnd] = this;
        AddNotifyIcon();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_added && _hwnd != nint.Zero)
        {
            var data = BaseData();
            TrayNative.ShellNotifyIcon(TrayNative.NimDelete, in data);
            _added = false;
        }

        if (_hwnd != nint.Zero)
        {
            Instances.Remove(_hwnd);
            TrayNative.DestroyWindow(_hwnd);
            _hwnd = nint.Zero;
        }

        if (_icon != nint.Zero)
        {
            TrayNative.DestroyIcon(_icon);
            _icon = nint.Zero;
        }
    }

    private void AddNotifyIcon()
    {
        NOTIFYICONDATA data = BaseData();
        data.uFlags = TrayNative.NifMessage | TrayNative.NifIcon | TrayNative.NifTip | TrayNative.NifShowTip;
        data.uCallbackMessage = TrayNative.WmTrayIcon;
        data.hIcon = _icon;
        data.szTip = "WinBoard";
        if (!TrayNative.ShellNotifyIcon(TrayNative.NimAdd, in data))
        {
            Debug.WriteLine($"WinBoard: Shell_NotifyIcon add failed ({Marshal.GetLastWin32Error()}).");
            return;
        }

        data.uVersion = TrayNative.NotifyIconVersion4;
        TrayNative.ShellNotifyIcon(TrayNative.NimSetVersion, in data);
        _added = true;
    }

    private NOTIFYICONDATA BaseData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = IconId,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private static void EnsureClass(nint module)
    {
        if (_classRegistered)
        {
            return;
        }

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _wndProcPtr,
            hInstance = module,
            lpszClassName = ClassName,
        };
        if (TrayNative.RegisterClassEx(in wc) == 0)
        {
            int err = Marshal.GetLastWin32Error();
            if (err != 1410) // ERROR_CLASS_ALREADY_EXISTS
            {
                Debug.WriteLine($"WinBoard: RegisterClassEx failed ({err}).");
            }
        }

        _classRegistered = true;
    }

    private static nint LoadIcon()
    {
        int cx = TrayNative.GetSystemMetrics(TrayNative.SmCxSmIcon);
        int cy = TrayNative.GetSystemMetrics(TrayNative.SmCySmIcon);
        if (cx <= 0)
        {
            cx = 16;
        }

        if (cy <= 0)
        {
            cy = 16;
        }

        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "winboard.ico");
        if (File.Exists(path))
        {
            nint fromFile = TrayNative.LoadImage(nint.Zero, path, TrayNative.ImageIcon, cx, cy, TrayNative.LrLoadFromFile);
            if (fromFile != nint.Zero)
            {
                return fromFile;
            }
        }

        nint fallback = TrayNative.LoadIcon(nint.Zero, (nint)TrayNative.IdiApplication);
        return fallback;
    }

    private static nint OnWndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == TrayNative.WmTrayIcon && Instances.TryGetValue(hWnd, out TrayIcon? tray))
        {
            tray.OnTrayMessage(lParam);
            return nint.Zero;
        }

        return TrayNative.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void OnTrayMessage(nint lParam)
    {
        uint evt = unchecked((uint)(lParam.ToInt64() & 0xFFFF));
        switch (evt)
        {
            case TrayNative.NinSelect:
            case TrayNative.NinKeySelect:
            case TrayNative.WmLButtonDblClk:
                Raise(ToggleRequested);
                break;
            case TrayNative.WmContextMenu:
            case TrayNative.WmRButtonUp:
                ShowContextMenu();
                break;
        }
    }

    private void ShowContextMenu()
    {
        if (!NativeMethods.GetCursorPos(out POINT pt))
        {
            return;
        }

        nint menu = TrayNative.CreatePopupMenu();
        if (menu == nint.Zero)
        {
            return;
        }

        try
        {
            TrayNative.AppendMenu(menu, TrayNative.MfString, TrayNative.CmdToggle, "Afficher / Masquer");
            TrayNative.AppendMenu(menu, TrayNative.MfString, TrayNative.CmdSettings, "Paramètres");
            TrayNative.AppendMenu(menu, TrayNative.MfSeparator, 0, string.Empty);
            TrayNative.AppendMenu(menu, TrayNative.MfString, TrayNative.CmdQuit, "Quitter");

            // Message-only HWND: SetForegroundWindow lets the menu dismiss on click-away
            // without activating the keyboard overlay.
            TrayNative.SetForegroundWindow(_hwnd);
            uint cmd = TrayNative.TrackPopupMenuEx(
                menu,
                TrayNative.TpmRightButton | TrayNative.TpmReturnCmd | TrayNative.TpmBottomAlign | TrayNative.TpmRightAlign,
                pt.X,
                pt.Y,
                _hwnd,
                nint.Zero);
            TrayNative.PostMessage(_hwnd, 0, nint.Zero, nint.Zero);

            switch (cmd)
            {
                case TrayNative.CmdToggle:
                    Raise(ToggleRequested);
                    break;
                case TrayNative.CmdSettings:
                    Raise(SettingsRequested);
                    break;
                case TrayNative.CmdQuit:
                    Raise(QuitRequested);
                    break;
            }
        }
        finally
        {
            TrayNative.DestroyMenu(menu);
        }
    }

    private void Raise(Action? handler)
    {
        if (handler is null)
        {
            return;
        }

        if (_dispatcher.HasThreadAccess)
        {
            handler();
            return;
        }

        _ = _dispatcher.TryEnqueue(() => handler());
    }
}
