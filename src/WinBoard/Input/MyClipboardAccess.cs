using System.Diagnostics;
using WinBoard.Core;

namespace WinBoard.Input;

/// <summary>
/// Opens MyClipboard Desktop via the frozen authorize protocol. No EXE
/// discovery, no alternate spellings — the button always launches
/// <see cref="MyClipboardContract.ProtocolUri"/>.
/// </summary>
internal static class MyClipboardAccess
{
    public static bool TryOpenAuthorizeProtocol()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = MyClipboardContract.ProtocolUri,
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
