using System.Diagnostics;
using WinBoard.Core;

namespace WinBoard.Input;

/// <summary>
/// Opens MyClipboard Desktop via the frozen authorize protocol. No EXE
/// path hunting, no alternate folder spellings — the button always launches
/// <see cref="MyClipboardContract.ProtocolUri"/> with shell execute.
/// If MyClipboard is already running and only flashed, best-effort focus.
/// </summary>
internal static class MyClipboardAccess
{
    public static bool TryOpenAuthorizeProtocol()
    {
        bool started = false;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = MyClipboardContract.ProtocolUri,
                UseShellExecute = true,
            });
            started = true;
        }
        catch
        {
            started = false;
        }

        TryFocusRunningMyClipboard();
        return started;
    }

    /// <summary>
    /// Deep-link handling is MyClipboard's job. WinBoard only brings an
    /// already-open MyClipboard window forward when the process is discoverable
    /// by name (no EXE search).
    /// </summary>
    private static void TryFocusRunningMyClipboard()
    {
        try
        {
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    string name = process.ProcessName;
                    if (name.Contains("MyClipBoard", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("MyClipboard", StringComparison.OrdinalIgnoreCase))
                    {
                        nint hwnd = process.MainWindowHandle;
                        if (hwnd != nint.Zero)
                        {
                            NativeMethods.ShowWindow(hwnd, NativeMethods.SwRestore);
                            NativeMethods.SetForegroundWindow(hwnd);
                        }
                    }
                }
            }
        }
        catch
        {
            // Best-effort only; protocol start is the contract.
        }
    }
}
