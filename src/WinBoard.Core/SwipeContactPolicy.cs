namespace WinBoard.Core;

/// <summary>Why a pointer session received an end-like signal.</summary>
public enum SwipeContactSignal
{
    Released,
    Canceled,
    CaptureLost,
}

/// <summary>What the keyboard should do with that signal.</summary>
public enum SwipeContactAction
{
    Ignore,
    /// <summary>Keep tracking; recapture if needed. Do not clear the trail.</summary>
    Continue,
    EndCommit,
    EndCancel,
}

/// <summary>
/// Pointer session for swipe/tap. CaptureLost on a no-activate overlay is
/// common when the previous swipe injects (SendInput / SetForegroundWindow /
/// suggestion rebuild) while the next finger is already down. That is not a
/// user cancel — ending the session would wipe the trail mid-gesture.
/// </summary>
public static class SwipeContactPolicy
{
    public static SwipeContactAction OnEndSignal(
        SwipeContactSignal signal,
        bool sessionActive,
        bool contactDown)
    {
        _ = contactDown;
        if (!sessionActive)
        {
            return SwipeContactAction.Ignore;
        }

        return signal switch
        {
            SwipeContactSignal.Released => SwipeContactAction.EndCommit,
            SwipeContactSignal.Canceled => SwipeContactAction.EndCancel,
            // IsInContact is unreliable after SendInput / focus changes. Treating
            // CaptureLost + "up" as cancel wiped live trails and skipped pointer
            // ids (0.8.14 diag). Recapture and wait for Released / Canceled, or
            // for the next letter finger to commit-then-begin.
            SwipeContactSignal.CaptureLost => SwipeContactAction.Continue,
            _ => SwipeContactAction.EndCancel,
        };
    }

    /// <summary>
    /// Suggestion rebuild, keyboard relayout, and HWND restore must wait until
    /// the finger is up. They drop capture and kill an in-flight trail.
    /// Injection itself may still run.
    /// </summary>
    public static bool AllowOverlayMutation(bool sessionActive) => !sessionActive;
}
