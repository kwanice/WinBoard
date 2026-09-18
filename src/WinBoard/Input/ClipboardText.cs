using Windows.ApplicationModel.DataTransfer;

namespace WinBoard.Input;

/// <summary>
/// Copies plain text to the Windows clipboard. WinRT first (WinUI), Win32
/// CF_UNICODETEXT as fallback. Does not activate the keyboard overlay.
/// </summary>
internal static class ClipboardText
{
    public static bool TryCopy(string text, nint ownerHwnd = 0)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        try
        {
            var package = new DataPackage();
            package.RequestedOperation = DataPackageOperation.Copy;
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            return true;
        }
        catch
        {
            return NativeMethods.TrySetClipboardText(text, ownerHwnd);
        }
    }
}
