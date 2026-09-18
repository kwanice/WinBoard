namespace WinBoard.Input;

/// <summary>
/// Remembers the focused target app and puts it back if WinBoard's HWND
/// (or a XAML island child) becomes foreground. SendInput only reaches the
/// app the user is typing in; chained swipes must not leave WinBoard active.
/// </summary>
internal static class InputTargetGuard
{
    private static nint _keyboardRoot;
    private static nint _target;

    public static void BindKeyboard(nint hwnd) => _keyboardRoot = hwnd;

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

    /// <summary>If the foreground window is a real app, remember it as the injection target.</summary>
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

    public static void RestoreIfStolen()
    {
        nint foreground = NativeMethods.GetForegroundWindow();
        if (IsUsableTarget(foreground))
        {
            _target = foreground;
            return;
        }

        // Only fight when the overlay itself became foreground. Settings / Diag
        // are activatable WinBoard windows and must keep focus while open.
        if (foreground == nint.Zero || IsKeyboardTree(foreground))
        {
            RestoreTarget();
        }
    }

    private static bool IsUsableTarget(nint hwnd)
    {
        if (hwnd == nint.Zero || !NativeMethods.IsWindow(hwnd) || IsKeyboardTree(hwnd))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        return processId != 0 && processId != NativeMethods.GetCurrentProcessId();
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
