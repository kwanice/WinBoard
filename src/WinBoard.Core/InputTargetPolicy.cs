namespace WinBoard.Core;

/// <summary>
/// Which HWND may receive swipe/tap SendInput. The diagnostic window is
/// same-process and must never become the remembered text target.
/// </summary>
public static class InputTargetPolicy
{
    /// <summary>
    /// A usable typing target is another process's window, not the keyboard
    /// overlay and not WinBoard's own Diag/Settings windows.
    /// </summary>
    public static bool IsUsableTextTarget(bool isKeyboardTree, bool sameProcess) =>
        !isKeyboardTree && !sameProcess;

    /// <summary>
    /// Put the overlay-stolen foreground back on the remembered app. Skip
    /// while a finger is down (that drops capture) and when a real app or
    /// Diag/Settings currently owns foreground.
    /// </summary>
    public static bool ShouldRestoreStolenForeground(
        bool contactDown,
        bool foregroundMissing,
        bool foregroundIsKeyboardTree,
        bool foregroundIsUsableTextTarget)
    {
        if (contactDown || foregroundIsUsableTextTarget)
        {
            return false;
        }

        return foregroundMissing || foregroundIsKeyboardTree;
    }

    /// <summary>
    /// Swipe/tap inject restores the remembered other-process HWND even if
    /// Diag is foreground. Never while a contact is down.
    /// </summary>
    public static bool ShouldRestoreForInject(bool contactDown) => !contactDown;
}
