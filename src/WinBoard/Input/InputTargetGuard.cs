using WinBoard.Core;

namespace WinBoard.Input;

/// <summary>
/// Remembers the focused target app and puts it back if WinBoard's HWND
/// (or a XAML island child) becomes foreground. SendInput only reaches the
/// app the user is typing in; chained swipes must not leave WinBoard active.
/// Diag/Settings are same-process and are never the text target.
/// </summary>
internal static class InputTargetGuard
{
    private static nint _keyboardRoot;
    private static nint _target;
    private static bool _contactDown;

    public static void BindKeyboard(nint hwnd) => _keyboardRoot = hwnd;

    /// <summary>
    /// True while a finger/pen/mouse button is down on the keyboard. HWND
    /// restore (SetForegroundWindow / AttachThreadInput) must not run then —
    /// it drops pointer capture and kills an in-flight swipe trail.
    /// Going idle does not restore here: that raced the next finger in the
    /// teardown gap (0.8.15–0.8.17).
    /// </summary>
    public static void SetContactDown(bool down) => _contactDown = down;

    public static bool ContactDown => _contactDown;

    public static bool IsKeyboardTree(nint hwnd)
    {
        if (hwnd == nint.Zero || _keyboardRoot == nint.Zero)
        {
            return false;
        }

        if (hwnd == _keyboardRoot)
        {
            return true;
        }

        return NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot) == _keyboardRoot;
    }

    /// <summary>
    /// Remember the focused other-process window as the injection target.
    /// Call before a gesture starts. Diag is same-process and is skipped.
    /// </summary>
    public static void NoteTarget()
    {
        nint foreground = NativeMethods.GetForegroundWindow();
        if (IsUsableTarget(foreground))
        {
            _target = foreground;
        }
    }

    /// <summary>Remember a live target, then restore it if WinBoard currently has foreground.</summary>
    public static void EnsureTargetForeground()
    {
        NoteTarget();
        RestoreIfStolen();
    }

    /// <summary>
    /// Put the remembered other-process HWND in front for SendInput, even if
    /// the Diag window currently has foreground. No-op while a contact is down.
    /// </summary>
    public static void RestoreForInject()
    {
        if (!InputTargetPolicy.ShouldRestoreForInject(_contactDown))
        {
            return;
        }

        NoteTarget();
        RestoreTarget();
    }

    public static void RestoreIfStolen()
    {
        nint foreground = NativeMethods.GetForegroundWindow();
        bool usable = IsUsableTarget(foreground);
        if (usable)
        {
            _target = foreground;
        }

        if (!InputTargetPolicy.ShouldRestoreStolenForeground(
                _contactDown,
                foregroundMissing: foreground == nint.Zero,
                foregroundIsKeyboardTree: IsKeyboardTree(foreground),
                foregroundIsUsableTextTarget: usable))
        {
            return;
        }

        RestoreTarget();
    }

    private static bool IsUsableTarget(nint hwnd)
    {
        if (hwnd == nint.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        bool sameProcess = processId == 0 || processId == NativeMethods.GetCurrentProcessId();
        return InputTargetPolicy.IsUsableTextTarget(
            IsKeyboardTree(hwnd),
            sameProcess);
    }

    private static void RestoreTarget()
    {
        nint target = _target;
        if (target == nint.Zero
            || target == _keyboardRoot
            || !NativeMethods.IsWindow(target)
            || NativeMethods.IsIconic(target))
        {
            return;
        }

        nint foreground = NativeMethods.GetForegroundWindow();
        if (foreground == target)
        {
            return;
        }

        uint ourThread = NativeMethods.GetCurrentThreadId();
        uint foregroundThread = foreground == nint.Zero
            ? 0
            : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        uint targetThread = NativeMethods.GetWindowThreadProcessId(target, out _);

        bool attachedForeground = false;
        bool attachedTarget = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != ourThread)
            {
                attachedForeground = NativeMethods.AttachThreadInput(ourThread, foregroundThread, true);
            }

            if (targetThread != 0 && targetThread != ourThread && targetThread != foregroundThread)
            {
                attachedTarget = NativeMethods.AttachThreadInput(ourThread, targetThread, true);
            }

            NativeMethods.SetForegroundWindow(target);
        }
        finally
        {
            if (attachedTarget)
            {
                NativeMethods.AttachThreadInput(ourThread, targetThread, false);
            }

            if (attachedForeground)
            {
                NativeMethods.AttachThreadInput(ourThread, foregroundThread, false);
            }
        }
    }
}
