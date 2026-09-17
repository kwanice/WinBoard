namespace WinBoard.Core;

/// <summary>
/// How a new key pointer should interact with an in-flight press.
/// WinBoard is not a fully independent multi-pointer keyboard: one primary
/// press (tap / swipe / space) plus an optional Shift hold.
/// </summary>
public enum KeyPressAction
{
    /// <summary>Drop this pointer (already tracked, or extra finger during swipe).</summary>
    Ignore,

    /// <summary>Start the primary tap/swipe/repeat on this key.</summary>
    BeginPrimary,

    /// <summary>Start a momentary Shift hold. Do not cancel the primary press.</summary>
    BeginShiftHold,

    /// <summary>
    /// A second non-Shift finger while another key is down (no Shift hold):
    /// cancel the first press, then start this one. Legacy single-pointer feel.
    /// </summary>
    CancelPrimaryThenBegin,
}

/// <summary>
/// Pure pointer-routing rules. Shift + letter coexist; a second finger mid-swipe
/// is ignored so the glide path is not ResetPress'd.
/// </summary>
public static class KeyPointerPolicy
{
    public static KeyPressAction OnPressed(
        uint pointerId,
        bool isShiftKey,
        uint? primaryPointerId,
        uint? shiftHoldPointerId,
        bool swiping)
    {
        if (primaryPointerId == pointerId || shiftHoldPointerId == pointerId)
        {
            return KeyPressAction.Ignore;
        }

        if (swiping)
        {
            return KeyPressAction.Ignore;
        }

        if (isShiftKey)
        {
            return shiftHoldPointerId is null
                ? KeyPressAction.BeginShiftHold
                : KeyPressAction.Ignore;
        }

        if (primaryPointerId is null)
        {
            return KeyPressAction.BeginPrimary;
        }

        if (shiftHoldPointerId is not null)
        {
            return KeyPressAction.Ignore;
        }

        return KeyPressAction.CancelPrimaryThenBegin;
    }

    /// <summary>Shift held + letter is a tap. Single-finger swipe is unchanged.</summary>
    public static bool AllowSwipeLatch(bool shiftHeld) => !shiftHeld;
}
